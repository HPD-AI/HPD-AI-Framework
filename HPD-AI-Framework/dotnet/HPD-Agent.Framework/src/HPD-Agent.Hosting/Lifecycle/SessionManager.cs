using System.Collections.Concurrent;
using HPD.Agent;

namespace HPD.Agent.Hosting.Lifecycle;

/// <summary>
/// Abstract base class for managing session and branch lifecycle,
/// branch operation locks, and session-level locks.
/// </summary>
/// <remarks>
/// Responsibilities:
/// <list type="bullet">
///   <item>Session and initial branch creation (delegated to <see cref="ISessionRepository"/>)</item>
///   <item>Per-branch operation lock (protects branch mutations that must not overlap)</item>
///   <item>Per-session exclusive lock (safe metadata updates)</item>
/// </list>
///
/// <b>Behavioral note:</b> <see cref="RemoveSession"/> only cleans up in-memory locks —
/// it does not delete repository data and does not touch the agent cache.
/// The agent is shared across sessions and is managed by <see cref="AgentManager"/>.
/// </remarks>
public abstract class SessionManager : IDisposable
{
    private readonly ISessionRepository _repository;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _branchOperationLocks = new();
    private readonly ConcurrentDictionary<string, BranchRunState> _activeBranchRuns = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
    private bool _disposed;

    protected SessionManager(ISessionRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>The typed session repository for this manager.</summary>
    public ISessionRepository Repository => _repository;

    // ─── Session lifecycle ───────────────────────────────────────────────

    /// <summary>
    /// Create a new session and its default "main" branch directly in the store.
    /// No agent or provider is required — sessions are provider-agnostic containers.
    /// </summary>
    public async Task<(string sessionId, string branchId)> CreateSessionAsync(
        string? sessionId = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default)
    {
        var id = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString() : sessionId;
        var session = new Session(id);
        var branch = session.CreateBranch("main");
        branch.Name = "main";

        if (metadata != null)
        {
            foreach (var kvp in metadata)
                session.AddMetadata(kvp.Key, kvp.Value);
        }

        await _repository.SaveSessionAsync(session, ct);
        await _repository.SaveBranchDocumentAsync(
            BranchEventDocumentBuilder.FromBranchSnapshot(id, branch),
            cancellationToken: ct);

        return (id, "main");
    }

    /// <summary>
    /// Clean up in-memory branch operation and session locks for a session.
    /// Does NOT delete repository data and does NOT evict any agent from <see cref="AgentManager"/>.
    /// </summary>
    public void RemoveSession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        _sessionLocks.TryRemove(sessionId, out _);

        var prefix = $"{sessionId}:";
        var keysToRemove = _branchOperationLocks.Keys.Where(k => k.StartsWith(prefix)).ToList();
        foreach (var key in keysToRemove)
            if (_branchOperationLocks.TryRemove(key, out var sem))
                sem.Dispose();

        foreach (var key in _activeBranchRuns.Keys.Where(k => k.StartsWith(prefix)).ToList())
            _activeBranchRuns.TryRemove(key, out _);
    }

    // ─── Branch run ownership ───────────────────────────────────────────

    public bool TryStartBranchRun(
        string agentId,
        string sessionId,
        string branchId,
        out BranchRunState run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        var candidate = new BranchRunState(
            Guid.NewGuid().ToString("N"),
            agentId,
            sessionId,
            branchId,
            DateTimeOffset.UtcNow);

        var key = BranchRunKey(sessionId, branchId);
        if (_activeBranchRuns.TryAdd(key, candidate))
        {
            run = candidate;
            return true;
        }

        run = _activeBranchRuns.TryGetValue(key, out var active)
            ? active
            : candidate;
        return false;
    }

    public BranchRunState? GetActiveBranchRun(string sessionId, string branchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        return _activeBranchRuns.TryGetValue(BranchRunKey(sessionId, branchId), out var run)
            ? run
            : null;
    }

    public bool CompleteBranchRun(string sessionId, string branchId, string runtimeRunId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRunId);

        var key = BranchRunKey(sessionId, branchId);
        return _activeBranchRuns.TryGetValue(key, out var current) &&
            current.RuntimeRunId == runtimeRunId &&
            _activeBranchRuns.TryRemove(key, out _);
    }

    public bool CompleteActiveBranchRun(string sessionId, string branchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        return _activeBranchRuns.TryRemove(BranchRunKey(sessionId, branchId), out _);
    }

    private static string BranchRunKey(string sessionId, string branchId) => $"{sessionId}:{branchId}";

    // ─── Branch operation locks ──────────────────────────────────────────

    /// <summary>
    /// Try to acquire the branch operation lock for a branch.
    /// Returns false if another exclusive branch operation is already in progress.
    /// </summary>
    public bool TryAcquireBranchOperationLock(string sessionId, string branchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        var key = $"{sessionId}:{branchId}";
        var semaphore = _branchOperationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        return semaphore.Wait(0);
    }

    /// <summary>Release the branch operation lock for a branch.</summary>
    public void ReleaseBranchOperationLock(string sessionId, string branchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        var key = $"{sessionId}:{branchId}";
        if (_branchOperationLocks.TryGetValue(key, out var semaphore))
        {
            try { semaphore.Release(); } catch (SemaphoreFullException) { }
        }
    }

    /// <summary>
    /// Remove and dispose the branch operation lock for a single branch.
    /// Call AFTER <see cref="ReleaseBranchOperationLock"/>, never before.
    /// </summary>
    public void RemoveBranchOperationLock(string sessionId, string branchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        var key = $"{sessionId}:{branchId}";
        if (_branchOperationLocks.TryRemove(key, out var sem))
            sem.Dispose();
    }

    // ─── Session locks ───────────────────────────────────────────────────

    /// <summary>Execute an action with exclusive session-level lock.</summary>
    public async Task<T> WithSessionLockAsync<T>(
        string sessionId,
        Func<Task<T>> action,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(action);

        var sessionLock = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(ct);
        try
        {
            return await action();
        }
        finally
        {
            sessionLock.Release();
        }
    }

    /// <summary>Execute a void action with exclusive session-level lock.</summary>
    public async Task WithSessionLockAsync(
        string sessionId,
        Func<Task> action,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(action);

        var sessionLock = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(ct);
        try
        {
            await action();
        }
        finally
        {
            sessionLock.Release();
        }
    }

    // ─── Abstract ────────────────────────────────────────────────────────

    /// <summary>
    /// Whether recursive branch deletion is permitted.
    /// Platform implementations read from their options.
    /// </summary>
    public virtual bool AllowRecursiveBranchDelete => false;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var kvp in _branchOperationLocks)
            kvp.Value.Dispose();
        foreach (var kvp in _sessionLocks)
            kvp.Value.Dispose();
        _activeBranchRuns.Clear();
    }
}

public sealed record BranchRunState(
    string RuntimeRunId,
    string AgentId,
    string SessionId,
    string BranchId,
    DateTimeOffset StartedAt);
