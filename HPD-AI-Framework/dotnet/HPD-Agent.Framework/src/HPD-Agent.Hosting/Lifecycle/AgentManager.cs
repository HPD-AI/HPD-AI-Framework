using System.Collections.Concurrent;
using HPD.Agent;

namespace HPD.Agent.Hosting.Lifecycle;

/// <summary>
/// Abstract base class for managing <see cref="StoredAgent"/> definitions and
/// cached <see cref="Agent"/> instances.
/// </summary>
/// <remarks>
/// Responsibilities:
/// <list type="bullet">
///   <item>Agent definition CRUD (delegated to <see cref="IAgentStore"/>)</item>
///   <item><see cref="Agent"/> instance build, cache, and idle eviction (keyed by agentId)</item>
/// </list>
///
/// Agent instances are cached by <c>agentId</c>, not by session — all sessions that share
/// an agent definition share one <see cref="Agent"/> instance. Eviction is purely
/// last-access based; <c>IsStreaming</c> is no longer tracked here.
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        // Fast path
        if (_agents.TryGetValue(agentId, out var entry))
        {
            entry.LastAccessed = DateTime.UtcNow;
            return entry.Agent;
        }

        // Slow path: build with per-agent lock
        var buildLock = _buildLocks.GetOrAdd(agentId, _ => new SemaphoreSlim(1, 1));
        await buildLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_agents.TryGetValue(agentId, out entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                return entry.Agent;
            }

            var stored = await _store.LoadAsync(agentId, ct)
                ?? throw new KeyNotFoundException($"Agent definition '{agentId}' not found.");

            var agent = await BuildAgentAsync(stored, ct);
            _agents[agentId] = new AgentEntry(agent);
            return agent;
        }
        finally
        {
            buildLock.Release();
        }
    }

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
    protected abstract Task<Agent> BuildAgentAsync(StoredAgent stored, CancellationToken ct);

    /// <summary>Idle eviction timeout from platform configuration.</summary>
    protected abstract TimeSpan GetIdleTimeout();

    // ─── Eviction ────────────────────────────────────────────────────────

    private void EvictAgent(string agentId)
    {
        _agents.TryRemove(agentId, out _);
        if (_buildLocks.TryRemove(agentId, out var sem))
            sem.Dispose();
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
