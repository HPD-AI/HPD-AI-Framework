// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Agent.Serialization;
using HPD.Base;

namespace HPD.Agent.Stores.HpdBase;

/// <summary>Restart-durable HPD Base implementation of the canonical session and thread journal.</summary>
public sealed class HpdBaseSessionStore : ISessionStore, IThreadDeltaStore
{
    private const int PageSize = 250;
    private const int AppendAttempts = 25;
    private const int MaximumAtomicAppendEvents = 98;
    private static readonly TimeSpan ObservationPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly PrincipalContext StorePrincipal = new()
    {
        AuthenticationState = PrincipalAuthenticationState.System,
        SubjectKind = AccessSubjectKind.System,
        SubjectId = "hpd-agent-stores",
    };

    private readonly BaseSession _session;
    private long _lastDeltaOrdinal = DateTimeOffset.UtcNow.UtcTicks;

    /// <summary>Creates an HPD Base-backed session store.</summary>
    public HpdBaseSessionStore(
        IBaseSessionFactory sessions,
        AgentEventCodec eventCodec)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        EventCodec = eventCodec ?? throw new ArgumentNullException(nameof(eventCodec));
        _session = sessions.For(StorePrincipal);
    }

    /// <inheritdoc />
    public AgentEventCodec EventCodec { get; }

    /// <inheritdoc />
    public StorePersistenceCapability PersistenceCapability =>
        StorePersistenceCapability.RestartDurable;

    /// <inheritdoc />
    public async ValueTask StageThreadDeltaAsync(
        ThreadKey thread,
        AgentEvent delta,
        CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        AgentEvent scoped = ThreadEventValidation.PrepareForAppend(
            thread.SessionId,
            thread.ThreadId,
            delta) with
        {
            ThreadSequenceNumber = 0
        };
        EventCodec.RequireDurable(scoped);
        (string messageId, string kind) = DeltaIdentity(scoped);
        long ordinal = NextDeltaOrdinal();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        HpdBaseThreadDelta stored = new()
        {
            SessionId = thread.SessionId,
            ThreadId = thread.ThreadId,
            MessageId = messageId,
            Kind = kind,
            Ordinal = ordinal,
            CreatedAt = now,
            Payload = EventCodec.Serialize(scoped),
        };
        RecordId id = DeltaRecordId(thread, scoped.EventId);
        BaseResult<BaseRecord<HpdBaseThreadDelta>> result = await _session
            .Collection(HpdBaseThreadDelta.Collection)
            .CreateAsync(id, stored, cancellationToken)
            .ConfigureAwait(false);
        if (result.TryGetValue(out _))
            return;
        if (result is not BaseFailure<BaseRecord<HpdBaseThreadDelta>> failure ||
            failure.Status != OperationStatus.Conflict)
        {
            result.RequireValue();
        }

        BaseRecord<HpdBaseThreadDelta>? existing = await GetRecordAsync(
            HpdBaseThreadDelta.Collection,
            id,
            cancellationToken).ConfigureAwait(false);
        if (existing is null ||
            !StringComparer.Ordinal.Equals(
                existing.Value.Payload,
                stored.Payload))
        {
            throw new InvalidOperationException(
                $"Delta event '{scoped.EventId}' conflicts with staged thread data.");
        }
    }

    /// <inheritdoc />
    public async ValueTask<ThreadEventAppendResult> FinalizeThreadDeltasAsync(
        ThreadKey thread,
        AgentEvent messageEnd,
        CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        AgentEvent scopedEnd = ThreadEventValidation.PrepareForAppend(
            thread.SessionId,
            thread.ThreadId,
            messageEnd);
        EventCodec.RequireDurable(scopedEnd);
        (string messageId, string kind) = DeltaIdentity(scopedEnd);
        List<BaseRecord<HpdBaseThreadDelta>> pending =
            await ReadPendingDeltasAsync(
                thread,
                messageId,
                kind,
                cancellationToken).ConfigureAwait(false);
        IReadOnlyList<AgentEvent> events = ThreadDeltaCoalescer.Coalesce(
            pending.Select(record => DeserializeDelta(record.Value)).ToArray(),
            scopedEnd);
        ThreadEventAppendResult result = await AppendThreadEventsAsync(
            thread,
            events,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await DeleteDeltaRecordsAsync(pending, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask RecoverThreadDeltasAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        var groups = new Dictionary<(string MessageId, string Kind),
            List<BaseRecord<HpdBaseThreadDelta>>>();
        await foreach (BaseRecord<HpdBaseThreadDelta> record in QueryRecordPagesAsync(
                           HpdBaseThreadDelta.Collection,
                           query => query
                               .Where(HpdBaseThreadDelta.Fields.SessionId, thread.SessionId)
                               .Where(HpdBaseThreadDelta.Fields.ThreadId, thread.ThreadId)
                               .OrderBy(HpdBaseThreadDelta.Fields.Ordinal)
                               .Take(PageSize),
                           cancellationToken).ConfigureAwait(false))
        {
            var key = (record.Value.MessageId, record.Value.Kind);
            if (!groups.TryGetValue(key, out List<BaseRecord<HpdBaseThreadDelta>>? values))
            {
                values = [];
                groups.Add(key, values);
            }
            values.Add(record);
        }

        foreach (List<BaseRecord<HpdBaseThreadDelta>> records in groups.Values
                     .OrderBy(static value => value[0].Value.Ordinal))
        {
            AgentEvent[] deltas = records
                .OrderBy(static value => value.Value.Ordinal)
                .Select(record => DeserializeDelta(record.Value))
                .ToArray();
            if (await JournalContainsAnyEventIdAsync(
                    thread,
                    deltas.Select(static value => value.EventId),
                    cancellationToken).ConfigureAwait(false))
            {
                await DeleteDeltaRecordsAsync(records, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            AgentEvent recoveredEnd = CreateRecoveredMessageEnd(
                thread,
                deltas[0],
                records[0].Value.MessageId,
                records[0].Value.Kind);
            await AppendThreadEventsAsync(
                thread,
                ThreadDeltaCoalescer.Coalesce(deltas, recoveredEnd),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await DeleteDeltaRecordsAsync(records, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<Session?> LoadSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        BaseRecord<HpdBaseSessionRecord>? record = await GetRecordAsync(
            HpdBaseSessionRecord.Collection,
            SessionRecordId(sessionId),
            cancellationToken).ConfigureAwait(false);
        if (record is null)
            return null;

        Session session = DeserializeSession(record.Value.Payload, sessionId);
        return await ThreadForkVisibility.IsSessionVisibleAsync(
                this, session, cancellationToken).ConfigureAwait(false)
            ? session
            : null;
    }

    /// <inheritdoc />
    public async Task SaveSessionAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.Id);
        HpdBaseSessionRecord stored = ToRecord(session);
        await _session.Collection(HpdBaseSessionRecord.Collection)
            .UpsertAsync(
                SessionRecordId(session.Id),
                stored,
                stored,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryCreateSessionAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.Id);
        BaseResult<BaseRecord<HpdBaseSessionRecord>> result = await _session
            .Collection(HpdBaseSessionRecord.Collection)
            .CreateAsync(
                SessionRecordId(session.Id),
                ToRecord(session),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.TryGetValue(out _))
            return true;
        if (result is BaseFailure<BaseRecord<HpdBaseSessionRecord>> failure &&
            failure.Status == OperationStatus.Conflict)
        {
            return false;
        }

        result.RequireValue();
        return false;
    }

    /// <inheritdoc />
    public async ValueTask<SessionPreparationResult> TryPrepareSessionAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (await TryCreateSessionAsync(session, cancellationToken).ConfigureAwait(false))
            return SessionPreparationResult.Created;

        Session? current = await LoadStoredSessionAsync(
            session.Id, cancellationToken).ConfigureAwait(false);
        return current?.Preparation == session.Preparation
            ? SessionPreparationResult.ExistingOwned
            : SessionPreparationResult.Conflict;
    }

    /// <inheritdoc />
    public async Task<List<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default)
    {
        var ids = new List<string>();
        await foreach (HpdBaseSessionRecord record in QueryRecordsAsync(
                           HpdBaseSessionRecord.Collection,
                           static query => query
                               .OrderBy(HpdBaseSessionRecord.Fields.SessionId)
                               .Take(PageSize),
                           cancellationToken).ConfigureAwait(false))
        {
            Session session = DeserializeSession(record.Payload, record.SessionId);
            if (await ThreadForkVisibility.IsSessionVisibleAsync(
                    this, session, cancellationToken).ConfigureAwait(false))
            {
                ids.Add(record.SessionId);
            }
        }
        return ids;
    }

    /// <inheritdoc />
    public async Task DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await _session.Collection(HpdBaseSessionRecord.Collection)
            .DeleteAsync(SessionRecordId(sessionId), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var threads = new List<ThreadKey>();
        await foreach (BaseRecord<HpdBaseThreadRecord> record in QueryRecordPagesAsync(
                           HpdBaseThreadRecord.Collection,
                           query => query
                               .Where(HpdBaseThreadRecord.Fields.SessionId, sessionId)
                               .OrderBy(HpdBaseThreadRecord.Fields.ThreadId)
                               .Take(PageSize),
                           cancellationToken).ConfigureAwait(false))
        {
            threads.Add(new ThreadKey(record.Value.SessionId, record.Value.ThreadId));
        }

        foreach (ThreadKey thread in threads)
            await DeleteThreadAsync(thread.SessionId, thread.ThreadId, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <inheritdoc />
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
        if (events.Count > MaximumAtomicAppendEvents)
        {
            throw new ArgumentException(
                $"HPD Base atomic thread appends support at most {MaximumAtomicAppendEvents} events per batch.",
                nameof(events));
        }

        AgentEvent[] proposed = events
            .Select(evt => ThreadEventValidation.PrepareForAppend(
                thread.SessionId, thread.ThreadId, evt))
            .ToArray();
        foreach (AgentEvent evt in proposed)
            EventCodec.RequireDurable(evt);
        if (proposed.Select(static evt => evt.EventId)
            .Distinct(StringComparer.Ordinal).Count() != proposed.Length)
        {
            throw new InvalidOperationException(
                "An append batch cannot contain duplicate EventIds.");
        }

        for (var attempt = 0; attempt < AppendAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BaseRecord<HpdBaseThreadRecord>? existing = await GetRecordAsync(
                HpdBaseThreadRecord.Collection,
                ThreadRecordId(thread),
                cancellationToken).ConfigureAwait(false);
            ThreadJournalState state = existing is null
                ? ThreadJournalState.Empty(thread)
                : DeserializeThreadState(existing.Value, existing.Revision);
            ThreadJournalCursor current = new(state.Generation, state.Head);
            if (condition.ExpectedCursor is ThreadJournalCursor expected && expected != current)
                throw new ThreadAppendConflictException(thread, expected, current);

            await EnsureEventIdsAvailableAsync(
                thread, state.Generation, proposed, cancellationToken).ConfigureAwait(false);

            AgentEvent[] committed = proposed.Select((evt, index) => evt with
            {
                ThreadSequenceNumber = state.Head + index + 1
            }).ToArray();
            ThreadDescriptor descriptor = state.Descriptor;
            foreach (AgentEvent evt in committed)
            {
                descriptor = ThreadDescriptorProjection.Apply(
                    thread,
                    descriptor,
                    state.MessageIds,
                    evt,
                    state.Generation,
                    evt.ThreadSequenceNumber);
            }

            HpdBaseThreadRecord threadRecord = ToRecord(thread, descriptor, state.MessageIds);
            HpdBaseThreadEventBlock block = ToBlock(
                thread,
                state.Generation,
                committed[0].ThreadSequenceNumber,
                committed[^1].ThreadSequenceNumber,
                committed);
            RecordId blockId = BlockRecordId(
                thread,
                state.Generation,
                committed[0].ThreadSequenceNumber);

            BaseBatchBuilder batch = _session.Atomic();
            batch.Create(HpdBaseThreadEventBlock.Collection, blockId, block);
            foreach (AgentEvent evt in committed)
            {
                HpdBaseThreadEventIdentity identity = new()
                {
                    SessionId = thread.SessionId,
                    ThreadId = thread.ThreadId,
                    Generation = state.Generation,
                    EventId = evt.EventId,
                    Sequence = evt.ThreadSequenceNumber,
                };
                batch.Create(
                    HpdBaseThreadEventIdentity.Collection,
                    EventIdentityRecordId(
                        thread,
                        state.Generation,
                        evt.EventId),
                    identity);
            }
            if (existing is null)
            {
                batch.Upsert(
                    HpdBaseThreadRecord.Collection,
                    ThreadRecordId(thread),
                    threadRecord,
                    threadRecord,
                    RecordUpsertExistenceCondition.CreateOnly);
            }
            else
            {
                batch.Upsert(
                    HpdBaseThreadRecord.Collection,
                    ThreadRecordId(thread),
                    threadRecord,
                    threadRecord,
                    RecordUpsertExistenceCondition.UpdateOnly,
                    existing.Revision);
            }

            try
            {
                (await batch.CommitAsync(cancellationToken).ConfigureAwait(false))
                    .RequireValue()
                    .RequireCommitted();
                return new ThreadEventAppendResult(
                    committed,
                    current,
                    new ThreadJournalCursor(state.Generation, descriptor.Head));
            }
            catch (Exception exception)
                when (IsConcurrencyFailure(exception) && condition.ExpectedCursor is null)
            {
                if (attempt + 1 == AppendAttempts)
                    throw;
            }
            catch (Exception exception)
                when (IsConcurrencyFailure(exception) &&
                      condition.ExpectedCursor is ThreadJournalCursor committedExpected)
            {
                BaseRecord<HpdBaseThreadRecord>? latest = await GetRecordAsync(
                    HpdBaseThreadRecord.Collection,
                    ThreadRecordId(thread),
                    cancellationToken).ConfigureAwait(false);
                ThreadJournalCursor actual = latest is null
                    ? ThreadJournalCursor.Start(1)
                    : new ThreadJournalCursor(latest.Value.Generation, latest.Value.Head);
                throw new ThreadAppendConflictException(
                    thread, committedExpected, actual);
            }
        }

        throw new InvalidOperationException(
            $"Thread '{thread.ThreadId}' could not be appended after {AppendAttempts} attempts.");
    }

    /// <inheritdoc />
    public async ValueTask<ThreadJournalReplaceResult> ReplaceThreadEventsAsync(
        ThreadKey thread,
        IReadOnlyList<AgentEvent> events,
        ThreadJournalCursor expectedCursor,
        CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            throw new ArgumentException("A replacement journal cannot be empty.", nameof(events));

        BaseRecord<HpdBaseThreadRecord>? existing = await GetRecordAsync(
            HpdBaseThreadRecord.Collection,
            ThreadRecordId(thread),
            cancellationToken).ConfigureAwait(false);
        if (existing is null)
            throw new InvalidOperationException($"Thread '{thread.ThreadId}' does not exist.");

        ThreadJournalState state = DeserializeThreadState(existing.Value, existing.Revision);
        ThreadJournalCursor previous = new(state.Generation, state.Head);
        if (previous != expectedCursor)
            throw new ThreadAppendConflictException(thread, expectedCursor, previous);

        AgentEvent[] proposed = events.Select(evt => ThreadEventValidation.PrepareForAppend(
                thread.SessionId, thread.ThreadId, evt))
            .ToArray();
        if (proposed.Select(static evt => evt.EventId)
            .Distinct(StringComparer.Ordinal).Count() != proposed.Length)
        {
            throw new InvalidOperationException(
                "A replacement journal cannot contain duplicate EventIds.");
        }
        foreach (AgentEvent evt in proposed)
            EventCodec.RequireDurable(evt);

        long generation = checked(previous.Generation + 1);
        AgentEvent[] committed = proposed.Select((evt, index) => evt with
        {
            ThreadSequenceNumber = index + 1
        }).ToArray();
        var messageIds = new HashSet<string>(StringComparer.Ordinal);
        ThreadDescriptor? descriptor = null;
        foreach (AgentEvent evt in committed)
        {
            descriptor = ThreadDescriptorProjection.Apply(
                thread,
                descriptor,
                messageIds,
                evt,
                generation,
                evt.ThreadSequenceNumber);
        }

        HpdBaseThreadRecord threadRecord = ToRecord(
            thread,
            descriptor!,
            messageIds);
        HpdBaseThreadEventBlock block = ToBlock(
            thread,
            generation,
            1,
            committed.Length,
            committed);
        BaseBatchBuilder batch = _session.Atomic();
        batch.Create(
            HpdBaseThreadEventBlock.Collection,
            BlockRecordId(thread, generation, 1),
            block);
        batch.Upsert(
            HpdBaseThreadRecord.Collection,
            ThreadRecordId(thread),
            threadRecord,
            threadRecord,
            RecordUpsertExistenceCondition.UpdateOnly,
            existing.Revision);

        try
        {
            (await batch.CommitAsync(cancellationToken).ConfigureAwait(false))
                .RequireValue()
                .RequireCommitted();
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            BaseRecord<HpdBaseThreadRecord>? latest = await GetRecordAsync(
                HpdBaseThreadRecord.Collection,
                ThreadRecordId(thread),
                cancellationToken).ConfigureAwait(false);
            ThreadJournalCursor actual = latest is null
                ? ThreadJournalCursor.Start(1)
                : new ThreadJournalCursor(latest.Value.Generation, latest.Value.Head);
            throw new ThreadAppendConflictException(thread, expectedCursor, actual);
        }

        return new ThreadJournalReplaceResult(
            committed,
            previous,
            new ThreadJournalCursor(generation, committed.Length));
    }

    /// <inheritdoc />
    public async ValueTask<ThreadDescriptor?> GetThreadAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        BaseRecord<HpdBaseThreadRecord>? record = await GetRecordAsync(
            HpdBaseThreadRecord.Collection,
            ThreadRecordId(thread),
            cancellationToken).ConfigureAwait(false);
        if (record is null)
            return null;

        ThreadDescriptor descriptor = DeserializeDescriptor(record.Value, thread);
        return descriptor.Kind != ThreadKind.FrameworkInternal &&
               await ThreadForkVisibility.IsVisibleAsync(
                   this, descriptor, cancellationToken).ConfigureAwait(false)
            ? descriptor
            : null;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ThreadDescriptor> ListThreadsAsync(
        string sessionId,
        ThreadListRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(request), "MaxCount must be positive.");

        var count = 0;
        await foreach (BaseRecord<HpdBaseThreadRecord> record in QueryRecordPagesAsync(
                           HpdBaseThreadRecord.Collection,
                           query => query
                               .Where(HpdBaseThreadRecord.Fields.SessionId, sessionId)
                               .OrderBy(HpdBaseThreadRecord.Fields.UpdatedAt)
                               .Take(PageSize),
                           cancellationToken).ConfigureAwait(false))
        {
            ThreadKey thread = new(record.Value.SessionId, record.Value.ThreadId);
            ThreadDescriptor descriptor = DeserializeDescriptor(record.Value, thread);
            if (descriptor.Kind == ThreadKind.FrameworkInternal ||
                (!request.IncludeHidden &&
                 descriptor.Visibility == ThreadVisibility.Hidden) ||
                !await ThreadForkVisibility.IsVisibleAsync(
                    this, descriptor, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            yield return descriptor;
            if (++count >= request.MaxCount)
                yield break;
        }
    }

    /// <inheritdoc />
    public async ValueTask<ThreadEventHead?> GetThreadEventHeadAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        BaseRecord<HpdBaseThreadRecord>? record = await GetRecordAsync(
            HpdBaseThreadRecord.Collection,
            ThreadRecordId(thread),
            cancellationToken).ConfigureAwait(false);
        return record is null
            ? null
            : new ThreadEventHead(
                record.Value.Generation,
                record.Value.Head,
                record.Value.UpdatedAt);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ThreadEventBatch> ReadThreadEventsAsync(
        ThreadKey thread,
        ThreadEventReadRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateThreadKey(thread);
        ValidateReadRequest(request);
        BaseRecord<HpdBaseThreadRecord>? stateRecord = await GetRecordAsync(
            HpdBaseThreadRecord.Collection,
            ThreadRecordId(thread),
            cancellationToken).ConfigureAwait(false);
        if (stateRecord is null)
            yield break;

        ThreadJournalCursor head = new(
            stateRecord.Value.Generation,
            stateRecord.Value.Head);
        if (request.After.Generation != head.Generation ||
            request.After.SequenceNumber > head.SequenceNumber)
        {
            throw new ThreadCursorConflictException(thread, request.After, head);
        }

        long through = Math.Min(
            request.Through ?? head.SequenceNumber,
            head.SequenceNumber);
        if (through <= request.After.SequenceNumber)
            yield break;

        var pending = new List<AgentEvent>(request.MaxBatchEventCount);
        long expectedSequence = request.After.SequenceNumber + 1;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await foreach (BaseRecord<HpdBaseThreadEventBlock> record in QueryRecordPagesAsync(
                           HpdBaseThreadEventBlock.Collection,
                           query => query
                               .Where(HpdBaseThreadEventBlock.Fields.SessionId, thread.SessionId)
                               .Where(HpdBaseThreadEventBlock.Fields.ThreadId, thread.ThreadId)
                               .Where(HpdBaseThreadEventBlock.Fields.Generation, head.Generation)
                               .OrderBy(HpdBaseThreadEventBlock.Fields.FirstSequence)
                               .Take(PageSize),
                           cancellationToken).ConfigureAwait(false))
        {
            HpdBaseThreadEventBlock block = record.Value;
            if (block.LastSequence <= request.After.SequenceNumber ||
                block.FirstSequence > through)
            {
                continue;
            }

            foreach (AgentEvent evt in DeserializeEvents(block, thread))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (evt.ThreadSequenceNumber <= request.After.SequenceNumber ||
                    evt.ThreadSequenceNumber > through)
                {
                    continue;
                }
                if (evt.ThreadSequenceNumber != expectedSequence ||
                    !seen.Add(evt.EventId))
                {
                    throw new InvalidDataException(
                        $"Thread '{thread.ThreadId}' journal is non-contiguous or contains duplicate EventIds.");
                }

                expectedSequence++;
                pending.Add(evt);
                if (pending.Count == request.MaxBatchEventCount)
                {
                    yield return ToBatch(head.Generation, pending);
                    pending = new List<AgentEvent>(request.MaxBatchEventCount);
                }
            }
        }

        if (expectedSequence != through + 1)
        {
            throw new InvalidDataException(
                $"Thread '{thread.ThreadId}' journal ended at sequence {expectedSequence - 1}, expected {through}.");
        }
        if (pending.Count > 0)
            yield return ToBatch(head.Generation, pending);
    }

    /// <inheritdoc />
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
            throw new ArgumentOutOfRangeException(nameof(options));

        ThreadEventHead? initial = await GetThreadEventHeadAsync(
            thread, cancellationToken).ConfigureAwait(false);
        if (initial is null)
            yield break;

        var cursor = after;
        while (true)
        {
            ThreadEventHead? head = await GetThreadEventHeadAsync(
                thread, cancellationToken).ConfigureAwait(false);
            if (head is null)
                throw new ThreadDeletedException(thread);
            if (head.Cursor.Generation != cursor.Generation ||
                cursor.SequenceNumber > head.Cursor.SequenceNumber)
            {
                throw new ThreadCursorConflictException(
                    thread, cursor, head.Cursor);
            }

            if (head.Cursor.SequenceNumber > cursor.SequenceNumber)
            {
                await foreach (ThreadEventBatch batch in ReadThreadEventsAsync(
                                   thread,
                                   new ThreadEventReadRequest(
                                       cursor,
                                       head.Cursor.SequenceNumber,
                                       options.MaxBatchEventCount),
                                   cancellationToken).ConfigureAwait(false))
                {
                    yield return batch;
                    cursor = new ThreadJournalCursor(
                        batch.Generation,
                        batch.LastThreadSequenceNumber);
                }
                continue;
            }

            await Task.Delay(
                ObservationPollInterval,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DeleteThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var thread = new ThreadKey(sessionId, threadId);
        ValidateThreadKey(thread);
        await _session.Collection(HpdBaseThreadRecord.Collection)
            .DeleteAsync(
                ThreadRecordId(thread),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await DeleteEventIdentitiesAsync(thread, cancellationToken)
            .ConfigureAwait(false);
        await DeleteEventBlocksAsync(thread, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteInactiveSessionsAsync(
        TimeSpan inactivityThreshold,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (inactivityThreshold < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(inactivityThreshold));

        DateTimeOffset cutoff = DateTimeOffset.UtcNow - inactivityThreshold;
        var ids = new List<string>();
        await foreach (HpdBaseSessionRecord record in QueryRecordsAsync(
                           HpdBaseSessionRecord.Collection,
                           query => query
                               .WhereLessThanOrEqual(
                                   HpdBaseSessionRecord.Fields.LastActivity,
                                   cutoff)
                               .OrderBy(HpdBaseSessionRecord.Fields.LastActivity)
                               .Take(PageSize),
                           cancellationToken).ConfigureAwait(false))
        {
            ids.Add(record.SessionId);
        }

        if (!dryRun)
        {
            foreach (string id in ids)
                await DeleteSessionAsync(id, cancellationToken).ConfigureAwait(false);
        }
        return ids.Count;
    }

    private async ValueTask EnsureEventIdsAvailableAsync(
        ThreadKey thread,
        long generation,
        IReadOnlyList<AgentEvent> proposed,
        CancellationToken cancellationToken)
    {
        BaseRecord<HpdBaseThreadRecord>? state = await GetRecordAsync(
            HpdBaseThreadRecord.Collection,
            ThreadRecordId(thread),
            cancellationToken).ConfigureAwait(false);
        if (state is null || state.Value.Head == 0)
            return;

        var requested = proposed.Select(static evt => evt.EventId)
            .ToHashSet(StringComparer.Ordinal);
        await foreach (BaseRecord<HpdBaseThreadEventBlock> record in QueryRecordPagesAsync(
                           HpdBaseThreadEventBlock.Collection,
                           query => query
                               .Where(HpdBaseThreadEventBlock.Fields.SessionId, thread.SessionId)
                               .Where(HpdBaseThreadEventBlock.Fields.ThreadId, thread.ThreadId)
                               .Where(HpdBaseThreadEventBlock.Fields.Generation, generation)
                               .OrderBy(HpdBaseThreadEventBlock.Fields.FirstSequence)
                               .Take(PageSize),
                           cancellationToken).ConfigureAwait(false))
        {
            foreach (AgentEvent evt in DeserializeEvents(record.Value, thread))
            {
                if (requested.Contains(evt.EventId))
                {
                    throw new InvalidOperationException(
                        $"Event '{evt.EventId}' is already committed to thread '{thread.ThreadId}'.");
                }
            }
        }
    }

    private async Task<List<BaseRecord<HpdBaseThreadDelta>>> ReadPendingDeltasAsync(
        ThreadKey thread,
        string messageId,
        string kind,
        CancellationToken cancellationToken)
    {
        var records = new List<BaseRecord<HpdBaseThreadDelta>>();
        await foreach (BaseRecord<HpdBaseThreadDelta> record in QueryRecordPagesAsync(
                           HpdBaseThreadDelta.Collection,
                           query => query
                               .Where(HpdBaseThreadDelta.Fields.SessionId, thread.SessionId)
                               .Where(HpdBaseThreadDelta.Fields.ThreadId, thread.ThreadId)
                               .Where(HpdBaseThreadDelta.Fields.MessageId, messageId)
                               .Where(HpdBaseThreadDelta.Fields.Kind, kind)
                               .OrderBy(HpdBaseThreadDelta.Fields.Ordinal)
                               .Take(PageSize),
                           cancellationToken).ConfigureAwait(false))
        {
            records.Add(record);
        }
        return records;
    }

    private async Task DeleteDeltaRecordsAsync(
        IEnumerable<BaseRecord<HpdBaseThreadDelta>> records,
        CancellationToken cancellationToken)
    {
        BaseCollectionSession<HpdBaseThreadDelta> collection =
            _session.Collection(HpdBaseThreadDelta.Collection);
        foreach (BaseRecord<HpdBaseThreadDelta> record in records)
        {
            await collection.DeleteAsync(
                record.Id,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<bool> JournalContainsAnyEventIdAsync(
        ThreadKey thread,
        IEnumerable<string> eventIds,
        CancellationToken cancellationToken)
    {
        HashSet<string> requested = eventIds.ToHashSet(StringComparer.Ordinal);
        if (requested.Count == 0)
            return false;
        BaseRecord<HpdBaseThreadRecord>? state = await GetRecordAsync(
            HpdBaseThreadRecord.Collection,
            ThreadRecordId(thread),
            cancellationToken).ConfigureAwait(false);
        if (state is null || state.Value.Head == 0)
            return false;

        await foreach (BaseRecord<HpdBaseThreadEventBlock> record in QueryRecordPagesAsync(
                           HpdBaseThreadEventBlock.Collection,
                           query => query
                               .Where(HpdBaseThreadEventBlock.Fields.SessionId, thread.SessionId)
                               .Where(HpdBaseThreadEventBlock.Fields.ThreadId, thread.ThreadId)
                               .Where(
                                   HpdBaseThreadEventBlock.Fields.Generation,
                                   state.Value.Generation)
                               .OrderBy(HpdBaseThreadEventBlock.Fields.FirstSequence)
                               .Take(PageSize),
                           cancellationToken).ConfigureAwait(false))
        {
            if (DeserializeEvents(record.Value, thread)
                .Any(evt => requested.Contains(evt.EventId)))
            {
                return true;
            }
        }
        return false;
    }

    private AgentEvent DeserializeDelta(HpdBaseThreadDelta delta) =>
        EventCodec.DeserializeEvent(delta.Payload);

    private static AgentEvent CreateRecoveredMessageEnd(
        ThreadKey thread,
        AgentEvent first,
        string messageId,
        string kind)
    {
        AgentEvent messageEnd = kind switch
        {
            "text" => new TextMessageEndEvent(messageId),
            "reasoning" => new ReasoningMessageEndEvent(messageId),
            _ => throw new InvalidDataException(
                $"Staged delta kind '{kind}' is not supported."),
        };
        return messageEnd with
        {
            EventId = $"{first.EventId}-recovered-end",
            SessionId = thread.SessionId,
            ThreadId = thread.ThreadId,
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = first.Metadata,
            TraceId = first.TraceId,
            SpanId = first.SpanId,
            ParentSpanId = first.ParentSpanId,
            EventFlowId = first.EventFlowId,
            ThreadExecutionId = first.ThreadExecutionId,
        };
    }

    private static (string MessageId, string Kind) DeltaIdentity(
        AgentEvent value) => value switch
        {
            TextDeltaEvent delta => (delta.MessageId, "text"),
            TextMessageEndEvent end => (end.MessageId, "text"),
            ReasoningDeltaEvent delta => (delta.MessageId, "reasoning"),
            ReasoningMessageEndEvent end => (end.MessageId, "reasoning"),
            _ => throw new ArgumentException(
                "Event is not a supported delta or message boundary.",
                nameof(value)),
        };

    private long NextDeltaOrdinal()
    {
        long candidate = DateTimeOffset.UtcNow.UtcTicks;
        while (true)
        {
            long current = Interlocked.Read(ref _lastDeltaOrdinal);
            long next = Math.Max(candidate, current + 1);
            if (Interlocked.CompareExchange(
                    ref _lastDeltaOrdinal,
                    next,
                    current) == current)
            {
                return next;
            }
        }
    }

    private async Task DeleteEventBlocksAsync(
        ThreadKey thread,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var ids = new List<RecordId>();
            await foreach (BaseRecord<HpdBaseThreadEventBlock> record in QueryRecordPagesAsync(
                               HpdBaseThreadEventBlock.Collection,
                               query => query
                                   .Where(HpdBaseThreadEventBlock.Fields.SessionId, thread.SessionId)
                                   .Where(HpdBaseThreadEventBlock.Fields.ThreadId, thread.ThreadId)
                                   .OrderBy(HpdBaseThreadEventBlock.Fields.Generation)
                                   .Take(PageSize),
                               cancellationToken).ConfigureAwait(false))
            {
                ids.Add(record.Id);
            }
            if (ids.Count == 0)
                return;

            BaseCollectionSession<HpdBaseThreadEventBlock> collection =
                _session.Collection(HpdBaseThreadEventBlock.Collection);
            foreach (RecordId id in ids)
                await collection.DeleteAsync(id, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
        }
    }

    private async Task DeleteEventIdentitiesAsync(
        ThreadKey thread,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var ids = new List<RecordId>();
            await foreach (BaseRecord<HpdBaseThreadEventIdentity> record in
                           QueryRecordPagesAsync(
                               HpdBaseThreadEventIdentity.Collection,
                               query => query
                                   .Where(
                                       HpdBaseThreadEventIdentity.Fields.SessionId,
                                       thread.SessionId)
                                   .Where(
                                       HpdBaseThreadEventIdentity.Fields.ThreadId,
                                       thread.ThreadId)
                                   .OrderBy(
                                       HpdBaseThreadEventIdentity.Fields.Generation)
                                   .Take(PageSize),
                               cancellationToken).ConfigureAwait(false))
            {
                ids.Add(record.Id);
            }
            if (ids.Count == 0)
                return;

            BaseCollectionSession<HpdBaseThreadEventIdentity> collection =
                _session.Collection(HpdBaseThreadEventIdentity.Collection);
            foreach (RecordId id in ids)
            {
                await collection.DeleteAsync(
                    id,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private HpdBaseSessionRecord ToRecord(Session session)
    {
        string payload = JsonSerializer.Serialize(
            session,
            SessionJsonContext.Combined.Session);
        return new HpdBaseSessionRecord
        {
            SessionId = session.Id,
            Payload = payload,
            LastActivity = new DateTimeOffset(
                session.LastActivity.ToUniversalTime(),
                TimeSpan.Zero),
        };
    }

    private static Session DeserializeSession(string payload, string sessionId)
    {
        try
        {
            Session session = JsonSerializer.Deserialize(
                    payload,
                    SessionJsonContext.Combined.Session)
                ?? throw new JsonException("Session payload was JSON null.");
            if (!StringComparer.Ordinal.Equals(session.Id, sessionId))
            {
                throw new InvalidDataException(
                    $"Stored session '{sessionId}' contains conflicting payload identity '{session.Id}'.");
            }
            return session;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Stored session '{sessionId}' contains invalid JSON.",
                exception);
        }
    }

    private static HpdBaseThreadRecord ToRecord(
        ThreadKey thread,
        ThreadDescriptor descriptor,
        IEnumerable<string> messageIds)
    {
        return new HpdBaseThreadRecord
        {
            SessionId = thread.SessionId,
            ThreadId = thread.ThreadId,
            Generation = descriptor.Generation,
            Head = descriptor.Head,
            UpdatedAt = descriptor.UpdatedAt,
            DescriptorPayload = JsonSerializer.Serialize(
                descriptor,
                SessionJsonContext.Combined.ThreadDescriptor),
            MessageIdsPayload = JsonSerializer.Serialize(
                messageIds.Order(StringComparer.Ordinal).ToArray(),
                HpdAgentStoreJsonContext.Default.StringArray),
        };
    }

    private static ThreadJournalState DeserializeThreadState(
        HpdBaseThreadRecord record,
        RevisionToken? revision)
    {
        ThreadKey thread = new(record.SessionId, record.ThreadId);
        ThreadDescriptor descriptor = DeserializeDescriptor(record, thread);
        string[] messageIds;
        try
        {
            messageIds = JsonSerializer.Deserialize(
                    record.MessageIdsPayload,
                    HpdAgentStoreJsonContext.Default.StringArray)
                ?? throw new JsonException("Thread message IDs were JSON null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Thread '{thread.ThreadId}' contains invalid message-ID metadata.",
                exception);
        }
        if (descriptor.Generation != record.Generation ||
            descriptor.Head != record.Head)
        {
            throw new InvalidDataException(
                $"Thread '{thread.ThreadId}' descriptor and indexed head disagree.");
        }

        return new ThreadJournalState(
            descriptor.Generation,
            descriptor.Head,
            descriptor,
            messageIds.ToHashSet(StringComparer.Ordinal),
            revision);
    }

    private static ThreadDescriptor DeserializeDescriptor(
        HpdBaseThreadRecord record,
        ThreadKey thread)
    {
        try
        {
            ThreadDescriptor descriptor = JsonSerializer.Deserialize(
                    record.DescriptorPayload,
                    SessionJsonContext.Combined.ThreadDescriptor)
                ?? throw new JsonException("Thread descriptor was JSON null.");
            if (descriptor.Key != thread)
            {
                throw new InvalidDataException(
                    $"Thread descriptor '{thread.ThreadId}' contains conflicting scope.");
            }
            return descriptor;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Thread descriptor '{thread.ThreadId}' contains invalid JSON.",
                exception);
        }
    }

    private HpdBaseThreadEventBlock ToBlock(
        ThreadKey thread,
        long generation,
        long firstSequence,
        long lastSequence,
        IReadOnlyList<AgentEvent> events)
    {
        string[] payload = events.Select(EventCodec.Serialize).ToArray();
        return new HpdBaseThreadEventBlock
        {
            SessionId = thread.SessionId,
            ThreadId = thread.ThreadId,
            Generation = generation,
            FirstSequence = firstSequence,
            LastSequence = lastSequence,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = JsonSerializer.Serialize(
                payload,
                HpdAgentStoreJsonContext.Default.StringArray),
        };
    }

    private IReadOnlyList<AgentEvent> DeserializeEvents(
        HpdBaseThreadEventBlock block,
        ThreadKey thread)
    {
        string[] payloads;
        try
        {
            payloads = JsonSerializer.Deserialize(
                    block.Payload,
                    HpdAgentStoreJsonContext.Default.StringArray)
                ?? throw new JsonException("Thread event block payload was JSON null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Thread '{thread.ThreadId}' event block contains invalid JSON.",
                exception);
        }

        var events = new AgentEvent[payloads.Length];
        for (var index = 0; index < payloads.Length; index++)
        {
            long expected = block.FirstSequence + index;
            try
            {
                events[index] = EventCodec.DeserializeEvent(payloads[index]);
            }
            catch (UnknownAgentEventDiscriminatorException exception)
            {
                throw new UnknownDurableAgentEventException(
                    exception.Discriminator,
                    thread.SessionId,
                    thread.ThreadId,
                    block.Generation,
                    expected,
                    exception.CodecDigest,
                    exception);
            }

            AgentEvent evt = events[index];
            if (evt.ThreadSequenceNumber != expected ||
                evt.ThreadSequenceNumber < block.FirstSequence ||
                evt.ThreadSequenceNumber > block.LastSequence)
            {
                throw new InvalidDataException(
                    $"Thread '{thread.ThreadId}' event block has an invalid sequence.");
            }
            ThreadEventValidation.RequirePersistableScope(
                thread.SessionId,
                thread.ThreadId,
                evt);
        }

        if (events.Length == 0 ||
            events[^1].ThreadSequenceNumber != block.LastSequence)
        {
            throw new InvalidDataException(
                $"Thread '{thread.ThreadId}' event block length does not match its indexed range.");
        }
        return events;
    }

    private async ValueTask<BaseRecord<T>?> GetRecordAsync<T>(
        BaseCollection<T> collection,
        RecordId id,
        CancellationToken cancellationToken)
    {
        BaseResult<BaseRecord<T>> result = await _session.Collection(collection)
            .GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseRecord<T>> failure &&
            failure.Status == OperationStatus.NotFound)
        {
            return null;
        }
        return result.RequireValue();
    }

    private async ValueTask<Session?> LoadStoredSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        BaseRecord<HpdBaseSessionRecord>? record = await GetRecordAsync(
            HpdBaseSessionRecord.Collection,
            SessionRecordId(sessionId),
            cancellationToken).ConfigureAwait(false);
        return record is null
            ? null
            : DeserializeSession(record.Value.Payload, sessionId);
    }

    private async IAsyncEnumerable<T> QueryRecordsAsync<T>(
        BaseCollection<T> collection,
        Func<BaseQuery<T>, BaseQuery<T>> configure,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        BaseQuery<T> query = configure(_session.Collection(collection).Query());
        while (true)
        {
            BasePage<BaseRecord<T>> page = (await query.PageAsync(
                    cancellationToken).ConfigureAwait(false))
                .RequireValue();
            foreach (BaseRecord<T> item in page.Items)
                yield return item.Value;

            if (!page.Page.HasMore ||
                string.IsNullOrWhiteSpace(page.Page.NextCursor))
            {
                yield break;
            }
            query = query.ContinueFrom(page.Page.NextCursor);
        }
    }

    private async IAsyncEnumerable<BaseRecord<T>> QueryRecordPagesAsync<T>(
        BaseCollection<T> collection,
        Func<BaseQuery<T>, BaseQuery<T>> configure,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        BaseQuery<T> query = configure(_session.Collection(collection).Query());
        while (true)
        {
            BasePage<BaseRecord<T>> page = (await query.PageAsync(
                    cancellationToken).ConfigureAwait(false))
                .RequireValue();
            foreach (BaseRecord<T> item in page.Items)
                yield return item;

            if (!page.Page.HasMore ||
                string.IsNullOrWhiteSpace(page.Page.NextCursor))
            {
                yield break;
            }
            query = query.ContinueFrom(page.Page.NextCursor);
        }
    }

    private static ThreadEventBatch ToBatch(
        long generation,
        IReadOnlyList<AgentEvent> events) =>
        new(
            events,
            generation,
            events[0].ThreadSequenceNumber,
            events[^1].ThreadSequenceNumber);

    private static bool IsConcurrencyFailure(Exception exception) =>
        exception switch
        {
            BaseBatchOutcomeException batch =>
                batch.Error?.Code is BaseMutationErrorCodes.TransactionConflict
                    or BaseMutationErrorCodes.RevisionConflict
                    or BaseMutationErrorCodes.UpsertPreconditionFailed,
            BaseOperationException operation =>
                operation.Error.Code is BaseMutationErrorCodes.TransactionConflict
                    or BaseMutationErrorCodes.RevisionConflict
                    or BaseMutationErrorCodes.UpsertPreconditionFailed,
            _ => false,
        };

    private static RecordId SessionRecordId(string sessionId) =>
        RecordId.Create(HashKey("session", sessionId));

    private static RecordId ThreadRecordId(ThreadKey thread) =>
        RecordId.Create(HashKey(
            "thread",
            thread.SessionId + "\0" + thread.ThreadId));

    private static RecordId BlockRecordId(
        ThreadKey thread,
        long generation,
        long firstSequence) =>
        RecordId.Create(HashKey(
            "event-block",
            $"{thread.SessionId}\0{thread.ThreadId}\0{generation}\0{firstSequence}"));

    private static RecordId DeltaRecordId(
        ThreadKey thread,
        string eventId) =>
        RecordId.Create(HashKey(
            "thread-delta",
            $"{thread.SessionId}\0{thread.ThreadId}\0{eventId}"));

    private static RecordId EventIdentityRecordId(
        ThreadKey thread,
        long generation,
        string eventId) =>
        RecordId.Create(HashKey(
            "thread-event-identity",
            $"{thread.SessionId}\0{thread.ThreadId}\0{generation}\0{eventId}"));

    private static string HashKey(string kind, string value) =>
        kind + "_" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static void ValidateThreadKey(ThreadKey thread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thread.SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(thread.ThreadId);
    }

    private static void ValidateReadRequest(ThreadEventReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.After.Generation <= 0 ||
            request.After.SequenceNumber < 0 ||
            request.MaxBatchEventCount <= 0 ||
            request.Through is long through &&
            through < request.After.SequenceNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private sealed record ThreadJournalState(
        long Generation,
        long Head,
        ThreadDescriptor Descriptor,
        HashSet<string> MessageIds,
        RevisionToken? Revision)
    {
        public static ThreadJournalState Empty(ThreadKey thread) =>
            new(
                1,
                0,
                new ThreadDescriptor(
                    thread,
                    new ThreadDefaultAgentBinding(string.Empty),
                    null,
                    null,
                    [],
                    ThreadKind.MainAgent,
                    ThreadVisibility.Visible,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    1,
                    0,
                    0,
                    null,
                    null,
                    new Dictionary<string, object>(StringComparer.Ordinal)),
                new HashSet<string>(StringComparer.Ordinal),
                null);
    }
}
