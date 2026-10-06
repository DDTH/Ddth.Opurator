using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Ddth.Opurator.Internal;

namespace Ddth.Opurator;

/// <summary>
/// Runs and tracks lightweight background tasks within the current process.
/// </summary>
public sealed class BackgroundTaskManager : IBackgroundTaskManager
{
    private static readonly TimeSpan MaximumSchedulerWait = TimeSpan.FromDays(1);

    private readonly ConcurrentDictionary<BackgroundTaskId, TaskRecord> _records = new();
    private readonly PriorityQueue<ScheduledOccurrence, long> _scheduledOccurrences = new();
    private readonly object _scheduleSyncRoot = new();
    private readonly object _lifecycleSyncRoot = new();
    private readonly SemaphoreSlim _scheduleChanged = new(0);
    private readonly Channel<ScheduledOccurrence> _readyQueue;
    private readonly CancellationTokenSource _shutdownSource = new();
    private readonly IBackgroundTaskClock _clock;
    private readonly Task _schedulerTask;
    private readonly Task[] _workerTasks;

    private bool _acceptingTasks = true;
    private Task? _shutdownTask;

    /// <summary>
    /// Creates a background task manager.
    /// </summary>
    public BackgroundTaskManager(BackgroundTaskManagerOptions? options = null)
        : this(options ?? new BackgroundTaskManagerOptions(), new SystemBackgroundTaskClock())
    {
    }

