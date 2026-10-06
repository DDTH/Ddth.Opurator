namespace Ddth.Opurator;

/// <summary>
/// Identifies a task registered with a <see cref="IBackgroundTaskManager"/>.
/// </summary>
public readonly record struct BackgroundTaskId(Guid Value)
{
    internal static BackgroundTaskId New()
    {
        return new BackgroundTaskId(Guid.NewGuid());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString("D");
    }
}
