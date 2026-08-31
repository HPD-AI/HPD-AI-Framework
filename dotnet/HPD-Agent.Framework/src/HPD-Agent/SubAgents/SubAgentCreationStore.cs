using System.Runtime.CompilerServices;

namespace HPD.Agent;

/// <summary>Uniquely identifies one model-issued child creation under its parent.</summary>
public sealed record SubAgentCreationKey(
    ThreadKey Parent,
    string ParentToolCallId,
    CapabilityId CapabilityId);

/// <summary>Describes the immutable payload used to allocate a durable child.</summary>
public sealed record SubAgentCreationRequest
{
    /// <summary>Gets the declared role name.</summary>
    public required string RoleName { get; init; }
    /// <summary>Gets the child agent definition identifier.</summary>
    public required string ChildAgentId { get; init; }
    /// <summary>Gets the resolved child topology.</summary>
    public required SubAgentCreationContext Context { get; init; }
    /// <summary>Gets a stable fingerprint of the initial semantic input.</summary>
    public required string InputFingerprint { get; init; }
}

/// <summary>Identifies the durable phase reached by a child creation.</summary>
public enum SubAgentCreationPhase
{
    /// <summary>Identity and exact routes have been allocated.</summary>
    Reserved,
    /// <summary>The child thread exists at the allocated route.</summary>
    ChildCreated,
    /// <summary>The parent registry contains the allocated child.</summary>
    Registered,
    /// <summary>The initial execution has acquired execution authority.</summary>
    InitialExecutionAdmitted,
    /// <summary>The initial execution reached a durable terminal result.</summary>
    Terminal,
    /// <summary>Automatic recovery could not safely complete registration.</summary>
    ReconciliationRequired
}

/// <summary>One durable creation allocation and its latest recovery phase.</summary>
public sealed record SubAgentCreationRecord
{
    /// <summary>Gets the idempotency key.</summary>
    public required SubAgentCreationKey Key { get; init; }
    /// <summary>Gets the immutable allocation request.</summary>
    public required SubAgentCreationRequest Request { get; init; }
    /// <summary>Gets the allocated parent-local identifier.</summary>
    public required SubAgentLocalId LocalId { get; init; }
    /// <summary>Gets the exact allocated child route.</summary>
    public required ThreadKey ChildThread { get; init; }
    /// <summary>Gets the semantic creation invocation identifier.</summary>
    public required string InvocationId { get; init; }
    /// <summary>Gets the initial exclusive execution identifier.</summary>
    public required string ThreadExecutionId { get; init; }
    /// <summary>Gets the latest durable phase.</summary>
    public required SubAgentCreationPhase Phase { get; init; }
    /// <summary>Gets the record revision used by conditional updates.</summary>
    public required long Revision { get; init; }
    /// <summary>Gets the allocation time.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
    /// <summary>Gets a terminal status when initial execution completed.</summary>
    public SubAgentOperationStatus? TerminalStatus { get; init; }
    /// <summary>Gets bounded terminal output when available.</summary>
    public string? TerminalOutput { get; init; }
    /// <summary>Gets the durable background-operation receipt when this creation ran asynchronously.</summary>
    public string? AgentOperationId { get; init; }
    /// <summary>Gets a stable terminal or reconciliation error.</summary>
    public SubAgentOperationError? Error { get; init; }
}

/// <summary>Returns an existing allocation or a newly committed reservation.</summary>
public sealed record SubAgentCreationReservationResult(
    SubAgentCreationRecord Record,
    bool Created);

/// <summary>Condition used to advance one creation state machine.</summary>
public readonly record struct SubAgentCreationWriteCondition(long ExpectedRevision);

