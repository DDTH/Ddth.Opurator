namespace Ddth.Opurator;

/// <summary>
/// Configures a <see cref="BackgroundTaskManager"/>.
/// </summary>
public sealed class BackgroundTaskManagerOptions
{
    /// <summary>
    /// Gets or sets the maximum number of task invocations that may run concurrently.
    /// </summary>
    public int MaxConcurrency { get; set; } = Math.Max(2, Environment.ProcessorCount * 2);
}
