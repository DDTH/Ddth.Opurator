using System.Diagnostics.CodeAnalysis;

namespace Ddth.Opurator;

/// <summary>
/// Schedules and tracks lightweight in-process background tasks.
/// </summary>
public interface IBackgroundTaskManager : IAsyncDisposable
{
    /// <summary>
    /// Gets the maximum number of concurrently running task invocations.
    /// </summary>
    int MaxConcurrency { get; }

    /// <summary>
    /// Schedules an operation that does not return a result.
    /// </summary>
    BackgroundTaskHandle RunOnce(
        Func<CancellationToken, Task> operation,
        RunOnceOptions? options = null);

    /// <summary>
    /// Schedules an operation that returns a typed result.
    /// </summary>
    BackgroundTaskHandle<TResult> RunOnce<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        RunOnceOptions? options = null);

    /// <summary>
    /// Schedules an operation that runs repeatedly without overlapping itself.
    /// </summary>
    BackgroundTaskHandle RunRepeatedly(
        Func<CancellationToken, Task> operation,
        RepeatOptions? options = null);

    /// <summary>
    /// Attempts to retrieve the latest task registration snapshot.
    /// </summary>
    bool TryGetSnapshot(
        BackgroundTaskId id,
        [NotNullWhen(true)] out BackgroundTaskSnapshot? snapshot);

    /// <summary>
    /// Attempts to retrieve a successfully completed typed result.
    /// </summary>
    bool TryGetResult<TResult>(
        BackgroundTaskId id,
        [MaybeNullWhen(false)] out TResult result);

    /// <summary>
    /// Requests cancellation without waiting for a running delegate to exit.
    /// </summary>
    CancellationRequestResult RequestCancellation(BackgroundTaskId id);

    /// <summary>
    /// Removes a terminal registration and its retained result or exception.
    /// </summary>
    bool TryRemove(BackgroundTaskId id);

    /// <summary>
    /// Stops accepting tasks, requests cancellation, and waits for running delegates to exit.
    /// </summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
