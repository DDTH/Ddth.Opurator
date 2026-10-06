namespace Ddth.Opurator;

/// <summary>
/// Describes a completed task invocation.
/// </summary>
public sealed record BackgroundTaskRunSnapshot(
    int RunNumber,
    DateTimeOffset ScheduledAt,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    BackgroundTaskRunOutcome Outcome,
    BackgroundTaskCancellationReason? CancellationReason,
    Exception? Exception);

/// <summary>
/// Provides an immutable view of a background task registration.
/// </summary>
public sealed record BackgroundTaskSnapshot(
    BackgroundTaskId Id,
    BackgroundTaskKind Kind,
    BackgroundTaskStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? CurrentRunStartedAt,
    DateTimeOffset? CompletedAt,
    int RunCount,
    int ConsecutiveFailures,
    bool HasResult,
    BackgroundTaskCancellationReason? CancellationReason,
    BackgroundTaskRunSnapshot? LastRun,
    Exception? Exception);
