using HPD.Events;
using HPD.Base.Events;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;

namespace HPD.Base.Stores;

public interface IRevisionedRecordStore : IRecordStore
{
    ValueTask<OperationResult<RecordEnvelope>> PatchIfRevisionAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordPatchRequest request,
        RevisionToken expectedRevision,
        OperationContext context,
        CancellationToken cancellationToken = default);

    ValueTask<OperationResult<RecordEnvelope>> ReplaceIfRevisionAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordReplaceRequest request,
        RevisionToken expectedRevision,
        OperationContext context,
        CancellationToken cancellationToken = default);
}

public interface IStreamingRecordStore : IRecordStore
{
    ValueTask<OperationResult<AsyncStream<RecordEnvelope>>> OpenStreamAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional record-store capability whose successful mutations atomically append
/// one durable BASE mutation-journal entry in the same provider transaction.
/// </summary>
public interface ITransactionalMutationJournalStore : IRecordStore
{
    /// <summary>Gets the currently retained journal position range.</summary>
    ValueTask<BaseMutationJournalBounds> GetMutationJournalBoundsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Reads a bounded ascending page of committed mutation entries.</summary>
    ValueTask<BaseMutationJournalPage> ReadMutationJournalAsync(
        BaseMutationJournalReadRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Finds one committed mutation by its stable event identity.</summary>
    ValueTask<BaseMutationJournalEntry?> FindMutationJournalEntryAsync(
        string eventId,
        CancellationToken cancellationToken = default);
}
