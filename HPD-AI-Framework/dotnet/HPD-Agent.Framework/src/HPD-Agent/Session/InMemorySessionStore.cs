using System.Collections.Concurrent;


namespace HPD.Agent;

/// <summary>
/// In-memory session store for development and testing.
/// V3 Architecture: Separate storage for Session metadata and branch event documents.
/// Data is lost on process restart.
/// </summary>
/// <remarks>
/// <para><b>Storage Structure:</b></para>
/// <code>
/// _sessions: ConcurrentDictionary&lt;string, Session&gt;        ← Session metadata
/// _branches: ConcurrentDictionary&lt;string, BranchEventDocument&gt; ← Event documents per branch
/// _uncommittedTurns: ConcurrentDictionary&lt;string, UncommittedTurn&gt; ← Crash recovery
/// </code>
/// </remarks>
public class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, BranchEventDocument>> _branches = new();
    private readonly ConcurrentDictionary<string, UncommittedTurn> _uncommittedTurns = new();

    // ═══════════════════════════════════════════════════════════════════
    // SESSION PERSISTENCE ( Metadata only)
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
    // BRANCH EVENT PERSISTENCE
    // ═══════════════════════════════════════════════════════════════════

    public Task<Branch?> LoadBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        if (_branches.TryGetValue(sessionId, out var sessionBranches) &&
            sessionBranches.TryGetValue(branchId, out var document))
        {
            var branch = BranchProjector.Project(document);
            if (_sessions.TryGetValue(sessionId, out var session))
                branch.Session = session;
            return Task.FromResult<Branch?>(branch);
        }

        return Task.FromResult<Branch?>(null);
    }

    public Task<BranchEventDocument?> LoadBranchDocumentAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        if (_branches.TryGetValue(sessionId, out var sessionBranches) &&
            sessionBranches.TryGetValue(branchId, out var document))
        {
            return Task.FromResult<BranchEventDocument?>(document);
        }

        return Task.FromResult<BranchEventDocument?>(null);
    }

    public Task SaveBranchDocumentAsync(
        BranchEventDocument document,
        long? expectedSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        BranchEventValidation.RequireDocumentScope(document, document.SessionId, document.BranchId);

        var sessionBranches = _branches.GetOrAdd(
            document.SessionId,
            _ => new ConcurrentDictionary<string, BranchEventDocument>());

        if (expectedSequenceNumber is not null &&
            sessionBranches.TryGetValue(document.BranchId, out var existing) &&
            existing.NextSequenceNumber - 1 != expectedSequenceNumber.Value)
        {
            throw new InvalidOperationException(
                $"Branch '{document.BranchId}' sequence mismatch. Expected {expectedSequenceNumber}, actual {existing.NextSequenceNumber - 1}.");
        }

        sessionBranches[document.BranchId] = document;
        return Task.CompletedTask;
    }

    public Task AppendBranchEventAsync(
        string sessionId,
        string branchId,
        AgentEvent evt,
        long? expectedSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        cancellationToken.ThrowIfCancellationRequested();

        var sessionBranches = _branches.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, BranchEventDocument>());
        sessionBranches.AddOrUpdate(
            branchId,
            _ =>
            {
                BranchEventValidation.RequirePersistableScope(sessionId, branchId, evt);
                evt.SequenceNumber = 1;
                return new BranchEventDocument
                {
                    SessionId = sessionId,
                    BranchId = branchId,
                    CreatedAt = evt.Timestamp,
                    UpdatedAt = evt.Timestamp,
                    NextSequenceNumber = 2,
                    Events = [evt]
                };
            },
            (_, existing) =>
            {
                if (expectedSequenceNumber is not null &&
                    existing.NextSequenceNumber - 1 != expectedSequenceNumber.Value)
                {
                    throw new InvalidOperationException(
                        $"Branch '{branchId}' sequence mismatch. Expected {expectedSequenceNumber}, actual {existing.NextSequenceNumber - 1}.");
                }

                BranchEventValidation.RequirePersistableScope(sessionId, branchId, evt);
                evt.SequenceNumber = existing.NextSequenceNumber;
                var events = existing.Events.ToList();
                events.Add(evt);
                return existing with
                {
                    UpdatedAt = evt.Timestamp,
                    NextSequenceNumber = existing.NextSequenceNumber + 1,
                    Events = events
                };
            });

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<AgentEvent> ReadBranchEventsAsync(
        string sessionId,
        string branchId,
        HPD.Events.ReplayReadOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var document = await LoadBranchDocumentAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false);
        if (document is null)
            yield break;

        await foreach (var evt in document.Events.FilterByReplayOptions(options, cancellationToken).ConfigureAwait(false))
            yield return evt;
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
