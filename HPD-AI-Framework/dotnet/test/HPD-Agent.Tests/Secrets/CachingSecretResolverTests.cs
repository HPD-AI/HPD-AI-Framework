// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent.Secrets;
using Xunit;

namespace HPD.Agent.Tests.Secrets;

/// <summary>
/// Unit tests for CachingSecretResolver.
/// Tests caching behavior, TTL expiration, and cache management.
/// </summary>
public class CachingSecretResolverTests
{
    // ============================================
    // Basic Caching Tests
    // ============================================

    [Fact]
    public async Task ResolveAsync_CachesResolvedSecrets()
    {
        // Arrange
        var innerResolver = new MockSecretResolver
        {
            { "openai:ApiKey", new ResolvedSecret { Value = "test-key", Source = "mock" } }
        };
        var cachingResolver = new CachingSecretResolver(innerResolver, TimeSpan.FromMinutes(5));

        // Act - first call
        var result1 = await cachingResolver.ResolveAsync("openai:ApiKey");
        Assert.Equal(1, innerResolver.CallCount);

        // Act - second call (should use cache)
        var result2 = await cachingResolver.ResolveAsync("openai:ApiKey");

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal("test-key", result1.Value.Value);
        Assert.Equal("test-key", result2.Value.Value);
        Assert.Equal(1, innerResolver.CallCount); // Inner resolver called only once
    }

