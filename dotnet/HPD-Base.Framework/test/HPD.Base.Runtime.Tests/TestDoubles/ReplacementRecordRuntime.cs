using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.Operations;

namespace HPD.Base.Runtime.Tests;

internal sealed class ReplacementRecordRuntime : IBaseRecordRuntime
{
    public ValueTask<OperationResult<RecordPage>> ListAsync(string collectionId, RecordQuery? query, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public ValueTask<OperationResult<RecordEnvelope>> GetAsync(string collectionId, RecordId id, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public ValueTask<OperationResult<RecordEnvelope>> CreateAsync(string collectionId, RecordCreateRequest request, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public ValueTask<OperationResult<RecordEnvelope>> PatchAsync(string collectionId, RecordId id, RecordPatchRequest request, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(string collectionId, RecordId id, RecordReplaceRequest request, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public ValueTask<OperationResult<DeleteResult>> DeleteAsync(string collectionId, RecordId id, RecordDeleteRequest request, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public ValueTask<OperationResult<RecordUpsertResult>> UpsertAsync(string collectionId, RecordUpsertRequest request, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public ValueTask<OperationResult<BaseRecordBatchResult>> BatchAsync(BaseRecordBatchRequest request, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
