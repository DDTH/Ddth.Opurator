namespace Ddth.Opurator;

/// <summary>
/// Configures a task that runs once.
/// </summary>
public sealed class RunOnceOptions
{
    /// <summary>
    /// Gets the delay before the task becomes eligible to run.
    /// Null, zero, and negative values run immediately.
    /// </summary>
    public TimeSpan? Delay { get; init; }

    /// <summary>
    /// Gets the timeout applied to the task invocation.
    /// Null and <see cref="Timeout.InfiniteTimeSpan"/> disable the timeout.
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}
