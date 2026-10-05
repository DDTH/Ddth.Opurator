using System.Diagnostics;

namespace Ddth.Opurator.Internal;

internal interface IBackgroundTaskClock
{
    DateTimeOffset UtcNow { get; }

    long GetTimestamp();

    long Add(long timestamp, TimeSpan delay);

    TimeSpan GetDelay(long dueTimestamp);

    Task Delay(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class SystemBackgroundTaskClock : IBackgroundTaskClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public long GetTimestamp()
    {
        return Stopwatch.GetTimestamp();
    }

    public long Add(long timestamp, TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return timestamp;
        }

        var delta = Math.Ceiling(delay.TotalSeconds * Stopwatch.Frequency);
        if (delta >= long.MaxValue - timestamp)
        {
            return long.MaxValue;
        }

        return timestamp + (long)delta;
    }

    public TimeSpan GetDelay(long dueTimestamp)
    {
        var remaining = dueTimestamp - Stopwatch.GetTimestamp();
        return remaining <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency);
    }

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }
}
