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
    private readonly ConcurrentDictionary<string, ThreadExecutionState> _threadExecutions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
    private readonly ConcurrentDictionary<string, ThreadExecutionProjectionCache> _threadExecutionProjections = new();
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
        string defaultAgentId,
        string? sessionId = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultAgentId);
        var id = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString() : sessionId;
        var session = new Session(id);
        var thread = session.CreateThread(defaultAgentId, "main");
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

        foreach (var key in _threadExecutions.Keys.Where(k => k.StartsWith(prefix)).ToList())
            _threadExecutions.TryRemove(key, out _);
        foreach (var key in _threadExecutionProjections.Keys.Where(k => k.StartsWith(prefix)).ToList())
            _threadExecutionProjections.TryRemove(key, out _);
    }

    // ─── Thread execution ownership ───────────────────────────────────────────

    public bool TryReserveThreadExecution(
        string agentId,
        string sessionId,
        string threadId,
        out ThreadExecutionState execution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var candidate = new ThreadExecutionState(
            Guid.NewGuid().ToString("N"),
            agentId,
            sessionId,
            threadId,
            DateTimeOffset.UtcNow,
            ThreadExecutionOwnership.Reserved);

        var key = ThreadExecutionKey(sessionId, threadId);
        if (_threadExecutions.TryAdd(key, candidate))
        {
            execution = candidate;
            return true;
        }

        execution = _threadExecutions.TryGetValue(key, out var active)
            ? active
            : candidate;
        return false;
    }

    public ThreadExecutionState? GetActiveThreadExecution(string sessionId, string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        return _threadExecutions.TryGetValue(ThreadExecutionKey(sessionId, threadId), out var execution) &&
            execution.Ownership == ThreadExecutionOwnership.Active
            ? execution
            : null;
    }

    public bool ActivateThreadExecution(string sessionId, string threadId, string threadExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadExecutionId);

        var key = ThreadExecutionKey(sessionId, threadId);
        while (_threadExecutions.TryGetValue(key, out var current))
        {
            if (current.ThreadExecutionId != threadExecutionId || current.Ownership != ThreadExecutionOwnership.Reserved)
                return false;

            if (_threadExecutions.TryUpdate(key, current with { Ownership = ThreadExecutionOwnership.Active }, current))
                return true;
        }

        return false;
    }

    public bool ReleaseThreadExecution(string sessionId, string threadId, string threadExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadExecutionId);

        var key = ThreadExecutionKey(sessionId, threadId);
        return _threadExecutions.TryGetValue(key, out var current) &&
            current.ThreadExecutionId == threadExecutionId &&
            _threadExecutions.TryRemove(new KeyValuePair<string, ThreadExecutionState>(key, current));
    }

    public bool ReleaseActiveThreadExecution(string sessionId, string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        return _threadExecutions.TryRemove(ThreadExecutionKey(sessionId, threadId), out _);
    }

    private static string ThreadExecutionKey(string sessionId, string threadId) => $"{sessionId}:{threadId}";

    public async ValueTask<IReadOnlyList<AgentEvent>?> GetThreadExecutionProjectionEventsAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var key = new ThreadKey(sessionId, threadId);
        var cacheKey = ThreadExecutionKey(sessionId, threadId);
        var cache = _threadExecutionProjections.GetOrAdd(cacheKey, static _ => new ThreadExecutionProjectionCache());
        await cache.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var head = await _store.GetThreadEventHeadAsync(key, cancellationToken).ConfigureAwait(false);
            if (head is null)
            {
                cache.Events.Clear();
                cache.AppliedCursor = default;
                return null;
            }

            if (cache.AppliedCursor.Generation != head.Generation)
            {
                cache.Events.Clear();
                cache.AppliedCursor = ThreadJournalCursor.Start(head.Generation);
            }

            if (head.ThreadSequenceNumber > cache.AppliedCursor.SequenceNumber)
            {
                await foreach (var batch in _store.ReadThreadEventsAsync(
                    key,
                    new ThreadEventReadRequest(cache.AppliedCursor, head.ThreadSequenceNumber),
                    cancellationToken).ConfigureAwait(false))
                {
                    cache.Events.AddRange(batch.Events.Where(ThreadExecutionProjector.IsProjectionEvent));
                    cache.AppliedCursor = new ThreadJournalCursor(batch.Generation, batch.LastThreadSequenceNumber);
                }
            }

            return cache.Events.ToArray();
        }
        finally
        {
            cache.Gate.Release();
        }
    }

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
        foreach (var kvp in _threadExecutionProjections)
            kvp.Value.Dispose();
        _threadExecutions.Clear();
        _threadExecutionProjections.Clear();
    }
}

internal sealed class ThreadExecutionProjectionCache : IDisposable
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public List<AgentEvent> Events { get; } = [];
    public ThreadJournalCursor AppliedCursor { get; set; }

    public void Dispose() => Gate.Dispose();
}

public sealed record ThreadExecutionState(
    string ThreadExecutionId,
    string AgentId,
    string SessionId,
    string ThreadId,
    DateTimeOffset StartedAt,
    ThreadExecutionOwnership Ownership);

public enum ThreadExecutionOwnership
{
    Reserved,
    Active
}
