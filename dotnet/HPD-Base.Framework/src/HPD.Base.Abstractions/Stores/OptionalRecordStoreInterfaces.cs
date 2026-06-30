using HPD.Events;
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
