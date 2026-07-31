
namespace HPD.Base;

internal sealed class DefaultBaseOperationalFailureMapper : IBaseOperationalFailureMapper
{
    public bool TryMap(Exception exception, OperationContext operation, out BaseError error, out OperationStatus status)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(operation);

        if (exception is TimeoutException or IOException)
        {
            status = OperationStatus.StoreError;
            error = new BaseError
            {
                Code = "base.runtime.store.dependencyFailure",
                Message = "Store dependency failed while processing the operation.",
                Category = ErrorCategory.Store,
                Target = operation.CollectionId,
                CorrelationId = operation.CorrelationId,
                Store = new StoreErrorInfo
                {
                    Retryable = exception is TimeoutException
                }
            };
            return true;
        }

        error = null!;
        status = OperationStatus.StoreError;
        return false;
    }
}
