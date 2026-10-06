namespace Ddth.Opurator;

/// <summary>
/// Describes how a background task is executed.
/// </summary>
public enum BackgroundTaskKind
{
    RunOnce,
    Repeated
}

/// <summary>
/// Describes the current state of a background task registration.
/// </summary>
public enum BackgroundTaskStatus
{
    Scheduled,
    Queued,
    Running,
    CancellationRequested,
    Completed,
    Canceled,
    TimedOut,
    Failed
}

/// <summary>
/// Describes the result of one task invocation.
/// </summary>
public enum BackgroundTaskRunOutcome
{
    Completed,
    Canceled,
    TimedOut,
    Failed
}

/// <summary>
/// Describes why cancellation was requested.
/// </summary>
public enum BackgroundTaskCancellationReason
{
    User,
    Timeout,
    ManagerShutdown
}

/// <summary>
/// Describes the result of requesting cancellation.
/// </summary>
public enum CancellationRequestResult
{
    Requested,
    AlreadyRequested,
    AlreadyCompleted,
    NotFound
}

/// <summary>
/// Controls how a repeated task responds to an invocation failure.
/// </summary>
public enum RepeatFailurePolicy
{
    Continue,
    Stop
}
