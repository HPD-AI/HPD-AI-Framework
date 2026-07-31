namespace HPD.Base.AspNetCore;

internal sealed class BaseRealtimeJoinRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);
    private readonly TimeProvider _timeProvider;
    private readonly int _maximum;
    private long _windowStartedAt;
    private int _attempts;

    public BaseRealtimeJoinRateLimiter(TimeProvider timeProvider, int maximum)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);

        _timeProvider = timeProvider;
        _maximum = maximum;
        _windowStartedAt = timeProvider.GetTimestamp();
    }

    public bool TryAcquire()
    {
        var now = _timeProvider.GetTimestamp();
        if (_timeProvider.GetElapsedTime(_windowStartedAt, now) >= Window)
        {
            _windowStartedAt = now;
            _attempts = 0;
        }

        if (_attempts >= _maximum)
            return false;

        _attempts++;
        return true;
    }
}