/// <summary>Durable authority for child allocation, replay, and crash recovery.</summary>
public interface ISubAgentCreationStore
{
    /// <summary>Atomically allocates or returns one creation identity.</summary>
    ValueTask<SubAgentCreationReservationResult> TryReserveSubAgentCreationAsync(
        SubAgentCreationKey key,
        SubAgentCreationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the latest record for an exact creation key.</summary>
    ValueTask<SubAgentCreationRecord?> GetSubAgentCreationAsync(
        SubAgentCreationKey key,
        CancellationToken cancellationToken = default);

    /// <summary>Reads nonterminal creations owned by one parent without scanning sessions.</summary>
    IAsyncEnumerable<SubAgentCreationRecord> ReadPendingSubAgentCreationsAsync(
        ThreadKey parent,
        CancellationToken cancellationToken = default);

    /// <summary>Reads every latest creation receipt that must survive compaction for idempotent replay.</summary>
    IAsyncEnumerable<SubAgentCreationRecord> ReadSubAgentCreationsAsync(
        ThreadKey parent,
        CancellationToken cancellationToken = default);

    /// <summary>Conditionally advances a creation record.</summary>
    ValueTask WriteSubAgentCreationAsync(
        SubAgentCreationRecord record,
        SubAgentCreationWriteCondition condition,
        CancellationToken cancellationToken = default);
}

/// <summary>Durable parent-journal implementation of <see cref="ISubAgentCreationStore"/>.</summary>
public sealed class JournalSubAgentCreationStore(ISessionStore store) : ISubAgentCreationStore
{
    private readonly ISessionStore _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async ValueTask<SubAgentCreationReservationResult> TryReserveSubAgentCreationAsync(
        SubAgentCreationKey key,
        SubAgentCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(request);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var projection = await SubAgentCreationProjection.ReadAsync(
                _store, key.Parent, cancellationToken).ConfigureAwait(false);
            if (projection.Records.TryGetValue(key, out var existing))
            {
                if (existing.Request != request)
                    throw new InvalidOperationException("subagent_creation_key_payload_conflict");
                return new SubAgentCreationReservationResult(existing, Created: false);
            }

            var ordinal = projection.Records.Values.Count(record =>
                string.Equals(record.Request.RoleName, request.RoleName, StringComparison.Ordinal)) + 1;
            var localId = new SubAgentLocalId($"{Normalize(request.RoleName)}-{ordinal}");
            var invocationId = Guid.NewGuid().ToString("N");
            var sessionId = request.Context == SubAgentCreationContext.Isolated
                ? $"subagent/{Normalize(request.RoleName)}/{invocationId[..12]}"
                : key.Parent.SessionId;
            var record = new SubAgentCreationRecord
            {
                Key = key,
                Request = request,
                LocalId = localId,
                ChildThread = new ThreadKey(
                    sessionId,
                    $"subagent/{Normalize(request.RoleName)}/{Normalize(localId.Value)}/{invocationId[..12]}"),
                InvocationId = invocationId,
                ThreadExecutionId = Guid.NewGuid().ToString("N"),
                AgentOperationId = $"subagent-{invocationId}",
                Phase = SubAgentCreationPhase.Reserved,
                Revision = 1,
                CreatedAt = DateTimeOffset.UtcNow
            };
            try
            {
                await AppendAsync(key.Parent, new SubAgentCreationReservedEvent(record), projection.Cursor, cancellationToken)
                    .ConfigureAwait(false);
                return new SubAgentCreationReservationResult(record, Created: true);
            }
            catch (ThreadAppendConflictException) when (attempt < 15) { }
        }
        throw new InvalidOperationException("subagent_creation_conflict");
    }

    /// <inheritdoc />
    public async ValueTask<SubAgentCreationRecord?> GetSubAgentCreationAsync(
        SubAgentCreationKey key,
        CancellationToken cancellationToken = default) =>
        (await SubAgentCreationProjection.ReadAsync(_store, key.Parent, cancellationToken).ConfigureAwait(false))
        .Records.GetValueOrDefault(key);

