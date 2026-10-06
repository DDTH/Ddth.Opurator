namespace Ddth.Opurator.Tests;

public class RepeatedTaskTests
{
    [Fact]
    public async Task RunRepeatedly_DoesNotOverlapAndRunsUntilCanceled()
    {
        await using var manager = CreateManager();
        var threeRunsCompleted = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        var maximumRunning = 0;
        var completedRuns = 0;

        var handle = manager.RunRepeatedly(
            async cancellationToken =>
            {
                var currentRunning = Interlocked.Increment(ref running);
                AsyncTestHelper.UpdateMaximum(ref maximumRunning, currentRunning);

                try
                {
                    await Task.Delay(20, cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref running);
                }

                if (Interlocked.Increment(ref completedRuns) >= 3)
                {
                    threeRunsCompleted.TrySetResult(null);
                }
            });

        await threeRunsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(
            CancellationRequestResult.Requested,
            manager.RequestCancellation(handle.Id));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await handle.Completion);

        Assert.Equal(1, Volatile.Read(ref maximumRunning));
        Assert.True(manager.TryGetSnapshot(handle.Id, out var snapshot));
        Assert.Equal(BackgroundTaskStatus.Canceled, snapshot?.Status);
        Assert.True(snapshot?.RunCount >= 3);
    }

    [Fact]
    public async Task RunRepeatedly_ContinuesAfterEachRunTimeout()
    {
        await using var manager = CreateManager();
        var invocation = 0;
        var secondRunStarted = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var handle = manager.RunRepeatedly(
            async cancellationToken =>
            {
                if (Interlocked.Increment(ref invocation) == 1)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return;
                }

                secondRunStarted.TrySetResult(null);
            },
            new RepeatOptions
            {
                Timeout = TimeSpan.FromMilliseconds(50),
                DelayBetweenRuns = TimeSpan.FromMilliseconds(200)
            });

        await AsyncTestHelper.WaitUntilAsync(
            () =>
            {
                return manager.TryGetSnapshot(handle.Id, out var snapshot)
                    && snapshot?.Status == BackgroundTaskStatus.Scheduled
                    && snapshot.LastRun?.Outcome == BackgroundTaskRunOutcome.TimedOut;
            });

        await secondRunStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        manager.RequestCancellation(handle.Id);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await handle.Completion);
    }

    [Fact]
    public async Task RunRepeatedly_ContinuesAfterFailureByDefault()
    {
        await using var manager = CreateManager();
        var invocation = 0;
        var secondRunStarted = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var handle = manager.RunRepeatedly(
            _ =>
            {
                if (Interlocked.Increment(ref invocation) == 1)
                {
                    throw new InvalidOperationException("Expected first-run failure.");
                }

                secondRunStarted.TrySetResult(null);
                return Task.CompletedTask;
            },
            new RepeatOptions
            {
                DelayBetweenRuns = TimeSpan.FromMilliseconds(200)
            });

        await AsyncTestHelper.WaitUntilAsync(
            () =>
            {
                return manager.TryGetSnapshot(handle.Id, out var snapshot)
                    && snapshot?.Status == BackgroundTaskStatus.Scheduled
                    && snapshot.LastRun?.Outcome == BackgroundTaskRunOutcome.Failed;
            });

        await secondRunStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        manager.RequestCancellation(handle.Id);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await handle.Completion);
    }

    [Fact]
    public async Task RunRepeatedly_CanStopAfterFailure()
    {
        await using var manager = CreateManager();
        var handle = manager.RunRepeatedly(
            _ => throw new InvalidOperationException("Expected failure."),
            new RepeatOptions
            {
                FailurePolicy = RepeatFailurePolicy.Stop
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await handle.Completion);

        Assert.Equal("Expected failure.", exception.Message);
        Assert.True(manager.TryGetSnapshot(handle.Id, out var snapshot));
        Assert.Equal(BackgroundTaskStatus.Failed, snapshot?.Status);
        Assert.Equal(1, snapshot?.RunCount);
    }

    [Fact]
    public async Task RunRepeatedly_CompletesWhenScheduleReturnsNoNextOccurrence()
    {
        await using var manager = CreateManager();
        var handle = manager.RunRepeatedly(
            _ => Task.CompletedTask,
            new RepeatOptions
            {
                Schedule = new SingleRunSchedule()
            });

        await handle.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(manager.TryGetSnapshot(handle.Id, out var snapshot));
        Assert.Equal(BackgroundTaskStatus.Completed, snapshot?.Status);
        Assert.Equal(BackgroundTaskRunOutcome.Completed, snapshot?.LastRun?.Outcome);
        Assert.Equal(1, snapshot?.RunCount);
    }

    private static BackgroundTaskManager CreateManager()
    {
        return new BackgroundTaskManager(
            new BackgroundTaskManagerOptions
            {
                MaxConcurrency = 2
            });
    }

    private sealed class SingleRunSchedule : IRepeatSchedule
    {
        public DateTimeOffset? GetNextOccurrence(RepeatScheduleContext context)
        {
            return null;
        }
    }
}
