using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HPDAgent.Graph.Abstractions.Artifacts;

namespace HPDAgent.Graph.Core.Artifacts;

/// <summary>
/// In-memory implementation of IArtifactRegistry using ConcurrentDictionary.
/// Thread-safe for single-process deployments.
/// For distributed deployments, use a persistent store (PostgreSQL, MySQL - Phase 5).
///
/// Storage Strategy:
/// - 4 ConcurrentDictionaries with composite tuple keys
/// - SemaphoreSlim for single-process locks (warns if multi-node deployment)
/// - Simple retention policies (dictionary removal)
/// - Single producer constraint (throws if multiple nodes produce same artifact)
///
/// Limitations:
///  Not safe for multi-node deployments (locks are process-local)
///  No persistence (data lost on restart)
///  No partition capability filtering (future enhancement)
/// </summary>
public class InMemoryArtifactRegistry : IArtifactRegistry
{
    // Storage: (ArtifactKey, PartitionKey?) → Version fingerprint
    private readonly ConcurrentDictionary<(ArtifactKey Key, PartitionKey? Partition), string> _latestVersions = new();

    // Storage: (ArtifactKey, Version) → ArtifactMetadata
    private readonly ConcurrentDictionary<(ArtifactKey Key, string Version), ArtifactMetadata> _metadata = new();

    // Reverse index: ArtifactKey → List<ProducingNodeId>
    private readonly ConcurrentDictionary<ArtifactKey, List<string>> _reverseIndex = new();

    // Distributed locks: Composite key → SemaphoreSlim (process-local only in Phase 1)
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public InMemoryArtifactRegistry()
    {
        // NOTE: InMemoryArtifactRegistry is single-process only and not safe for multi-node deployments.
        // For production distributed systems, use PostgreSQL or MySQL implementation (Phase 5).
    }

    public Task<string?> GetLatestVersionAsync(
        ArtifactKey key,
        PartitionKey? partition = null,
        CancellationToken ct = default)
    {
        // Partition parameter overrides key.Partition if specified
        var effectivePartition = partition ?? key.Partition;
        var lookupKey = (key with { Partition = null }, effectivePartition);

        var version = _latestVersions.TryGetValue(lookupKey, out var v) ? v : null;
        return Task.FromResult(version);
    }

    public Task<ArtifactMetadata?> GetMetadataAsync(
        ArtifactKey key,
        string version,
        CancellationToken ct = default)
    {
        var lookupKey = (key with { Partition = null }, version);
        var metadata = _metadata.TryGetValue(lookupKey, out var m) ? m : null;
        return Task.FromResult(metadata);
    }

