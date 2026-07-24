using System.Collections.Concurrent;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;
using System.Text.Json;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public enum DebugSessionStatus
{
    Created,
    Initializing,
    Configuring,
    Running,
    PartiallyStopped,
    Stopped,
    Terminating,
    Terminated,
    Faulted
}

public sealed record DebugTreeOwnership(
    string AgentRuntimeRegistrationId,
    string SessionId,
    string ThreadId,
    string DebugTreeId,
    string EnvironmentId,
    long EnvironmentRevision);

public sealed record DebugThreadSnapshot(
    int ThreadId,
    bool IsStopped,
    string? StopReason,
    string? StopDescription,
    long SuspensionEpoch,
    long ResumptionGeneration,
    string? Name = null);

internal sealed class DebugThreadState(int threadId)
{
    public int ThreadId { get; } = threadId;
    public bool IsStopped { get; private set; }
    public string? StopReason { get; private set; }
    public string? StopDescription { get; private set; }
    public long SuspensionEpoch { get; private set; }
    public long ResumptionGeneration { get; private set; }
    public string? Name { get; private set; }

    public void ObserveName(string? name) => Name = Bound(name, 1024);

    public void Stop(string? reason, string? description)
    {
        IsStopped = true;
        StopReason = Bound(reason, 256);
        StopDescription = Bound(description, 1024);
        checked { SuspensionEpoch++; }
    }

    public long Continue()
    {
        IsStopped = false;
        StopReason = null;
        StopDescription = null;
        checked { return ++ResumptionGeneration; }
    }

    public DebugThreadSnapshot Snapshot() => new(
        ThreadId, IsStopped, StopReason, StopDescription, SuspensionEpoch, ResumptionGeneration, Name);

    public void Restore(DebugThreadSnapshot snapshot)
    {
        if (snapshot.ThreadId != ThreadId)
            throw new InvalidOperationException("A debugger thread snapshot cannot be restored to another thread.");
        IsStopped = snapshot.IsStopped;
        StopReason = snapshot.StopReason;
        StopDescription = snapshot.StopDescription;
        SuspensionEpoch = snapshot.SuspensionEpoch;
        ResumptionGeneration = snapshot.ResumptionGeneration;
        Name = snapshot.Name;
    }

    private static string? Bound(string? value, int maximum)
        => value is null ? null : value[..Math.Min(value.Length, maximum)];
}

internal sealed class DebugSessionState
{
    private readonly object _gate = new();
    private readonly Dictionary<int, DebugThreadState> _threads = [];
    private readonly Dictionary<long, DebugOutcomeWaiter> _waiters = [];
    private long _nextWaiterId;
    private int? _primaryStoppedThreadId;
    private DebugResumeTransition? _pendingResumeTransition;

    public DebugSessionStatus Status { get; private set; } = DebugSessionStatus.Created;
    /// <summary>
    /// Adapter-designated thread from the most recent stopped event. This
    /// remains distinct when an all-threads-stopped event suspends every thread.
    /// </summary>
    public int? PrimaryStoppedThreadId
    {
        get { lock (_gate) return _primaryStoppedThreadId; }
    }
    public IReadOnlyList<DebugThreadSnapshot> Threads
    {
        get { lock (_gate) return _threads.Values.OrderBy(x => x.ThreadId).Select(x => x.Snapshot()).ToArray(); }
    }

    public void Transition(DebugSessionStatus status)
    {
        lock (_gate)
        {
            if (!IsValidTransition(Status, status))
                throw new InvalidOperationException($"Invalid debug-session transition from {Status} to {status}.");
            Status = status;
            if (status is DebugSessionStatus.Terminated or DebugSessionStatus.Faulted)
                SettleWaitersLocked(new DebugSessionEndedException(status));
        }
    }

    public void ObserveThread(int threadId)
    {
        if (threadId <= 0) throw new ArgumentOutOfRangeException(nameof(threadId));
        lock (_gate) _threads.TryAdd(threadId, new(threadId));
    }

    public void ReconcileThreads(IReadOnlyList<HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated.Thread> threads)
    {
        ArgumentNullException.ThrowIfNull(threads);
        lock (_gate)
        {
            var retained = threads.Take(4096).Select(x => x.Id).ToHashSet();
            foreach (var thread in threads.Take(4096))
                GetOrAddThreadLocked(thread.Id).ObserveName(thread.Name);
            foreach (var id in _threads.Keys.Where(id => !retained.Contains(id)).ToArray())
                _threads.Remove(id);
            if (_primaryStoppedThreadId is { } primary && !retained.Contains(primary))
                _primaryStoppedThreadId = null;
        }
    }

