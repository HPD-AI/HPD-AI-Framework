using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;

namespace HPD.Base.Runtime.Operations;

public interface IBaseRecordRuntime
{
    ValueTask<OperationResult<RecordPage>> ListAsync(
        string collectionId,
        RecordQuery? query,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);

    ValueTask<OperationResult<RecordEnvelope>> GetAsync(
        string collectionId,
        RecordId id,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);

    ValueTask<OperationResult<RecordEnvelope>> CreateAsync(
        string collectionId,
        RecordCreateRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);

    ValueTask<OperationResult<RecordEnvelope>> PatchAsync(
        string collectionId,
        RecordId id,
        RecordPatchRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);

    ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(
        string collectionId,
        RecordId id,
        RecordReplaceRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);

    ValueTask<OperationResult<DeleteResult>> DeleteAsync(
        string collectionId,
        RecordId id,
        RecordDeleteRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);
}
