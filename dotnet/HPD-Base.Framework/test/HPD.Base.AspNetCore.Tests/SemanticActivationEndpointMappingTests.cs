using HPD.Base.AspNetCore;

namespace HPD.Base.AspNetCore.Tests;

public sealed class SemanticActivationEndpointMappingTests
{
    public static TheoryData<OperationStatus, ErrorCategory, string, int, string> StableMappings => new()
    {
        { OperationStatus.PolicyDenied, ErrorCategory.Authorization, BaseSemanticActivationErrorCodes.Unauthorized, 404, "The requested operation is unavailable." },
        { OperationStatus.ValidationFailed, ErrorCategory.Validation, BaseSemanticActivationErrorCodes.NotInstalled, 400, "The semantic activation contract is unavailable." },
        { OperationStatus.ValidationFailed, ErrorCategory.Validation, BaseSemanticActivationErrorCodes.Invalid, 400, "The semantic activation request is invalid." },
        { OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.FingerprintConflict, 409, "The semantic identity was used with different activation semantics." },
        { OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.ActivationNotTerminal, 409, "The semantic activation is not terminal." },
        { OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.GuardLost, 409, "The activation child authority is no longer current." },
        { OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.RestoreConflict, 409, "The semantic activation restore authority changed." },
        { OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.GraphChanged, 409, "The semantic activation contract changed." },
        { OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.CapacityUnavailable, 409, "Semantic activation capacity is unavailable." },
        { OperationStatus.ValidationFailed, ErrorCategory.Validation, BaseSemanticActivationErrorCodes.BudgetExceeded, 413, "The semantic activation operation exceeded its installed limits." },
        { OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.CancelledBeforeInfluence, 408, "The semantic activation operation was cancelled." },
        { OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.CancelledRolledBack, 408, "The semantic activation operation was cancelled and rolled back." },
        { OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.AcquisitionTimeout, 503, "Semantic activation authority acquisition timed out." },
        { OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.TransactionTimeout, 503, "The semantic activation transaction timed out and was rolled back." },
        { OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.CommitIndeterminate, 503, "The semantic activation commit outcome requires reconciliation." },
        { OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.ReceiptResolutionTimeout, 503, "The semantic activation receipt could not be resolved in time." },
        { OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.ExternalPublicationPending, 503, "Semantic activation recovery publication requires reconciliation." },
        { OperationStatus.CapabilityUnavailable, ErrorCategory.Capability, BaseSemanticActivationErrorCodes.ExternalAuthorityUnavailable, 503, "Semantic activation recovery authority is unavailable." },
        { OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.MaintenanceTimeout, 503, "Semantic activation maintenance did not complete in time." },
        { OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.MaintenanceIndeterminate, 503, "Semantic activation maintenance requires reconciliation." },
        { OperationStatus.CapabilityUnavailable, ErrorCategory.Capability, BaseSemanticActivationErrorCodes.RecoveryProofUnavailable, 503, "Semantic activation recovery proof is unavailable." },
        { OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.RecoveryProofInvalid, 503, "Semantic activation recovery proof is invalid." },
        { OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.CompactionBlocked, 409, "Semantic activation compaction is not currently permitted." },
        { OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.MigrationBlocked, 409, "Semantic activation migration requirements are not satisfied." },
        { OperationStatus.Conflict, ErrorCategory.Conflict, BaseSemanticActivationErrorCodes.RemovalBlocked, 409, "The semantic activation definition cannot be removed." },
        { OperationStatus.CapabilityUnavailable, ErrorCategory.Capability, BaseSemanticActivationErrorCodes.CapabilityUnavailable, 424, "The semantic activation capability is unavailable." },
        { OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.Quarantined, 503, "Semantic activation authority is quarantined pending recovery." },
        { OperationStatus.CapabilityUnavailable, ErrorCategory.Capability, BaseSemanticActivationErrorCodes.ProviderContractInvalid, 424, "The semantic activation provider returned invalid evidence." },
        { OperationStatus.StoreError, ErrorCategory.Store, BaseSemanticActivationErrorCodes.Corrupt, 503, "Semantic activation authority requires operator attention." },
    };

    [Theory]
    [MemberData(nameof(StableMappings))]
    public void Semantic_failures_use_the_locked_non_enumerating_HTTP_contract(
        OperationStatus operationStatus, ErrorCategory category, string code, int expectedStatus, string expectedMessage)
    {
        (int status, string mappedCode, string message) = SemanticActivationAdministrationEndpoints.FailureMapping(
            operationStatus, new BaseError { Code = code, Category = category, Message = "hostile internal message" });
        status.Should().Be(expectedStatus);
        mappedCode.Should().Be(code);
        message.Should().Be(expectedMessage);
    }

    [Theory]
    [InlineData(OperationStatus.StoreError, ErrorCategory.Authorization, BaseSemanticActivationErrorCodes.Unauthorized)]
    [InlineData(OperationStatus.PolicyDenied, ErrorCategory.Store, BaseSemanticActivationErrorCodes.Unauthorized)]
    [InlineData(OperationStatus.ValidationFailed, ErrorCategory.Validation, BaseSemanticActivationErrorCodes.CommitIndeterminate)]
    [InlineData(OperationStatus.Conflict, ErrorCategory.Conflict, "base.semanticActivation.unknown")]
    public void Malformed_failure_tuples_fail_as_provider_contract_invalid(
        OperationStatus status, ErrorCategory category, string code)
    {
        (int http, string mappedCode, string message) = SemanticActivationAdministrationEndpoints.FailureMapping(
            status, new BaseError { Code = code, Category = category, Message = "must not escape" });

        http.Should().Be(424);
        mappedCode.Should().Be(BaseSemanticActivationErrorCodes.ProviderContractInvalid);
        message.Should().Be("The semantic activation provider returned invalid evidence.");
    }
}