    public void RemoveThread(int threadId)
    {
        lock (_gate)
        {
            _threads.Remove(threadId);
            if (_primaryStoppedThreadId == threadId)
                _primaryStoppedThreadId = null;
        }
    }

    public void ObserveStopped(int? threadId, bool allThreadsStopped, string reason, string? description)
    {
        lock (_gate)
        {
            _pendingResumeTransition = null;
            _primaryStoppedThreadId = threadId is > 0 ? threadId : null;
            if (allThreadsStopped)
            {
                if (threadId is > 0) _threads.TryAdd(threadId.Value, new(threadId.Value));
                foreach (var thread in _threads.Values)
                {
                    var isPrimary = threadId is null || thread.ThreadId == threadId;
                    thread.Stop(isPrimary ? reason : null, isPrimary ? description : null);
                }
            }
            else
            {
                if (threadId is not > 0) throw new InvalidOperationException("A partial stopped event requires a thread ID.");
                var thread = GetOrAddThreadLocked(threadId.Value);
                thread.Stop(reason, description);
            }
            RecomputeStatusLocked();
            CompleteMatchingWaitersLocked();
        }
    }

    public DebugResumeTransition BeginResume(int threadId, bool allThreadsContinued)
    {
        lock (_gate)
        {
            if (_pendingResumeTransition is not null)
                throw new InvalidOperationException(
                    "A debugger resume transition is already pending.");
            if (threadId <= 0)
                throw new ArgumentOutOfRangeException(nameof(threadId));
            if (!_threads.TryGetValue(threadId, out var selected) || !selected.IsStopped)
                throw new DebugSemanticException(DebugSemanticFailureReason.InvalidSessionState,
                    $"Debug thread '{threadId}' is not stopped.");

            var affected = allThreadsContinued
                ? _threads.Values.ToArray()
                : [selected];
            var snapshots = affected.ToDictionary(x => x.ThreadId, x => x.Snapshot());
            var primary = _primaryStoppedThreadId;
            if (allThreadsContinued)
            {
                foreach (var thread in affected) thread.Continue();
                _primaryStoppedThreadId = null;
            }
            else
            {
                selected.Continue();
                if (_primaryStoppedThreadId == threadId)
                    _primaryStoppedThreadId = null;
            }
            RecomputeStatusLocked();
            return _pendingResumeTransition = new DebugResumeTransition(
                threadId, allThreadsContinued, primary, snapshots);
        }
    }

    public bool TryRollbackResume(DebugResumeTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        lock (_gate)
        {
            if (!ReferenceEquals(_pendingResumeTransition, transition))
                return false;
            foreach (var (threadId, before) in transition.ThreadsBefore)
            {
                if (!_threads.TryGetValue(threadId, out var current))
                    return false;
                var snapshot = current.Snapshot();
                if (snapshot.IsStopped ||
                    snapshot.ResumptionGeneration != before.ResumptionGeneration + 1 ||
                    snapshot.SuspensionEpoch != before.SuspensionEpoch)
                    return false;
            }
            RestoreResumeLocked(transition);
            _pendingResumeTransition = null;
            return true;
        }
    }

    public void ObserveContinued(int threadId, bool allThreadsContinued)
    {
        lock (_gate)
        {
            if (_pendingResumeTransition is { } pending)
            {
                if (pending.ThreadId == threadId &&
                    pending.AllThreadsContinued == allThreadsContinued)
                {
                    _pendingResumeTransition = null;
                    return;
                }
                RestoreResumeLocked(pending);
                _pendingResumeTransition = null;
            }
            if (allThreadsContinued)
            {
                foreach (var thread in _threads.Values) thread.Continue();
                _primaryStoppedThreadId = null;
            }
            else
            {
                if (threadId <= 0) throw new InvalidOperationException("A partial continued event requires a thread ID.");
                GetOrAddThreadLocked(threadId).Continue();
                if (_primaryStoppedThreadId == threadId)
                    _primaryStoppedThreadId = null;
            }
            RecomputeStatusLocked();
        }
    }

    private void RestoreResumeLocked(DebugResumeTransition transition)
    {
        foreach (var (threadId, before) in transition.ThreadsBefore)
            _threads[threadId].Restore(before);
        _primaryStoppedThreadId = transition.PrimaryStoppedThreadIdBefore;
        RecomputeStatusLocked();
    }

