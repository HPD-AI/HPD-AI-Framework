using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace HPD.Agent;

/// <summary>
/// Append-oriented in-memory implementation of the canonical thread journal.
/// Data is lost on process restart.
/// </summary>
public sealed class InMemorySessionStore : ISessionStore
{
    private const int SegmentCapacity = 256;

    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly ConcurrentDictionary<ThreadKey, ThreadJournal> _threads = new();

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

        var cursor = request.After;
        while (true)
        {
            var batch = await journal.ReadBatchAsync(
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
        long after,
        ThreadObservationOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        ArgumentNullException.ThrowIfNull(options);
        if (after < 0)
            throw new ArgumentOutOfRangeException(nameof(after));
        if (options.MaxBatchEventCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxBatchEventCount must be positive.");
        if (!_threads.TryGetValue(thread, out var journal))
            yield break;

        var cursor = after;
        while (true)
        {
            var read = await journal.ReadOrWaitAsync(
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
        if (request.After < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "After must be non-negative.");
        if (request.Through is long through && through < request.After)
            throw new ArgumentOutOfRangeException(nameof(request), "Through cannot be less than After.");
        if (request.MaxBatchEventCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "MaxBatchEventCount must be positive.");
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
                if (condition.ExpectedHead is long expected && expected != _head)
                    throw new ThreadAppendConflictException(_key, expected, _head);

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
                    _descriptor = ThreadDescriptorProjection.Apply(_key, _descriptor, _messageIds, evt, _head);
                }

                result = new ThreadEventAppendResult(committed, previousHead, _head);
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
                    : new ThreadEventHead(_head, _descriptor.UpdatedAt);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask<ThreadEventBatch?> ReadBatchAsync(
            long after,
            long? through,
            int maxCount,
            CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDeleted();
                if (after > _head)
                    throw new ThreadCursorConflictException(_key, after, _head);
                return CopyBatch(after, through ?? _head, maxCount);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask<ReadOrWaitResult> ReadOrWaitAsync(
            long after,
            int maxCount,
            CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDeleted();
                if (after > _head)
                    throw new ThreadCursorConflictException(_key, after, _head);

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
            return new ThreadEventBatch(events, first, last);
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

        private static TaskCompletionSource NewSignal()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record ReadOrWaitResult(ThreadEventBatch? Batch, Task CommitSignal);
}
