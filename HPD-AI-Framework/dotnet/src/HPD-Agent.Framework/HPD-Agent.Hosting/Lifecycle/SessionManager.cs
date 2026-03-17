using System.Collections.Concurrent;
using HPD.Agent;

namespace HPD.Agent.Hosting.Lifecycle;

/// <summary>
/// Abstract base class for managing session and branch lifecycle,
/// stream locks, and session-level locks.
/// </summary>
/// <remarks>
/// Responsibilities:
/// <list type="bullet">
///   <item>Session and initial branch creation (delegated to <see cref="ISessionStore"/>)</item>
///   <item>Per-branch stream lock (prevents concurrent streams on same branch)</item>
///   <item>Per-session exclusive lock (safe metadata updates)</item>
/// </list>
///
/// <b>Behavioral note:</b> <see cref="RemoveSession"/> only cleans up in-memory locks —
/// it does not delete store data and does not touch the agent cache.
/// The agent is shared across sessions and is managed by <see cref="AgentManager"/>.
/// </remarks>
public abstract class SessionManager : IDisposable
{
    private readonly ISessionStore _store;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _streamLocks = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
    private bool _disposed;

    protected SessionManager(ISessionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>The session store for this manager.</summary>
    public ISessionStore Store => _store;

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
        session.Store = _store;

        if (metadata != null)
        {
            foreach (var kvp in metadata)
                session.AddMetadata(kvp.Key, kvp.Value);
        }

        await _store.SaveSessionAsync(session, ct);
        await _store.SaveBranchAsync(id, branch, ct);

        return (id, "main");
    }

    /// <summary>
    /// Clean up in-memory stream and session locks for a session.
    /// Does NOT delete store data and does NOT evict any agent from <see cref="AgentManager"/>.
    /// </summary>
    public void RemoveSession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        _sessionLocks.TryRemove(sessionId, out _);

        var prefix = $"{sessionId}:";
        var keysToRemove = _streamLocks.Keys.Where(k => k.StartsWith(prefix)).ToList();
        foreach (var key in keysToRemove)
            if (_streamLocks.TryRemove(key, out var sem))
                sem.Dispose();
    }

    // ─── Stream locks ────────────────────────────────────────────────────

    /// <summary>
    /// Try to acquire the stream lock for a branch.
    /// Returns false if a stream is already in progress on this branch.
    /// </summary>
    public bool TryAcquireStreamLock(string sessionId, string branchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        var key = $"{sessionId}:{branchId}";
        var semaphore = _streamLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        return semaphore.Wait(0);
    }

    /// <summary>Release the stream lock for a branch.</summary>
    public void ReleaseStreamLock(string sessionId, string branchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        var key = $"{sessionId}:{branchId}";
        if (_streamLocks.TryGetValue(key, out var semaphore))
        {
            try { semaphore.Release(); } catch (SemaphoreFullException) { }
        }
    }

    /// <summary>
    /// Remove and dispose the stream lock for a single branch.
    /// Call AFTER <see cref="ReleaseStreamLock"/>, never before.
    /// </summary>
    public void RemoveBranchStreamLock(string sessionId, string branchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        var key = $"{sessionId}:{branchId}";
        if (_streamLocks.TryRemove(key, out var sem))
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
        foreach (var kvp in _streamLocks)
            kvp.Value.Dispose();
        foreach (var kvp in _sessionLocks)
            kvp.Value.Dispose();
    }
}