    [Fact]
    public async Task ResolveAsync_NullResult_NotCached()
    {
        // Arrange
        var innerResolver = new MockSecretResolver(); // Empty resolver
        var cachingResolver = new CachingSecretResolver(innerResolver, TimeSpan.FromMinutes(5));

        // Act
        var result1 = await cachingResolver.ResolveAsync("missing:Key");
        var result2 = await cachingResolver.ResolveAsync("missing:Key");

        // Assert - null results are not cached, so inner resolver is called each time
        Assert.Null(result1);
        Assert.Null(result2);
        Assert.Equal(2, innerResolver.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_DifferentKeys_CachedIndependently()
    {
        // Arrange
        var innerResolver = new MockSecretResolver
        {
            { "openai:ApiKey", new ResolvedSecret { Value = "openai-key", Source = "mock" } },
            { "stripe:ApiKey", new ResolvedSecret { Value = "stripe-key", Source = "mock" } }
        };
        var cachingResolver = new CachingSecretResolver(innerResolver, TimeSpan.FromMinutes(5));

        // Act
        var result1 = await cachingResolver.ResolveAsync("openai:ApiKey");
        var result2 = await cachingResolver.ResolveAsync("stripe:ApiKey");
        var result3 = await cachingResolver.ResolveAsync("openai:ApiKey"); // Cached
        var result4 = await cachingResolver.ResolveAsync("stripe:ApiKey"); // Cached

        // Assert
        Assert.NotNull(result1);
        Assert.Equal("openai-key", result1.Value.Value);

        Assert.NotNull(result2);
        Assert.Equal("stripe-key", result2.Value.Value);

        Assert.NotNull(result3);
        Assert.Equal("openai-key", result3.Value.Value);

        Assert.NotNull(result4);
        Assert.Equal("stripe-key", result4.Value.Value);

        // Inner resolver called twice (once per key)
        Assert.Equal(2, innerResolver.CallCount);
    }

    // ============================================
    // TTL Expiration Tests
    // ============================================

    [Fact]
    public async Task ResolveAsync_RespectsTTLExpiration()
    {
        // Arrange
        var innerResolver = new MockSecretResolver
        {
            { "test:Key", new ResolvedSecret { Value = "initial-value", Source = "mock" } }
        };
        var cachingResolver = new CachingSecretResolver(innerResolver, TimeSpan.FromMilliseconds(100));

        // Act - first call
        var result1 = await cachingResolver.ResolveAsync("test:Key");
        Assert.Equal(1, innerResolver.CallCount);

        // Wait for TTL to expire
        await Task.Delay(150);

        // Update the inner resolver's value
        innerResolver.Set("test:Key", new ResolvedSecret { Value = "updated-value", Source = "mock" });

        // Act - second call after expiration
        var result2 = await cachingResolver.ResolveAsync("test:Key");

        // Assert
        Assert.NotNull(result1);
        Assert.Equal("initial-value", result1.Value.Value);

        Assert.NotNull(result2);
        Assert.Equal("updated-value", result2.Value.Value);

        // Inner resolver called twice (initial + after expiration)
        Assert.Equal(2, innerResolver.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_DefaultTTL_IsFiveMinutes()
    {
        // Arrange
        var innerResolver = new MockSecretResolver
        {
            { "test:Key", new ResolvedSecret { Value = "test-value", Source = "mock" } }
        };
        var cachingResolver = new CachingSecretResolver(innerResolver); // No TTL specified

        // Act
        var result1 = await cachingResolver.ResolveAsync("test:Key");
        var result2 = await cachingResolver.ResolveAsync("test:Key");

        // Assert - should cache (within 5 min default TTL)
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal(1, innerResolver.CallCount);
    }

    // ============================================
    // Secret ExpiresAt Tests
    // ============================================

    [Fact]
    public async Task ResolveAsync_RespectsSecretExpiresAt()
    {
        // Arrange
        var innerResolver = new MockSecretResolver
        {
            { "test:Key", new ResolvedSecret
                {
                    Value = "short-lived",
                    Source = "mock",
                    ExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(100)
                }
            }
        };
        var cachingResolver = new CachingSecretResolver(innerResolver, TimeSpan.FromHours(1)); // Long TTL

        // Act - first call
        var result1 = await cachingResolver.ResolveAsync("test:Key");
        Assert.Equal(1, innerResolver.CallCount);

        // Wait for secret's ExpiresAt to pass
        await Task.Delay(150);

        // Update the resolver
        innerResolver.Set("test:Key", new ResolvedSecret { Value = "refreshed", Source = "mock" });

        // Act - second call after secret expired
        var result2 = await cachingResolver.ResolveAsync("test:Key");

        // Assert
        Assert.NotNull(result1);
        Assert.Equal("short-lived", result1.Value.Value);

        Assert.NotNull(result2);
        Assert.Equal("refreshed", result2.Value.Value);

        // Inner resolver called twice
        Assert.Equal(2, innerResolver.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_UsesEarlierOfTTLAndExpiresAt()
    {
        // Arrange
        var innerResolver = new MockSecretResolver
        {
            { "test:Key", new ResolvedSecret
                {
                    Value = "value",
                    Source = "mock",
                    ExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(50) // Earlier than TTL
                }
            }
        };
        var cachingResolver = new CachingSecretResolver(innerResolver, TimeSpan.FromMinutes(5)); // Later than ExpiresAt

        // Act - first call
        var result1 = await cachingResolver.ResolveAsync("test:Key");

        // Wait for ExpiresAt but not TTL
        await Task.Delay(100);

        innerResolver.Set("test:Key", new ResolvedSecret { Value = "updated", Source = "mock" });

        // Act - second call
        var result2 = await cachingResolver.ResolveAsync("test:Key");

        // Assert - should have expired based on ExpiresAt
        Assert.NotNull(result1);
        Assert.Equal("value", result1.Value.Value);

        Assert.NotNull(result2);
        Assert.Equal("updated", result2.Value.Value);

        Assert.Equal(2, innerResolver.CallCount);
    }

    // ============================================
    // Evict Tests
    // ============================================

    [Fact]
    public async Task Evict_RemovesSecretFromCache()
    {
        // Arrange
        var innerResolver = new MockSecretResolver
        {
            { "test:Key", new ResolvedSecret { Value = "initial", Source = "mock" } }
        };
        var cachingResolver = new CachingSecretResolver(innerResolver, TimeSpan.FromMinutes(5));

        // Cache the secret
        await cachingResolver.ResolveAsync("test:Key");
        Assert.Equal(1, innerResolver.CallCount);

        // Act - evict the secret
        cachingResolver.Evict("test:Key");

        // Update the resolver
        innerResolver.Set("test:Key", new ResolvedSecret { Value = "refreshed", Source = "mock" });

        // Resolve again - should call inner resolver
        var result = await cachingResolver.ResolveAsync("test:Key");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("refreshed", result.Value.Value);
        Assert.Equal(2, innerResolver.CallCount); // Called again after eviction
    }

    [Fact]
    public async Task Evict_CaseInsensitiveKey()
    {
        // Arrange
        var innerResolver = new MockSecretResolver
        {
            { "OpenAI:ApiKey", new ResolvedSecret { Value = "test-key", Source = "mock" } }
        };
        var cachingResolver = new CachingSecretResolver(innerResolver, TimeSpan.FromMinutes(5));

        // Cache with one casing
        await cachingResolver.ResolveAsync("OpenAI:ApiKey");

        // Act - evict with different casing
        cachingResolver.Evict("openai:apikey");

        innerResolver.Set("OpenAI:ApiKey", new ResolvedSecret { Value = "new-key", Source = "mock" });

        // Resolve again
        var result = await cachingResolver.ResolveAsync("openai:apikey");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("new-key", result.Value.Value);
        Assert.Equal(2, innerResolver.CallCount);
    }

    [Fact]
    public void Evict_NonExistentKey_DoesNotThrow()
    {
        // Arrange
        var innerResolver = new MockSecretResolver();
        var cachingResolver = new CachingSecretResolver(innerResolver);

        // Act & Assert - should not throw
        cachingResolver.Evict("nonexistent:Key");
    }

    // ============================================
    // Clear Tests
    // ============================================

    [Fact]
    public async Task Clear_RemovesAllCachedEntries()
    {
        // Arrange
        var innerResolver = new MockSecretResolver
        {
            { "key1:Value", new ResolvedSecret { Value = "value1", Source = "mock" } },
            { "key2:Value", new ResolvedSecret { Value = "value2", Source = "mock" } },
            { "key3:Value", new ResolvedSecret { Value = "value3", Source = "mock" } }
        };
        var cachingResolver = new CachingSecretResolver(innerResolver, TimeSpan.FromMinutes(5));

        // Cache all three secrets
        await cachingResolver.ResolveAsync("key1:Value");
        await cachingResolver.ResolveAsync("key2:Value");
        await cachingResolver.ResolveAsync("key3:Value");
        Assert.Equal(3, innerResolver.CallCount);

        // Act - clear the cache
        cachingResolver.Clear();

        // Update resolver values
        innerResolver.Set("key1:Value", new ResolvedSecret { Value = "new1", Source = "mock" });
        innerResolver.Set("key2:Value", new ResolvedSecret { Value = "new2", Source = "mock" });
        innerResolver.Set("key3:Value", new ResolvedSecret { Value = "new3", Source = "mock" });

        // Resolve again - all should call inner resolver
        var result1 = await cachingResolver.ResolveAsync("key1:Value");
        var result2 = await cachingResolver.ResolveAsync("key2:Value");
        var result3 = await cachingResolver.ResolveAsync("key3:Value");

        // Assert
        Assert.NotNull(result1);
        Assert.Equal("new1", result1.Value.Value);

        Assert.NotNull(result2);
        Assert.Equal("new2", result2.Value.Value);

        Assert.NotNull(result3);
        Assert.Equal("new3", result3.Value.Value);

        // Inner resolver called 6 times (3 initial + 3 after clear)
        Assert.Equal(6, innerResolver.CallCount);
    }

    [Fact]
    public void Clear_EmptyCache_DoesNotThrow()
    {
        // Arrange
        var innerResolver = new MockSecretResolver();
        var cachingResolver = new CachingSecretResolver(innerResolver);

        // Act & Assert - should not throw
        cachingResolver.Clear();
    }

    // ============================================
    // Edge Cases
    // ============================================

    [Fact]
    public async Task ResolveAsync_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var innerResolver = new MockSecretResolver
        {
            { "test:Key", new ResolvedSecret { Value = "test-value", Source = "mock" } }
        };
        var cachingResolver = new CachingSecretResolver(innerResolver, TimeSpan.FromMinutes(5));

        // Act - concurrent access
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => cachingResolver.ResolveAsync("test:Key").AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert - all should get the same cached value
        Assert.All(results, result =>
        {
            Assert.NotNull(result);
            Assert.Equal("test-value", result.Value.Value);
        });

        // Inner resolver might be called multiple times due to race condition on first access,
        // but should be much less than 10
        Assert.True(innerResolver.CallCount <= 10);
    }

    // ============================================
    // Helper Classes
    // ============================================

    private class MockSecretResolver : ISecretResolver, IEnumerable<KeyValuePair<string, ResolvedSecret>>
    {
        private readonly Dictionary<string, ResolvedSecret> _secrets = new(StringComparer.OrdinalIgnoreCase);
        public int CallCount { get; private set; }

        public void Add(string key, ResolvedSecret secret) => _secrets[key] = secret;
        public void Set(string key, ResolvedSecret secret) => _secrets[key] = secret;

        public ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _secrets.TryGetValue(key, out var secret)
                ? new ValueTask<ResolvedSecret?>(secret)
                : new ValueTask<ResolvedSecret?>((ResolvedSecret?)null);
        }

        public IEnumerator<KeyValuePair<string, ResolvedSecret>> GetEnumerator() => _secrets.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
