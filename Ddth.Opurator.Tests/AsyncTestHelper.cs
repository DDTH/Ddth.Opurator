using System.Diagnostics;

namespace Ddth.Opurator.Tests;

internal static class AsyncTestHelper
{
    public static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan? timeout = null)
    {
        var maximumWait = timeout ?? TimeSpan.FromSeconds(5);
        var stopwatch = Stopwatch.StartNew();

        while (!condition())
        {
            if (stopwatch.Elapsed >= maximumWait)
            {
                throw new TimeoutException("The expected condition was not reached in time.");
            }

            await Task.Delay(10);
        }
    }

    public static void UpdateMaximum(ref int maximum, int candidate)
    {
        var currentMaximum = Volatile.Read(ref maximum);

        while (candidate > currentMaximum)
        {
            var previous = Interlocked.CompareExchange(
                ref maximum,
                candidate,
                currentMaximum);
            if (previous == currentMaximum)
            {
                return;
            }

            currentMaximum = previous;
        }
    }
}
