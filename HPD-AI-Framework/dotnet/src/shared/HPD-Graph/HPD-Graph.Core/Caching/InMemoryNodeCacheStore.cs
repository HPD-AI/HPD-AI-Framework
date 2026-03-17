using HPDAgent.Graph.Abstractions.Caching;

namespace HPDAgent.Graph.Core.Caching;

/// <summary>
/// In-memory implementation of node cache store.
/// Fast L1 cache - use for development and testing.
/// Production should use multi-tier caching (Memory → SQLite → Redis → S3).
/// </summary>
public class InMemoryNodeCacheStore : INodeCacheStore
{
    private readonly Dictionary<string, CachedNodeResult> _cache = new();
    private readonly object _lock = new();

    public Task<CachedNodeResult?> GetAsync(string fingerprint, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(fingerprint, out var result))
            {
                // Check expiration
                if (result.IsExpired)
                {
                    // Remove expired entry
                    _cache.Remove(fingerprint);
                    return Task.FromResult<CachedNodeResult?>(null);
                }

                return Task.FromResult<CachedNodeResult?>(result);
            }

            return Task.FromResult<CachedNodeResult?>(null);
        }
    }

    public Task SetAsync(string fingerprint, CachedNodeResult result, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _cache[fingerprint] = result;
        }
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string fingerprint, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_cache.ContainsKey(fingerprint));
        }
    }

    public Task DeleteAsync(string fingerprint, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _cache.Remove(fingerprint);
        }
        return Task.CompletedTask;
    }

    public Task ClearAllAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            _cache.Clear();
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Get cache statistics.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        lock (_lock)
        {
            return new CacheStatistics
            {
                TotalEntries = _cache.Count,
                TotalSizeBytes = EstimateSize()
            };
        }
    }

    private long EstimateSize()
    {
        // Rough estimate: each fingerprint ~64 bytes + result data
        return _cache.Count * 1024; // Assume 1KB per cached result on average
    }
}

/// <summary>
/// Cache statistics.
/// </summary>
public sealed record CacheStatistics
{
    public int TotalEntries { get; init; }
    public long TotalSizeBytes { get; init; }
}
