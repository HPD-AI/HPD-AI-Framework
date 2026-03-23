using FluentAssertions;
using HPDAgent.Graph.Abstractions.Caching;
using HPDAgent.Graph.Core.Caching;
using Xunit;

namespace HPDAgent.Graph.Tests.Caching;

public class CacheTtlTests
{
    [Fact]
    public async Task CachedResult_WithoutTtl_NeverExpires()
    {
        var cached = new CachedNodeResult
        {
            Outputs = new Dictionary<string, object> { ["result"] = 42 },
            CachedAt = DateTimeOffset.UtcNow.AddDays(-365), // 1 year ago
            Ttl = null
        };

        cached.IsExpired.Should().BeFalse();
    }

    [Fact]
    public async Task CachedResult_WithinTtl_NotExpired()
    {
        var cached = new CachedNodeResult
        {
            Outputs = new Dictionary<string, object> { ["result"] = 42 },
            CachedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Ttl = TimeSpan.FromMinutes(10)
        };

        cached.IsExpired.Should().BeFalse();
    }

    [Fact]
    public async Task CachedResult_BeyondTtl_IsExpired()
    {
        var cached = new CachedNodeResult
        {
            Outputs = new Dictionary<string, object> { ["result"] = 42 },
            CachedAt = DateTimeOffset.UtcNow.AddMinutes(-15),
            Ttl = TimeSpan.FromMinutes(10)
        };

        cached.IsExpired.Should().BeTrue();
    }

    [Fact]
    public async Task InMemoryCacheStore_GetExpiredEntry_ReturnsNull()
    {
        var store = new InMemoryNodeCacheStore();

        var cached = new CachedNodeResult
        {
            Outputs = new Dictionary<string, object> { ["result"] = 42 },
            CachedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
            Ttl = TimeSpan.FromSeconds(1) // Expires after 1 second
        };

        await store.SetAsync("fingerprint1", cached);

        // Wait for expiration
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        var result = await store.GetAsync("fingerprint1");

        result.Should().BeNull();
    }

    [Fact]
    public async Task InMemoryCacheStore_GetValidEntry_ReturnsResult()
    {
        var store = new InMemoryNodeCacheStore();

        var cached = new CachedNodeResult
        {
            Outputs = new Dictionary<string, object> { ["result"] = 42 },
            CachedAt = DateTimeOffset.UtcNow,
            Ttl = TimeSpan.FromMinutes(10)
        };

        await store.SetAsync("fingerprint1", cached);

        var result = await store.GetAsync("fingerprint1");

        result.Should().NotBeNull();
        result!.Outputs["result"].Should().Be(42);
    }

    [Fact]
    public async Task InMemoryCacheStore_ExpiredEntryRemoved_NotInCache()
    {
        var store = new InMemoryNodeCacheStore();

        var cached = new CachedNodeResult
        {
            Outputs = new Dictionary<string, object> { ["result"] = 42 },
            CachedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
            Ttl = TimeSpan.FromSeconds(1)
        };

        await store.SetAsync("fingerprint1", cached);
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        // First access removes it
        var firstAccess = await store.GetAsync("fingerprint1");
        firstAccess.Should().BeNull();

        // Second access should also return null
        var secondAccess = await store.GetAsync("fingerprint1");
        secondAccess.Should().BeNull();

        // Verify it's not in the cache
        var exists = await store.ExistsAsync("fingerprint1");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task CacheOptions_DefaultValues_AreCorrect()
    {
        var options = new CacheOptions();

        options.Strategy.Should().Be(CacheKeyStrategy.InputsAndCode);
        options.Ttl.Should().BeNull();
        options.Invalidation.Should().Be(CacheInvalidation.OnCodeChange);
    }

    [Fact]
    public async Task CacheOptions_CustomValues_AreApplied()
    {
        var options = new CacheOptions
        {
            Strategy = CacheKeyStrategy.Inputs,
            Ttl = TimeSpan.FromHours(24),
            Invalidation = CacheInvalidation.Never
        };

        options.Strategy.Should().Be(CacheKeyStrategy.Inputs);
        options.Ttl.Should().Be(TimeSpan.FromHours(24));
        options.Invalidation.Should().Be(CacheInvalidation.Never);
    }

    [Fact]
    public async Task CacheKeyStrategy_AllValuesExist()
    {
        // Ensure all enum values are defined
        var strategies = Enum.GetValues<CacheKeyStrategy>();
        strategies.Should().Contain(CacheKeyStrategy.Inputs);
        strategies.Should().Contain(CacheKeyStrategy.InputsAndCode);
        strategies.Should().Contain(CacheKeyStrategy.InputsCodeAndConfig);
    }

    [Fact]
    public async Task CacheInvalidation_AllValuesExist()
    {
        // Ensure all enum values are defined
        var invalidations = Enum.GetValues<CacheInvalidation>();
        invalidations.Should().Contain(CacheInvalidation.Never);
        invalidations.Should().Contain(CacheInvalidation.OnCodeChange);
        invalidations.Should().Contain(CacheInvalidation.OnConfigChange);
        invalidations.Should().Contain(CacheInvalidation.OnInputChange);
    }

    [Fact]
    public async Task InMemoryCacheStore_MultipleEntriesWithDifferentTtl_WorksCorrectly()
    {
        var store = new InMemoryNodeCacheStore();

        // Entry with short TTL
        var shortTtl = new CachedNodeResult
        {
            Outputs = new Dictionary<string, object> { ["result"] = 1 },
            CachedAt = DateTimeOffset.UtcNow,
            Ttl = TimeSpan.FromSeconds(1)
        };

        // Entry with long TTL
        var longTtl = new CachedNodeResult
        {
            Outputs = new Dictionary<string, object> { ["result"] = 2 },
            CachedAt = DateTimeOffset.UtcNow,
            Ttl = TimeSpan.FromMinutes(10)
        };

        // Entry with no TTL
        var noTtl = new CachedNodeResult
        {
            Outputs = new Dictionary<string, object> { ["result"] = 3 },
            CachedAt = DateTimeOffset.UtcNow,
            Ttl = null
        };

        await store.SetAsync("short", shortTtl);
        await store.SetAsync("long", longTtl);
        await store.SetAsync("none", noTtl);

        // Wait for short TTL to expire
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        var shortResult = await store.GetAsync("short");
        var longResult = await store.GetAsync("long");
        var noneResult = await store.GetAsync("none");

        shortResult.Should().BeNull();
        longResult.Should().NotBeNull();
        noneResult.Should().NotBeNull();
    }
}
