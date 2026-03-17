using System.Collections.Concurrent;

namespace HPD.Agent.Secrets;

/// <summary>
/// Caches resolved secrets with a configurable TTL.
/// Wraps any ISecretResolver to avoid repeated network calls.
///
/// Not included in the default chain — built-in resolvers (env vars, config)
/// are all local and fast. This is for user-added vault resolvers:
///
///   builder.AddSecretResolver(new CachingSecretResolver(
///       new AzureKeyVaultSecretResolver(vaultUri),
///       ttl: TimeSpan.FromMinutes(5)));
///
/// Cache entries expire based on the earlier of:
///   - The configured TTL
///   - The secret's ExpiresAt (if set by the inner resolver)
/// </summary>
public sealed class CachingSecretResolver : ISecretResolver
{
    private readonly ISecretResolver _inner;
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public CachingSecretResolver(ISecretResolver inner, TimeSpan? ttl = null)
    {
        _inner = inner;
        _ttl = ttl ?? TimeSpan.FromMinutes(5);
    }

    public async ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var cached) && !cached.IsExpired)
            return cached.Secret;

        var result = await _inner.ResolveAsync(key, ct);

        if (result.HasValue)
        {
            var expiresAt = result.Value.ExpiresAt.HasValue
                ? Min(DateTimeOffset.UtcNow + _ttl, result.Value.ExpiresAt.Value)
                : DateTimeOffset.UtcNow + _ttl;

            _cache[key] = new CacheEntry(result.Value, expiresAt);
        }

        return result;
    }

    /// <summary>Evicts a cached secret (e.g., after a 401 response).</summary>
    public void Evict(string key) => _cache.TryRemove(key, out _);

    /// <summary>Clears all cached secrets.</summary>
    public void Clear() => _cache.Clear();

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private readonly record struct CacheEntry(ResolvedSecret Secret, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }
}