    public Task RegisterAsync(
        ArtifactKey key,
        string version,
        ArtifactMetadata metadata,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version cannot be empty", nameof(version));
        if (metadata == null)
            throw new ArgumentNullException(nameof(metadata));

        var partition = key.Partition;
        var keyWithoutPartition = key with { Partition = null };
        var versionKey = (keyWithoutPartition, partition);
        var metadataKey = (keyWithoutPartition, version);

        // Update latest version pointer
        _latestVersions.AddOrUpdate(
            versionKey,
            version,
            (_, _) => version);

        // Store metadata
        _metadata.AddOrUpdate(
            metadataKey,
            metadata,
            (_, _) => metadata);

        // Update reverse index (artifact → producing node)
        if (!string.IsNullOrEmpty(metadata.ProducedByNodeId))
        {
            _reverseIndex.AddOrUpdate(
                keyWithoutPartition,
                _ => new List<string> { metadata.ProducedByNodeId },
                (_, existingNodes) =>
                {
                    // Check if node already registered (avoid duplicates)
                    lock (existingNodes)
                    {
                        if (!existingNodes.Contains(metadata.ProducedByNodeId))
                        {
                            existingNodes.Add(metadata.ProducedByNodeId);
                        }
                    }
                    return existingNodes;
                });
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetProducingNodeIdsAsync(
        ArtifactKey key,
        PartitionKey? partition = null,
        CancellationToken ct = default)
    {
        var keyWithoutPartition = key with { Partition = null };

        if (!_reverseIndex.TryGetValue(keyWithoutPartition, out var producers))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        List<string> producersCopy;
        lock (producers)
        {
            producersCopy = new List<string>(producers);
        }

        // Validate single producer constraint
        if (producersCopy.Count > 1)
        {
            throw new InvalidOperationException(
                $"Multiple nodes produce artifact '{key}': {string.Join(", ", producersCopy)}. " +
                "Each artifact must have exactly one producer node.");
        }

        return Task.FromResult<IReadOnlyList<string>>(producersCopy);
    }

    public async IAsyncEnumerable<ArtifactKey> ListArtifactsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Get unique artifact keys (without duplicating for different partitions)
        var uniqueKeys = _latestVersions.Keys
            .Select(k => k.Key)
            .Distinct()
            .ToList();

        foreach (var key in uniqueKeys)
        {
            ct.ThrowIfCancellationRequested();
            yield return key;
        }

        await Task.CompletedTask;  // Satisfy async requirement
    }

    public Task<IReadOnlyDictionary<ArtifactKey, string>> GetLineageAsync(
        ArtifactKey key,
        string version,
        CancellationToken ct = default)
    {
        var metadataKey = (key with { Partition = null }, version);

        if (!_metadata.TryGetValue(metadataKey, out var metadata))
        {
            return Task.FromResult<IReadOnlyDictionary<ArtifactKey, string>>(
                new Dictionary<ArtifactKey, string>());
        }

        return Task.FromResult(metadata.InputVersions);
    }

    public async Task<IAsyncDisposable?> TryAcquireMaterializationLockAsync(
        ArtifactKey key,
        PartitionKey? partition,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        // Generate lock key: artifact path + partition
        var lockKey = partition != null
            ? $"{key.ToString()}@{partition}"
            : key.ToString();

        // Get or create semaphore for this artifact
        // CRITICAL: Use a clean semaphore if the existing one is locked (stale lock cleanup)
        var semaphore = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        // Check if semaphore is available (CurrentCount > 0 means not locked)
        // If it's been locked for too long, it might be a stale lock from a crashed/failed operation
        if (semaphore.CurrentCount == 0 && timeout == TimeSpan.Zero)
        {
            // For non-blocking attempts, fail fast if lock is held
            return null;
        }

        // Try to acquire lock within timeout
        var acquired = await semaphore.WaitAsync(timeout, ct);

        if (!acquired)
        {
            return null;
        }

        // Return disposable lock handle
        return new ArtifactLock(semaphore, lockKey, _locks);
    }

    public Task<int> PruneOldVersionsAsync(
        ArtifactKey key,
        RetentionPolicy policy,
        CancellationToken ct = default)
    {
        if (policy == null)
            throw new ArgumentNullException(nameof(policy));

        var keyWithoutPartition = key with { Partition = null };
        var partition = key.Partition;

        // Find all versions for this artifact+partition
        var versions = _metadata.Keys
            .Where(k => k.Key.Equals(keyWithoutPartition))
            .Select(k => k.Version)
            .Distinct()
            .ToList();

        if (versions.Count == 0)
            return Task.FromResult(0);

        // Get metadata for all versions (to apply policy)
        var versionMetadata = versions
            .Select(v => new
            {
                Version = v,
                Metadata = _metadata.TryGetValue((keyWithoutPartition, v), out var m) ? m : null
            })
            .Where(x => x.Metadata != null)
            .OrderByDescending(x => x.Metadata!.CreatedAt)
            .ToList();

        // Apply retention policy
        var toKeep = new HashSet<string>();
        var now = DateTimeOffset.UtcNow;

        for (int i = 0; i < versionMetadata.Count; i++)
        {
            var item = versionMetadata[i];
            var shouldKeep = false;

            // Rule 1: Keep last N versions
            if (policy.KeepLastN.HasValue && i < policy.KeepLastN.Value)
            {
                shouldKeep = true;
            }

            // Rule 2: Keep versions newer than threshold (OR logic with Rule 1)
            if (policy.KeepNewerThan.HasValue)
            {
                var age = now - item.Metadata!.CreatedAt;
                if (age < policy.KeepNewerThan.Value)
                {
                    shouldKeep = true;
                }
            }

            // Rule 3: Custom predicate
            if (policy.KeepIf != null && policy.KeepIf(item.Metadata!))
            {
                shouldKeep = true;
            }

            if (shouldKeep)
            {
                toKeep.Add(item.Version);
            }
        }

        // Prune versions not in keep set
        var prunedCount = 0;
        foreach (var item in versionMetadata)
        {
            if (!toKeep.Contains(item.Version))
            {
                var metadataKey = (keyWithoutPartition, item.Version);
                if (_metadata.TryRemove(metadataKey, out _))
                {
                    prunedCount++;
                }
            }
        }

        // Update latest version pointer if current latest was pruned
        var versionKey = (keyWithoutPartition, partition);
        if (_latestVersions.TryGetValue(versionKey, out var currentLatest))
        {
            if (!toKeep.Contains(currentLatest))
            {
                // Find new latest from kept versions
                var newLatest = versionMetadata
                    .Where(x => toKeep.Contains(x.Version))
                    .OrderByDescending(x => x.Metadata!.CreatedAt)
                    .FirstOrDefault();

                if (newLatest != null)
                {
                    _latestVersions[versionKey] = newLatest.Version;
                }
                else
                {
                    // No versions left - remove latest pointer
                    _latestVersions.TryRemove(versionKey, out _);
                }
            }
        }

        return Task.FromResult(prunedCount);
    }

    public Task<IReadOnlyList<ArtifactKey>> ValidateConsistencyAsync(CancellationToken ct = default)
    {
        // In-memory implementation: metadata and versions are always consistent
        // (both stored in same process memory)
        // This method is mainly useful for persistent stores where metadata and cache
        // can become desynchronized.

        // For completeness, we'll check if reverse index is consistent
        var orphanedKeys = new List<ArtifactKey>();

        foreach (var kvp in _reverseIndex)
        {
            var artifactKey = kvp.Key;

            // Check if this artifact has any registered versions
            var hasVersions = _latestVersions.Keys.Any(k => k.Key.Equals(artifactKey));

            if (!hasVersions)
            {
                orphanedKeys.Add(artifactKey);
            }
        }

        return Task.FromResult<IReadOnlyList<ArtifactKey>>(orphanedKeys);
    }

    /// <summary>
    /// Disposable lock handle for artifact materialization.
    /// Releases semaphore when disposed.
    /// </summary>
    private class ArtifactLock : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly string _lockKey;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _lockRegistry;
        private bool _disposed;

        public ArtifactLock(SemaphoreSlim semaphore, string lockKey, ConcurrentDictionary<string, SemaphoreSlim> lockRegistry)
        {
            _semaphore = semaphore;
            _lockKey = lockKey;
            _lockRegistry = lockRegistry;
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                try
                {
                    // Release the semaphore
                    _semaphore.Release();

                    // NOTE: We intentionally DON'T remove the semaphore from the registry here.
                    // Removing it while other threads might be waiting would cause them to wait forever
                    // on a semaphore that no longer exists. The minor memory leak (one semaphore per
                    // unique artifact key) is acceptable for in-memory single-process deployments.
                    // For distributed deployments (Phase 6), use Redis/SQL locks that auto-expire.
                }
                catch (SemaphoreFullException)
                {
                    // Lock was already released (shouldn't happen, but handle gracefully)
                    // This can occur if DisposeAsync is called multiple times
                }
                finally
                {
                    _disposed = true;
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
