namespace HPD.Base;

internal static class BaseSubjectFailureContract
{
    internal static BaseError NormalizeProviderError(OperationStatus status, BaseError? error)
    {
        if (error is not null && TryDescribe(status, error.Code, error.Category, out string message))
            return new BaseError { Code = error.Code, Message = message, Category = error.Category };

        return new BaseError
        {
            Code = BaseSubjectErrorCodes.ProviderContractInvalid,
            Message = "The subject validation provider returned an invalid result.",
            Category = ErrorCategory.Store,
        };
    }

    internal static OperationStatus NormalizeProviderStatus(OperationStatus status, BaseError? error) =>
        error is not null && TryDescribe(status, error.Code, error.Category, out _) ? status : OperationStatus.StoreError;

    internal static BaseError Error(string code) => code switch
    {
        BaseSubjectErrorCodes.ContractInvalid => New(code, "The subject contract is invalid.", ErrorCategory.Validation),
        BaseSubjectErrorCodes.RegistrationConflict => New(code, "The subject contract conflicts with the installed graph.", ErrorCategory.Conflict),
        BaseSubjectErrorCodes.ReferenceInvalid => New(code, "The subject reference is invalid.", ErrorCategory.Validation),
        BaseSubjectErrorCodes.ValidationUnavailable => New(code, "Subject validation is unavailable.", ErrorCategory.Capability),
        BaseSubjectErrorCodes.GuaranteeUnavailable => New(code, "The required subject validation guarantee is unavailable.", ErrorCategory.Unsupported),
        BaseSubjectErrorCodes.BudgetExceeded => New(code, "The subject validation limit was exceeded.", ErrorCategory.Validation),
        BaseSubjectErrorCodes.ProviderContractInvalid => New(code, "The subject validation provider returned an invalid result.", ErrorCategory.Store),
        BaseSubjectErrorCodes.SchemaGenerationChanged => New(code, "The subject validation authority changed.", ErrorCategory.Conflict),
        BaseSubjectErrorCodes.TransactionConflict => New(code, "The subject validation transaction conflicted.", ErrorCategory.Conflict),
        BaseSubjectErrorCodes.CommitIndeterminate => New(code, "The subject reference mutation outcome is indeterminate.", ErrorCategory.Store),
        BaseSubjectErrorCodes.ReceiptMismatch => New(code, "The mutation identity belongs to a different request.", ErrorCategory.Conflict),
        _ => New(BaseSubjectErrorCodes.ProviderContractInvalid, "The subject validation provider returned an invalid result.", ErrorCategory.Store),
    };

    private static bool TryDescribe(OperationStatus status, string code, ErrorCategory category, out string message)
    {
        BaseError expected = Error(code);
        OperationStatus expectedStatus = code switch
        {
            BaseSubjectErrorCodes.ValidationUnavailable or BaseSubjectErrorCodes.GuaranteeUnavailable => OperationStatus.CapabilityUnavailable,
            BaseSubjectErrorCodes.BudgetExceeded or BaseSubjectErrorCodes.ContractInvalid or BaseSubjectErrorCodes.ReferenceInvalid => OperationStatus.ValidationFailed,
            BaseSubjectErrorCodes.SchemaGenerationChanged or BaseSubjectErrorCodes.TransactionConflict or BaseSubjectErrorCodes.ReceiptMismatch or BaseSubjectErrorCodes.RegistrationConflict => OperationStatus.Conflict,
            BaseSubjectErrorCodes.ProviderContractInvalid or BaseSubjectErrorCodes.CommitIndeterminate => OperationStatus.StoreError,
            _ => (OperationStatus)(-1),
        };
        if (status == expectedStatus && category == expected.Category && expected.Code == code)
        {
            message = expected.Message;
            return true;
        }
        message = string.Empty;
        return false;
    }

    private static BaseError New(string code, string message, ErrorCategory category) =>
        new() { Code = code, Message = message, Category = category };
}
