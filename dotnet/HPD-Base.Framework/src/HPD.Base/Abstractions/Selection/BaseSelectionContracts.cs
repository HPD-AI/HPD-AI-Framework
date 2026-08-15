using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Identifies the canonical mutation performed for every selected record.</summary>
public enum BaseSelectionMutationKind
{
    /// <summary>Applies one fixed merge patch to every selected record.</summary>
    MergePatch,
    /// <summary>Deletes every selected record.</summary>
    Delete,
}

/// <summary>Identifies the audience of an explicitly projected selection endpoint.</summary>
public enum BaseSelectionEndpointAudience
{
    /// <summary>Maps through an authenticated application endpoint group.</summary>
    Application,
    /// <summary>Maps through an authenticated control-plane endpoint group.</summary>
    ControlPlane,
}

/// <summary>Identifies the portable serializability proof supplied by a store.</summary>
public enum BaseAtomicSelectionIsolationClass
{
    /// <summary>The provider supplies native serializable isolation.</summary>
    NativeSerializable,
    /// <summary>The provider owns a serializing write transaction before selection.</summary>
    WriteOwningSerializable,
    /// <summary>The provider validates complete logical range evidence at commit.</summary>
    OptimisticRangeValidatedSerializable,
}

/// <summary>Identifies logical constraint-attribution classes certified by a store.</summary>
[Flags]
public enum BaseConstraintAttributionClass
{
    /// <summary>No logical constraint attribution is certified.</summary>
    None = 0,
    /// <summary>Record identity conflicts are attributed.</summary>
    RecordIdentity = 1,
    /// <summary>Unique index conflicts are attributed.</summary>
    UniqueIndex = 2,
    /// <summary>Relation conflicts are attributed.</summary>
    Relation = 4,
}

/// <summary>Identifies a logical access shape retained for serializability validation.</summary>
public enum BaseIndexAccessShape
{
    /// <summary>One record-ID point.</summary>
    RecordIdPoint,
    /// <summary>One record-ID range.</summary>
    RecordIdRange,
    /// <summary>One logical-index point.</summary>
    LogicalIndexPoint,
    /// <summary>One logical-index range.</summary>
    LogicalIndexRange,
    /// <summary>One logical-index prefix range.</summary>
    LogicalIndexPrefixRange,
    /// <summary>One collection-generation scan.</summary>
    CollectionGenerationScan,
}

/// <summary>Identifies the revision precondition evaluated for every selected record.</summary>
public enum BaseRevisionRequirementKind
{
    /// <summary>No revision precondition.</summary>
    None,
    /// <summary>The selected record must exist with an authoritative revision.</summary>
    Exists,
    /// <summary>The selected record must have one exact revision.</summary>
    Exact,
}

/// <summary>Identifies a field precondition evaluated for every selected record.</summary>
public enum BasePreviousFieldRequirementKind
{
    /// <summary>The field must equal one canonical value.</summary>
    Equal,
    /// <summary>The field must be explicitly null.</summary>
    IsNull,
    /// <summary>The field must be missing.</summary>
    IsMissing,
    /// <summary>The field must be present.</summary>
    IsDefined,
}

/// <summary>Declares one revision precondition.</summary>
public sealed record BaseRevisionRequirement
{
    /// <summary>Gets the precondition kind.</summary>
    public required BaseRevisionRequirementKind Kind { get; init; }
    /// <summary>Gets the exact required revision when <see cref="Kind"/> is <see cref="BaseRevisionRequirementKind.Exact"/>.</summary>
    public RevisionToken? ExactRevision { get; init; }
}

/// <summary>Declares one canonical field precondition.</summary>
public sealed record BasePreviousFieldRequirement
{
    /// <summary>Gets the stable field identifier.</summary>
    public required string FieldId { get; init; }
    /// <summary>Gets the precondition kind.</summary>
    public required BasePreviousFieldRequirementKind Kind { get; init; }
    /// <summary>Gets the canonical equality value when required.</summary>
    public QueryValue? Value { get; init; }
}

/// <summary>Declares all preconditions evaluated against each selected record.</summary>
public sealed record BasePreviousStateRequirement
{
    /// <summary>Gets the revision precondition.</summary>
    public required BaseRevisionRequirement Revision { get; init; }
    /// <summary>Gets the canonical field preconditions.</summary>
    public required ImmutableArray<BasePreviousFieldRequirement> Fields { get; init; }
    /// <summary>Gets the immutable empty precondition.</summary>
    public static BasePreviousStateRequirement None { get; } = new()
    {
        Revision = new BaseRevisionRequirement { Kind = BaseRevisionRequirementKind.None },
        Fields = ImmutableArray<BasePreviousFieldRequirement>.Empty,
    };
}

