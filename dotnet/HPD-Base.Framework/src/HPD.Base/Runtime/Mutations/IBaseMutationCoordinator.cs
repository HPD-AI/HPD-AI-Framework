
namespace HPD.Base;

internal interface IBaseMutationCoordinator
{
    /// <summary>Executes the execute single async operation.</summary>
    ValueTask<OperationResult<BaseRecordBatchItemResult>> ExecuteSingleAsync(
        BaseRecordBatchItem item,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken);

    /// <summary>Executes the execute batch async operation.</summary>
    ValueTask<OperationResult<BaseRecordBatchResult>> ExecuteBatchAsync(
        BaseRecordBatchRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken);
}
