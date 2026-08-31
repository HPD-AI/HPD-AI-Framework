using System.Runtime.CompilerServices;

namespace HPD.Agent;

/// <summary>Durable multi-journal topology operation used to stage a thread fork.</summary>
public sealed record ThreadForkOperationRecord
{
    public required string OperationId { get; init; }
    public required ThreadKey Source { get; init; }
    public required ThreadKey Target { get; init; }
    public required ThreadJournalCursor SourceBoundary { get; init; }
    public required SubAgentForkPolicy SubAgentPolicy { get; init; }
    public required ThreadForkOperationStatus Status { get; init; }
    public required long Revision { get; init; }
    public required IReadOnlyList<ThreadKey> PreparedChildren { get; init; }
    public string? Error { get; init; }
}

public readonly record struct ThreadForkOperationWriteCondition(long ExpectedRevision);

/// <summary>Durable authority for recoverable parent/child fork topology commits.</summary>
public interface IThreadForkOperationStore
{
    ValueTask<ThreadForkOperationRecord?> GetThreadForkOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default);
    ValueTask WriteThreadForkOperationAsync(
        ThreadForkOperationRecord operation,
        ThreadForkOperationWriteCondition condition,
        CancellationToken cancellationToken = default);
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
        await foreach (var operation in operationStore.ReadThreadForkOperationsAsync(thread, cancellationToken)
                           .ConfigureAwait(false))
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
    internal static async ValueTask<bool> IsVisibleAsync(
        ISessionStore store,
        ThreadDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (!descriptor.Metadata.TryGetValue("forkOperationId", out var rawOperationId) ||
            Convert.ToString(rawOperationId) is not { Length: > 0 } operationId)
            return true;
        if (!descriptor.Metadata.TryGetValue("forkSourceSessionId", out var rawSession) ||
            !descriptor.Metadata.TryGetValue("forkSourceThreadId", out var rawThread))
            return false;
        var source = new ThreadKey(Convert.ToString(rawSession)!, Convert.ToString(rawThread)!);
        var operation = await new JournalThreadForkOperationStore(store, source)
            .GetThreadForkOperationAsync(operationId, cancellationToken).ConfigureAwait(false);
        return operation?.Status == ThreadForkOperationStatus.Committed;
    }
}
