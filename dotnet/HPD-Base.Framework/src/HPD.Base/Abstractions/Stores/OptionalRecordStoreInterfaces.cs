using HPD.Events;

namespace HPD.Base;

/// <summary>Defines the istreaming record store contract.</summary>
public interface IStreamingRecordStore : IRecordStore
{
    /// <summary>Executes the open stream async operation.</summary>
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
