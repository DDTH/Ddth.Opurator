namespace Ddth.Opurator;

/// <summary>
/// Represents an accepted background task.
/// </summary>
public sealed class BackgroundTaskHandle
{
    internal BackgroundTaskHandle(BackgroundTaskId id, Task completion)
    {
        Id = id;
        Completion = completion;
    }

    /// <summary>
    /// Gets the task registration identifier.
    /// </summary>
    public BackgroundTaskId Id { get; }

    /// <summary>
    /// Gets a task that completes when the registration reaches a terminal state.
    /// </summary>
    public Task Completion { get; }
}

/// <summary>
/// Represents an accepted background task that produces a result.
/// </summary>
/// <typeparam name="TResult">The result type.</typeparam>
public sealed class BackgroundTaskHandle<TResult>
{
    internal BackgroundTaskHandle(BackgroundTaskId id, Task<TResult> completion)
    {
        Id = id;
        Completion = completion;
    }

    /// <summary>
    /// Gets the task registration identifier.
    /// </summary>
    public BackgroundTaskId Id { get; }

    /// <summary>
    /// Gets the typed completion task.
    /// </summary>
    public Task<TResult> Completion { get; }
}
