using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HPD.Agent.Serialization;
using HPD.Agent.Permissions;

namespace HPD.Agent;

/// <summary>
/// Append-oriented in-memory implementation of the canonical thread journal.
/// Data is lost on process restart.
/// </summary>
public sealed class InMemorySessionStore : ISessionStore, IThreadDeltaStore, HPD.Agent.Permissions.IPermissionPreferenceStore
{
    private readonly ConcurrentDictionary<string, PreferenceState> _permissionPreferences = new(StringComparer.Ordinal);
    private const int SegmentCapacity = 256;

    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly ConcurrentDictionary<ThreadKey, ThreadJournal> _threads = new();
    private readonly ConcurrentDictionary<(ThreadKey Thread, string MessageId, Type Kind), List<AgentEvent>> _pendingDeltas = new();

    /// <summary>Creates an in-memory store bound to one immutable event codec.</summary>
    public InMemorySessionStore(AgentEventCodec eventCodec)
    {
        EventCodec = eventCodec ?? throw new ArgumentNullException(nameof(eventCodec));
    }

    /// <inheritdoc />
    public AgentEventCodec EventCodec { get; }

    /// <inheritdoc />
    public ValueTask StageThreadDeltaAsync(
        ThreadKey thread,
        AgentEvent delta,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EventCodec.RequireDurable(delta);
        var key = PendingKey(thread, delta);
        var pending = _pendingDeltas.GetOrAdd(key, static _ => []);
        lock (pending)
            pending.Add(delta with { ThreadSequenceNumber = 0 });
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask<ThreadEventAppendResult> FinalizeThreadDeltasAsync(
        ThreadKey thread,
        AgentEvent messageEnd,
        CancellationToken cancellationToken = default)
    {
        var key = PendingKey(thread, messageEnd);
        _pendingDeltas.TryRemove(key, out var pending);
        AgentEvent[] deltas = [];
        if (pending is not null)
        {
            lock (pending)
                deltas = pending.ToArray();
        }
        var events = ThreadDeltaCoalescer.Coalesce(deltas, messageEnd);
        try
        {
            return await AppendThreadEventsAsync(thread, events, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (pending is not null)
                _pendingDeltas.TryAdd(key, pending);
            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask RecoverThreadDeltasAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    private static (ThreadKey Thread, string MessageId, Type Kind) PendingKey(ThreadKey thread, AgentEvent evt) => evt switch
    {
        TextDeltaEvent delta => (thread, delta.MessageId, typeof(TextDeltaEvent)),
        TextMessageEndEvent end => (thread, end.MessageId, typeof(TextDeltaEvent)),
        ReasoningDeltaEvent delta => (thread, delta.MessageId, typeof(ReasoningDeltaEvent)),
        ReasoningMessageEndEvent end => (thread, end.MessageId, typeof(ReasoningDeltaEvent)),
        _ => throw new ArgumentException("Event is not a supported delta or message boundary.", nameof(evt))
    };

    public Task<Session?> LoadSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_sessions.GetValueOrDefault(sessionId));
    }

    public Task SaveSessionAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task<List<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_sessions.Keys.ToList());
    }

    public async Task DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessions.TryRemove(sessionId, out _);

