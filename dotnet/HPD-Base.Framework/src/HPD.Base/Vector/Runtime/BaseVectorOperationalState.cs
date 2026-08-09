namespace HPD.Base;

internal sealed class BaseVectorOperationalState
{
    private long _active;
    private long _quarantined;

    public long Active => Interlocked.Read(ref _active);
    public long Quarantined => Interlocked.Read(ref _quarantined);
    public void Enter() => Interlocked.Increment(ref _active);
    public void Exit() => Interlocked.Decrement(ref _active);
    public void Quarantine() { Interlocked.Decrement(ref _active); Interlocked.Increment(ref _quarantined); }
    public void ReleaseQuarantine() => Interlocked.Decrement(ref _quarantined);
}
