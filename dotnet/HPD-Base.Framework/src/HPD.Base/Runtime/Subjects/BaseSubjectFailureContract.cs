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
        BaseSubjectErrorCodes.LifecycleContractInvalid => New(code, "The subject lifecycle contract is invalid.", ErrorCategory.Validation),
        BaseSubjectErrorCodes.LifecycleRegistrationConflict => New(code, "The subject lifecycle registration conflicts with the installed graph.", ErrorCategory.Conflict),
        BaseSubjectErrorCodes.LifecycleUnauthorized => New(code, "The subject lifecycle operation is not authorized.", ErrorCategory.Authorization),
        BaseSubjectErrorCodes.LifecycleTransitionInvalid => New(code, "The subject lifecycle transition is invalid.", ErrorCategory.Conflict),
        BaseSubjectErrorCodes.SequenceExhausted => New(code, "The subject lifecycle sequence is exhausted.", ErrorCategory.Conflict),
        BaseSubjectErrorCodes.LifetimeGenerationExhausted => New(code, "The subject lifetime generation is exhausted.", ErrorCategory.Conflict),
        BaseSubjectErrorCodes.LifecycleIncarnationUnavailable => New(code, "A subject incarnation could not be allocated.", ErrorCategory.Store),
        BaseSubjectErrorCodes.CursorInvalid => New(code, "The subject lifecycle cursor is invalid.", ErrorCategory.Validation),
        BaseSubjectErrorCodes.CursorExpired => New(code, "The subject lifecycle cursor has expired.", ErrorCategory.Conflict),
        BaseSubjectErrorCodes.CursorScopeMismatch => New(code, "The subject lifecycle cursor is not valid for this scope.", ErrorCategory.Authorization),
        BaseSubjectErrorCodes.ScopeAuthorityInvalid => New(code, "The subject lifecycle scope authority is invalid.", ErrorCategory.Authorization),
        BaseSubjectErrorCodes.CursorOvertaken => New(code, "The subject lifecycle cursor is no longer retained.", ErrorCategory.Conflict),
        BaseSubjectErrorCodes.LifecycleReconciliationUnavailable => New(code, "Subject lifecycle reconciliation is unavailable.", ErrorCategory.Capability),
        BaseSubjectErrorCodes.LifecycleProviderContractInvalid => New(code, "The provider cannot satisfy the subject lifecycle contract.", ErrorCategory.Capability),
        BaseSubjectErrorCodes.LifecycleCapacityExceeded => New(code, "Subject lifecycle capacity is unavailable.", ErrorCategory.Store),
        BaseSubjectErrorCodes.Timeout => New(code, "The subject lifecycle operation timed out.", ErrorCategory.Store),
        BaseSubjectErrorCodes.LifecycleCommitIndeterminate => New(code, "The subject lifecycle commit outcome is indeterminate.", ErrorCategory.Store),
        BaseSubjectErrorCodes.MaintenanceRequired => New(code, "Subject lifecycle maintenance must complete before this operation.", ErrorCategory.Capability),
        BaseSubjectErrorCodes.ScopeProtectionRotationConflict => New(code, "The subject scope-protection rotation conflicts with current authority.", ErrorCategory.Conflict),
        "base.activation.guardRequired" => New(code, "An activation guard is required for this subject lifecycle operation.", ErrorCategory.Conflict),
        "base.activation.guardInvalid" => New(code, "The activation guard is not valid for this subject lifecycle operation.", ErrorCategory.Conflict),
        _ => New(BaseSubjectErrorCodes.ProviderContractInvalid, "The subject validation provider returned an invalid result.", ErrorCategory.Store),
    };

    private static bool TryDescribe(OperationStatus status, string code, ErrorCategory category, out string message)
    {
        BaseError expected = Error(code);
        OperationStatus expectedStatus = code switch
        {
            BaseSubjectErrorCodes.ValidationUnavailable or BaseSubjectErrorCodes.GuaranteeUnavailable or BaseSubjectErrorCodes.LifecycleReconciliationUnavailable or BaseSubjectErrorCodes.LifecycleProviderContractInvalid or BaseSubjectErrorCodes.MaintenanceRequired => OperationStatus.CapabilityUnavailable,
            BaseSubjectErrorCodes.BudgetExceeded or BaseSubjectErrorCodes.ContractInvalid or BaseSubjectErrorCodes.ReferenceInvalid or BaseSubjectErrorCodes.LifecycleContractInvalid or BaseSubjectErrorCodes.CursorInvalid => OperationStatus.ValidationFailed,
            BaseSubjectErrorCodes.SchemaGenerationChanged or BaseSubjectErrorCodes.TransactionConflict or BaseSubjectErrorCodes.ReceiptMismatch or BaseSubjectErrorCodes.RegistrationConflict or BaseSubjectErrorCodes.LifecycleRegistrationConflict or BaseSubjectErrorCodes.LifecycleTransitionInvalid or BaseSubjectErrorCodes.SequenceExhausted or BaseSubjectErrorCodes.LifetimeGenerationExhausted or BaseSubjectErrorCodes.CursorExpired or BaseSubjectErrorCodes.CursorOvertaken or BaseSubjectErrorCodes.ScopeProtectionRotationConflict or "base.activation.guardRequired" or "base.activation.guardInvalid" => OperationStatus.Conflict,
            BaseSubjectErrorCodes.LifecycleUnauthorized or BaseSubjectErrorCodes.CursorScopeMismatch or BaseSubjectErrorCodes.ScopeAuthorityInvalid => OperationStatus.PolicyDenied,
            BaseSubjectErrorCodes.ProviderContractInvalid or BaseSubjectErrorCodes.CommitIndeterminate or BaseSubjectErrorCodes.LifecycleCapacityExceeded or BaseSubjectErrorCodes.LifecycleIncarnationUnavailable or BaseSubjectErrorCodes.Timeout or BaseSubjectErrorCodes.LifecycleCommitIndeterminate => OperationStatus.StoreError,
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
