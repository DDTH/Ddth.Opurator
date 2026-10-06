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

## Getting Started

### Installation

Install the latest stable version:

```sh
dotnet add package Ddth.Opurator
```

To install a specific version, replace `<version>` with the required release:

```sh
dotnet add package Ddth.Opurator --version <version>
```

### Register or create the task manager

For applications using dependency injection, register one application-wide
`IBackgroundTaskManager`:

```csharp
builder.Services.AddOpurator();
```

Inject `IBackgroundTaskManager` into services that schedule or inspect tasks.
The service provider owns the manager's lifetime.

For applications without dependency injection, create and dispose the manager
directly:

```csharp
await using var manager = new BackgroundTaskManager();
```

The following examples assume an available task manager named `manager`.

### One-shot tasks

Use `RunOnce` for an operation that should execute only once. It can run
immediately or after a delay and can optionally return a typed result:

```csharp
var oneShotHandle = manager.RunOnce(
    async cancellationToken =>
    {
        // Simulate a long-running asynchronous operation.
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        return 42;
    },
    new RunOnceOptions
    {
        Delay = TimeSpan.FromSeconds(1),
        Timeout = TimeSpan.FromSeconds(10)
    });

var result = await oneShotHandle.Completion;
Console.WriteLine($"{oneShotHandle.Id}: {result}");
```

`Delay` controls when the task becomes eligible to run, while `Timeout` applies
to the operation itself. Omit `Delay` to run immediately. Use the non-generic
`RunOnce` overload when no result is required.

`BackgroundTaskHandle<TResult>.Completion` provides the type-safe result. The
same result remains available through `TryGetResult<TResult>` until the
registration is removed or the manager is disposed.

### Repeated tasks

Use `RunRepeatedly` for recurring work:

```csharp
var repeatedHandle = manager.RunRepeatedly(
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
```

`InitialDelay` controls the first run. `DelayBetweenRuns` is measured from one
run's completion to the next run, and `Timeout` applies independently to every
run. Invocations from the same registration never overlap.

By default, failed and timed-out runs are recorded and the registration
continues. Set `FailurePolicy` to `RepeatFailurePolicy.Stop` to stop after an
exception. Use a positive delay to avoid a tight execution loop.

For schedules other than a fixed post-run delay, implement `IRepeatSchedule`
and assign it to `RepeatOptions.Schedule`. Returning `null` from
`GetNextOccurrence` completes the registration.

### Querying and cleanup

Use a task ID to inspect the latest state, retrieve a completed typed result,
or remove a terminal registration:

```csharp
if (manager.TryGetSnapshot(oneShotHandle.Id, out var snapshot))
{
    Console.WriteLine(snapshot.Status);
    Console.WriteLine(snapshot.RunCount);
    Console.WriteLine(snapshot.LastRun?.Outcome);
}

if (manager.TryGetResult<int>(oneShotHandle.Id, out var result))
{
    Console.WriteLine(result);
}

manager.TryRemove(oneShotHandle.Id);
```

Statuses include `Scheduled`, `Queued`, `Running`,
`CancellationRequested`, `Completed`, `Canceled`, `TimedOut`, and `Failed`.
`TryRemove` succeeds only for a terminal registration. Until it is removed,
the manager retains its snapshot, result, or exception in memory.

### Cancellation and shutdown

Request cancellation by task ID without blocking for a running operation to
exit:

```csharp
var cancellationResult = manager.RequestCancellation(repeatedHandle.Id);
Console.WriteLine(cancellationResult);

try
{
    await repeatedHandle.Completion;
}
catch (OperationCanceledException)
{
    // The registration has stopped.
}
```

Cancellation and timeout are cooperative. Each operation must observe its
`CancellationToken`; otherwise it continues to occupy a concurrency slot until
it exits.

For a manager created without dependency injection, `await using` shuts it down
automatically. To stop it earlier, call:

```csharp
await manager.ShutdownAsync();
```

Applications using dependency injection should let the service provider dispose
the manager. Shutdown stops accepting new tasks, requests cancellation for
existing registrations, and waits for running operations to exit. All task
state is in memory and is lost when the application exits or reloads.

## License

This package is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## Contributing & Support

Feel free to create [pull requests](https://github.com/DDTH/Ddth.Opurator/compare/contrib_wait_to_merge...) or [issues](https://github.com/DDTH/Ddth.Opurator/issues) to report bugs or suggest new features.