    public DebugOutcomeWaitRegistration RegisterStopWaiter(int threadId, long minimumResumptionGeneration)
    {
        if (threadId <= 0) throw new ArgumentOutOfRangeException(nameof(threadId));
        lock (_gate)
        {
            var id = checked(++_nextWaiterId);
            var waiter = new DebugOutcomeWaiter(id, threadId, minimumResumptionGeneration);
            _waiters.Add(id, waiter);
            CompleteMatchingWaitersLocked();
            return new(waiter.Task, () => CancelWaiter(id));
        }
    }

    private void CancelWaiter(long id)
    {
        lock (_gate)
            if (_waiters.Remove(id, out var waiter)) waiter.Cancel();
    }

    private void CompleteMatchingWaitersLocked()
    {
        foreach (var waiter in _waiters.Values.ToArray())
        {
            if (_threads.TryGetValue(waiter.ThreadId, out var thread) && thread.IsStopped &&
                thread.ResumptionGeneration >= waiter.MinimumResumptionGeneration)
            {
                _waiters.Remove(waiter.Id);
                waiter.Complete(thread.Snapshot());
            }
        }
    }

    private void SettleWaitersLocked(Exception exception)
    {
        foreach (var waiter in _waiters.Values) waiter.Fail(exception);
        _waiters.Clear();
    }

    private DebugThreadState GetOrAddThreadLocked(int threadId)
    {
        if (!_threads.TryGetValue(threadId, out var thread))
            _threads.Add(threadId, thread = new(threadId));
        return thread;
    }

    private void RecomputeStatusLocked()
    {
        if (Status is DebugSessionStatus.Terminating or DebugSessionStatus.Terminated or DebugSessionStatus.Faulted) return;
        var stopped = _threads.Values.Count(x => x.IsStopped);
        Status = stopped == 0 ? DebugSessionStatus.Running
            : stopped == _threads.Count ? DebugSessionStatus.Stopped
            : DebugSessionStatus.PartiallyStopped;
    }

    private static bool IsValidTransition(DebugSessionStatus current, DebugSessionStatus next)
        => current == next || (current, next) switch
        {
            (DebugSessionStatus.Created, DebugSessionStatus.Initializing) => true,
            (DebugSessionStatus.Initializing, DebugSessionStatus.Configuring) => true,
            (DebugSessionStatus.Configuring, DebugSessionStatus.Running or DebugSessionStatus.Stopped or DebugSessionStatus.PartiallyStopped) => true,
            (DebugSessionStatus.Running or DebugSessionStatus.Stopped or DebugSessionStatus.PartiallyStopped, DebugSessionStatus.Terminating) => true,
            (_, DebugSessionStatus.Terminated or DebugSessionStatus.Faulted) => true,
            _ => false
        };

    private sealed class DebugOutcomeWaiter(long id, int threadId, long minimumResumptionGeneration)
    {
        private readonly TaskCompletionSource<DebugThreadSnapshot> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long Id { get; } = id;
        public int ThreadId { get; } = threadId;
        public long MinimumResumptionGeneration { get; } = minimumResumptionGeneration;
        public Task<DebugThreadSnapshot> Task => _completion.Task;
        public void Complete(DebugThreadSnapshot snapshot) => _completion.TrySetResult(snapshot);
        public void Cancel() => _completion.TrySetCanceled();
        public void Fail(Exception exception) => _completion.TrySetException(exception);
    }
}

internal sealed record DebugResumeTransition(
    int ThreadId,
    bool AllThreadsContinued,
    int? PrimaryStoppedThreadIdBefore,
    IReadOnlyDictionary<int, DebugThreadSnapshot> ThreadsBefore);

internal sealed class DebugOutcomeWaitRegistration(Task<DebugThreadSnapshot> task, Action cancel) : IDisposable
{
    private Action? _cancel = cancel;
    public Task<DebugThreadSnapshot> Task { get; } = task;
    public void Dispose() => Interlocked.Exchange(ref _cancel, null)?.Invoke();
}

public sealed class DebugSessionEndedException(DebugSessionStatus status)
    : InvalidOperationException($"The debug session ended with status '{status}'.")
{
    public DebugSessionStatus Status { get; } = status;
}