    /// <inheritdoc />
    public async IAsyncEnumerable<SubAgentCreationRecord> ReadPendingSubAgentCreationsAsync(
        ThreadKey parent,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var projection = await SubAgentCreationProjection.ReadAsync(_store, parent, cancellationToken)
            .ConfigureAwait(false);
        foreach (var record in projection.Records.Values
                     .Where(static record => record.Phase is not SubAgentCreationPhase.Terminal)
                     .OrderBy(static record => record.LocalId.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SubAgentCreationRecord> ReadSubAgentCreationsAsync(
        ThreadKey parent,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var projection = await SubAgentCreationProjection.ReadAsync(_store, parent, cancellationToken)
            .ConfigureAwait(false);
        foreach (var record in projection.Records.Values.OrderBy(static record => record.LocalId.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
        }
    }

    /// <inheritdoc />
    public async ValueTask WriteSubAgentCreationAsync(
        SubAgentCreationRecord record,
        SubAgentCreationWriteCondition condition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var projection = await SubAgentCreationProjection.ReadAsync(
                _store, record.Key.Parent, cancellationToken).ConfigureAwait(false);
            if (!projection.Records.TryGetValue(record.Key, out var current))
                throw new InvalidOperationException("subagent_creation_not_found");
            if (current.Revision != condition.ExpectedRevision)
                throw new InvalidOperationException("subagent_creation_write_conflict");
            if (record.Revision != current.Revision + 1)
                throw new InvalidOperationException("subagent_creation_revision_invalid");
            try
            {
                await AppendAsync(record.Key.Parent, new SubAgentCreationAdvancedEvent(record), projection.Cursor, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (ThreadAppendConflictException) when (attempt < 15) { }
        }
        throw new InvalidOperationException("subagent_creation_write_conflict");
    }

    private async ValueTask AppendAsync(
        ThreadKey parent,
        AgentEvent evt,
        ThreadJournalCursor cursor,
        CancellationToken cancellationToken) =>
        await _store.AppendThreadEventsAsync(
            parent,
            [evt with { SessionId = parent.SessionId, ThreadId = parent.ThreadId }],
            new ThreadAppendCondition(cursor),
            cancellationToken).ConfigureAwait(false);

    private static string Normalize(string value) => new(value.Trim().ToLowerInvariant()
        .Select(static character => char.IsLetterOrDigit(character) ? character : '-')
        .ToArray());
}

/// <summary>Commits a newly allocated child creation.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SUBAGENT_CREATION_RESERVED")]
public sealed record SubAgentCreationReservedEvent(SubAgentCreationRecord Record) : AgentEvent;

/// <summary>Commits an idempotent phase transition for a child creation.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SUBAGENT_CREATION_ADVANCED")]
public sealed record SubAgentCreationAdvancedEvent(SubAgentCreationRecord Record) : AgentEvent;

internal sealed record SubAgentCreationProjection(
    ThreadJournalCursor Cursor,
    IReadOnlyDictionary<SubAgentCreationKey, SubAgentCreationRecord> Records)
{
    internal static async ValueTask<SubAgentCreationProjection> ReadAsync(
        ISessionStore store,
        ThreadKey parent,
        CancellationToken cancellationToken)
    {
        var head = await store.GetThreadEventHeadAsync(parent, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("subagent_parent_not_found");
        var records = new Dictionary<SubAgentCreationKey, SubAgentCreationRecord>();
        await foreach (var batch in store.ReadThreadEventsAsync(
            parent,
            new ThreadEventReadRequest(ThreadJournalCursor.Start(head.Generation), head.ThreadSequenceNumber),
            cancellationToken).ConfigureAwait(false))
        {
            foreach (var evt in batch.Events)
            {
                switch (evt)
                {
                    case SubAgentCreationReservedEvent reserved:
                        records[reserved.Record.Key] = reserved.Record;
                        break;
                    case SubAgentCreationAdvancedEvent advanced:
                        records[advanced.Record.Key] = advanced.Record;
                        break;
                    case SubAgentRegistrySeedEvent seed:
                        foreach (var record in seed.PendingCreations)
                            records[record.Key] = record;
                        break;
                }
            }
        }
        return new SubAgentCreationProjection(head.Cursor, records);
    }
}
