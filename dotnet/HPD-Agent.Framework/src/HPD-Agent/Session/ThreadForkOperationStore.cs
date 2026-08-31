using System.Runtime.CompilerServices;

namespace HPD.Agent;

/// <summary>Typed immutable preparation authority embedded in a staged thread descriptor.</summary>
public sealed record ThreadPreparationDescriptor(
    string OperationId,
    ThreadKey Source,
    string RequestFingerprint,
    string? TargetSeedFingerprint = null);

/// <summary>Typed immutable preparation authority embedded in a staged isolated session.</summary>
public sealed record SessionPreparationDescriptor(
    string OperationId,
    ThreadKey Source,
    string RequestFingerprint,
    string? TargetSeedFingerprint = null);

/// <summary>Result of conditionally creating a staged isolated session.</summary>
public enum SessionPreparationResult
{
    /// <summary>The prepared session was created.</summary>
    Created,
    /// <summary>The exact same operation already prepared the session.</summary>
    ExistingOwned,
    /// <summary>The route is owned by another session or preparation request.</summary>
    Conflict
}

/// <summary>Durable multi-journal topology operation used to stage a thread fork.</summary>
public sealed record ThreadForkOperationRecord
{
    /// <summary>Gets the idempotent operation identifier.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the source parent thread.</summary>
    public required ThreadKey Source { get; init; }
    /// <summary>Gets the target parent thread.</summary>
    public required ThreadKey Target { get; init; }
    /// <summary>Gets the exact immutable source boundary.</summary>
    public required ThreadJournalCursor SourceBoundary { get; init; }
    /// <summary>Gets the immutable hash of the complete fork request.</summary>
    public required string RequestFingerprint { get; init; }
    /// <summary>Gets the effective direct-child policy.</summary>
    public required SubAgentForkPolicy SubAgentPolicy { get; init; }
    /// <summary>Gets the current durable operation phase.</summary>
    public required ThreadForkOperationStatus Status { get; init; }
    /// <summary>Gets the conditional-write revision.</summary>
    public required long Revision { get; init; }
    /// <summary>Gets all child routes prepared before parent visibility.</summary>
    public required IReadOnlyList<ThreadKey> PreparedChildren { get; init; }
    /// <summary>Gets the authoritative deterministic direct-child outcomes.</summary>
    public required IReadOnlyList<SubAgentForkChildOutcome> ChildOutcomes { get; init; }
    /// <summary>Gets the immutable target seed hash once planning is complete.</summary>
    public string? TargetSeedFingerprint { get; init; }
    /// <summary>Gets a bounded terminal or reconciliation error.</summary>
    public string? Error { get; init; }
}

/// <summary>Conditional-write requirement for one fork operation revision.</summary>
public readonly record struct ThreadForkOperationWriteCondition(long ExpectedRevision);

/// <summary>Durable authority for recoverable parent/child fork topology commits.</summary>
public interface IThreadForkOperationStore
{
    /// <summary>Gets an exact operation owned by this source journal.</summary>
    ValueTask<ThreadForkOperationRecord?> GetThreadForkOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default);
    /// <summary>Conditionally advances one operation.</summary>
    ValueTask WriteThreadForkOperationAsync(
        ThreadForkOperationRecord operation,
        ThreadForkOperationWriteCondition condition,
        CancellationToken cancellationToken = default);
    /// <summary>Reads operations that still require completion or reconciliation.</summary>
    IAsyncEnumerable<ThreadForkOperationRecord> ReadPendingThreadForkOperationsAsync(
        ThreadKey source,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the latest record for every fork operation owned by the source thread.</summary>
    IAsyncEnumerable<ThreadForkOperationRecord> ReadThreadForkOperationsAsync(
        ThreadKey source,
        CancellationToken cancellationToken = default);
}

