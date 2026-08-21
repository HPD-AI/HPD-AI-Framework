using System.Collections.Concurrent;

namespace HPD.Base;

internal enum BaseActivationProviderCallOutcome
{
    Completed,
    TimedOut,
    Cancelled,
    Failed,
    Capacity,
}

internal sealed record BaseActivationProviderCallResult<T>(
    BaseActivationProviderCallOutcome Outcome,
    T? Value);

internal sealed class BaseActivationProviderExecutionGate : IAsyncDisposable
{
    private readonly BaseActivationOperationalState _state;
    private readonly SemaphoreSlim _capacity = new(32, 32);
    private readonly ConcurrentDictionary<long, Task> _retained = new();
    private long _sequence;
    private int _stopping;

    internal BaseActivationProviderExecutionGate(BaseActivationOperationalState? state = null) =>
        _state = state ?? new BaseActivationOperationalState();

    internal int RetainedCount => _retained.Count;

    internal async ValueTask<BaseActivationProviderCallResult<T>> ExecuteAsync<T>(
        Func<CancellationToken, ValueTask<T>> execute,
        TimeSpan acquisitionTimeout,
        TimeSpan operationTimeout,
        CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(execute);
        if (Volatile.Read(ref _stopping) != 0)
            return new(BaseActivationProviderCallOutcome.Capacity, default);
        try
        {
            if (!await _capacity.WaitAsync(acquisitionTimeout, caller).ConfigureAwait(false))
                return new(BaseActivationProviderCallOutcome.Capacity, default);
        }
        catch (OperationCanceledException)
        {
            return new(BaseActivationProviderCallOutcome.Cancelled, default);
        }

        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(caller);
        _state.Enter();
        Task<T> task;
        try { task = execute(lifetime.Token).AsTask(); }
        catch
        {
            lifetime.Dispose();
            _capacity.Release();
            _state.Complete();
            return new(BaseActivationProviderCallOutcome.Failed, default);
        }
        try
        {
            T value = await task.WaitAsync(operationTimeout, caller).ConfigureAwait(false);
            lifetime.Dispose();
            _capacity.Release();
            _state.Complete();
            return new(BaseActivationProviderCallOutcome.Completed, value);
        }
        catch (TimeoutException)
        {
            lifetime.Cancel();
            _state.Quarantine();
            Retain(task, lifetime);
            return new(BaseActivationProviderCallOutcome.TimedOut, default);
        }
        catch (OperationCanceledException) when (caller.IsCancellationRequested)
        {
            lifetime.Cancel();
            if (task.IsCompleted) { lifetime.Dispose(); _capacity.Release(); _state.Complete(); }
            else { _state.Quarantine(); Retain(task, lifetime); }
            return new(BaseActivationProviderCallOutcome.Cancelled, default);
        }
        catch
        {
            lifetime.Dispose();
            _capacity.Release();
            _state.Complete();
            return new(BaseActivationProviderCallOutcome.Failed, default);
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
            _state.ReleaseQuarantine();
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
