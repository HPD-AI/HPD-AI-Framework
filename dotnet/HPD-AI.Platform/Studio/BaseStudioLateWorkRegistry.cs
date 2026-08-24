namespace HPD.AI.Platform.Studio;

/// <summary>Bounds and observes provider work that may outlive its Studio request deadline.</summary>
public sealed class BaseStudioLateWorkRegistry
{
    private const int MaximumOutstanding = 32;
    private int _outstanding;
    /// <summary>Gets the current retained operation count for health projection.</summary>
    public int OutstandingCount => Volatile.Read(ref _outstanding);
    /// <summary>Attempts to reserve one bounded producer-operation slot.</summary>
    public bool TryEnter(out BaseStudioLateWorkLease lease)
    {
        while (true)
        {
            int current = Volatile.Read(ref _outstanding);
            if (current >= MaximumOutstanding) { lease = null!; return false; }
            if (Interlocked.CompareExchange(ref _outstanding, current + 1, current) == current)
            { lease = new(this); return true; }
        }
    }
    internal void Release() => Interlocked.Decrement(ref _outstanding);
}

/// <summary>Owns one producer slot and can transfer it to retained late work.</summary>
public sealed class BaseStudioLateWorkLease : IDisposable
{
    private BaseStudioLateWorkRegistry? _owner;
    internal BaseStudioLateWorkLease(BaseStudioLateWorkRegistry owner) => _owner = owner;
    /// <summary>Retains and observes timed-out work until it actually terminates.</summary>
    public void Retain(Task work)
    {
        ArgumentNullException.ThrowIfNull(work);
        BaseStudioLateWorkRegistry? owner = Interlocked.Exchange(ref _owner, null);
        if (owner is null) throw new InvalidOperationException("Studio late-work ownership was already consumed.");
        _ = work.ContinueWith(static (completed, state) =>
        {
            _ = completed.Exception;
            ((BaseStudioLateWorkRegistry)state!).Release();
        }, owner, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    /// <inheritdoc />
    public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
}