        foreach (var (key, journal) in _threads.Where(pair => pair.Key.SessionId == sessionId))
        {
            if (_threads.TryRemove(key, out _))
                await journal.MarkDeletedAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<ThreadEventAppendResult> AppendThreadEventsAsync(
        ThreadKey thread,
        IReadOnlyList<AgentEvent> events,
        ThreadAppendCondition condition = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var evt in events)
            EventCodec.RequireDurable(evt);
        ValidateThreadKey(thread);
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            throw new ArgumentException("At least one event is required.", nameof(events));

        var proposed = events
            .Select(evt => ThreadEventValidation.PrepareForAppend(thread.SessionId, thread.ThreadId, evt))
            .ToArray();

        var journal = _threads.GetOrAdd(thread, static key => new ThreadJournal(key));
        return await journal.AppendAsync(proposed, condition, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ThreadJournalReplaceResult> ReplaceThreadEventsAsync(
        ThreadKey thread,
        IReadOnlyList<AgentEvent> events,
        ThreadJournalCursor expectedCursor,
        CancellationToken cancellationToken = default)
    {
        foreach (var evt in events)
            EventCodec.RequireDurable(evt);
        ValidateThreadKey(thread);
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            throw new ArgumentException("A replacement journal cannot be empty.", nameof(events));
        if (!_threads.TryGetValue(thread, out var journal))
            throw new InvalidOperationException($"Thread '{thread.ThreadId}' does not exist.");
        var proposed = events.Select(evt => ThreadEventValidation.PrepareForAppend(
            thread.SessionId, thread.ThreadId, evt)).ToArray();
        return await journal.ReplaceAsync(proposed, expectedCursor, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ThreadDescriptor?> GetThreadAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        return _threads.TryGetValue(thread, out var journal)
            ? await journal.GetDescriptorAsync(cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async IAsyncEnumerable<ThreadDescriptor> ListThreadsAsync(
        string sessionId,
        ThreadListRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "MaxCount must be positive.");

        var count = 0;
        foreach (var (key, journal) in _threads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!StringComparer.Ordinal.Equals(key.SessionId, sessionId))
                continue;

            var descriptor = await journal.GetDescriptorAsync(cancellationToken).ConfigureAwait(false);
            if (descriptor is null || (!request.IncludeHidden && descriptor.Visibility == ThreadVisibility.Hidden))
                continue;

            yield return descriptor;
            if (++count >= request.MaxCount)
                yield break;
        }
    }

    public async ValueTask<ThreadEventHead?> GetThreadEventHeadAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        return _threads.TryGetValue(thread, out var journal)
            ? await journal.GetHeadAsync(cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async IAsyncEnumerable<ThreadEventBatch> ReadThreadEventsAsync(
        ThreadKey thread,
        ThreadEventReadRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        ValidateReadRequest(request);
        if (!_threads.TryGetValue(thread, out var journal))
            yield break;

        var cursor = request.After.SequenceNumber;
        while (true)
        {
            var batch = await journal.ReadBatchAsync(
                request.After.Generation,
                cursor,
                request.Through,
                request.MaxBatchEventCount,
                cancellationToken).ConfigureAwait(false);
            if (batch is null)
                yield break;

            yield return batch;
            cursor = batch.LastThreadSequenceNumber;
            if (request.Through is long through && cursor >= through)
                yield break;
        }
    }

    public async IAsyncEnumerable<ThreadEventBatch> ObserveThreadEventsAsync(
        ThreadKey thread,
        ThreadJournalCursor after,
        ThreadObservationOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        ArgumentNullException.ThrowIfNull(options);
        if (after.Generation <= 0 || after.SequenceNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(after));
        if (options.MaxBatchEventCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxBatchEventCount must be positive.");
        if (!_threads.TryGetValue(thread, out var journal))
            yield break;

        var cursor = after.SequenceNumber;
        while (true)
        {
            var read = await journal.ReadOrWaitAsync(
                after.Generation,
                cursor,
                options.MaxBatchEventCount,
                cancellationToken).ConfigureAwait(false);

            if (read.Batch is null)
            {
                await read.CommitSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            yield return read.Batch;
            cursor = read.Batch.LastThreadSequenceNumber;
        }
    }

    public async Task DeleteThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var key = new ThreadKey(sessionId, threadId);
        if (_threads.TryRemove(key, out var journal))
            await journal.MarkDeletedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteInactiveSessionsAsync(
        TimeSpan inactivityThreshold,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - inactivityThreshold;
        var sessionIds = _sessions
            .Where(pair => pair.Value.LastActivity < cutoff)
            .Select(pair => pair.Key)
            .ToArray();

        if (!dryRun)
        {
            foreach (var sessionId in sessionIds)
                await DeleteSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }

        return sessionIds.Length;
    }

    private static void ValidateThreadKey(ThreadKey thread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thread.SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(thread.ThreadId);
    }

    private static void ValidateReadRequest(ThreadEventReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.After.Generation <= 0 || request.After.SequenceNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "After must be non-negative.");
        if (request.Through is long through && through < request.After.SequenceNumber)
            throw new ArgumentOutOfRangeException(nameof(request), "Through cannot be less than After.");
        if (request.MaxBatchEventCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "MaxBatchEventCount must be positive.");
    }

    /// <inheritdoc />
    public async ValueTask<PermissionPreferenceSnapshot> ReadAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var state = _permissionPreferences.GetOrAdd(sessionId, static _ => new PreferenceState());
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return state.Snapshot with { Records = state.Snapshot.Records.ToArray() }; }
        finally { state.Gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<PermissionPreferenceCommitResult> CommitAsync(
        PermissionPreferenceCommit commit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (commit.AuditThread.SessionId != commit.SessionId)
            throw new InvalidOperationException("Permission audit thread must belong to the preference session.");
        var state = _permissionPreferences.GetOrAdd(commit.SessionId, static _ => new PreferenceState());
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (state.Idempotency.TryGetValue(commit.IdempotencyKey, out var replay))
            {
                if (replay.State == PermissionPreferenceOutboxState.Pending ||
                    replay is { State: PermissionPreferenceOutboxState.Claimed, ClaimExpiresAt: { } expiry } &&
                    expiry <= DateTimeOffset.UtcNow)
                {
                    replay = replay with
                    {
                        State = PermissionPreferenceOutboxState.Claimed,
                        ClaimToken = Guid.NewGuid().ToString("N"),
                        ClaimantId = commit.PublisherClaimantId,
                        ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1)
                    };
                    state.Outbox[replay.SettlementId] = replay;
                    state.Idempotency[replay.IdempotencyKey] = replay;
                }
                return new PermissionPreferenceCommitResult
                {
                    Status = PermissionPreferenceCommitStatus.AlreadyCommitted,
                    CurrentVersion = state.Snapshot.Version,
                    Outbox = replay
                };
            }
            if (state.Snapshot.Version != commit.ExpectedVersion)
                return new PermissionPreferenceCommitResult
                {
                    Status = PermissionPreferenceCommitStatus.VersionConflict,
                    CurrentVersion = state.Snapshot.Version
                };
            if (commit.Replacement.Version != commit.ExpectedVersion + 1)
                throw new InvalidOperationException("Permission replacement version must advance exactly once.");
            var appended = await AppendThreadEventsAsync(
                commit.AuditThread,
                [commit.Event],
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var committedEvent = (PermissionPreferenceChangedEvent)appended.CommittedEvents.Single();
            var outbox = new PermissionPreferenceOutboxRecord
            {
                SettlementId = Guid.NewGuid().ToString("N"),
                ClaimToken = Guid.NewGuid().ToString("N"),
                ClaimantId = commit.PublisherClaimantId,
                ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
                State = PermissionPreferenceOutboxState.Claimed,
                SessionId = commit.SessionId,
                AuditThread = commit.AuditThread,
                CommittedEvent = committedEvent,
                ThreadSequenceNumber = committedEvent.ThreadSequenceNumber,
                IdempotencyKey = commit.IdempotencyKey
            };
            state.Snapshot = commit.Replacement with { Records = commit.Replacement.Records.ToArray() };
            state.Outbox[outbox.SettlementId] = outbox;
            state.Idempotency[commit.IdempotencyKey] = outbox;
            return new PermissionPreferenceCommitResult
            {
                Status = PermissionPreferenceCommitStatus.Committed,
                CurrentVersion = state.Snapshot.Version,
                Outbox = outbox
            };
        }
        finally { state.Gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PermissionPreferenceOutboxRecord>> ClaimPendingPublicationAsync(
        string sessionId,
        string claimantId,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimantId);
        if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
        var state = _permissionPreferences.GetOrAdd(sessionId, static _ => new PreferenceState());
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var claimed = new List<PermissionPreferenceOutboxRecord>();
            foreach (var pending in state.Outbox.Values
                .Where(value => value.State == PermissionPreferenceOutboxState.Pending ||
                    value is { State: PermissionPreferenceOutboxState.Claimed, ClaimExpiresAt: { } expiry } &&
                    expiry <= DateTimeOffset.UtcNow)
                .OrderBy(static value => value.ThreadSequenceNumber)
                .Take(maxCount))
            {
                var value = pending with
                {
                    State = PermissionPreferenceOutboxState.Claimed,
                    ClaimToken = Guid.NewGuid().ToString("N"),
                    ClaimantId = claimantId,
                    ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1)
                };
                state.Outbox[value.SettlementId] = value;
                state.Idempotency[value.IdempotencyKey] = value;
                claimed.Add(value);
            }
            return claimed;
        }
        finally { state.Gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<bool> AcknowledgePublicationAsync(
        string settlementId,
        string claimToken,
        CancellationToken cancellationToken)
    {
        foreach (var state in _permissionPreferences.Values)
        {
            await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!state.Outbox.TryGetValue(settlementId, out var current)) continue;
                if (current.State == PermissionPreferenceOutboxState.Acknowledged) return true;
                if (current.State != PermissionPreferenceOutboxState.Claimed || current.ClaimToken != claimToken)
                    return false;
                var acknowledged = current with
                {
                    State = PermissionPreferenceOutboxState.Acknowledged,
                    ClaimToken = null,
                    ClaimantId = null,
                    ClaimExpiresAt = null
                };
                state.Outbox[settlementId] = acknowledged;
                state.Idempotency[acknowledged.IdempotencyKey] = acknowledged;
                return true;
            }
            finally { state.Gate.Release(); }
        }
        return false;
    }

    private sealed class PreferenceState
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal PermissionPreferenceSnapshot Snapshot { get; set; } = new(0, []);
        internal Dictionary<string, PermissionPreferenceOutboxRecord> Outbox { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, PermissionPreferenceOutboxRecord> Idempotency { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ThreadJournal
    {
        private readonly ThreadKey _key;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly List<AgentEvent[]> _segments = [];
        private readonly HashSet<string> _eventIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _messageIds = new(StringComparer.Ordinal);
        private TaskCompletionSource _commitSignal = NewSignal();
        private ThreadDescriptor? _descriptor;
        private int _tailCount;
        private long _head;
        private long _generation = 1;
        private bool _deleted;

        public ThreadJournal(ThreadKey key) => _key = key;

        public async ValueTask<ThreadEventAppendResult> AppendAsync(
            IReadOnlyList<AgentEvent> proposed,
            ThreadAppendCondition condition,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource signal;
            ThreadEventAppendResult result;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDeleted();
                var current = new ThreadJournalCursor(_generation, _head);
                if (condition.ExpectedCursor is ThreadJournalCursor expected && expected != current)
                    throw new ThreadAppendConflictException(_key, expected, current);

                var duplicate = proposed.FirstOrDefault(evt => _eventIds.Contains(evt.EventId));
                if (duplicate is not null)
                    throw new InvalidOperationException($"Event '{duplicate.EventId}' is already committed to thread '{_key.ThreadId}'.");
                if (proposed.Select(evt => evt.EventId).Distinct(StringComparer.Ordinal).Count() != proposed.Count)
                    throw new InvalidOperationException("An append batch cannot contain duplicate EventIds.");

                var previousHead = _head;
                var committed = new AgentEvent[proposed.Count];
                for (var index = 0; index < proposed.Count; index++)
                {
                    var evt = proposed[index] with { ThreadSequenceNumber = ++_head };
                    committed[index] = evt;
                    AppendToSegment(evt);
                    _eventIds.Add(evt.EventId);
                    _descriptor = ThreadDescriptorProjection.Apply(_key, _descriptor, _messageIds, evt, _generation, _head);
                }

                result = new ThreadEventAppendResult(
                    committed,
                    new ThreadJournalCursor(_generation, previousHead),
                    new ThreadJournalCursor(_generation, _head));
                signal = _commitSignal;
                _commitSignal = NewSignal();
            }
            finally
            {
                _gate.Release();
            }

            signal.TrySetResult();
            return result;
        }

        public async ValueTask<ThreadJournalReplaceResult> ReplaceAsync(
            IReadOnlyList<AgentEvent> proposed,
            ThreadJournalCursor expectedCursor,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource signal;
            ThreadJournalReplaceResult result;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDeleted();
                var previousCursor = new ThreadJournalCursor(_generation, _head);
                if (previousCursor != expectedCursor)
                    throw new ThreadAppendConflictException(_key, expectedCursor, previousCursor);
                if (proposed.Select(evt => evt.EventId).Distinct(StringComparer.Ordinal).Count() != proposed.Count)
                    throw new InvalidOperationException("A replacement journal cannot contain duplicate EventIds.");

                _generation++;
                _segments.Clear();
                _eventIds.Clear();
                _messageIds.Clear();
                _descriptor = null;
                _tailCount = 0;
                _head = 0;

                var committed = new AgentEvent[proposed.Count];
                for (var index = 0; index < proposed.Count; index++)
                {
                    var evt = proposed[index] with { ThreadSequenceNumber = ++_head };
                    committed[index] = evt;
                    AppendToSegment(evt);
                    _eventIds.Add(evt.EventId);
                    _descriptor = ThreadDescriptorProjection.Apply(_key, _descriptor, _messageIds, evt, _generation, _head);
                }

                result = new ThreadJournalReplaceResult(
                    committed,
                    previousCursor,
                    new ThreadJournalCursor(_generation, _head));
                signal = _commitSignal;
                _commitSignal = NewSignal();
            }
            finally
            {
                _gate.Release();
            }

            signal.TrySetException(new ThreadJournalReplacedException(
                _key, result.PreviousCursor, result.CurrentCursor));
            return result;
        }

        public async ValueTask<ThreadDescriptor?> GetDescriptorAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDeleted();
                return _descriptor;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask<ThreadEventHead?> GetHeadAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDeleted();
                return _descriptor is null
                    ? null
                    : new ThreadEventHead(_generation, _head, _descriptor.UpdatedAt);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask<ThreadEventBatch?> ReadBatchAsync(
            long generation,
            long after,
            long? through,
            int maxCount,
            CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDeleted();
                ValidateCursor(new ThreadJournalCursor(generation, after));
                return CopyBatch(after, through ?? _head, maxCount);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask<ReadOrWaitResult> ReadOrWaitAsync(
            long generation,
            long after,
            int maxCount,
            CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDeleted();
                ValidateCursor(new ThreadJournalCursor(generation, after));

                var batch = CopyBatch(after, _head, maxCount);
                return batch is null
                    ? new ReadOrWaitResult(null, _commitSignal.Task)
                    : new ReadOrWaitResult(batch, Task.CompletedTask);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask MarkDeletedAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource signal;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_deleted)
                    return;
                _deleted = true;
                signal = _commitSignal;
                _commitSignal = NewSignal();
            }
            finally
            {
                _gate.Release();
            }

            signal.TrySetException(new ThreadDeletedException(_key));
        }

        private void AppendToSegment(AgentEvent evt)
        {
            if (_segments.Count == 0 || _tailCount == SegmentCapacity)
            {
                _segments.Add(new AgentEvent[SegmentCapacity]);
                _tailCount = 0;
            }

            _segments[^1][_tailCount++] = evt;
        }

        private ThreadEventBatch? CopyBatch(long after, long through, int maxCount)
        {
            var first = after + 1;
            var last = Math.Min(Math.Min(through, _head), after + maxCount);
            if (first > last)
                return null;

            var events = new AgentEvent[checked((int)(last - first + 1))];
            for (var index = 0; index < events.Length; index++)
                events[index] = GetAt(first + index);
            return new ThreadEventBatch(events, _generation, first, last);
        }

        private AgentEvent GetAt(long sequence)
        {
            var zeroBased = checked((int)(sequence - 1));
            return _segments[zeroBased / SegmentCapacity][zeroBased % SegmentCapacity];
        }

        private void ThrowIfDeleted()
        {
            if (_deleted)
                throw new ThreadDeletedException(_key);
        }

        private void ValidateCursor(ThreadJournalCursor cursor)
        {
            var head = new ThreadJournalCursor(_generation, _head);
            if (cursor.Generation != _generation || cursor.SequenceNumber > _head)
                throw new ThreadCursorConflictException(_key, cursor, head);
        }

        private static TaskCompletionSource NewSignal()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record ReadOrWaitResult(ThreadEventBatch? Batch, Task CommitSignal);
}
