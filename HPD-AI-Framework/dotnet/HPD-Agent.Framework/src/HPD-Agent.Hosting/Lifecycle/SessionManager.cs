using System.Collections.Concurrent;
using HPD.Agent;

namespace HPD.Agent.Hosting.Lifecycle;

/// <summary>
/// Abstract base class for managing session and thread lifecycle,
/// thread operation locks, and session-level locks.
/// </summary>
/// <remarks>
/// Responsibilities:
/// <list type="bullet">
///   <item>Session and initial thread creation (delegated to <see cref="ISessionStore"/>)</item>
///   <item>Per-thread operation lock (protects thread mutations that must not overlap)</item>
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
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _threadOperationLocks = new();
    private readonly ConcurrentDictionary<string, ThreadRunState> _activeThreadRuns = new();
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
    /// Create a new session and its default "main" thread directly in the store.
    /// No agent or provider is required — sessions are provider-agnostic containers.
    /// </summary>
    public async Task<(string sessionId, string threadId)> CreateSessionAsync(
        string? sessionId = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default)
    {
        var id = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString() : sessionId;
        var session = new Session(id);
        var thread = session.CreateThread("main");
        thread.Name = "main";
        session.Store = _store;

        if (metadata != null)
        {
            foreach (var kvp in metadata)
                session.AddMetadata(kvp.Key, kvp.Value);
        }

        await _store.SaveSessionAsync(session, ct);
        await _store.SaveInitialThreadAsync(id, thread, ct);

        return (id, "main");
    }

    /// <summary>
    /// Clean up in-memory thread operation and session locks for a session.
    /// Does NOT delete store data and does NOT evict any agent from <see cref="AgentManager"/>.
    /// </summary>
    public void RemoveSession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        _sessionLocks.TryRemove(sessionId, out _);

        var prefix = $"{sessionId}:";
        var keysToRemove = _threadOperationLocks.Keys.Where(k => k.StartsWith(prefix)).ToList();
        foreach (var key in keysToRemove)
            if (_threadOperationLocks.TryRemove(key, out var sem))
                sem.Dispose();

        foreach (var key in _activeThreadRuns.Keys.Where(k => k.StartsWith(prefix)).ToList())
            _activeThreadRuns.TryRemove(key, out _);
    }

    // ─── Thread run ownership ───────────────────────────────────────────

    public bool TryStartThreadRun(
        string agentId,
        string sessionId,
        string threadId,
        out ThreadRunState run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var candidate = new ThreadRunState(
            Guid.NewGuid().ToString("N"),
            agentId,
            sessionId,
            threadId,
            DateTimeOffset.UtcNow);

        var key = ThreadRunKey(sessionId, threadId);
        if (_activeThreadRuns.TryAdd(key, candidate))
        {
            run = candidate;
            return true;
        }

        run = _activeThreadRuns.TryGetValue(key, out var active)
            ? active
            : candidate;
        return false;
    }

    public ThreadRunState? GetActiveThreadRun(string sessionId, string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        return _activeThreadRuns.TryGetValue(ThreadRunKey(sessionId, threadId), out var run)
            ? run
            : null;
    }

    public bool CompleteThreadRun(string sessionId, string threadId, string runtimeRunId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRunId);

        var key = ThreadRunKey(sessionId, threadId);
        return _activeThreadRuns.TryGetValue(key, out var current) &&
            current.RuntimeRunId == runtimeRunId &&
            _activeThreadRuns.TryRemove(key, out _);
    }

    public bool CompleteActiveThreadRun(string sessionId, string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        return _activeThreadRuns.TryRemove(ThreadRunKey(sessionId, threadId), out _);
    }

    private static string ThreadRunKey(string sessionId, string threadId) => $"{sessionId}:{threadId}";

    // ─── Thread operation locks ──────────────────────────────────────────

    /// <summary>
    /// Try to acquire the thread operation lock for a thread.
    /// Returns false if another exclusive thread operation is already in progress.
    /// </summary>
    public bool TryAcquireThreadOperationLock(string sessionId, string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var key = $"{sessionId}:{threadId}";
        var semaphore = _threadOperationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        return semaphore.Wait(0);
    }

    /// <summary>Release the thread operation lock for a thread.</summary>
    public void ReleaseThreadOperationLock(string sessionId, string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var key = $"{sessionId}:{threadId}";
        if (_threadOperationLocks.TryGetValue(key, out var semaphore))
        {
            try { semaphore.Release(); } catch (SemaphoreFullException) { }
        }
    }

    /// <summary>
    /// Remove and dispose the thread operation lock for a single thread.
    /// Call AFTER <see cref="ReleaseThreadOperationLock"/>, never before.
    /// </summary>
    public void RemoveThreadOperationLock(string sessionId, string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var key = $"{sessionId}:{threadId}";
        if (_threadOperationLocks.TryRemove(key, out var sem))
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
    /// Whether recursive thread deletion is permitted.
    /// Platform implementations read from their options.
    /// </summary>
    public virtual bool AllowRecursiveThreadDelete => false;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var kvp in _threadOperationLocks)
            kvp.Value.Dispose();
        foreach (var kvp in _sessionLocks)
            kvp.Value.Dispose();
        _activeThreadRuns.Clear();
    }
}

public sealed record ThreadRunState(
    string RuntimeRunId,
    string AgentId,
    string SessionId,
    string ThreadId,
    DateTimeOffset StartedAt);
