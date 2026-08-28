namespace HPD.Base;

internal static class BaseActivationFailureContract
{
    internal static OperationResult<T> ProviderContractInvalid<T>() => new()
    {
        Status = OperationStatus.CapabilityUnavailable,
        Error = new BaseError
        {
            Code = "base.activation.providerContractInvalid",
            Message = "The provider cannot satisfy the activation contract.",
            Category = ErrorCategory.Capability,
        },
    };
}
