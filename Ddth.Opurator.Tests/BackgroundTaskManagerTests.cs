namespace Ddth.Opurator.Tests;

public class BackgroundTaskManagerTests
{
    [Fact]
    public void Constructor_RejectsConcurrencyBelowTwo()
    {
        var options = new BackgroundTaskManagerOptions
        {
            MaxConcurrency = 1
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BackgroundTaskManager(options));
    }

    [Fact]
    public async Task RunOnce_ReturnsTypedResultAndCompletedSnapshot()
    {
        await using var manager = CreateManager();

        var handle = manager.RunOnce(_ => Task.FromResult(42));
        var result = await handle.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(42, result);
        Assert.NotEqual(Guid.Empty, handle.Id.Value);
        Assert.True(manager.TryGetResult<int>(handle.Id, out var storedResult));
        Assert.Equal(42, storedResult);
        Assert.False(manager.TryGetResult<string>(handle.Id, out _));

        Assert.True(manager.TryGetSnapshot(handle.Id, out var snapshot));
        Assert.NotNull(snapshot);
        Assert.Equal(BackgroundTaskStatus.Completed, snapshot.Status);
        Assert.Equal(BackgroundTaskRunOutcome.Completed, snapshot.LastRun?.Outcome);
        Assert.Equal(1, snapshot.RunCount);
        Assert.True(snapshot.HasResult);
        Assert.True(manager.TryRemove(handle.Id));
        Assert.False(manager.TryGetSnapshot(handle.Id, out _));
        Assert.False(manager.TryRemove(handle.Id));
    }

    [Fact]
    public async Task RunOnce_LimitsConcurrentInvocations()
    {
        await using var manager = CreateManager();
        var release = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var twoStarted = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var current = 0;
        var maximum = 0;
        var started = 0;

        async Task Operation(CancellationToken cancellationToken)
        {
            var running = Interlocked.Increment(ref current);
            AsyncTestHelper.UpdateMaximum(ref maximum, running);
            if (Interlocked.Increment(ref started) == 2)
            {
                twoStarted.TrySetResult(null);
            }

            try
            {
                await release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref current);
            }
        }

        var handles = Enumerable
            .Range(0, 6)
            .Select(_ => manager.RunOnce(Operation))
            .ToArray();

        await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        Assert.Equal(2, Volatile.Read(ref maximum));
        Assert.Equal(2, Volatile.Read(ref started));

        release.TrySetResult(null);
        await Task.WhenAll(handles.Select(handle => handle.Completion))
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunOnce_CanBeCanceledBeforeItsDelayIsDue()
    {
        await using var manager = CreateManager();
        var invoked = false;
        var handle = manager.RunOnce(
            _ =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            new RunOnceOptions
            {
                Delay = TimeSpan.FromMinutes(1)
            });

        Assert.True(manager.TryGetSnapshot(handle.Id, out var scheduledSnapshot));
        Assert.Equal(BackgroundTaskStatus.Scheduled, scheduledSnapshot?.Status);
        Assert.Equal(
            CancellationRequestResult.Requested,
            manager.RequestCancellation(handle.Id));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await handle.Completion);

        Assert.False(invoked);
        Assert.True(manager.TryGetSnapshot(handle.Id, out var canceledSnapshot));
        Assert.Equal(BackgroundTaskStatus.Canceled, canceledSnapshot?.Status);
        Assert.Equal(
            BackgroundTaskCancellationReason.User,
            canceledSnapshot?.CancellationReason);
    }

    [Fact]
    public async Task RunOnce_TimeoutCancelsTheTokenAndReportsTimedOut()
    {
        await using var manager = CreateManager();
        var cancellationObserved = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var handle = manager.RunOnce(
            async cancellationToken =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancellationObserved.TrySetResult(null);
                    throw;
                }
            },
            new RunOnceOptions
            {
                Timeout = TimeSpan.FromMilliseconds(50)
            });

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<TimeoutException>(
            async () => await handle.Completion);

        Assert.True(manager.TryGetSnapshot(handle.Id, out var snapshot));
        Assert.Equal(BackgroundTaskStatus.TimedOut, snapshot?.Status);
        Assert.Equal(BackgroundTaskRunOutcome.TimedOut, snapshot?.LastRun?.Outcome);
        Assert.Equal(
            BackgroundTaskCancellationReason.Timeout,
            snapshot?.CancellationReason);
    }

    [Fact]
    public async Task RunOnce_TimeoutWaitsForAnUncooperativeDelegateToExit()
    {
        await using var manager = CreateManager();
        var started = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = manager.RunOnce(
            async _ =>
            {
                started.TrySetResult(null);
                await release.Task;
            },
            new RunOnceOptions
            {
                Timeout = TimeSpan.FromMilliseconds(50)
            });

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await AsyncTestHelper.WaitUntilAsync(
                () =>
                {
                    return manager.TryGetSnapshot(handle.Id, out var snapshot)
                        && snapshot.Status == BackgroundTaskStatus.CancellationRequested
                        && snapshot.CancellationReason == BackgroundTaskCancellationReason.Timeout;
                });

            Assert.False(handle.Completion.IsCompleted);
        }
        finally
        {
            release.TrySetResult(null);
        }

        await Assert.ThrowsAsync<TimeoutException>(
            async () => await handle.Completion);
    }

    [Fact]
    public async Task Shutdown_CancelsRunningTasksAndRejectsNewTasks()
    {
        await using var manager = CreateManager();
        var started = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = manager.RunOnce(
            async cancellationToken =>
            {
                started.TrySetResult(null);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await handle.Completion);
        Assert.True(manager.TryGetSnapshot(handle.Id, out var snapshot));
        Assert.Equal(BackgroundTaskStatus.Canceled, snapshot?.Status);
        Assert.Equal(
            BackgroundTaskCancellationReason.ManagerShutdown,
            snapshot?.CancellationReason);
        Assert.Throws<InvalidOperationException>(
            () => manager.RunOnce(_ => Task.CompletedTask));
    }

    private static BackgroundTaskManager CreateManager()
    {
        return new BackgroundTaskManager(
            new BackgroundTaskManagerOptions
            {
                MaxConcurrency = 2
            });
    }
}