/// <summary>Source-journal implementation of the durable fork-operation authority.</summary>
public sealed class JournalThreadForkOperationStore(ISessionStore store, ThreadKey source)
    : IThreadForkOperationStore
{
    private readonly ISessionStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ThreadKey _source = source;

    public async ValueTask<ThreadForkOperationRecord?> GetThreadForkOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(cancellationToken).ConfigureAwait(false)).Records.GetValueOrDefault(operationId);

    public async ValueTask WriteThreadForkOperationAsync(
        ThreadForkOperationRecord operation,
        ThreadForkOperationWriteCondition condition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Source != _source) throw new InvalidOperationException("thread_fork_source_mismatch");
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var state = await ReadAsync(cancellationToken).ConfigureAwait(false);
            state.Records.TryGetValue(operation.OperationId, out var current);
            var revision = current?.Revision ?? 0;
            if (revision != condition.ExpectedRevision || operation.Revision != revision + 1)
                throw new InvalidOperationException("thread_fork_operation_conflict");
            var evt = new ThreadForkOperationChangedEvent(operation)
            {
                SessionId = _source.SessionId,
                ThreadId = _source.ThreadId
            };
            try
            {
                await _store.AppendThreadEventsAsync(
                    _source,
                    [evt],
                    new ThreadAppendCondition(state.Cursor),
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (ThreadAppendConflictException) when (attempt < 15) { }
        }
        throw new InvalidOperationException("thread_fork_operation_conflict");
    }

    public async IAsyncEnumerable<ThreadForkOperationRecord> ReadPendingThreadForkOperationsAsync(
        ThreadKey source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (source != _source) yield break;
        var state = await ReadAsync(cancellationToken).ConfigureAwait(false);
        foreach (var operation in state.Records.Values.Where(static operation =>
                     operation.Status is not ThreadForkOperationStatus.Committed and
                     not ThreadForkOperationStatus.Aborted))
            yield return operation;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ThreadForkOperationRecord> ReadThreadForkOperationsAsync(
        ThreadKey source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (source != _source) yield break;
        var state = await ReadAsync(cancellationToken).ConfigureAwait(false);
        foreach (var operation in state.Records.Values.OrderBy(static value => value.OperationId, StringComparer.Ordinal))
            yield return operation;
    }

    private async ValueTask<(ThreadJournalCursor Cursor, Dictionary<string, ThreadForkOperationRecord> Records)> ReadAsync(
        CancellationToken cancellationToken)
    {
        var head = await _store.GetThreadEventHeadAsync(_source, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("thread_fork_source_missing");
        var records = new Dictionary<string, ThreadForkOperationRecord>(StringComparer.Ordinal);
        await foreach (var batch in _store.ReadThreadEventsAsync(
            _source,
            new ThreadEventReadRequest(ThreadJournalCursor.Start(head.Generation), head.ThreadSequenceNumber),
            cancellationToken).ConfigureAwait(false))
            foreach (var evt in batch.Events)
                if (evt is ThreadForkOperationChangedEvent changed)
                    records[changed.Operation.OperationId] = changed.Operation;
        return (head.Cursor, records);
    }
}

[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("THREAD_FORK_OPERATION_CHANGED")]
public sealed record ThreadForkOperationChangedEvent(ThreadForkOperationRecord Operation) : AgentEvent;

/// <summary>Preserves the latest durable fork-operation facts across a destructive journal rebase.</summary>
public sealed class ThreadForkOperationRebaseSeedProvider(ISessionStore store)
    : IThreadJournalRebaseSeedProvider
{
    private readonly ISessionStore _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentEvent>> CreateSeedEventsAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default)
    {
        var operationStore = new JournalThreadForkOperationStore(_store, thread);
        var events = new List<AgentEvent>();
        var records = new List<ThreadForkOperationRecord>();
        await foreach (var operation in operationStore.ReadThreadForkOperationsAsync(thread, cancellationToken)
                           .ConfigureAwait(false))
            records.Add(operation);
        foreach (var operation in records.OrderBy(static operation => operation.OperationId, StringComparer.Ordinal))
        {
            events.Add(new ThreadForkOperationChangedEvent(operation)
            {
                SessionId = thread.SessionId,
                ThreadId = thread.ThreadId
            });
        }
        return events;
    }
}

internal static class ThreadForkVisibility
{
    internal static async ValueTask<bool> IsSessionVisibleAsync(
        ISessionStore store,
        Session session,
        CancellationToken cancellationToken)
    {
        if (session.Preparation is { } preparation)
        {
            ThreadForkOperationRecord? preparedOperation;
            try
            {
                preparedOperation = await new JournalThreadForkOperationStore(store, preparation.Source)
                    .GetThreadForkOperationAsync(preparation.OperationId, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception) when (exception.Message == "thread_fork_source_missing")
            {
                return false;
            }
            return (preparedOperation?.Status is ThreadForkOperationStatus.Committed or ThreadForkOperationStatus.ReconciliationRequired) &&
                   string.Equals(preparedOperation.RequestFingerprint, preparation.RequestFingerprint, StringComparison.Ordinal);
        }
        if (!session.Metadata.TryGetValue("forkOperationId", out var rawOperationId) ||
            Convert.ToString(rawOperationId) is not { Length: > 0 } operationId)
            return true;
        if (!session.Metadata.TryGetValue("forkSourceSessionId", out var rawSession) ||
            !session.Metadata.TryGetValue("forkSourceThreadId", out var rawThread))
            return false;
        var source = new ThreadKey(Convert.ToString(rawSession)!, Convert.ToString(rawThread)!);
        var operation = await new JournalThreadForkOperationStore(store, source)
            .GetThreadForkOperationAsync(operationId, cancellationToken).ConfigureAwait(false);
        return operation?.Status is ThreadForkOperationStatus.Committed or ThreadForkOperationStatus.ReconciliationRequired;
    }

    internal static async ValueTask<bool> IsVisibleAsync(
        ISessionStore store,
        ThreadDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (descriptor.Preparation is { } preparation)
        {
            ThreadForkOperationRecord? preparedOperation;
            try
            {
                preparedOperation = await new JournalThreadForkOperationStore(store, preparation.Source)
                    .GetThreadForkOperationAsync(preparation.OperationId, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception) when (exception.Message == "thread_fork_source_missing")
            {
                return false;
            }
            return (preparedOperation?.Status is ThreadForkOperationStatus.Committed or ThreadForkOperationStatus.ReconciliationRequired) &&
                   string.Equals(preparedOperation.RequestFingerprint, preparation.RequestFingerprint, StringComparison.Ordinal);
        }
        if (!descriptor.Metadata.TryGetValue("forkOperationId", out var rawOperationId) ||
            Convert.ToString(rawOperationId) is not { Length: > 0 } operationId)
            return true;
        if (!descriptor.Metadata.TryGetValue("forkSourceSessionId", out var rawSession) ||
            !descriptor.Metadata.TryGetValue("forkSourceThreadId", out var rawThread))
            return false;
        var source = new ThreadKey(Convert.ToString(rawSession)!, Convert.ToString(rawThread)!);
        var operation = await new JournalThreadForkOperationStore(store, source)
            .GetThreadForkOperationAsync(operationId, cancellationToken).ConfigureAwait(false);
        return operation?.Status is ThreadForkOperationStatus.Committed or ThreadForkOperationStatus.ReconciliationRequired;
    }
}
