namespace Ddth.Opurator;

/// <summary>
/// Configures a repeatedly running task.
/// </summary>
public sealed class RepeatOptions
{
    /// <summary>
    /// Gets the delay before the first run.
    /// Null, zero, and negative values run immediately.
    /// </summary>
    public TimeSpan? InitialDelay { get; init; }

    /// <summary>
    /// Gets the timeout applied independently to every run.
    /// Null and <see cref="Timeout.InfiniteTimeSpan"/> disable the timeout.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Gets the fixed delay measured from one run's completion to the next run.
    /// Ignored when <see cref="Schedule"/> is supplied.
    /// </summary>
    public TimeSpan? DelayBetweenRuns { get; init; }

    /// <summary>
    /// Gets a custom schedule used to calculate future occurrences.
    /// </summary>
    public IRepeatSchedule? Schedule { get; init; }

    /// <summary>
    /// Gets the behavior used when an invocation throws an exception.
    /// Timeouts continue independently of this policy.
    /// </summary>
    public RepeatFailurePolicy FailurePolicy { get; init; } = RepeatFailurePolicy.Continue;
}
