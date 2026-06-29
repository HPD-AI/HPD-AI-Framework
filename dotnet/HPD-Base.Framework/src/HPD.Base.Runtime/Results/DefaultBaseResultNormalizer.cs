using HPD.Base.Results;
using HPD.Base.Runtime;

namespace HPD.Base.Runtime.Results;

internal sealed class DefaultBaseResultNormalizer : IBaseResultNormalizer
{
    public OperationResult<T> NormalizeStoreResult<T>(
        OperationResult<T> result,
        OperationContext operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (result.Status.IsSuccess())
        {
            return result.Value is null
                ? OperationResults.StoreError<T>(new BaseError
                {
                    Code = "base.runtime.store.nullSuccessValue",
                    Message = "Store returned a successful result without a value.",
                    Category = ErrorCategory.Store,
                    Target = operation.CollectionId,
                    CorrelationId = operation.CorrelationId
                })
                : result;
        }

        var error = result.Error ?? new BaseError
        {
            Code = ErrorCode(result.Status),
            Message = "Store returned a failed result without error details.",
            Category = ErrorCategoryFor(result.Status),
            Target = operation.CollectionId,
            CorrelationId = operation.CorrelationId
        };

        return result with
        {
            Value = default,
            Error = error
        };
    }

    private static string ErrorCode(OperationStatus status) => status switch
    {
        OperationStatus.NotFound => "base.runtime.store.notFound",
        OperationStatus.Conflict => "base.runtime.store.conflict",
        OperationStatus.ValidationFailed => "base.runtime.store.validationFailed",
        OperationStatus.PolicyDenied => "base.runtime.store.policyDenied",
        OperationStatus.Unauthorized => "base.runtime.store.unauthorized",
        OperationStatus.Unsupported => "base.runtime.store.unsupported",
        OperationStatus.CapabilityUnavailable => "base.runtime.store.capabilityUnavailable",
        OperationStatus.StoreError => "base.runtime.store.error",
        _ => "base.runtime.store.failure"
    };

    private static ErrorCategory ErrorCategoryFor(OperationStatus status) => status switch
    {
        OperationStatus.NotFound => ErrorCategory.NotFound,
        OperationStatus.Conflict => ErrorCategory.Conflict,
        OperationStatus.ValidationFailed => ErrorCategory.Validation,
        OperationStatus.PolicyDenied => ErrorCategory.Authorization,
        OperationStatus.Unauthorized => ErrorCategory.Authentication,
        OperationStatus.Unsupported => ErrorCategory.Unsupported,
        OperationStatus.CapabilityUnavailable => ErrorCategory.Capability,
        OperationStatus.StoreError => ErrorCategory.Store,
        _ => ErrorCategory.Unexpected
    };
}
