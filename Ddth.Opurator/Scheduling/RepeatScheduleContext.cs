namespace Ddth.Opurator;

/// <summary>
/// Provides information about the invocation that just completed.
/// </summary>
public sealed record RepeatScheduleContext(
    int RunNumber,
    int ConsecutiveFailures,
    DateTimeOffset ScheduledAt,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    BackgroundTaskRunOutcome Outcome,
    Exception? Exception);
