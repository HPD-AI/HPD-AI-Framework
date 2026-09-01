using ModelContextProtocol.Protocol;
using HPD.Agent.MCP;

namespace HPD.Agent.Tests.MCPServer;

public sealed class McpCatalogPageCacheTests
{
    [Fact]
    public async Task FreshPageCoalescesAndInvalidationRefetches()
    {
        var cache = new McpCatalogPageCache(new McpCatalogOptions
        {
            MaximumTtl = TimeSpan.FromHours(1)
        });
        var calls = 0;

        ValueTask<ListToolsResult> Fetch(CancellationToken _) => ValueTask.FromResult(new ListToolsResult
        {
            Tools = [],
            TimeToLive = TimeSpan.FromMinutes(10),
            CacheScope = CacheScope.Public
        }.Tap(_ => Interlocked.Increment(ref calls)));

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => cache.GetAsync("server\ntools\n", Fetch, default).AsTask()));
        Assert.Equal(1, calls);

        cache.Invalidate("server\ntools\n");
        await cache.GetAsync("server\ntools\n", Fetch, default);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ExpiredPageServesStaleOnlyAfterTransientFailure()
    {
        var cache = new McpCatalogPageCache(new McpCatalogOptions
        {
            StaleRetention = TimeSpan.FromMinutes(1),
            ServeStaleOnTransientFailure = true
        });
        var page = new ListToolsResult
        {
            Tools = [],
            TimeToLive = TimeSpan.Zero,
            CacheScope = CacheScope.Private
        };
        await cache.GetAsync("server\ntools\n", _ => ValueTask.FromResult(page), default);

        var stale = await cache.GetAsync<ListToolsResult>(
            "server\ntools\n",
            _ => ValueTask.FromException<ListToolsResult>(new IOException("transient")),
            default);

        Assert.Same(page, stale);
    }
}

internal static class McpCatalogPageCacheTestExtensions
{
    internal static T Tap<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
