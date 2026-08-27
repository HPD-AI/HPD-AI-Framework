using System.Collections.Concurrent;
using ModelContextProtocol.Protocol;

namespace HPD.Agent.MCP;

/// <summary>Applies SEP-2549 freshness and scope rules to raw MCP catalog pages.</summary>
internal sealed class McpCatalogPageCache(McpCatalogOptions options)
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly string _defaultPrivateScope = Guid.NewGuid().ToString("N");

    internal async ValueTask<T> GetAsync<T>(
        string partition,
        Func<CancellationToken, ValueTask<T>> fetch,
        CancellationToken cancellationToken,
        string? privateScope = null) where T : class, ICacheableResult
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partition);
        ArgumentNullException.ThrowIfNull(fetch);
        var now = DateTimeOffset.UtcNow;
        privateScope ??= _defaultPrivateScope;
        if (TryGet<T>(partition, privateScope, now, freshOnly: true, out var fresh))
            return fresh!;

        var gate = _gates.GetOrAdd(partition, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (TryGet<T>(partition, privateScope, now, freshOnly: true, out fresh))
                return fresh!;
            try
            {
                var value = await fetch(cancellationToken).ConfigureAwait(false);
                var ttl = value.TimeToLive.GetValueOrDefault();
                if (ttl > options.MaximumTtl)
                    ttl = options.MaximumTtl;
                var scope = value.CacheScope == CacheScope.Private ? privateScope : string.Empty;
                _entries[Key(partition, scope)] = new Entry(
                    value,
                    now + (ttl > TimeSpan.Zero ? ttl : TimeSpan.Zero),
                    now + (ttl > TimeSpan.Zero ? ttl : TimeSpan.Zero) + options.StaleRetention);
                return value;
            }
            catch when (!cancellationToken.IsCancellationRequested &&
                options.ServeStaleOnTransientFailure &&
                TryGet<T>(partition, privateScope, DateTimeOffset.UtcNow, freshOnly: false, out var stale))
            {
                return stale!;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    internal void Invalidate(string partitionPrefix)
    {
        foreach (var key in _entries.Keys.Where(key => key.StartsWith(partitionPrefix, StringComparison.Ordinal)))
            _entries.TryRemove(key, out _);
    }

    internal bool TryGetMetadata(
        string partition,
        string privateScope,
        out McpCatalogPageMetadata metadata)
    {
        foreach (var scope in new[] { privateScope, string.Empty })
        {
            if (!_entries.TryGetValue(Key(partition, scope), out var entry))
                continue;
            metadata = new McpCatalogPageMetadata(
                entry.FreshUntil,
                scope.Length == 0 ? CacheScope.Public : CacheScope.Private);
            return true;
        }
        metadata = default;
        return false;
    }

    private bool TryGet<T>(string partition, string privateScope, DateTimeOffset now, bool freshOnly, out T? value)
        where T : class
    {
        foreach (var scope in new[] { privateScope, string.Empty })
        {
            if (!_entries.TryGetValue(Key(partition, scope), out var entry) ||
                (freshOnly ? now > entry.FreshUntil : now > entry.StaleUntil) ||
                entry.Value is not T typed)
                continue;
            value = typed;
            return true;
        }
        value = null;
        return false;
    }

    private static string Key(string partition, string scope) => $"{partition}\n{scope}";

    private sealed record Entry(object Value, DateTimeOffset FreshUntil, DateTimeOffset StaleUntil);
}

internal readonly record struct McpCatalogPageMetadata(
    DateTimeOffset FreshUntil,
    CacheScope Scope);
