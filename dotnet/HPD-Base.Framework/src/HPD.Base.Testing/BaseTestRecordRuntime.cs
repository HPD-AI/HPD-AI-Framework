using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Runtime.Operations;

namespace HPD.Base.Testing;

internal sealed class BaseTestRecordRuntime(
    IBaseRecordRuntime inner,
    BaseTestFaults faults) : IBaseRecordRuntime
{
    public ValueTask<OperationResult<RecordPage>> ListAsync(
        string collectionId,
        RecordQuery? query,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        inner.ListAsync(collectionId, query, principal, operation, cancellationToken);

    public ValueTask<OperationResult<RecordEnvelope>> GetAsync(
        string collectionId,
        RecordId id,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        inner.GetAsync(collectionId, id, principal, operation, cancellationToken);

    public ValueTask<OperationResult<RecordEnvelope>> CreateAsync(
        string collectionId,
        RecordCreateRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        inner.CreateAsync(collectionId, request, principal, operation, cancellationToken);

    public ValueTask<OperationResult<RecordEnvelope>> PatchAsync(
        string collectionId,
        RecordId id,
        RecordPatchRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        inner.PatchAsync(collectionId, id, request, principal, operation, cancellationToken);

    public ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(
        string collectionId,
        RecordId id,
        RecordReplaceRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        inner.ReplaceAsync(collectionId, id, request, principal, operation, cancellationToken);

    public ValueTask<OperationResult<DeleteResult>> DeleteAsync(
        string collectionId,
        RecordId id,
        RecordDeleteRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(collectionId, id, request, principal, operation, cancellationToken);

    public ValueTask<OperationResult<RecordUpsertResult>> UpsertAsync(
        string collectionId,
        RecordUpsertRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default) =>
        inner.UpsertAsync(collectionId, request, principal, operation, cancellationToken);

    public ValueTask<OperationResult<BaseRecordBatchResult>> BatchAsync(
        BaseRecordBatchRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default)
    {
        if (request.Mode == BaseRecordBatchExecutionMode.Atomic &&
            faults.TakeAtomicCommitFailure())
        {
            return ValueTask.FromResult(new OperationResult<BaseRecordBatchResult>
            {
                Status = OperationStatus.StoreError,
                Error = BaseTestFaults.AtomicCommitError(),
            });
        }

        return inner.BatchAsync(request, principal, operation, cancellationToken);
    }
}
