
namespace HPD.Base;

internal interface IBaseMutationCoordinator
{
    ValueTask<OperationResult<BaseRecordBatchItemResult>> ExecuteSingleAsync(
        BaseRecordBatchItem item,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken);

    ValueTask<OperationResult<BaseRecordBatchResult>> ExecuteBatchAsync(
        BaseRecordBatchRequest request,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken);
}