    internal BackgroundTaskManager(
        BackgroundTaskManagerOptions options,
        IBackgroundTaskClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        if (options.MaxConcurrency < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BackgroundTaskManagerOptions.MaxConcurrency),
                options.MaxConcurrency,
                "MaxConcurrency must be at least 2.");
        }

        MaxConcurrency = options.MaxConcurrency;
        _clock = clock;
        _readyQueue = Channel.CreateUnbounded<ScheduledOccurrence>(
            new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = false,
                SingleWriter = true
            });

        _schedulerTask = Task.Run(SchedulerLoopAsync);
        _workerTasks = Enumerable
            .Range(0, MaxConcurrency)
            .Select(_ => Task.Run(WorkerLoopAsync))
            .ToArray();
    }

    /// <inheritdoc />
    public int MaxConcurrency { get; }

    /// <inheritdoc />
    public BackgroundTaskHandle RunOnce(
        Func<CancellationToken, Task> operation,
        RunOnceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var completion = new VoidTaskCompletionSink();
        var record = CreateRecord(
            BackgroundTaskKind.RunOnce,
            async cancellationToken =>
            {
                var operationTask = operation(cancellationToken)
                    ?? throw new InvalidOperationException("The background operation returned a null task.");

                await operationTask.ConfigureAwait(false);
                return null;
            },
            NormalizeTimeout(options?.Timeout),
            repeatSchedule: null,
            RepeatFailurePolicy.Stop,
            producesResult: false,
            resultType: null,
            completion);

        AcceptAndSchedule(record, NormalizeDelay(options?.Delay));
        return new BackgroundTaskHandle(record.Id, completion.Completion);
    }

    /// <inheritdoc />
    public BackgroundTaskHandle<TResult> RunOnce<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        RunOnceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var completion = new ResultTaskCompletionSink<TResult>();
        var record = CreateRecord(
            BackgroundTaskKind.RunOnce,
            async cancellationToken =>
            {
                var operationTask = operation(cancellationToken)
                    ?? throw new InvalidOperationException("The background operation returned a null task.");

                return await operationTask.ConfigureAwait(false);
            },
            NormalizeTimeout(options?.Timeout),
            repeatSchedule: null,
            RepeatFailurePolicy.Stop,
            producesResult: true,
            typeof(TResult),
            completion);

        AcceptAndSchedule(record, NormalizeDelay(options?.Delay));
        return new BackgroundTaskHandle<TResult>(record.Id, completion.TypedCompletion);
    }

    /// <inheritdoc />
    public BackgroundTaskHandle RunRepeatedly(
        Func<CancellationToken, Task> operation,
        RepeatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        options ??= new RepeatOptions();

        if (!Enum.IsDefined(typeof(RepeatFailurePolicy), options.FailurePolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(RepeatOptions.FailurePolicy),
                options.FailurePolicy,
                "The repeat failure policy is invalid.");
        }

        var completion = new VoidTaskCompletionSink();
        var schedule = options.Schedule
            ?? new FixedDelaySchedule(NormalizeDelay(options.DelayBetweenRuns));
        var record = CreateRecord(
            BackgroundTaskKind.Repeated,
            async cancellationToken =>
            {
                var operationTask = operation(cancellationToken)
                    ?? throw new InvalidOperationException("The background operation returned a null task.");

                await operationTask.ConfigureAwait(false);
                return null;
            },
            NormalizeTimeout(options.Timeout),
            schedule,
            options.FailurePolicy,
            producesResult: false,
            resultType: null,
            completion);

        AcceptAndSchedule(record, NormalizeDelay(options.InitialDelay));
        return new BackgroundTaskHandle(record.Id, completion.Completion);
    }

    /// <inheritdoc />
    public bool TryGetSnapshot(
        BackgroundTaskId id,
        [NotNullWhen(true)] out BackgroundTaskSnapshot? snapshot)
    {
        if (!_records.TryGetValue(id, out var record))
        {
            snapshot = null;
            return false;
        }

        lock (record.SyncRoot)
        {
            snapshot = new BackgroundTaskSnapshot(
                record.Id,
                record.Kind,
                record.Status,
                record.CreatedAt,
                record.NextRunAt,
                record.CurrentRunStartedAt,
                record.CompletedAt,
                record.RunCount,
                record.ConsecutiveFailures,
                record.HasResult,
                record.CancellationReason,
                record.LastRun,
                record.Exception);
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryGetResult<TResult>(
        BackgroundTaskId id,
        [MaybeNullWhen(false)] out TResult result)
    {
        if (!_records.TryGetValue(id, out var record))
        {
            result = default;
            return false;
        }

        lock (record.SyncRoot)
        {
            if (!record.HasResult || record.ResultType != typeof(TResult))
            {
                result = default;
                return false;
            }

            result = (TResult)record.Result!;
            return true;
        }
    }

    /// <inheritdoc />
    public CancellationRequestResult RequestCancellation(BackgroundTaskId id)
    {
        return _records.TryGetValue(id, out var record)
            ? RequestCancellation(record, BackgroundTaskCancellationReason.User)
            : CancellationRequestResult.NotFound;
    }

    /// <inheritdoc />
    public bool TryRemove(BackgroundTaskId id)
    {
        if (!_records.TryGetValue(id, out var record))
        {
            return false;
        }

        lock (record.SyncRoot)
        {
            return IsTerminal(record.Status)
                && _records.TryRemove(id, out _);
        }
    }

    /// <inheritdoc />
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        var shutdownTask = EnsureShutdownStarted();
        await shutdownTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await EnsureShutdownStarted().ConfigureAwait(false);
    }

    private TaskRecord CreateRecord(
        BackgroundTaskKind kind,
        Func<CancellationToken, Task<object?>> operation,
        TimeSpan? timeout,
        IRepeatSchedule? repeatSchedule,
        RepeatFailurePolicy failurePolicy,
        bool producesResult,
        Type? resultType,
        ITaskCompletionSink completion)
    {
        return new TaskRecord(
            BackgroundTaskId.New(),
            kind,
            operation,
            timeout,
            repeatSchedule,
            failurePolicy,
            producesResult,
            resultType,
            completion,
            _clock.UtcNow);
    }

    private void AcceptAndSchedule(TaskRecord record, TimeSpan delay)
    {
        var scheduledAt = AddDelay(record.CreatedAt, delay, nameof(delay));

        lock (_lifecycleSyncRoot)
        {
            if (!_acceptingTasks)
            {
                throw new InvalidOperationException("The background task manager is shutting down.");
            }

            if (!_records.TryAdd(record.Id, record))
            {
                throw new InvalidOperationException("Unable to allocate a unique background task identifier.");
            }
        }

        if (!TrySchedule(record, scheduledAt))
        {
            CompleteCancellationAfterScheduling(record);
        }
    }

    private bool TrySchedule(TaskRecord record, DateTimeOffset scheduledAt)
    {
        ScheduledOccurrence occurrence;

        lock (record.SyncRoot)
        {
            if (IsTerminal(record.Status)
                || record.Status == BackgroundTaskStatus.CancellationRequested)
            {
                return false;
            }

            var delay = scheduledAt - _clock.UtcNow;
            var dueTimestamp = _clock.Add(
                _clock.GetTimestamp(),
                delay > TimeSpan.Zero ? delay : TimeSpan.Zero);

            record.Generation++;
            record.Status = BackgroundTaskStatus.Scheduled;
            record.NextRunAt = scheduledAt;
            record.CurrentRunStartedAt = null;
            record.CompletedAt = null;
            record.CancellationReason = null;

            occurrence = new ScheduledOccurrence(
                record.Id,
                record.Generation,
                scheduledAt,
                dueTimestamp);
        }

        lock (_scheduleSyncRoot)
        {
            _scheduledOccurrences.Enqueue(occurrence, occurrence.DueTimestamp);
        }

        _scheduleChanged.Release();
        return true;
    }

    private async Task SchedulerLoopAsync()
    {
        Exception? schedulerException = null;

        try
        {
            while (true)
            {
                _shutdownSource.Token.ThrowIfCancellationRequested();
                DrainScheduleSignal();

                var hasDueOccurrence = false;
                var dueOccurrence = default(ScheduledOccurrence);
                var wait = Timeout.InfiniteTimeSpan;

                if (TryGetNextScheduledOccurrence(out var nextOccurrence, out wait)
                    && wait <= TimeSpan.Zero)
                {
                    lock (_scheduleSyncRoot)
                    {
                        if (_scheduledOccurrences.TryPeek(out var currentOccurrence, out _)
                            && currentOccurrence == nextOccurrence)
                        {
                            dueOccurrence = _scheduledOccurrences.Dequeue();
                            hasDueOccurrence = true;
                        }
                    }
                }

                if (hasDueOccurrence)
                {
                    QueueDueOccurrence(dueOccurrence);
                    continue;
                }

                if (wait > MaximumSchedulerWait)
                {
                    wait = MaximumSchedulerWait;
                }

                await _scheduleChanged
                    .WaitAsync(wait, _shutdownSource.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_shutdownSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            schedulerException = exception;
            StopAcceptingTasks();
            FailPendingTasks(exception);
            RecordCancellationExceptionForAll(TryCancel(_shutdownSource));
        }
        finally
        {
            _readyQueue.Writer.TryComplete(schedulerException);
        }
    }

    private void QueueDueOccurrence(ScheduledOccurrence occurrence)
    {
        if (!_records.TryGetValue(occurrence.TaskId, out var record))
        {
            return;
        }

        lock (record.SyncRoot)
        {
            if (record.Generation != occurrence.Generation
                || record.Status != BackgroundTaskStatus.Scheduled)
            {
                return;
            }

            record.Status = BackgroundTaskStatus.Queued;
        }

        if (!_readyQueue.Writer.TryWrite(occurrence))
        {
            FailRecord(
                record,
                new InvalidOperationException("The background task ready queue is no longer available."));
        }
    }

    private async Task WorkerLoopAsync()
    {
        await foreach (var occurrence in _readyQueue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (!_records.TryGetValue(occurrence.TaskId, out var record))
            {
                continue;
            }

            try
            {
                await ExecuteOccurrenceAsync(record, occurrence).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                FailRecord(record, exception);
            }
        }
    }

    private async Task ExecuteOccurrenceAsync(
        TaskRecord record,
        ScheduledOccurrence occurrence)
    {
        int runNumber;
        DateTimeOffset startedAt;

        lock (record.SyncRoot)
        {
            if (record.Generation != occurrence.Generation
                || record.Status != BackgroundTaskStatus.Queued)
            {
                return;
            }

            startedAt = _clock.UtcNow;
            runNumber = ++record.RunCount;
            record.Status = BackgroundTaskStatus.Running;
            record.NextRunAt = null;
            record.CurrentRunStartedAt = startedAt;
            record.CancellationReason = null;
            record.Exception = null;
        }

        var invocationResult = await InvokeOperationAsync(record).ConfigureAwait(false);
        var completedAt = _clock.UtcNow;

        if (record.Kind == BackgroundTaskKind.RunOnce)
        {
            CompleteRunOnce(
                record,
                occurrence,
                runNumber,
                startedAt,
                completedAt,
                invocationResult);
            return;
        }

        CompleteRepeatedRun(
            record,
            occurrence,
            runNumber,
            startedAt,
            completedAt,
            invocationResult);
    }

    private async Task<InvocationResult> InvokeOperationAsync(TaskRecord record)
    {
        using var timeoutSource = new CancellationTokenSource();
        using var invocationSource = CancellationTokenSource.CreateLinkedTokenSource(
            record.CancellationSource.Token,
            _shutdownSource.Token,
            timeoutSource.Token);
        using var timeoutMonitorSource = new CancellationTokenSource();

        var timeoutMonitorTask = record.Timeout is { } timeout
            ? MonitorTimeoutAsync(record, timeout, timeoutSource, timeoutMonitorSource.Token)
            : Task.CompletedTask;

        object? result = null;
        Exception? exception = null;

        try
        {
            result = await record.Operation(invocationSource.Token).ConfigureAwait(false);
        }
        catch (Exception operationException)
        {
            exception = operationException;
        }
        finally
        {
            var monitorCancellationException = TryCancel(timeoutMonitorSource);
            if (monitorCancellationException is not null)
            {
                RecordCancellationException(record, monitorCancellationException);
            }

            try
            {
                await timeoutMonitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutMonitorSource.IsCancellationRequested)
            {
            }
            catch (Exception monitorException)
            {
                exception = CombineExceptions(exception, monitorException);
            }
        }

        return new InvocationResult(result, exception);
    }

    private async Task MonitorTimeoutAsync(
        TaskRecord record,
        TimeSpan timeout,
        CancellationTokenSource timeoutSource,
        CancellationToken cancellationToken)
    {
        try
        {
            await _clock.Delay(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var reason = MarkCancellationRequested(
            record,
            BackgroundTaskCancellationReason.Timeout);

        if (reason == BackgroundTaskCancellationReason.Timeout)
        {
            RecordCancellationException(record, TryCancel(timeoutSource));
        }
    }

    private static void CompleteRunOnce(
        TaskRecord record,
        ScheduledOccurrence occurrence,
        int runNumber,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        InvocationResult invocationResult)
    {
        BackgroundTaskRunOutcome outcome;
        BackgroundTaskCancellationReason? cancellationReason;
        Exception? exception;

        lock (record.SyncRoot)
        {
            cancellationReason = record.CancellationReason;
            outcome = DetermineOutcome(cancellationReason, invocationResult.Exception);
            exception = CreateRunException(
                record,
                outcome,
                invocationResult.Exception,
                runNumber);

            record.Status = ToTerminalStatus(outcome);
            record.NextRunAt = null;
            record.CurrentRunStartedAt = null;
            record.CompletedAt = completedAt;
            record.ConsecutiveFailures = outcome is
                BackgroundTaskRunOutcome.Failed or BackgroundTaskRunOutcome.TimedOut
                ? 1
                : 0;
            record.CancellationReason = cancellationReason;
            record.Exception = exception;
            record.Result = outcome == BackgroundTaskRunOutcome.Completed
                ? invocationResult.Result
                : null;
            record.HasResult = outcome == BackgroundTaskRunOutcome.Completed
                && record.ProducesResult;
            record.LastRun = new BackgroundTaskRunSnapshot(
                runNumber,
                occurrence.ScheduledAt,
                startedAt,
                completedAt,
                outcome,
                cancellationReason,
                exception);
        }

        CompleteHandle(record, outcome, invocationResult.Result, exception);
    }

    private void CompleteRepeatedRun(
        TaskRecord record,
        ScheduledOccurrence occurrence,
        int runNumber,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        InvocationResult invocationResult)
    {
        BackgroundTaskRunOutcome outcome;
        BackgroundTaskCancellationReason? cancellationReason;
        Exception? exception;
        RepeatScheduleContext? scheduleContext = null;
        var terminal = false;

        lock (record.SyncRoot)
        {
            cancellationReason = record.CancellationReason;
            outcome = DetermineOutcome(cancellationReason, invocationResult.Exception);
            exception = CreateRunException(
                record,
                outcome,
                invocationResult.Exception,
                runNumber);

            record.NextRunAt = null;
            record.CurrentRunStartedAt = null;
            record.Exception = exception;
            record.ConsecutiveFailures = outcome switch
            {
                BackgroundTaskRunOutcome.Completed => 0,
                BackgroundTaskRunOutcome.Failed or BackgroundTaskRunOutcome.TimedOut
                    => record.ConsecutiveFailures + 1,
                _ => record.ConsecutiveFailures
            };
            record.LastRun = new BackgroundTaskRunSnapshot(
                runNumber,
                occurrence.ScheduledAt,
                startedAt,
                completedAt,
                outcome,
                cancellationReason,
                exception);

            if (outcome == BackgroundTaskRunOutcome.Canceled)
            {
                record.Status = BackgroundTaskStatus.Canceled;
                record.CompletedAt = completedAt;
                terminal = true;
            }
            else if (outcome == BackgroundTaskRunOutcome.Failed
                && record.FailurePolicy == RepeatFailurePolicy.Stop)
            {
                record.Status = BackgroundTaskStatus.Failed;
                record.CompletedAt = completedAt;
                terminal = true;
            }
            else
            {
                record.Status = BackgroundTaskStatus.Running;
                scheduleContext = new RepeatScheduleContext(
                    runNumber,
                    record.ConsecutiveFailures,
                    occurrence.ScheduledAt,
                    startedAt,
                    completedAt,
                    outcome,
                    exception);
            }
        }

        if (terminal)
        {
            CompleteHandle(record, outcome, result: null, exception);
            return;
        }

        ScheduleNextRepeatedRun(record, scheduleContext!);
    }

    private void ScheduleNextRepeatedRun(
        TaskRecord record,
        RepeatScheduleContext context)
    {
        DateTimeOffset? nextOccurrence;

        try
        {
            nextOccurrence = record.RepeatSchedule!.GetNextOccurrence(context);
        }
        catch (Exception exception)
        {
            CompleteRepeatScheduleFailure(record, exception);
            return;
        }

        if (nextOccurrence is null)
        {
            CompleteRepeatNaturally(record);
            return;
        }

        if (!TrySchedule(record, nextOccurrence.Value))
        {
            CompleteCancellationAfterScheduling(record);
        }
    }

    private void CompleteRepeatScheduleFailure(
        TaskRecord record,
        Exception exception)
    {
        var canceled = false;

        lock (record.SyncRoot)
        {
            if (record.Status == BackgroundTaskStatus.CancellationRequested)
            {
                record.Status = BackgroundTaskStatus.Canceled;
                record.Exception = CombineExceptions(record.Exception, exception);
                canceled = true;
            }
            else if (!IsTerminal(record.Status))
            {
                record.Status = BackgroundTaskStatus.Failed;
                record.Exception = CombineExceptions(record.Exception, exception);
            }
            else
            {
                return;
            }

            record.CompletedAt = _clock.UtcNow;
            record.NextRunAt = null;
            record.CurrentRunStartedAt = null;
        }

        if (canceled)
        {
            record.Completion.TrySetCanceled();
        }
        else
        {
            record.Completion.TrySetException(record.Exception!);
        }
    }

    private void CompleteRepeatNaturally(TaskRecord record)
    {
        var canceled = false;

        lock (record.SyncRoot)
        {
            if (record.Status == BackgroundTaskStatus.CancellationRequested)
            {
                record.Status = BackgroundTaskStatus.Canceled;
                canceled = true;
            }
            else if (!IsTerminal(record.Status))
            {
                record.Status = BackgroundTaskStatus.Completed;
                record.CancellationReason = null;
            }
            else
            {
                return;
            }

            record.CompletedAt = _clock.UtcNow;
            record.NextRunAt = null;
            record.CurrentRunStartedAt = null;
        }

        if (canceled)
        {
            record.Completion.TrySetCanceled();
        }
        else
        {
            record.Completion.TrySetResult(null);
        }
    }

    private void CompleteCancellationAfterScheduling(TaskRecord record)
    {
        var shouldComplete = false;

        lock (record.SyncRoot)
        {
            if (record.Status == BackgroundTaskStatus.CancellationRequested)
            {
                record.Status = BackgroundTaskStatus.Canceled;
                record.CompletedAt = _clock.UtcNow;
                record.NextRunAt = null;
                record.CurrentRunStartedAt = null;
                shouldComplete = true;
            }
        }

        if (shouldComplete)
        {
            record.Completion.TrySetCanceled();
        }
    }

    private CancellationRequestResult RequestCancellation(
        TaskRecord record,
        BackgroundTaskCancellationReason reason)
    {
        var cancelLifetime = false;
        var completeImmediately = false;

        lock (record.SyncRoot)
        {
            if (IsTerminal(record.Status))
            {
                return CancellationRequestResult.AlreadyCompleted;
            }

            if (record.Status == BackgroundTaskStatus.CancellationRequested)
            {
                if (record.CancellationReason != BackgroundTaskCancellationReason.Timeout
                    || reason == BackgroundTaskCancellationReason.Timeout)
                {
                    return CancellationRequestResult.AlreadyRequested;
                }
            }

            record.Generation++;
            record.CancellationReason = reason;
            record.NextRunAt = null;

            if (record.Status is BackgroundTaskStatus.Running
                or BackgroundTaskStatus.CancellationRequested)
            {
                record.Status = BackgroundTaskStatus.CancellationRequested;
            }
            else
            {
                record.Status = BackgroundTaskStatus.Canceled;
                record.CompletedAt = _clock.UtcNow;
                record.CurrentRunStartedAt = null;
                record.Exception = null;
                completeImmediately = true;
            }

            cancelLifetime = reason != BackgroundTaskCancellationReason.Timeout;
        }

        _scheduleChanged.Release();

        if (cancelLifetime)
        {
            RecordCancellationException(record, TryCancel(record.CancellationSource));
        }

        if (completeImmediately)
        {
            record.Completion.TrySetCanceled();
        }

        return CancellationRequestResult.Requested;
    }

    private bool TryGetNextScheduledOccurrence(
        out ScheduledOccurrence occurrence,
        out TimeSpan wait)
    {
        while (true)
        {
            lock (_scheduleSyncRoot)
            {
                if (!_scheduledOccurrences.TryPeek(out occurrence, out _))
                {
                    wait = Timeout.InfiniteTimeSpan;
                    return false;
                }
            }

            if (IsCurrentOccurrence(occurrence))
            {
                wait = _clock.GetDelay(occurrence.DueTimestamp);
                return true;
            }

            lock (_scheduleSyncRoot)
            {
                if (_scheduledOccurrences.TryPeek(out var currentOccurrence, out _)
                    && currentOccurrence == occurrence)
                {
                    _scheduledOccurrences.Dequeue();
                }
            }
        }
    }

    private bool IsCurrentOccurrence(ScheduledOccurrence occurrence)
    {
        if (!_records.TryGetValue(occurrence.TaskId, out var record))
        {
            return false;
        }

        lock (record.SyncRoot)
        {
            return record.Generation == occurrence.Generation
                && record.Status == BackgroundTaskStatus.Scheduled;
        }
    }

    private static BackgroundTaskCancellationReason? MarkCancellationRequested(
        TaskRecord record,
        BackgroundTaskCancellationReason reason)
    {
        lock (record.SyncRoot)
        {
            if (IsTerminal(record.Status))
            {
                return record.CancellationReason;
            }

            if (record.Status == BackgroundTaskStatus.CancellationRequested
                && record.CancellationReason is not null)
            {
                return record.CancellationReason;
            }

            if (record.Status == BackgroundTaskStatus.Running)
            {
                record.Status = BackgroundTaskStatus.CancellationRequested;
                record.CancellationReason = reason;
            }

            return record.CancellationReason;
        }
    }

    private static void CompleteHandle(
        TaskRecord record,
        BackgroundTaskRunOutcome outcome,
        object? result,
        Exception? exception)
    {
        switch (outcome)
        {
            case BackgroundTaskRunOutcome.Completed:
                record.Completion.TrySetResult(result);
                break;
            case BackgroundTaskRunOutcome.Canceled:
                record.Completion.TrySetCanceled();
                break;
            case BackgroundTaskRunOutcome.TimedOut:
            case BackgroundTaskRunOutcome.Failed:
                record.Completion.TrySetException(
                    exception
                    ?? new InvalidOperationException("The background task failed without an exception."));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
        }
    }

    private static Exception? CreateRunException(
        TaskRecord record,
        BackgroundTaskRunOutcome outcome,
        Exception? operationException,
        int runNumber)
    {
        var recordedException = record.Exception;

        if (operationException is OperationCanceledException
            && outcome is BackgroundTaskRunOutcome.Canceled or BackgroundTaskRunOutcome.TimedOut)
        {
            operationException = null;
        }

        var combinedException = CombineExceptions(recordedException, operationException);
        if (outcome != BackgroundTaskRunOutcome.TimedOut)
        {
            return combinedException;
        }

        var timeoutException = new TimeoutException(
            $"Background task '{record.Id}' run {runNumber} exceeded its timeout.",
            combinedException);
        return timeoutException;
    }

    private static BackgroundTaskRunOutcome DetermineOutcome(
        BackgroundTaskCancellationReason? cancellationReason,
        Exception? operationException)
    {
        return cancellationReason switch
        {
            BackgroundTaskCancellationReason.Timeout => BackgroundTaskRunOutcome.TimedOut,
            BackgroundTaskCancellationReason.User
                or BackgroundTaskCancellationReason.ManagerShutdown
                => BackgroundTaskRunOutcome.Canceled,
            _ when operationException is not null => BackgroundTaskRunOutcome.Failed,
            _ => BackgroundTaskRunOutcome.Completed
        };
    }

    private static BackgroundTaskStatus ToTerminalStatus(BackgroundTaskRunOutcome outcome)
    {
        return outcome switch
        {
            BackgroundTaskRunOutcome.Completed => BackgroundTaskStatus.Completed,
            BackgroundTaskRunOutcome.Canceled => BackgroundTaskStatus.Canceled,
            BackgroundTaskRunOutcome.TimedOut => BackgroundTaskStatus.TimedOut,
            BackgroundTaskRunOutcome.Failed => BackgroundTaskStatus.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
        };
    }

    private void FailPendingTasks(Exception exception)
    {
        foreach (var record in _records.Values)
        {
            BackgroundTaskStatus status;

            lock (record.SyncRoot)
            {
                status = record.Status;
            }

            if (status is BackgroundTaskStatus.Scheduled or BackgroundTaskStatus.Queued)
            {
                FailRecord(record, exception);
            }
            else if (!IsTerminal(status))
            {
                RequestCancellation(
                    record,
                    BackgroundTaskCancellationReason.ManagerShutdown);
            }
        }
    }

    private void FailRecord(TaskRecord record, Exception exception)
    {
        var shouldComplete = false;

        lock (record.SyncRoot)
        {
            if (IsTerminal(record.Status))
            {
                return;
            }

            record.Generation++;
            record.Status = BackgroundTaskStatus.Failed;
            record.NextRunAt = null;
            record.CurrentRunStartedAt = null;
            record.CompletedAt = _clock.UtcNow;
            record.Exception = CombineExceptions(record.Exception, exception);
            shouldComplete = true;
        }

        if (shouldComplete)
        {
            record.Completion.TrySetException(record.Exception!);
        }
    }

    private Task EnsureShutdownStarted()
    {
        lock (_lifecycleSyncRoot)
        {
            if (_shutdownTask is not null)
            {
                return _shutdownTask;
            }

            _acceptingTasks = false;
            _shutdownTask = Task.Run(ShutdownCoreAsync);
            return _shutdownTask;
        }
    }

    private async Task ShutdownCoreAsync()
    {
        foreach (var record in _records.Values)
        {
            RequestCancellation(record, BackgroundTaskCancellationReason.ManagerShutdown);
        }

        RecordCancellationExceptionForAll(TryCancel(_shutdownSource));

        var backgroundTasks = new Task[_workerTasks.Length + 1];
        backgroundTasks[0] = _schedulerTask;
        Array.Copy(_workerTasks, 0, backgroundTasks, 1, _workerTasks.Length);
        await Task.WhenAll(backgroundTasks).ConfigureAwait(false);
    }

    private void StopAcceptingTasks()
    {
        lock (_lifecycleSyncRoot)
        {
            _acceptingTasks = false;
        }
    }

    private void DrainScheduleSignal()
    {
        while (_scheduleChanged.Wait(0))
        {
            // Drain all pending signals before recalculating the scheduler wait.
        }
    }

    private void RecordCancellationExceptionForAll(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        foreach (var record in _records.Values)
        {
            RecordCancellationException(record, exception);
        }
    }

    private static void RecordCancellationException(
        TaskRecord record,
        Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        lock (record.SyncRoot)
        {
            record.Exception = CombineExceptions(record.Exception, exception);
        }
    }

    private static Exception? TryCancel(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
            return null;
        }
        catch (AggregateException exception)
        {
            return exception;
        }
    }

    private static Exception? CombineExceptions(
        Exception? first,
        Exception? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return new AggregateException(first, second);
    }

    private static bool IsTerminal(BackgroundTaskStatus status)
    {
        return status is
            BackgroundTaskStatus.Completed
            or BackgroundTaskStatus.Canceled
            or BackgroundTaskStatus.TimedOut
            or BackgroundTaskStatus.Failed;
    }

    private static TimeSpan NormalizeDelay(TimeSpan? delay)
    {
        return delay is { } value && value > TimeSpan.Zero
            ? value
            : TimeSpan.Zero;
    }

    private static TimeSpan? NormalizeTimeout(TimeSpan? timeout)
    {
        if (timeout is null || timeout == Timeout.InfiniteTimeSpan)
        {
            return null;
        }

        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "Timeout must be non-negative, null, or Timeout.InfiniteTimeSpan.");
        }

        return timeout;
    }

    private static DateTimeOffset AddDelay(
        DateTimeOffset value,
        TimeSpan delay,
        string parameterName)
    {
        try
        {
            return value + delay;
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                delay,
                "The delay produces a date outside the supported range.");
        }
    }

    private sealed record InvocationResult(object? Result, Exception? Exception);
}
