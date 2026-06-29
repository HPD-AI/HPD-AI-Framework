using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;
using HPD.Base.Stores;

namespace HPD.Base.Runtime.Tests;

internal sealed class FakeRevisionedRecordStore : FakeRecordStore, IRevisionedRecordStore
{
    public FakeRevisionedRecordStore(string storeId)
        : base(storeId)
    {
    }

    public int PatchIfRevisionCalls { get; private set; }
    public int ReplaceIfRevisionCalls { get; private set; }

    public ValueTask<OperationResult<RecordEnvelope>> PatchIfRevisionAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordPatchRequest request,
        RevisionToken expectedRevision,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        _ = expectedRevision;
        PatchIfRevisionCalls++;
        PatchCalls++;
        LastPatchRequest = request;
        return UpsertPayload(collection, id, request.Patch);
    }

    public ValueTask<OperationResult<RecordEnvelope>> ReplaceIfRevisionAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordReplaceRequest request,
        RevisionToken expectedRevision,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        _ = expectedRevision;
        ReplaceIfRevisionCalls++;
        ReplaceCalls++;
        LastReplaceRequest = request;
        return UpsertPayload(collection, id, request.Payload);
    }
}