/// <summary>Defines caller-narrowable execution options for a selection mutation.</summary>
public sealed record BaseSelectionMutationExecutionOptions
{
    /// <summary>Gets the optional caller observation timeout.</summary>
    public TimeSpan? CallerWaitTimeout { get; init; }
}

/// <summary>Returns the bounded outcome of one atomic selection mutation.</summary>
public sealed record BaseSelectionMutationResult
{
    /// <summary>Gets the number of selected records.</summary>
    public required int SelectedCount { get; init; }
    /// <summary>Gets the number of committed mutations.</summary>
    public required int MutatedCount { get; init; }
    /// <summary>Gets the canonical batch outcome.</summary>
    public required BaseRecordBatchOutcome Outcome { get; init; }
    /// <summary>Gets whether the request committed or replayed a receipt.</summary>
    public BaseMutationRequestDisposition RequestDisposition { get; init; }
}

/// <summary>Defines the immutable limits of one installed selection-operation profile.</summary>
public sealed record BaseSelectionOperationLimits
{
    /// <summary>Gets the maximum query nodes.</summary>
    public required int MaximumQueryNodes { get; init; }
    /// <summary>Gets the maximum query depth.</summary>
    public required int MaximumQueryDepth { get; init; }
    /// <summary>Gets the maximum literal values.</summary>
    public required int MaximumLiteralValues { get; init; }
    /// <summary>Gets the maximum selected records.</summary>
    public required int MaximumSelectedRecords { get; init; }
    /// <summary>Gets the maximum selected canonical bytes.</summary>
    public required long MaximumSelectedBytes { get; init; }
    /// <summary>Gets the maximum produced mutations.</summary>
    public required int MaximumProducedMutations { get; init; }
    /// <summary>Gets the maximum query executions.</summary>
    public required int MaximumQueryExecutions { get; init; }
    /// <summary>Gets the maximum read intervals.</summary>
    public required int MaximumReadIntervals { get; init; }
    /// <summary>Gets the maximum written bytes.</summary>
    public required long MaximumWrittenBytes { get; init; }
    /// <summary>Gets the maximum mutation-fact bytes.</summary>
    public required long MaximumFactBytes { get; init; }
    /// <summary>Gets the maximum journal bytes.</summary>
    public required long MaximumJournalBytes { get; init; }
    /// <summary>Gets the maximum receipt bytes.</summary>
    public required long MaximumReceiptBytes { get; init; }
    /// <summary>Gets the maximum relation checks.</summary>
    public required int MaximumRelationChecks { get; init; }
    /// <summary>Gets the maximum unique-constraint checks.</summary>
    public required int MaximumUniqueConstraintChecks { get; init; }
    /// <summary>Gets the maximum previous-state field requirements.</summary>
    public required int MaximumPreviousStateRequirements { get; init; }
    /// <summary>Gets the maximum transient canonical bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets the maximum result bytes.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets the provider acquisition timeout.</summary>
    public required TimeSpan AcquisitionTimeout { get; init; }
    /// <summary>Gets the transaction execution timeout.</summary>
    public required TimeSpan ExecutionTimeout { get; init; }
    /// <summary>Gets the caller commit-observation timeout.</summary>
    public required TimeSpan CallerCommitObservationTimeout { get; init; }
}

/// <summary>Defines immutable host safety maxima for transaction-bound selection mutations.</summary>
public sealed record HPDBaseSelectionMutationOptions
{
    /// <summary>Gets the complete host maxima; every installed profile may only narrow these values.</summary>
    public required BaseSelectionOperationLimits HostMaxima { get; init; }
    /// <summary>Gets the maximum canonical receipt-identity bytes.</summary>
    public required int MaximumReceiptIdentityBytes { get; init; }
    /// <summary>Gets the maximum provider evidence-token bytes.</summary>
    public required int MaximumEvidenceTokenBytes { get; init; }
    /// <summary>Gets the maximum projected route-name bytes.</summary>
    public required int MaximumRouteNameBytes { get; init; }
    /// <summary>Gets the maximum projected HTTP body bytes.</summary>
    public required int MaximumRequestBodyBytes { get; init; }
}

/// <summary>Defines an optional HTTP projection for one installed profile.</summary>
public sealed record BaseSelectionHttpProjection
{
    /// <summary>Gets the endpoint audience.</summary>
    public required BaseSelectionEndpointAudience Audience { get; init; }
    /// <summary>Gets the stable route-name segment.</summary>
    public required string RouteName { get; init; }
    /// <summary>Gets the maximum request-body bytes.</summary>
    public required int MaximumRequestBodyBytes { get; init; }
    /// <summary>Gets whether L41 client projection is enabled.</summary>
    public required bool GenerateL41Client { get; init; }
}

