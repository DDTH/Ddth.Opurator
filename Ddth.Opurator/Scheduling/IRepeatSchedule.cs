namespace Ddth.Opurator;

/// <summary>
/// Calculates the next occurrence of a repeated task.
/// </summary>
public interface IRepeatSchedule
{
    /// <summary>
    /// Returns the next occurrence, or null to complete the registration.
    /// </summary>
    DateTimeOffset? GetNextOccurrence(RepeatScheduleContext context);
}
