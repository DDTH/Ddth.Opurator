namespace Ddth.Opurator.Internal;

internal interface ITaskCompletionSink
{
    Task Completion { get; }

    void TrySetResult(object? result);

    void TrySetException(Exception exception);

    void TrySetCanceled();
}

internal sealed class VoidTaskCompletionSink : ITaskCompletionSink
{
    private readonly TaskCompletionSource<object?> _source =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Completion => _source.Task;

    public void TrySetResult(object? result)
    {
        _source.TrySetResult(null);
    }

    public void TrySetException(Exception exception)
    {
        _source.TrySetException(exception);
    }

    public void TrySetCanceled()
    {
        _source.TrySetCanceled();
    }
}

internal sealed class ResultTaskCompletionSink<TResult> : ITaskCompletionSink
{
    private readonly TaskCompletionSource<TResult> _source =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<TResult> TypedCompletion => _source.Task;

    public Task Completion => _source.Task;

    public void TrySetResult(object? result)
    {
        _source.TrySetResult((TResult)result!);
    }

    public void TrySetException(Exception exception)
    {
        _source.TrySetException(exception);
    }

    public void TrySetCanceled()
    {
        _source.TrySetCanceled();
    }
}
