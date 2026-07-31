using HPD.Base;

namespace HPD.Base.Tests;

internal sealed class ThrowingRecordStore : IRecordStore
{
    private readonly Exception _exception;

    public ThrowingRecordStore(string storeId, Exception exception)
    {
        _exception = exception;
        Capabilities = new FakeRecordStore(storeId).Capabilities;
    }

    public StoreCapabilityDescriptor Capabilities { get; }

    public ValueTask<OperationResult<RecordPage>> ListAsync(CollectionDefinition collection, RecordQuery query, OperationContext context, CancellationToken cancellationToken = default) =>
        throw _exception;

    public ValueTask<OperationResult<RecordEnvelope>> GetAsync(CollectionDefinition collection, RecordId id, OperationContext context, CancellationToken cancellationToken = default) =>
        throw _exception;

    public ValueTask<OperationResult<RecordEnvelope>> CreateAsync(CollectionDefinition collection, RecordCreateRequest request, OperationContext context, CancellationToken cancellationToken = default) =>
        throw _exception;

    public ValueTask<OperationResult<RecordEnvelope>> PatchAsync(CollectionDefinition collection, RecordId id, RecordPatchRequest request, OperationContext context, CancellationToken cancellationToken = default) =>
        throw _exception;

    public ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(CollectionDefinition collection, RecordId id, RecordReplaceRequest request, OperationContext context, CancellationToken cancellationToken = default) =>
        throw _exception;

    public ValueTask<OperationResult<DeleteResult>> DeleteAsync(CollectionDefinition collection, RecordId id, RecordDeleteRequest request, OperationContext context, CancellationToken cancellationToken = default) =>
        throw _exception;
}
