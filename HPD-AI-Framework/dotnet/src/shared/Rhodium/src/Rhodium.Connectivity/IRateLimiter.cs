namespace Rhodium.Connectivity;

/// <summary>
/// Rate limiter for outbound exchange requests.
/// Prevents exceeding exchange rate limits.
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Attempt to acquire permits. Returns false if limit exceeded.
    /// </summary>
    /// <param name="permits">Number of permits to acquire</param>
    /// <returns>True if permits acquired, false if rate limited</returns>
    bool TryAcquire(int permits = 1);

    /// <summary>
    /// Wait until permits are available.
    /// </summary>
    /// <param name="permits">Number of permits to acquire</param>
    /// <param name="ct">Cancellation token</param>
    Task WaitAsync(int permits = 1, CancellationToken ct = default);

    /// <summary>
    /// Current number of available permits.
    /// </summary>
    int AvailablePermits { get; }
}

/// <summary>
/// No-op rate limiter (always allows). Used for backtesting.
/// </summary>
public sealed class NoopRateLimiter : IRateLimiter
{
    public static readonly NoopRateLimiter Instance = new();

    public bool TryAcquire(int permits = 1) => true;
    public Task WaitAsync(int permits = 1, CancellationToken ct = default) => Task.CompletedTask;
    public int AvailablePermits => int.MaxValue;
}

/// <summary>
/// Token bucket rate limiter for live trading.
/// </summary>
public sealed class TokenBucketRateLimiter : IRateLimiter
{
    private readonly int _maxTokens;
    private readonly TimeSpan _refillInterval;
    private int _tokens;
    private DateTimeOffset _lastRefill;
    private readonly object _lock = new();

    public TokenBucketRateLimiter(int maxTokens, TimeSpan refillInterval)
    {
        _maxTokens = maxTokens;
        _refillInterval = refillInterval;
        _tokens = maxTokens;
        _lastRefill = DateTimeOffset.UtcNow;
    }

    public bool TryAcquire(int permits = 1)
    {
        lock (_lock)
        {
            Refill();
            if (_tokens >= permits)
            {
                _tokens -= permits;
                return true;
            }
            return false;
        }
    }

    public async Task WaitAsync(int permits = 1, CancellationToken ct = default)
    {
        while (!TryAcquire(permits))
        {
            await Task.Delay(_refillInterval / _maxTokens, ct);
        }
    }

    public int AvailablePermits
    {
        get { lock (_lock) { Refill(); return _tokens; } }
    }

    private void Refill()
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = now - _lastRefill;
        var tokensToAdd = (int)(elapsed / _refillInterval * _maxTokens);
        if (tokensToAdd > 0)
        {
            _tokens = Math.Min(_maxTokens, _tokens + tokensToAdd);
            _lastRefill = now;
        }
    }
}
