using System.Collections.Concurrent;
using HPD.Agent;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed record AgentCacheEvictionResult(
    int AgentCount,
    int BuildLockCount)
{
    public bool EvictedAny => AgentCount > 0 || BuildLockCount > 0;
}

/// <summary>
/// Abstract base class for managing <see cref="StoredAgent"/> definitions and
/// cached <see cref="Agent"/> instances.
/// </summary>
/// <remarks>
/// Responsibilities:
/// <list type="bullet">
///   <item>Agent definition CRUD (delegated to <see cref="IAgentStore"/>)</item>
///   <item><see cref="Agent"/> instance build, cache, and idle eviction (keyed by runtime owner)</item>
/// </list>
///
/// Unscoped agent instances are cached by <c>agentId</c>. Hosted runtime instances are cached
/// by <c>agentId/sessionId/threadId</c>, giving every thread its own runtime queue.
/// Eviction is purely last-access based; <c>IsStreaming</c> is no longer tracked here.
/// </remarks>
public abstract class AgentManager : IDisposable
{
    private readonly IAgentStore _store;
    private readonly ConcurrentDictionary<string, AgentEntry> _agents = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _buildLocks = new();
    private readonly Timer _evictionTimer;
    private bool _disposed;

    protected AgentManager(IAgentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _evictionTimer = new Timer(EvictIdleAgents, null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Store used for persisted agent definitions.
    /// </summary>
    protected IAgentStore AgentStore => _store;

    // ─── Definition CRUD ────────────────────────────────────────────────

    /// <summary>Create and persist a new agent definition.</summary>
    public async Task<StoredAgent> CreateDefinitionAsync(
        AgentConfig config,
        string? name = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var stored = new StoredAgent
        {
            Id = Guid.NewGuid().ToString(),
            Name = name ?? config.Name,
            Config = config,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Metadata = metadata
        };

        await _store.SaveAsync(stored, ct);
        return stored;
    }

    /// <summary>Load a stored agent definition by ID. Returns null if not found.</summary>
    public Task<StoredAgent?> GetDefinitionAsync(string agentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return _store.LoadAsync(agentId, ct);
    }

    /// <summary>List all stored agent definitions.</summary>
    public async Task<IReadOnlyList<StoredAgent>> ListDefinitionsAsync(CancellationToken ct = default)
    {
        var ids = await _store.ListIdsAsync(ct);
        var result = new List<StoredAgent>(ids.Count);
        foreach (var id in ids)
        {
            var agent = await _store.LoadAsync(id, ct);
            if (agent != null)
                result.Add(agent);
        }
        return result;
    }

    /// <summary>
    /// Update an agent definition. Evicts the cached <see cref="Agent"/> instance immediately —
    /// the next stream request on any session builds a fresh instance from the updated definition.
    /// Active streams finish with their existing instance (they hold a reference).
    /// </summary>
    public async Task<StoredAgent> UpdateDefinitionAsync(
        string agentId,
        AgentConfig config,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(config);

        var existing = await _store.LoadAsync(agentId, ct)
            ?? throw new KeyNotFoundException($"Agent '{agentId}' not found.");

        existing.Config = config;
        existing.UpdatedAt = DateTime.UtcNow;

        await _store.SaveAsync(existing, ct);

        // Evict cached instance — next request will rebuild
        EvictAgent(agentId);

        return existing;
    }

    /// <summary>
    /// Delete an agent definition and evict the cached instance.
    /// </summary>
    public async Task DeleteDefinitionAsync(string agentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        await _store.DeleteAsync(agentId, ct);
        EvictAgent(agentId);
    }

    // ─── Instance access ─────────────────────────────────────────────────

    /// <summary>
    /// Get or build an <see cref="Agent"/> instance for the given agent ID.
    /// Uses async-safe per-agent locking to prevent duplicate builds.
    /// </summary>
    public virtual async Task<Agent> GetOrBuildAgentAsync(string agentId, CancellationToken ct = default)
        => await GetOrBuildAgentCoreAsync(agentId, agentId, ct).ConfigureAwait(false);

    /// <summary>
    /// Get or build a thread-owned runtime instance for the given agent definition.
    /// </summary>
    public virtual async Task<Agent> GetOrBuildAgentRuntimeAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        return await GetOrBuildAgentCoreAsync(
            agentId,
            RuntimeCacheKey(agentId, sessionId, threadId),
            ct).ConfigureAwait(false);
    }

    private async Task<Agent> GetOrBuildAgentCoreAsync(
        string agentId,
        string cacheKey,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);

        // Fast path
        if (_agents.TryGetValue(cacheKey, out var entry))
        {
            entry.LastAccessed = DateTime.UtcNow;
            return entry.Agent;
        }

        // Slow path: build with per-agent lock
        var buildLock = _buildLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await buildLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_agents.TryGetValue(cacheKey, out entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                return entry.Agent;
            }

            var agent = await BuildAgentAsync(agentId, ct);
            _agents[cacheKey] = new AgentEntry(agent);
            return agent;
        }
        finally
        {
            buildLock.Release();
        }
    }

    private static string RuntimeCacheKey(string agentId, string sessionId, string threadId) =>
        $"{agentId}::{sessionId}::{threadId}";

    /// <summary>
    /// Return the cached <see cref="Agent"/> instance for an agent ID without building.
    /// Returns null if not yet built or already evicted.
    /// </summary>
    public Agent? GetAgent(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return _agents.TryGetValue(agentId, out var entry) ? entry.Agent : null;
    }

    /// <summary>
    /// Return the cached thread-owned runtime <see cref="Agent"/> instance without building.
    /// Hosted interactive responses must target this runtime cache because request waiters
    /// live on the thread runtime that emitted the request.
    /// </summary>
    public Agent? GetRuntimeAgent(string agentId, string sessionId, string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var cacheKey = RuntimeCacheKey(agentId, sessionId, threadId);
        return _agents.TryGetValue(cacheKey, out var entry) ? entry.Agent : null;
    }

    /// <summary>
    /// Marks cached instances for one agent definition as stale and evicts them.
    /// Future requests rebuild from the current definition and contribution pipeline.
    /// Active turns keep using any <see cref="Agent"/> reference they already hold.
    /// </summary>
    public AgentCacheEvictionResult MarkAgentStale(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return EvictAgent(agentId);
    }

    /// <summary>
    /// Marks every cached agent instance as stale. Used when backend package changes
    /// may affect the global provider, middleware, tool, or contributor pipeline.
    /// </summary>
    public AgentCacheEvictionResult MarkAllAgentsStale()
    {
        var removedAgents = 0;
        var removedLocks = 0;
        foreach (var key in _agents.Keys.ToArray())
        {
            if (_agents.TryRemove(key, out _))
            {
                removedAgents++;
            }
        }

        foreach (var key in _buildLocks.Keys.ToArray())
        {
            if (_buildLocks.TryRemove(key, out var sem))
            {
                sem.Dispose();
                removedLocks++;
            }
        }

        return new AgentCacheEvictionResult(removedAgents, removedLocks);
    }

    /// <summary>
    /// Seeds a definition with the exact <paramref name="agentId"/> into the store.
    /// Use this for synthesizing fallback definitions when no stored definition exists.
    /// </summary>
    protected async Task SeedDefinitionAsync(string agentId, AgentConfig config, CancellationToken ct = default)
    {
        var stored = new StoredAgent
        {
            Id = agentId,
            Name = config.Name,
            Config = config,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _store.SaveAsync(stored, ct);
    }

    // ─── Abstract ────────────────────────────────────────────────────────

    /// <summary>Platform-specific agent build logic.</summary>
    protected abstract Task<Agent> BuildAgentAsync(string agentId, CancellationToken ct);

    /// <summary>Idle eviction timeout from platform configuration.</summary>
    protected abstract TimeSpan GetIdleTimeout();

    // ─── Eviction ────────────────────────────────────────────────────────

    private AgentCacheEvictionResult EvictAgent(string agentId)
    {
        var removedAgents = 0;
        var removedLocks = 0;
        if (_agents.TryRemove(agentId, out _))
        {
            removedAgents++;
        }

        if (_buildLocks.TryRemove(agentId, out var sem))
        {
            sem.Dispose();
            removedLocks++;
        }

        var runtimePrefix = $"{agentId}::";
        foreach (var key in _agents.Keys.Where(k => k.StartsWith(runtimePrefix, StringComparison.Ordinal)).ToList())
        {
            if (_agents.TryRemove(key, out _))
            {
                removedAgents++;
            }
        }

        foreach (var key in _buildLocks.Keys.Where(k => k.StartsWith(runtimePrefix, StringComparison.Ordinal)).ToList())
        {
            if (_buildLocks.TryRemove(key, out var runtimeSem))
            {
                runtimeSem.Dispose();
                removedLocks++;
            }
        }

        return new AgentCacheEvictionResult(removedAgents, removedLocks);
    }

    private void EvictIdleAgents(object? state)
    {
        if (_disposed) return;
        var cutoff = DateTime.UtcNow - GetIdleTimeout();

        foreach (var kvp in _agents)
        {
            if (kvp.Value.LastAccessed < cutoff)
                EvictAgent(kvp.Key);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _evictionTimer.Dispose();
        foreach (var kvp in _buildLocks)
            kvp.Value.Dispose();
    }

    private sealed class AgentEntry
    {
        public Agent Agent { get; }
        public DateTime LastAccessed { get; set; }

        public AgentEntry(Agent agent)
        {
            Agent = agent ?? throw new ArgumentNullException(nameof(agent));
            LastAccessed = DateTime.UtcNow;
        }
    }
}
