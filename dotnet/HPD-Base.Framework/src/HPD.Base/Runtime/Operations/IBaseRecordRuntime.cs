
namespace HPD.Base;

/// <summary>Executes portable record reads and canonical Runtime-owned mutations.</summary>
public interface IBaseRecordRuntime
{
    /// <summary>Lists records visible to the principal.</summary>
    ValueTask<OperationResult<RecordPage>> ListAsync(
        string collectionId,
        RecordQuery? query,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);

    /// <summary>Gets one record visible to the principal.</summary>
    ValueTask<OperationResult<RecordEnvelope>> GetAsync(
        string collectionId,
        RecordId id,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);

    /// <summary>Creates one record through the canonical mutation processor.</summary>
    ValueTask<OperationResult<RecordEnvelope>> CreateAsync(
        string collectionId,
        RecordCreateRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);

    /// <summary>Patches one record through the canonical mutation processor.</summary>
    ValueTask<OperationResult<RecordEnvelope>> PatchAsync(
        string collectionId,
        RecordId id,
        RecordPatchRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces one record through the canonical mutation processor.</summary>
    ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(
        string collectionId,
        RecordId id,
        RecordReplaceRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes one record through the canonical mutation processor.</summary>
    ValueTask<OperationResult<DeleteResult>> DeleteAsync(
        string collectionId,
        RecordId id,
        RecordDeleteRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically creates or updates one record by its stable identifier.</summary>
    /// <param name="collectionId">The target collection identifier.</param>
    /// <param name="request">The closed record-ID upsert request.</param>
    /// <param name="principal">The principal evaluated inside the provider mutation boundary.</param>
    /// <param name="operation">The aggregate operation context.</param>
    /// <param name="cancellationToken">Cancellation requested before confirmed commit.</param>
    /// <returns>The committed branch and safely projected record.</returns>
    ValueTask<OperationResult<RecordUpsertResult>> UpsertAsync(
        string collectionId,
        RecordUpsertRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a bounded ordered record mutation batch.</summary>
    /// <param name="request">The typed batch request and execution mode.</param>
    /// <param name="principal">The one principal shared by every batch item.</param>
    /// <param name="operation">The stable aggregate operation context.</param>
    /// <param name="cancellationToken">Cancellation requested before confirmed commit.</param>
    /// <returns>The ordered aggregate and per-item commit dispositions.</returns>
    ValueTask<OperationResult<BaseRecordBatchResult>> BatchAsync(
        BaseRecordBatchRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);
}
