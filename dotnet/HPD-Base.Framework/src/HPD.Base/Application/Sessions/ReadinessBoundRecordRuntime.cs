namespace HPD.Base;

internal sealed class ReadinessBoundRecordRuntime(
    IBaseRecordRuntime inner,
    IHPDBaseApplication application) : IBaseRecordRuntime
{
    /// <summary>Executes the list async operation.</summary>
    public ValueTask<OperationResult<RecordPage>> ListAsync(string collectionId, RecordQuery? query, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        Ready() ? inner.ListAsync(collectionId, query, principal, operation, cancellationToken) : Failure<RecordPage>();

    /// <summary>Executes the get async operation.</summary>
    public ValueTask<OperationResult<RecordEnvelope>> GetAsync(string collectionId, RecordId id, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        Ready() ? inner.GetAsync(collectionId, id, principal, operation, cancellationToken) : Failure<RecordEnvelope>();

    /// <summary>Executes the create async operation.</summary>
    public ValueTask<OperationResult<RecordEnvelope>> CreateAsync(string collectionId, RecordCreateRequest request, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        Ready() ? inner.CreateAsync(collectionId, request, principal, operation, cancellationToken) : Failure<RecordEnvelope>();

    /// <summary>Executes the patch async operation.</summary>
    public ValueTask<OperationResult<RecordEnvelope>> PatchAsync(string collectionId, RecordId id, RecordPatchRequest request, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        Ready() ? inner.PatchAsync(collectionId, id, request, principal, operation, cancellationToken) : Failure<RecordEnvelope>();

    /// <summary>Executes the replace async operation.</summary>
    public ValueTask<OperationResult<RecordEnvelope>> ReplaceAsync(string collectionId, RecordId id, RecordReplaceRequest request, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        Ready() ? inner.ReplaceAsync(collectionId, id, request, principal, operation, cancellationToken) : Failure<RecordEnvelope>();

    /// <summary>Executes the delete async operation.</summary>
    public ValueTask<OperationResult<DeleteResult>> DeleteAsync(string collectionId, RecordId id, RecordDeleteRequest request, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        Ready() ? inner.DeleteAsync(collectionId, id, request, principal, operation, cancellationToken) : Failure<DeleteResult>();

    /// <summary>Executes the upsert async operation.</summary>
    public ValueTask<OperationResult<RecordUpsertResult>> UpsertAsync(string collectionId, RecordUpsertRequest request, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        Ready() ? inner.UpsertAsync(collectionId, request, principal, operation, cancellationToken) : Failure<RecordUpsertResult>();

    /// <summary>Executes the batch async operation.</summary>
    public ValueTask<OperationResult<BaseRecordBatchResult>> BatchAsync(BaseRecordBatchRequest request, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken = default) =>
        Ready() ? inner.BatchAsync(request, principal, operation, cancellationToken) : Failure<BaseRecordBatchResult>();

    private bool Ready() => application.CurrentReadiness.State == BaseApplicationReadinessState.Ready;

    private static ValueTask<OperationResult<T>> Failure<T>() => ValueTask.FromResult(
        OperationResults.CapabilityUnavailable<T>(new BaseError
        {
            Code = "base.application.notReady",
            Message = "HPD.BASE is not ready.",
            Category = ErrorCategory.Capability,
        }));
}