/// <summary>Defines one immutable installed atomic-selection operation.</summary>
public sealed record BaseSelectionOperationProfile
{
    /// <summary>Gets the stable profile identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive semantic version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the owning application identifier.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the owning collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the exact required grant identifier.</summary>
    public required string RequiredGrantId { get; init; }
    /// <summary>Gets the fixed mutation kind.</summary>
    public required BaseSelectionMutationKind MutationKind { get; init; }
    /// <summary>Gets the optional HTTP projection.</summary>
    public BaseSelectionHttpProjection? HttpProjection { get; init; }
    /// <summary>Gets the complete immutable limits.</summary>
    public required BaseSelectionOperationLimits Limits { get; init; }
}

/// <summary>Binds one collection to its exact authoritative generation.</summary>
public sealed record BaseCollectionGenerationRequirement
{
    /// <summary>Gets the stable collection identity.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the positive collection generation.</summary>
    public required long CollectionGeneration { get; init; }
}

/// <summary>Binds an atomic mutation to one coherent multi-collection authority observation.</summary>
public sealed record BaseAtomicMutationAuthorityRequirement
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the persistent store-instance identity.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the positive restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the positive schema generation.</summary>
    public required long SchemaGeneration { get; init; }
    /// <summary>Gets exact collection generations in ordinal collection-ID order.</summary>
    public required ImmutableArray<BaseCollectionGenerationRequirement> Collections { get; init; }
}

/// <summary>Returns transaction-local evidence for one coherent multi-collection authority.</summary>
public sealed record BaseAtomicMutationAuthorityEvidence
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the persistent store-instance identity.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the positive restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the positive schema generation.</summary>
    public required long SchemaGeneration { get; init; }
    /// <summary>Gets exact collection generations in ordinal collection-ID order.</summary>
    public required ImmutableArray<BaseCollectionGenerationRequirement> Collections { get; init; }
    /// <summary>Gets the certified transaction isolation.</summary>
    public required BaseAtomicSelectionIsolationClass Isolation { get; init; }
    /// <summary>Gets opaque transaction-local evidence bytes.</summary>
    public required ImmutableArray<byte> TransactionEvidenceToken { get; init; }
}

/// <summary>Requests one bounded transaction-local selection.</summary>
public sealed record BaseAtomicSelectionRequest
{
    /// <summary>Gets the resolved collection.</summary>
    public required CollectionDefinition Collection { get; init; }
    /// <summary>Gets the canonical policy-constrained query.</summary>
    public required RecordQuery Query { get; init; }
    /// <summary>Gets the canonical record codec version.</summary>
    public required int CanonicalRecordCodecVersion { get; init; }
}

/// <summary>Returns one normalized logical read interval.</summary>
public sealed record BaseAtomicReadIntervalEvidence
{
    /// <summary>Gets the logical access-path identifier.</summary>
    public required string LogicalAccessPathId { get; init; }
    /// <summary>Gets the canonical lower bound.</summary>
    public required ImmutableArray<byte> CanonicalLowerBound { get; init; }
    /// <summary>Gets whether the lower bound is inclusive.</summary>
    public required bool LowerInclusive { get; init; }
    /// <summary>Gets the canonical upper bound.</summary>
    public required ImmutableArray<byte> CanonicalUpperBound { get; init; }
    /// <summary>Gets whether the upper bound is inclusive.</summary>
    public required bool UpperInclusive { get; init; }
}

/// <summary>Returns recomputable accounting for one selection.</summary>
public sealed record BaseAtomicSelectionAccounting
{
    /// <summary>Gets the selected record count.</summary>
    public required int SelectedRecords { get; init; }
    /// <summary>Gets the selected canonical bytes.</summary>
    public required long SelectedBytes { get; init; }
    /// <summary>Gets the normalized read-interval count.</summary>
    public required int ReadIntervals { get; init; }
    /// <summary>Gets the canonical evidence bytes.</summary>
    public required long EvidenceBytes { get; init; }
}

