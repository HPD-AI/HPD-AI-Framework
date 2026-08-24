namespace HPD.Base;

/// <summary>Defines stable semantic activation error codes.</summary>
public static class BaseSemanticActivationErrorCodes
{
    /// <summary>Authorization was not established.</summary>
    public const string Unauthorized = "base.semanticActivation.unauthorized";
    /// <summary>The semantic definition is unavailable.</summary>
    public const string NotInstalled = "base.semanticActivation.notInstalled";
    /// <summary>The request is invalid.</summary>
    public const string Invalid = "base.semanticActivation.invalid";
    /// <summary>The identity conflicts with prior semantics.</summary>
    public const string FingerprintConflict = "base.semanticActivation.fingerprintConflict";
    /// <summary>The mapped activation is not terminal.</summary>
    public const string ActivationNotTerminal = "base.semanticActivation.activationNotTerminal";
    /// <summary>The guarded parent authority was lost.</summary>
    public const string GuardLost = "base.semanticActivation.guardLost";
    /// <summary>Restore authority changed.</summary>
    public const string RestoreConflict = "base.semanticActivation.restoreConflict";
    /// <summary>Graph authority changed.</summary>
    public const string GraphChanged = "base.semanticActivation.graphChanged";
    /// <summary>Installed capacity is unavailable.</summary>
    public const string CapacityUnavailable = "base.semanticActivation.capacityUnavailable";
    /// <summary>An installed budget was exceeded.</summary>
    public const string BudgetExceeded = "base.semanticActivation.budgetExceeded";
    /// <summary>Cancellation occurred before provider influence.</summary>
    public const string CancelledBeforeInfluence = "base.semanticActivation.cancelledBeforeInfluence";
    /// <summary>Cancellation produced a confirmed rollback.</summary>
    public const string CancelledRolledBack = "base.semanticActivation.cancelledRolledBack";
    /// <summary>Authority acquisition exceeded its deadline.</summary>
    public const string AcquisitionTimeout = "base.semanticActivation.acquisitionTimeout";
    /// <summary>The provider transaction timed out and rolled back.</summary>
    public const string TransactionTimeout = "base.semanticActivation.transactionTimeout";
    /// <summary>The commit outcome requires receipt reconciliation.</summary>
    public const string CommitIndeterminate = "base.semanticActivation.commitIndeterminate";
    /// <summary>Receipt resolution exceeded its deadline.</summary>
    public const string ReceiptResolutionTimeout = "base.semanticActivation.receiptResolutionTimeout";
    /// <summary>Compaction preconditions are not satisfied.</summary>
    public const string CompactionBlocked = "base.semanticActivation.compactionBlocked";
    /// <summary>Migration preconditions are not satisfied.</summary>
    public const string MigrationBlocked = "base.semanticActivation.migrationBlocked";
    /// <summary>Removal preconditions are not satisfied.</summary>
    public const string RemovalBlocked = "base.semanticActivation.removalBlocked";
    /// <summary>The provider capability is unavailable.</summary>
    public const string CapabilityUnavailable = "base.semanticActivation.capabilityUnavailable";
    /// <summary>Provider evidence violated the contract.</summary>
    public const string ProviderContractInvalid = "base.semanticActivation.providerContractInvalid";
    /// <summary>Semantic authority is corrupt.</summary>
    public const string Corrupt = "base.semanticActivation.corrupt";
    /// <summary>Maintenance requires reconciliation.</summary>
    public const string MaintenanceIndeterminate = "base.semanticActivation.maintenanceIndeterminate";
    /// <summary>Bounded maintenance did not complete before its effective deadline.</summary>
    public const string MaintenanceTimeout = "base.semanticActivation.maintenanceTimeout";
    /// <summary>The external recovery publication remains durably pending after local commit.</summary>
    public const string ExternalPublicationPending = "base.semanticActivation.externalPublicationPending";
    /// <summary>The installed external recovery authority is unavailable.</summary>
    public const string ExternalAuthorityUnavailable = "base.semanticActivation.externalAuthorityUnavailable";
    /// <summary>The authenticated external recovery proof is invalid.</summary>
    public const string RecoveryProofInvalid = "base.semanticActivation.recoveryProofInvalid";
    /// <summary>The required external recovery proof is unavailable.</summary>
    public const string RecoveryProofUnavailable = "base.semanticActivation.recoveryProofUnavailable";
    /// <summary>Semantic authority is quarantined pending recovery.</summary>
    public const string Quarantined = "base.semanticActivation.quarantined";
}
