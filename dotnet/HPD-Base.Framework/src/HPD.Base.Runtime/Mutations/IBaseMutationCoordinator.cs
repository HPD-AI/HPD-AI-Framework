using HPD.Base.Policy;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;

namespace HPD.Base.Runtime.Mutations;

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
