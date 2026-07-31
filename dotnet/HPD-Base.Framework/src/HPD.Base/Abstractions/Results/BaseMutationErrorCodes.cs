namespace HPD.Base;

/// <summary>Defines the stable bounded public failure codes reserved by the mutation runtime.</summary>
public static class BaseMutationErrorCodes
{
    /// <summary>The batch envelope is malformed.</summary>
    public const string BatchInvalid = "base.runtime.batch.invalid";
    /// <summary>One batch item is malformed.</summary>
    public const string BatchItemInvalid = "base.runtime.batch.itemInvalid";
    /// <summary>The batch exceeds the operation-count limit.</summary>
    public const string BatchOperationLimitExceeded = "base.runtime.batch.operationLimitExceeded";
    /// <summary>The batch exceeds the canonical payload-size limit.</summary>
    public const string BatchPayloadLimitExceeded = "base.runtime.batch.payloadLimitExceeded";
    /// <summary>The batch contains a duplicate item handle.</summary>
    public const string BatchDuplicateItem = "base.runtime.batch.duplicateItem";
    /// <summary>The requested batch execution mode is unsupported.</summary>
    public const string BatchModeUnsupported = "base.runtime.batch.modeUnsupported";
    /// <summary>An atomic batch resolves to more than one store instance.</summary>
    public const string BatchMultipleStores = "base.runtime.batch.multipleStores";
    /// <summary>The resolved provider cannot execute an atomic batch.</summary>
    public const string BatchAtomicUnsupported = "base.runtime.batch.atomicUnsupported";
    /// <summary>The resolved provider cannot execute a cross-collection atomic batch.</summary>
    public const string BatchCrossCollectionUnsupported = "base.runtime.batch.crossCollectionUnsupported";
    /// <summary>A provisional batch item was rolled back.</summary>
    public const string BatchRolledBack = "base.runtime.batch.rolledBack";
    /// <summary>A batch item was skipped after an earlier failure.</summary>
    public const string BatchSkipped = "base.runtime.batch.skipped";
    /// <summary>The provider cannot determine whether the batch committed.</summary>
    public const string BatchIndeterminate = "base.runtime.batch.indeterminate";
    /// <summary>The upsert request is malformed.</summary>
    public const string UpsertInvalid = "base.runtime.upsert.invalid";
    /// <summary>The resolved provider cannot execute atomic upsert.</summary>
    public const string UpsertUnsupported = "base.runtime.upsert.unsupported";
    /// <summary>The selected upsert branch violates its existence precondition.</summary>
    public const string UpsertPreconditionFailed = "base.runtime.upsert.preconditionFailed";
    /// <summary>An expected record revision does not match.</summary>
    public const string RevisionConflict = "base.runtime.revision.conflict";
    /// <summary>The provider rejected an atomic transaction because of a concurrency conflict.</summary>
    public const string TransactionConflict = "base.runtime.transaction.conflict";
    /// <summary>The provider transaction exceeded its bounded lifetime.</summary>
    public const string TransactionTimeout = "base.runtime.transaction.timeout";
}