/// <summary>Reports exact provider-certified aggregate accounting before selection commit.</summary>
public sealed record BaseSelectionMutationCommitAccounting
{
    /// <summary>Gets canonical authoritative bytes written.</summary>
    public required long WrittenBytes { get; init; }
    /// <summary>Gets canonical mutation-fact bytes.</summary>
    public required long FactBytes { get; init; }
    /// <summary>Gets exact durable journal framing and payload bytes.</summary>
    public required long JournalBytes { get; init; }
    /// <summary>Gets the complete durable receipt bytes.</summary>
    public required long ReceiptBytes { get; init; }
    /// <summary>Gets logical relation checks performed.</summary>
    public required int RelationChecks { get; init; }
    /// <summary>Gets logical unique checks performed.</summary>
    public required int UniqueConstraintChecks { get; init; }
    /// <summary>Gets canonical projected result bytes.</summary>
    public required long ResultBytes { get; init; }
    /// <summary>Gets aggregate retained transient canonical bytes.</summary>
    public required long TransientBytes { get; init; }
}

/// <summary>Describes one provider's immutable L43 capability.</summary>
public sealed record BaseSelectionMutationCapability
{
    /// <summary>Gets whether selection mutation is supported.</summary>
    public required bool IsSupported { get; init; }
    /// <summary>Gets certified maxima.</summary>
    public required BaseSelectionOperationLimits CertifiedMaxima { get; init; }
    /// <summary>Gets certified isolation.</summary>
    public required BaseAtomicSelectionIsolationClass Isolation { get; init; }
    /// <summary>Gets supported receipt-envelope versions.</summary>
    public required ImmutableArray<int> ReceiptEnvelopeFormatVersions { get; init; }
    /// <summary>Gets supported canonical codec versions.</summary>
    public required ImmutableArray<int> CanonicalCodecVersions { get; init; }
    /// <summary>Gets supported filter operators.</summary>
    public required ImmutableArray<FilterOperator> SupportedFilterOperators { get; init; }
    /// <summary>Gets supported filter node kinds.</summary>
    public required ImmutableArray<FilterNodeKind> SupportedFilterNodeKinds { get; init; }
    /// <summary>Gets supported logical access shapes.</summary>
    public required ImmutableArray<BaseIndexAccessShape> SupportedIndexShapes { get; init; }
    /// <summary>Gets constraint-attribution capabilities.</summary>
    public required BaseConstraintAttributionClass ConstraintAttribution { get; init; }
    /// <summary>Gets whether receipt-only commit is supported.</summary>
    public required bool SupportsReceiptOnlyCommit { get; init; }
    /// <summary>Gets whether logical read-interval evidence is supplied.</summary>
    public required bool SuppliesReadIntervalEvidence { get; init; }
    /// <summary>Gets whether relations participate transactionally.</summary>
    public required bool SupportsRelationParticipation { get; init; }
    /// <summary>Gets whether session mutations read their provisional writes.</summary>
    public required bool SupportsReadYourWrites { get; init; }
    /// <summary>Gets whether cancellation is bounded.</summary>
    public required bool SupportsBoundedCancellation { get; init; }
    /// <summary>Gets whether commit observation is bounded.</summary>
    public required bool SupportsBoundedCommitObservation { get; init; }
}

/// <summary>Defines stable L43 failure codes.</summary>
public static class BaseSelectionErrorCodes
{
    /// <summary>The selection contract is invalid.</summary>
    public const string ContractInvalid = "base.selection.contractInvalid";
    /// <summary>The installed operation profile is invalid.</summary>
    public const string ProfileInvalid = "base.selection.profileInvalid";
    /// <summary>The profile is duplicated.</summary>
    public const string ProfileDuplicate = "base.selection.profileDuplicate";
    /// <summary>The profile is not installed.</summary>
    public const string ProfileNotFound = "base.selection.profileNotFound";
    /// <summary>A request narrowing is invalid.</summary>
    public const string LimitInvalid = "base.selection.limitInvalid";
    /// <summary>An operation limit was exceeded.</summary>
    public const string LimitExceeded = "base.selection.limitExceeded";
    /// <summary>The selected provider lacks a required capability.</summary>
    public const string CapabilityMissing = "base.selection.capabilityMissing";
    /// <summary>The effective policy cannot be enforced before selection.</summary>
    public const string PolicyUnsupported = "base.selection.policyUnsupported";
    /// <summary>The bound schema authority changed.</summary>
    public const string SchemaGenerationChanged = "base.selection.schemaGenerationChanged";
    /// <summary>The portable serializable transaction conflicted.</summary>
    public const string TransactionConflict = "base.selection.transactionConflict";
    /// <summary>Selection execution timed out before commit.</summary>
    public const string Timeout = "base.selection.timeout";
    /// <summary>Selection execution was cancelled before commit.</summary>
    public const string Cancelled = "base.selection.cancelled";
    /// <summary>Selection commit is indeterminate.</summary>
    public const string CommitIndeterminate = "base.selection.commitIndeterminate";
}
