using System.Collections.Concurrent;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public sealed record DebugAdapterAvailabilityCacheKey(
    string AdapterId,
    string PackageId,
    string EnvironmentId,
    long EnvironmentRevision,
    string TargetPlatform,
    string CanonicalWorkspaceRoot,
    string ProjectMarkerFingerprint,
    long LaunchPolicyRevision,
    string TrustPolicyRevision,
    long EndpointCatalogRevision);

public interface IDebugAdapterAvailabilityCache
{
    ValueTask<DebugAdapterAvailability> GetOrProbeAsync(
        DebugAdapterAvailabilityCacheKey key,
        Func<CancellationToken, ValueTask<DebugAdapterAvailability>> probe,
        CancellationToken cancellationToken = default);

    void InvalidateEnvironment(string environmentId);
    void InvalidateEndpointCatalog(long currentRevision);
}

public sealed class DebugAdapterAvailabilityCache : IDebugAdapterAvailabilityCache
{
    private readonly ConcurrentDictionary<DebugAdapterAvailabilityCacheKey, CacheEntry> _entries = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _positiveTtl;
    private readonly TimeSpan _negativeTtl;

    public DebugAdapterAvailabilityCache(
        TimeProvider? timeProvider = null,
        TimeSpan? positiveTtl = null,
        TimeSpan? negativeTtl = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _positiveTtl = positiveTtl ?? TimeSpan.FromSeconds(30);
        _negativeTtl = negativeTtl ?? TimeSpan.FromSeconds(5);
    }

    public async ValueTask<DebugAdapterAvailability> GetOrProbeAsync(
        DebugAdapterAvailabilityCacheKey key,
        Func<CancellationToken, ValueTask<DebugAdapterAvailability>> probe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(probe);

        while (true)
        {
            var now = _timeProvider.GetUtcNow();
            if (_entries.TryGetValue(key, out var current) && current.ExpiresAt > now)
                return await current.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (current is not null)
                _entries.TryRemove(new KeyValuePair<DebugAdapterAvailabilityCacheKey, CacheEntry>(key, current));

            var created = new CacheEntry(() => ProbeAndClassifyAsync(key, probe), DateTimeOffset.MaxValue);
            if (!_entries.TryAdd(key, created))
                continue;
            try
            {
                return await created.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (created.Value.IsFaulted || created.Value.IsCanceled)
                    _entries.TryRemove(new KeyValuePair<DebugAdapterAvailabilityCacheKey, CacheEntry>(key, created));
                throw;
            }
        }
    }

    public void InvalidateEnvironment(string environmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        foreach (var pair in _entries)
            if (pair.Key.EnvironmentId.Equals(environmentId, StringComparison.Ordinal))
                _entries.TryRemove(pair);
    }

    public void InvalidateEndpointCatalog(long currentRevision)
    {
        foreach (var pair in _entries)
            if (pair.Key.EndpointCatalogRevision != currentRevision)
                _entries.TryRemove(pair);
    }

    private async Task<DebugAdapterAvailability> ProbeAndClassifyAsync(
        DebugAdapterAvailabilityCacheKey key,
        Func<CancellationToken, ValueTask<DebugAdapterAvailability>> probe)
    {
        try
        {
            var result = await probe(CancellationToken.None).ConfigureAwait(false);
            var ttl = result.Kind == DebugAdapterAvailabilityKind.Available ? _positiveTtl : _negativeTtl;
            if (_entries.TryGetValue(key, out var entry))
                entry.ExpiresAt = _timeProvider.GetUtcNow() + ttl;
            return result;
        }
        catch
        {
            _entries.TryRemove(key, out _);
            throw;
        }
    }

    private sealed class CacheEntry(Func<Task<DebugAdapterAvailability>> valueFactory, DateTimeOffset expiresAt)
    {
        private readonly Lazy<Task<DebugAdapterAvailability>> _value = new(valueFactory, LazyThreadSafetyMode.ExecutionAndPublication);
        public Task<DebugAdapterAvailability> Value => _value.Value;
        public DateTimeOffset ExpiresAt { get; set; } = expiresAt;
    }
}
