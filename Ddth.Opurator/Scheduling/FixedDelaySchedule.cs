namespace Ddth.Opurator;

/// <summary>
/// Schedules the next run a fixed duration after the previous run completes.
/// </summary>
public sealed class FixedDelaySchedule : IRepeatSchedule
{
    /// <summary>
    /// Creates a fixed-delay schedule.
    /// </summary>
    public FixedDelaySchedule(TimeSpan delay)
    {
        Delay = delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    /// <summary>
    /// Gets the delay between runs.
    /// </summary>
    public TimeSpan Delay { get; }

    /// <inheritdoc />
    public DateTimeOffset? GetNextOccurrence(RepeatScheduleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CompletedAt + Delay;
    }
}
