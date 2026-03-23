using FluentAssertions;
using HPD.Auth.Authorization.Services;
using Xunit;

namespace HPD.Auth.Authorization.Tests.Services;

[Trait("Category", "Services")]
public class InMemoryRateLimitServiceTests
{
    private static InMemoryRateLimitService CreateService() => new();

    [Fact]
    public async Task First_N_requests_succeed()
    {
        var svc = CreateService();
        const string key = "test-key-1";

        var results = new List<bool>();
        for (var i = 0; i < 3; i++)
            results.Add(await svc.CheckRateLimitAsync(key, maxRequests: 3, window: TimeSpan.FromMinutes(1)));

        results.Should().AllBeEquivalentTo(true);
    }

    [Fact]
    public async Task N_plus_one_request_fails()
    {
        var svc = CreateService();
        const string key = "test-key-2";

        for (var i = 0; i < 3; i++)
            await svc.CheckRateLimitAsync(key, maxRequests: 3, window: TimeSpan.FromMinutes(1));

        var fourth = await svc.CheckRateLimitAsync(key, maxRequests: 3, window: TimeSpan.FromMinutes(1));

        fourth.Should().BeFalse();
    }

    [Fact]
    public async Task Window_expiry_resets_counter()
    {
        var svc = CreateService();
        const string key = "test-key-3";
        var shortWindow = TimeSpan.FromMilliseconds(50);

        // Fill up the bucket
        for (var i = 0; i < 2; i++)
            await svc.CheckRateLimitAsync(key, maxRequests: 2, window: shortWindow);

        // Confirm limit is hit
        var overLimit = await svc.CheckRateLimitAsync(key, maxRequests: 2, window: shortWindow);
        overLimit.Should().BeFalse();

        // Wait for window to expire
        await Task.Delay(100);

        // Should be allowed again after reset
        var afterReset = await svc.CheckRateLimitAsync(key, maxRequests: 2, window: shortWindow);
        afterReset.Should().BeTrue();
    }

    [Fact]
    public async Task Different_keys_are_independent()
    {
        var svc = CreateService();

        // Fill key-A to the limit
        for (var i = 0; i < 3; i++)
            await svc.CheckRateLimitAsync("key-A", maxRequests: 3, window: TimeSpan.FromMinutes(1));

        // key-B should still be allowed
        var resultB = await svc.CheckRateLimitAsync("key-B", maxRequests: 3, window: TimeSpan.FromMinutes(1));

        resultB.Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_requests_thread_safe()
    {
        var svc = CreateService();
        const string key = "concurrent-key";
        const int maxRequests = 10;
        const int totalRequests = 50;

        var tasks = Enumerable.Range(0, totalRequests)
            .Select(_ => svc.CheckRateLimitAsync(key, maxRequests, TimeSpan.FromMinutes(1)))
            .ToList();

        var results = await Task.WhenAll(tasks);

        var successCount = results.Count(r => r);
        successCount.Should().Be(maxRequests);
    }

    [Fact]
    public async Task Counter_increments_atomically()
    {
        var svc = CreateService();
        const string key = "atomic-key";
        const int maxRequests = 5;
        const int totalRequests = 10;

        var tasks = Enumerable.Range(0, totalRequests)
            .Select(_ => svc.CheckRateLimitAsync(key, maxRequests, TimeSpan.FromMinutes(1)))
            .ToList();

        var results = await Task.WhenAll(tasks);

        var successCount = results.Count(r => r);
        successCount.Should().BeLessThanOrEqualTo(maxRequests, "no double-counting under race conditions");
    }

    [Fact]
    public async Task MaxRequests_one_allows_first_then_blocks()
    {
        var svc = CreateService();
        const string key = "single-key";

        var first  = await svc.CheckRateLimitAsync(key, maxRequests: 1, window: TimeSpan.FromMinutes(1));
        var second = await svc.CheckRateLimitAsync(key, maxRequests: 1, window: TimeSpan.FromMinutes(1));

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public async Task Request_exactly_at_window_boundary_is_still_blocked()
    {
        // The implementation uses '>' not '>=' so a request at exactly T+window
        // is NOT reset — it's still within the old window.
        // This test documents that boundary behaviour.
        var svc = CreateService();
        const string key = "boundary-key";
        var window = TimeSpan.FromMilliseconds(80);

        await svc.CheckRateLimitAsync(key, maxRequests: 1, window: window);

        // Wait almost (but not quite) the full window
        await Task.Delay(60);

        var stillBlocked = await svc.CheckRateLimitAsync(key, maxRequests: 1, window: window);
        stillBlocked.Should().BeFalse("window has not yet expired");

        // Wait for the full window to pass
        await Task.Delay(60);

        var nowAllowed = await svc.CheckRateLimitAsync(key, maxRequests: 1, window: window);
        nowAllowed.Should().BeTrue("window has now expired");
    }
}
