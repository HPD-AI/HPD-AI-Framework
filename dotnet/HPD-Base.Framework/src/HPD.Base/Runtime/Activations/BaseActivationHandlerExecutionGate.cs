using System.Collections.Concurrent;

namespace HPD.Base;

internal enum BaseActivationHandlerExecutionOutcome
{
    Completed,
    TimedOut,
    Cancelled,
    Failed,
    Capacity,
}

internal sealed record BaseActivationHandlerExecutionResult<T>(
    BaseActivationHandlerExecutionOutcome Outcome,
    T? Value);

internal sealed class BaseActivationHandlerExecutionGate : IAsyncDisposable
{
    private readonly SemaphoreSlim _capacity = new(32, 32);
    private readonly ConcurrentDictionary<long, Task> _retained = new();
    private long _sequence;
    private int _stopping;

    internal int RetainedCount => _retained.Count;

    internal async ValueTask<BaseActivationHandlerExecutionResult<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> execute,
        TimeSpan timeout,
        CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(execute);
        if (Volatile.Read(ref _stopping) != 0)
            return new(BaseActivationHandlerExecutionOutcome.Capacity, default);
        try
        {
            if (!await _capacity.WaitAsync(TimeSpan.FromSeconds(5), caller).ConfigureAwait(false))
                return new(BaseActivationHandlerExecutionOutcome.Capacity, default);
        }
        catch (OperationCanceledException)
        {
            return new(BaseActivationHandlerExecutionOutcome.Cancelled, default);
        }

        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(caller);
        Task<T> task;
        try { task = execute(lifetime.Token); }
        catch
        {
            lifetime.Dispose();
            _capacity.Release();
            return new(BaseActivationHandlerExecutionOutcome.Failed, default);
        }
        try
        {
            T value = await task.WaitAsync(timeout, caller).ConfigureAwait(false);
            lifetime.Dispose();
            _capacity.Release();
            return new(BaseActivationHandlerExecutionOutcome.Completed, value);
        }
        catch (TimeoutException)
        {
            lifetime.Cancel();
            Retain(task, lifetime);
            return new(BaseActivationHandlerExecutionOutcome.TimedOut, default);
        }
        catch (OperationCanceledException) when (caller.IsCancellationRequested)
        {
            lifetime.Cancel();
            if (task.IsCompleted) { lifetime.Dispose(); _capacity.Release(); }
            else Retain(task, lifetime);
            return new(BaseActivationHandlerExecutionOutcome.Cancelled, default);
        }
        catch
        {
            lifetime.Dispose();
            _capacity.Release();
            return new(BaseActivationHandlerExecutionOutcome.Failed, default);
        }
    }

    private void Retain(Task task, CancellationTokenSource lifetime)
    {
        long id = checked(Interlocked.Increment(ref _sequence));
        _retained[id] = task;
        _ = task.ContinueWith(completed =>
        {
            _ = completed.Exception;
            _retained.TryRemove(id, out _);
            lifetime.Dispose();
            _capacity.Release();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _stopping, 1);
        Task[] retained = _retained.Values.ToArray();
        if (retained.Length == 0) return;
        try { await Task.WhenAll(retained).WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false); }
        catch { }
    }
}
