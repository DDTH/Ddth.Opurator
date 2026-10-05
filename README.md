[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![Actions Status](https://github.com/DDTH/Ddth.Opurator/workflows/ci/badge.svg)](https://github.com/DDTH/Ddth.Opurator/actions)
[![codecov](https://codecov.io/gh/DDTH/Ddth.Opurator/graph/badge.svg)](https://codecov.io/gh/DDTH/Ddth.Opurator)
[![Release](https://img.shields.io/github/release/DDTH/Ddth.Opurator.svg?style=flat-square)](RELEASE-NOTES.md)

**Ddth.Opurator** is a lightweight in-process background task manager for .NET. It
runs delayed, one-shot, and repeated asynchronous operations with bounded
concurrency, status tracking, cooperative cancellation, and per-run timeouts.

## Features

- **Bounded asynchronous execution:** runs background operations in-process
  while limiting concurrent work.
- **One-shot and recurring scheduling:** supports immediate or delayed one-shot
  tasks and repeated tasks with fixed or custom schedules.
- **Safe recurring execution:** prevents overlapping runs and applies timeout
  and failure handling independently to each invocation.
- **Typed results and runtime visibility:** exposes completion results, status,
  timestamps, run history, and exceptions through task handles and snapshots.
- **Controlled lifecycle:** provides cooperative cancellation and graceful
  asynchronous shutdown for registered operations.
- **Built-in dependency injection:** registers an application-wide manager for
  container-managed lifetime in ASP.NET Core and Blazor WebAssembly applications.

## Installation

```sh
dotnet add package Ddth.Opurator
```

## Quick guide

Applications using `Microsoft.Extensions.DependencyInjection`, including ASP.NET
Core and Blazor WebAssembly applications, can register one application-wide
manager:

```csharp
builder.Services.AddOpurator();
```

Inject `IBackgroundTaskManager` where tasks are scheduled. The service provider
owns the singleton manager and shuts it down when the application is disposed.

For applications without dependency injection, create and dispose a manager
directly:

```csharp
await using var manager = new BackgroundTaskManager();

var handle = manager.RunOnce(
    async cancellationToken =>
    {
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        return "completed";
    },
    new RunOnceOptions
    {
        Timeout = TimeSpan.FromSeconds(10)
    });

Console.WriteLine(await handle.Completion);
```

Use `RunOnce` for individual work items and `RunRepeatedly` for recurring work.
Keep the manager alive for as long as its registered tasks should run, and
dispose it during application shutdown.

### One-shot tasks

```csharp
await using var manager = new BackgroundTaskManager();

var handle = manager.RunOnce(
    async cancellationToken =>
    {
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        return 42;
    },
    new RunOnceOptions
    {
        Delay = TimeSpan.FromSeconds(1),
        Timeout = TimeSpan.FromSeconds(10)
    });

var result = await handle.Completion;
Console.WriteLine($"{handle.Id}: {result}");
```

`BackgroundTaskHandle<TResult>.Completion` provides the type-safe result.
The same result can be retrieved later with `TryGetResult<TResult>` while the
registration remains tracked.

### Repeated tasks

```csharp
var handle = manager.RunRepeatedly(
    async cancellationToken =>
    {
        await RefreshCacheAsync(cancellationToken);
    },
    new RepeatOptions
    {
        InitialDelay = TimeSpan.FromSeconds(5),
        Timeout = TimeSpan.FromSeconds(30),
        DelayBetweenRuns = TimeSpan.FromMinutes(1)
    });

// Request cancellation without blocking for the delegate to exit.
manager.RequestCancellation(handle.Id);

try
{
    await handle.Completion;
}
catch (OperationCanceledException)
{
    // The repeated registration has stopped.
}
```

Repeated tasks do not overlap themselves. By default, a failed or timed-out
run is recorded and the next run is scheduled normally. Set
`RepeatOptions.FailurePolicy` to `RepeatFailurePolicy.Stop` to stop after an
exception. Timeouts always continue unless the registration is canceled.
A zero delay requeues the task immediately, so callers should configure a
positive delay when continuous execution would create a tight loop.

For schedules other than a fixed post-run delay, implement `IRepeatSchedule`.
Returning `null` from `GetNextOccurrence` completes the registration.

### Querying and cleanup

```csharp
if (manager.TryGetSnapshot(handle.Id, out var snapshot))
{
    Console.WriteLine(snapshot.Status);
    Console.WriteLine(snapshot.LastRun?.Outcome);
}

// Terminal registrations retain their status/result until explicitly removed
// or until the manager is discarded.
manager.TryRemove(handle.Id);
```

Statuses include `Scheduled`, `Queued`, `Running`,
`CancellationRequested`, `Completed`, `Canceled`, `TimedOut`, and `Failed`.

### Cancellation and shutdown

Cancellation and timeout are cooperative. The supplied delegate must observe
its `CancellationToken`. If it ignores cancellation, the invocation continues
to occupy its concurrency slot, and a repeated task will not start another
overlapping run.

Disposing the manager stops accepting new registrations, requests cancellation
for existing registrations, and waits for running delegates to exit. All
statuses and results are in-memory and are lost when the process exits.

## License

This package is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## Contributing & Support

Feel free to create [pull requests](https://github.com/DDTH/Ddth.Opurator/compare/contrib_wait_to_merge...) or [issues](https://github.com/DDTH/Ddth.Opurator/issues) to report bugs or suggest new features.
