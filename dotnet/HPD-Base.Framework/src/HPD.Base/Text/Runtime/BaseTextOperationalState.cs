namespace HPD.Base;

internal sealed class BaseTextOperationalState : IAsyncDisposable
{
    private readonly SemaphoreSlim _slots = new(8, 8);
    private long _active;
    private long _quarantined;
    internal long Active => Interlocked.Read(ref _active);
    internal long Quarantined => Interlocked.Read(ref _quarantined);
    internal void Enter() => Interlocked.Increment(ref _active);
    internal void Exit() => Interlocked.Decrement(ref _active);
    internal void Quarantine() { Interlocked.Decrement(ref _active); Interlocked.Increment(ref _quarantined); }
    internal void ReleaseQuarantine() => Interlocked.Decrement(ref _quarantined);

    internal async ValueTask<T> InvokeAsync<T>(Func<CancellationToken, ValueTask<T>> invoke, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!await _slots.WaitAsync(timeout, cancellationToken).ConfigureAwait(false)) throw new TimeoutException();
        Enter(); var lifetime = new CancellationTokenSource(timeout); bool release = true; Task<T>? work = null;
        try { work = invoke(lifetime.Token).AsTask(); return await work.WaitAsync(timeout, cancellationToken).ConfigureAwait(false); }
        catch when (work is { IsCompleted: false }) { release = false; Quarantine(); _ = ReleaseQuarantinedAsync(work, lifetime); throw; }
        finally { if (release) { lifetime.Dispose(); Exit(); _slots.Release(); } }
    }

    private async Task ReleaseQuarantinedAsync<T>(Task<T> work, CancellationTokenSource lifetime)
    {
        try { T completed = await work.ConfigureAwait(false); if (completed is IAsyncDisposable disposable) await disposable.DisposeAsync().ConfigureAwait(false); }
        catch { }
        finally { lifetime.Dispose(); ReleaseQuarantine(); _slots.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { while (Active + Quarantined > 0) await Task.Delay(TimeSpan.FromMilliseconds(10), drain.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (drain.IsCancellationRequested) { }
        if (Active + Quarantined == 0) _slots.Dispose();
    }
}
