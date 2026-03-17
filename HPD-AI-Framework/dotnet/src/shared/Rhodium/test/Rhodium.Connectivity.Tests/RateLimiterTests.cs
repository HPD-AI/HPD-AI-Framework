namespace Rhodium.Connectivity.Tests;

public class NoopRateLimiterTests
{
    [Fact]
    public void TryAcquire_AlwaysReturnsTrue()
    {
        var limiter = NoopRateLimiter.Instance;

        Assert.True(limiter.TryAcquire());
        Assert.True(limiter.TryAcquire(100));
        Assert.True(limiter.TryAcquire(int.MaxValue));
    }

    [Fact]
    public void AvailablePermits_ReturnsMaxValue()
    {
        var limiter = NoopRateLimiter.Instance;

        Assert.Equal(int.MaxValue, limiter.AvailablePermits);
    }

    [Fact]
    public async Task WaitAsync_CompletesImmediately()
    {
        var limiter = NoopRateLimiter.Instance;

        var task = limiter.WaitAsync(100);

        Assert.True(task.IsCompleted);
        await task; // Should not throw
    }

    [Fact]
    public void Instance_ReturnsSameInstance()
    {
        var instance1 = NoopRateLimiter.Instance;
        var instance2 = NoopRateLimiter.Instance;

        Assert.Same(instance1, instance2);
    }
}

public class TokenBucketRateLimiterTests
{
    [Fact]
    public void Constructor_InitializesWithMaxTokens()
    {
        var limiter = new TokenBucketRateLimiter(10, TimeSpan.FromSeconds(1));

        Assert.Equal(10, limiter.AvailablePermits);
    }

    [Fact]
    public void TryAcquire_ConsumesTokens()
    {
        var limiter = new TokenBucketRateLimiter(10, TimeSpan.FromSeconds(1));

        Assert.True(limiter.TryAcquire(3));
        Assert.Equal(7, limiter.AvailablePermits);

        Assert.True(limiter.TryAcquire(5));
        Assert.Equal(2, limiter.AvailablePermits);
    }

    [Fact]
    public void TryAcquire_ReturnsFalseWhenInsufficientTokens()
    {
        var limiter = new TokenBucketRateLimiter(5, TimeSpan.FromSeconds(1));

        Assert.True(limiter.TryAcquire(5));
        Assert.False(limiter.TryAcquire(1));
        Assert.Equal(0, limiter.AvailablePermits);
    }

    [Fact]
    public void TryAcquire_DefaultsToOnePermit()
    {
        var limiter = new TokenBucketRateLimiter(10, TimeSpan.FromSeconds(1));

        Assert.True(limiter.TryAcquire());
        Assert.Equal(9, limiter.AvailablePermits);
    }

    [Fact]
    public void TryAcquire_ReturnsFalseWhenRequestExceedsAvailable()
    {
        var limiter = new TokenBucketRateLimiter(5, TimeSpan.FromSeconds(1));

        Assert.False(limiter.TryAcquire(10));
        Assert.Equal(5, limiter.AvailablePermits); // Tokens not consumed on failure
    }

    [Fact]
    public async Task WaitAsync_WaitsUntilTokensAvailable()
    {
        // Use a very short refill interval for testing
        var limiter = new TokenBucketRateLimiter(10, TimeSpan.FromMilliseconds(100));

        // Consume all tokens
        Assert.True(limiter.TryAcquire(10));
        Assert.Equal(0, limiter.AvailablePermits);

        // Wait should complete after refill
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await limiter.WaitAsync(1, cts.Token);

        // Should have acquired 1 token
        Assert.True(limiter.AvailablePermits >= 0);
    }

    [Fact]
    public async Task WaitAsync_CanBeCancelled()
    {
        var limiter = new TokenBucketRateLimiter(1, TimeSpan.FromHours(1)); // Very slow refill

        // Consume all tokens
        Assert.True(limiter.TryAcquire(1));

        // Try to acquire more - should timeout
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            limiter.WaitAsync(1, cts.Token));
    }

    [Fact]
    public async Task Refill_AddsTokensOverTime()
    {
        var limiter = new TokenBucketRateLimiter(10, TimeSpan.FromMilliseconds(50));

        // Consume all tokens
        Assert.True(limiter.TryAcquire(10));
        Assert.Equal(0, limiter.AvailablePermits);

        // Wait for refill
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        // Should have some tokens now
        Assert.True(limiter.AvailablePermits > 0);
    }

    [Fact]
    public async Task Refill_DoesNotExceedMaxTokens()
    {
        var limiter = new TokenBucketRateLimiter(10, TimeSpan.FromMilliseconds(10));

        // Wait for multiple potential refill cycles
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        // Should not exceed max
        Assert.True(limiter.AvailablePermits <= 10);
    }
}
