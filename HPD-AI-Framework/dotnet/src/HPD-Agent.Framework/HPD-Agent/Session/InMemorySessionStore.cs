using System.Collections.Concurrent;


namespace HPD.Agent;

/// <summary>
/// In-memory session store for development and testing.
/// V3 Architecture: Separate storage for Session (metadata) and Branches (conversations).
/// Data is lost on process restart.
/// </summary>
/// <remarks>
/// <para><b>Storage Structure:</b></para>
/// <code>
/// _sessions: ConcurrentDictionary&lt;string, Session&gt;        ← Session metadata
/// _branches: ConcurrentDictionary&lt;string, List&lt;Branch&gt;&gt; ← All branches per session
/// _uncommittedTurns: ConcurrentDictionary&lt;string, UncommittedTurn&gt; ← Crash recovery
/// _assetStore: InMemoryAssetStore                        ← Shared assets
/// </code>
/// </remarks>
public class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Branch>> _branches = new();
    private readonly ConcurrentDictionary<string, UncommittedTurn> _uncommittedTurns = new();
    private readonly InMemoryContentStore _contentStore;

    public InMemorySessionStore()
    {
        _contentStore = new InMemoryContentStore();
    }

    /// <inheritdoc />
    public IContentStore? GetContentStore(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _contentStore;
    }

    // ═══════════════════════════════════════════════════════════════════
    // SESSION PERSISTENCE (V3: Metadata only)
    // ═══════════════════════════════════════════════════════════════════

    public Task<Session?> LoadSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<Session?>(session);
        }

        return Task.FromResult<Session?>(null);
    }

    public Task SaveSessionAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task<List<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_sessions.Keys.ToList());
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryRemove(sessionId, out _);
        _branches.TryRemove(sessionId, out _);
        _uncommittedTurns.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════
    // BRANCH PERSISTENCE (V3: New)
    // ═══════════════════════════════════════════════════════════════════

    public Task<Branch?> LoadBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        if (_branches.TryGetValue(sessionId, out var sessionBranches) &&
            sessionBranches.TryGetValue(branchId, out var branch))
        {
            return Task.FromResult<Branch?>(branch);
        }

        return Task.FromResult<Branch?>(null);
    }

    public Task SaveBranchAsync(
        string sessionId,
        Branch branch,
        CancellationToken cancellationToken = default)
    {
        var sessionBranches = _branches.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, Branch>());
        sessionBranches[branch.Id] = branch;
        return Task.CompletedTask;
    }

    public Task<List<string>> ListBranchIdsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (_branches.TryGetValue(sessionId, out var sessionBranches))
        {
            return Task.FromResult(sessionBranches.Keys.ToList());
        }

        return Task.FromResult(new List<string>());
    }

    public Task DeleteBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        if (_branches.TryGetValue(sessionId, out var sessionBranches))
        {
            sessionBranches.TryRemove(branchId, out _);
        }

        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════
    // UNCOMMITTED TURN (Crash Recovery - session-scoped)
    // ═══════════════════════════════════════════════════════════════════

    public Task<UncommittedTurn?> LoadUncommittedTurnAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        _uncommittedTurns.TryGetValue(sessionId, out var turn);
        return Task.FromResult(turn);
    }

    public Task SaveUncommittedTurnAsync(
        UncommittedTurn turn,
        CancellationToken cancellationToken = default)
    {
        _uncommittedTurns[turn.SessionId] = turn;
        return Task.CompletedTask;
    }

    public Task DeleteUncommittedTurnAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        _uncommittedTurns.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════
    // CLEANUP
    // ═══════════════════════════════════════════════════════════════════

    public Task<int> DeleteInactiveSessionsAsync(
        TimeSpan inactivityThreshold,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - inactivityThreshold;
        var sessionsToRemove = new List<string>();

        foreach (var kvp in _sessions)
        {
            if (kvp.Value.LastActivity < cutoff)
            {
                sessionsToRemove.Add(kvp.Key);
            }
        }

        if (!dryRun)
        {
            foreach (var sessionId in sessionsToRemove)
            {
                _sessions.TryRemove(sessionId, out _);
                _branches.TryRemove(sessionId, out _);
                _uncommittedTurns.TryRemove(sessionId, out _);
            }
        }

        return Task.FromResult(sessionsToRemove.Count);
    }
}