internal sealed class DebugSession : IAsyncDisposable
{
    private int _disposed;
    private long _nextFollowUpId;
    public required string SessionId { get; init; }
    public required string RootSessionId { get; init; }
    public string? ParentSessionId { get; init; }
    public required DebugAdapterStartMethod AdapterStartMethod { get; init; }
    public required DebugProtocolClient Protocol { get; init; }
    public required DebugAdapterStartPlan AdapterPlan { get; init; }
    public Capabilities? Capabilities { get; set; }
    public int? ExitCode { get; set; }
    public JsonElement? RestartData { get; set; }
    public DebugSessionState State { get; } = new();
    public DebugSessionProjections Projections { get; } = new();
    public DebugOutputBuffer Output { get; } = new();
    public DebugProgressProjection Progress { get; } = new();
    public DebugOutputEventCoalescer? OutputEvents { get; set; }
    public DebugProgressEventCoalescer? ProgressEvents { get; set; }
    public DebugAdapterBreakpointStateStore AdapterBreakpoints { get; } = new();
    public ConcurrentDictionary<string, byte> ChildSessionIds { get; } = new(StringComparer.Ordinal);
    public List<IDisposable> HandlerRegistrations { get; } = [];
    public ConcurrentDictionary<long, Task> FollowUpTasks { get; } = [];
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    public void ScheduleFollowUp(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var id = Interlocked.Increment(ref _nextFollowUpId);
        var task = RunFollowUpAsync(operation);
        FollowUpTasks[id] = task;
        _ = task.ContinueWith(completed => FollowUpTasks.TryRemove(id, out _),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task RunFollowUpAsync(Func<Task> operation)
    {
        try { await operation().ConfigureAwait(false); }
        catch { Projections.RecordFollowUpFailure(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var registration in HandlerRegistrations) registration.Dispose();
        Progress.Dispose();
        if (OutputEvents is not null) await OutputEvents.DisposeAsync().ConfigureAwait(false);
        if (ProgressEvents is not null) await ProgressEvents.DisposeAsync().ConfigureAwait(false);
        await Protocol.DisposeAsync().ConfigureAwait(false);
        try { await Task.WhenAll(FollowUpTasks.Values).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
    }
}

internal sealed class DebugSessionTree : IAsyncDisposable
{
    private readonly object _activeGate = new();
    private readonly Dictionary<long, TaskCompletionSource<DebugTreeStopSnapshot>> _treeStopWaiters = [];
    private long _nextTreeStopWaiterId;
    private int _terminalScheduled;
    private long _observerFailures;
    private int _ownedResourcesDisposed;
    private int _disposed;
    public required DebugTreeOwnership Ownership { get; init; }
    public required string RootSessionId { get; init; }
    public required DebugRuntimeBinding RuntimeBinding { get; init; }
    public required DebugTreeAuthorization Authorization { get; init; }
    public required DebugArtifactWriter Artifacts { get; init; }
    public LaunchDebugOperation? SemanticRestartOperation { get; init; }
    public ITreeDebugEventPublisher? EventPublisher { get; init; }
    public DebugContinuationTokenRegistry Continuations { get; } = new();
    public DebugBreakpointStore Breakpoints { get; } = new();
    public ConcurrentDictionary<string, DebugSession> Sessions { get; } = new(StringComparer.Ordinal);
    public ConcurrentQueue<IDebugOwnedResource> OwnedResources { get; } = new();
    public ConcurrentQueue<DebugStoredArtifact> StoredArtifacts { get; } = new();
    public string? ActiveSessionId { get; private set; }
    public long ObserverFailures => Interlocked.Read(ref _observerFailures);
    public void RecordObserverFailure() => Interlocked.Increment(ref _observerFailures);

    public bool TryScheduleTerminal(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Interlocked.CompareExchange(ref _terminalScheduled, 1, 0) != 0) return false;
        _ = Task.Run(async () =>
        {
            try { await operation().ConfigureAwait(false); }
            catch { Interlocked.Increment(ref _observerFailures); }
        });
        return true;
    }

    public void AddStoredArtifact(DebugStoredArtifact artifact)
    {
        StoredArtifacts.Enqueue(artifact);
        while (StoredArtifacts.Count > 128) StoredArtifacts.TryDequeue(out _);
    }

    public void AddSession(DebugSession session)
    {
        RuntimeBinding.State.ThrowIfUnavailable();
        Authorization.ValidateCurrent(RuntimeBinding, session.AdapterPlan);
        if (!Sessions.TryAdd(session.SessionId, session))
            throw new InvalidOperationException($"Debug session '{session.SessionId}' already exists.");
        lock (_activeGate) ActiveSessionId ??= session.SessionId;
    }

    public DebugSession SelectSession(string? explicitSessionId = null)
    {
        var id = explicitSessionId;
        lock (_activeGate) id ??= ActiveSessionId;
        if (id is null || !Sessions.TryGetValue(id, out var session))
            throw new KeyNotFoundException("No live debug protocol session is selectable.");
        return session;
    }

    public void ActivateSession(string sessionId)
    {
        if (!Sessions.TryGetValue(sessionId, out var session) ||
            session.State.Status is DebugSessionStatus.Terminated or DebugSessionStatus.Faulted)
            throw new KeyNotFoundException(
                $"Live debug session '{sessionId}' is not selectable.");
        lock (_activeGate) ActiveSessionId = sessionId;
    }

    public void ObserveStopped(string sessionId)
    {
        if (!Sessions.TryGetValue(sessionId, out var session)) throw new KeyNotFoundException(sessionId);
        lock (_activeGate)
        {
            ActiveSessionId = sessionId;
            var stoppedThread = session.State.Threads.FirstOrDefault(x => x.IsStopped);
            if (stoppedThread is not null)
            {
                var snapshot = new DebugTreeStopSnapshot(sessionId, stoppedThread);
                foreach (var waiter in _treeStopWaiters.Values) waiter.TrySetResult(snapshot);
                _treeStopWaiters.Clear();
            }
        }
    }

    public DebugTreeStopWaitRegistration RegisterTreeStopWaiter()
    {
        lock (_activeGate)
        {
            var existing = Sessions.Values
                .OrderByDescending(x => x.CreatedAt)
                .SelectMany(x => x.State.Threads.Where(thread => thread.IsStopped)
                    .Select(thread => new DebugTreeStopSnapshot(x.SessionId, thread)))
                .FirstOrDefault();
            if (existing is not null)
                return new(Task.FromResult(existing), static () => { });
            var id = checked(++_nextTreeStopWaiterId);
            var completion = new TaskCompletionSource<DebugTreeStopSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            _treeStopWaiters.Add(id, completion);
            return new(completion.Task, () => CancelTreeStopWaiter(id));
        }
    }

    private void CancelTreeStopWaiter(long id)
    {
        lock (_activeGate)
            if (_treeStopWaiters.Remove(id, out var waiter)) waiter.TrySetCanceled();
    }

    public void ObserveTerminated(string sessionId)
    {
        lock (_activeGate)
        {
            if (!string.Equals(ActiveSessionId, sessionId, StringComparison.Ordinal)) return;
            ActiveSessionId = Sessions.Values
                .Where(x => x.SessionId != sessionId && x.State.Status == DebugSessionStatus.Stopped)
                .OrderByDescending(x => x.CreatedAt).Select(x => x.SessionId).FirstOrDefault()
                ?? Sessions.Values.Where(x => x.SessionId != sessionId && x.State.Status is not (DebugSessionStatus.Terminated or DebugSessionStatus.Faulted))
                    .OrderByDescending(x => x.CreatedAt).Select(x => x.SessionId).FirstOrDefault()
                ?? (Sessions.TryGetValue(RootSessionId, out var root) && root.SessionId != sessionId ? root.SessionId : null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Continuations.Clear();
        lock (_activeGate)
        {
            foreach (var waiter in _treeStopWaiters.Values)
                waiter.TrySetException(new ObjectDisposedException(nameof(DebugSessionTree)));
            _treeStopWaiters.Clear();
        }
        await StopAndDrainOwnedResourcesAsync().ConfigureAwait(false);
        foreach (var session in Sessions.Values)
            try { await session.DisposeAsync().ConfigureAwait(false); } catch { }
        Sessions.Clear();
        while (OwnedResources.TryDequeue(out _)) { }
        await Breakpoints.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Stops and drains owned resources while retaining their bounded snapshots
    /// until terminal projection has completed.
    /// </summary>
    internal async ValueTask StopAndDrainOwnedResourcesAsync()
    {
        if (Interlocked.Exchange(ref _ownedResourcesDisposed, 1) != 0)
            return;
        foreach (var resource in OwnedResources.ToArray().AsEnumerable().Reverse())
            try { await resource.DisposeAsync().ConfigureAwait(false); } catch { }
    }
}

internal sealed record DebugStoredArtifact(
    string Kind, string DebugSessionId, string ContentId, string Scope, string? Version,
    IReadOnlyDictionary<string, string> Metadata);

internal sealed record DebugTreeStopSnapshot(string DebugSessionId, DebugThreadSnapshot Thread);

internal sealed class DebugTreeStopWaitRegistration(Task<DebugTreeStopSnapshot> task, Action cancel) : IDisposable
{
    private Action? _cancel = cancel;
    public Task<DebugTreeStopSnapshot> Task { get; } = task;
    public void Dispose() => Interlocked.Exchange(ref _cancel, null)?.Invoke();
}
