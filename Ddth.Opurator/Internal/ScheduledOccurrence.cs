namespace Ddth.Opurator.Internal;

internal readonly record struct ScheduledOccurrence(
    BackgroundTaskId TaskId,
    long Generation,
    DateTimeOffset ScheduledAt,
    long DueTimestamp);
