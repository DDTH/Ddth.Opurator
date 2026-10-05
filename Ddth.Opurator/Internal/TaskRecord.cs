namespace Ddth.Opurator.Internal;

internal sealed class TaskRecord
{
    public TaskRecord(
        BackgroundTaskId id,
        BackgroundTaskKind kind,
        Func<CancellationToken, Task<object?>> operation,
        TimeSpan? timeout,
        IRepeatSchedule? repeatSchedule,
        RepeatFailurePolicy failurePolicy,
        bool producesResult,
        Type? resultType,
        ITaskCompletionSink completion,
        DateTimeOffset createdAt)
    {
        Id = id;
        Kind = kind;
        Operation = operation;
        Timeout = timeout;
        RepeatSchedule = repeatSchedule;
        FailurePolicy = failurePolicy;
        ProducesResult = producesResult;
        ResultType = resultType;
        Completion = completion;
        CreatedAt = createdAt;
    }

    public object SyncRoot { get; } = new();

    public BackgroundTaskId Id { get; }

    public BackgroundTaskKind Kind { get; }

    public Func<CancellationToken, Task<object?>> Operation { get; }

    public TimeSpan? Timeout { get; }

    public IRepeatSchedule? RepeatSchedule { get; }

    public RepeatFailurePolicy FailurePolicy { get; }

    public bool ProducesResult { get; }

    public Type? ResultType { get; }

    public ITaskCompletionSink Completion { get; }

    public CancellationTokenSource CancellationSource { get; } = new();

    public DateTimeOffset CreatedAt { get; }

    public BackgroundTaskStatus Status { get; set; }

    public long Generation { get; set; }

    public DateTimeOffset? NextRunAt { get; set; }

    public DateTimeOffset? CurrentRunStartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public int RunCount { get; set; }

    public int ConsecutiveFailures { get; set; }

    public BackgroundTaskCancellationReason? CancellationReason { get; set; }

    public BackgroundTaskRunSnapshot? LastRun { get; set; }

    public Exception? Exception { get; set; }

    public object? Result { get; set; }

    public bool HasResult { get; set; }
}
