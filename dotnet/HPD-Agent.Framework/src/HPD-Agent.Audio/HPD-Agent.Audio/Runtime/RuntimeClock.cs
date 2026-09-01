namespace HPD.Agent.Audio.Runtime;

public sealed class RuntimeClock
{
    private DateTimeOffset _utcNow;

    public RuntimeClock(DateTimeOffset? utcNow = null)
    {
        _utcNow = utcNow ?? DateTimeOffset.UnixEpoch;
    }

    public DateTimeOffset UtcNow => _utcNow;

    public DateTimeOffset Tick(TimeSpan? delta = null)
    {
        _utcNow = _utcNow.Add(delta ?? TimeSpan.FromMilliseconds(1));
        return _utcNow;
    }

    public void Set(DateTimeOffset utcNow) => _utcNow = utcNow;
}
