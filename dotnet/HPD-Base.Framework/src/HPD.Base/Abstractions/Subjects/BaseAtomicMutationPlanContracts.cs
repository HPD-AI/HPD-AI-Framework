using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Contains current authority for one graph-installed lifecycle consumer projection.</summary>
public sealed record BaseCapturedSubjectLifecycleConsumerProjection
{
    /// <summary>Gets the stable consumer ID.</summary>
    public required string ConsumerId { get; init; }
    /// <summary>Gets the consumer version.</summary>
    public required int ConsumerVersion { get; init; }
    /// <summary>Gets the finalized consumer checksum.</summary>
    public required string ConsumerChecksum { get; init; }
    /// <summary>Gets the exported contract ID.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the exported contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the current positive projection generation.</summary>
    public required long ProjectionGeneration { get; init; }
    /// <summary>Gets the graph generation that published this projection.</summary>
    public required long PublishedGraphGeneration { get; init; }
}

/// <summary>Binds a generated exporter lifecycle mutation to one exact current lifetime.</summary>
public sealed record BaseSubjectLifecycleTransitionPrecondition
{
    /// <summary>Gets the expected subject reference.</summary>
    public required BaseOwnedSubjectReference Subject { get; init; }
    /// <summary>Gets the expected current sequence when final retirement is requested.</summary>
    public long? ExpectedSubjectSequence { get; init; }
    /// <summary>Gets the only permitted resulting lifecycle state.</summary>
    public required BaseSubjectLifecycleState ResultingState { get; init; }
}

/// <summary>Classifies the provider-resolved disposition of one canonical mutation intent.</summary>
public enum BaseCapturedMutationDisposition
{
    /// <summary>The operation creates a previously absent record.</summary>
    Create = 0,
    /// <summary>The operation updates a present record.</summary>
    Update = 1,
    /// <summary>The operation deletes a present record.</summary>
    Delete = 2,
}

/// <summary>Contains one deeply owned unresolved item in a canonical atomic mutation intent.</summary>
public sealed record BaseAtomicMutationIntentItem
{
    /// <summary>Gets the dense zero-based request ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the target collection definition.</summary>
    public required CollectionDefinition Collection { get; init; }
    /// <summary>Gets the caller-requested mutation kind.</summary>
    public required BaseRecordMutationKind RequestedKind { get; init; }
    /// <summary>Gets the canonical target record ID.</summary>
    public required RecordId RecordId { get; init; }
    /// <summary>Gets whether BASE Runtime, rather than the caller, assigned this create identifier.</summary>
    public bool RuntimeAssignedRecordId { get; init; }
    /// <summary>Gets the unresolved create request, when applicable.</summary>
    public RecordCreateRequest? Create { get; init; }
    /// <summary>Gets the unresolved patch request, when applicable.</summary>
    public RecordPatchRequest? Patch { get; init; }
    /// <summary>Gets the unresolved replace request, when applicable.</summary>
    public RecordReplaceRequest? Replace { get; init; }
    /// <summary>Gets the unresolved upsert request, when applicable.</summary>
    public RecordUpsertRequest? Upsert { get; init; }
    /// <summary>Gets the unresolved delete request, when applicable.</summary>
    public RecordDeleteRequest? Delete { get; init; }
    /// <summary>Gets every bounded relation target that may be required after authoritative disposition is resolved.</summary>
    public required ImmutableArray<BaseAtomicRelationTargetIntent> RelationTargets { get; init; }
    /// <summary>Gets the principal-bound operation context.</summary>
    public required OperationContext Operation { get; init; }
}

/// <summary>Identifies one relation target whose state must be captured before Runtime finalization.</summary>
public sealed record BaseAtomicRelationTargetIntent
{
    /// <summary>Gets the source field stable identity.</summary>
    public required string SourceFieldId { get; init; }
    /// <summary>Gets the target collection definition.</summary>
    public required CollectionDefinition TargetCollection { get; init; }
    /// <summary>Gets the canonical target record identity.</summary>
    public required RecordId TargetRecordId { get; init; }
}

/// <summary>Contains one deeply owned caller-semantic mutation intent before state-dependent policy evaluation.</summary>
public sealed record BaseAtomicMutationIntent
{
    /// <summary>Gets the canonical SHA-256 intent digest.</summary>
    public required string IntentDigest { get; init; }
    /// <summary>Gets the coherent multi-collection authority requirement.</summary>
    public required BaseAtomicMutationAuthorityRequirement Authority { get; init; }
    /// <summary>Gets dense canonical intent items.</summary>
    public required ImmutableArray<BaseAtomicMutationIntentItem> Items { get; init; }
}

/// <summary>Classifies the one closed atomic execution shape.</summary>
public enum BaseAtomicMutationExecutionKind
{
    /// <summary>Executes ordinary L30 record mutations.</summary>
    RecordMutations = 0,
    /// <summary>Executes one L43 selection mutation.</summary>
    SelectionMutation = 1,
    /// <summary>Executes one L50 registered module mutation.</summary>
    ModuleMutation = 2,
    /// <summary>Creates durable activations without ordinary mutation items.</summary>
    ActivationCreation = 3,
}

/// <summary>Requests one transaction-bound capture under one safety authority.</summary>
public sealed record BaseAtomicExecutionRequest
{
    /// <summary>Gets the closed execution shape.</summary>
    public required BaseAtomicMutationExecutionKind Kind { get; init; }
    /// <summary>Gets the caller-semantic intent.</summary>
    public required BaseAtomicMutationIntent Intent { get; init; }
    /// <summary>Gets the optional L43 capture extension.</summary>
    public BaseSelectionMutationCaptureExtension? Selection { get; init; }
    /// <summary>Gets the optional L50 capture extension.</summary>
    public BaseModuleMutationCaptureExtension? Module { get; init; }
    /// <summary>Gets graph-selected lifecycle projection capture authority.</summary>
    public ImmutableArray<BaseSubjectLifecycleConsumerProjectionCaptureRequest> LifecycleConsumerProjections { get; init; } = [];
    /// <summary>Gets retirement projection capture authority.</summary>
    public BaseSubjectRetirementCaptureExtension? SubjectRetirement { get; init; }
    /// <summary>Gets ordered durable activations created by this execution.</summary>
    public BaseActivationCreationExtension? Activations { get; init; }
    /// <summary>Gets the optional same-store activation fence.</summary>
    public BaseActivationGuard? ActivationGuard { get; init; }
    /// <summary>Gets the optional L53 semantic activation operation.</summary>
    public BaseAtomicSemanticActivationExtension? SemanticActivation { get; init; }
    /// <summary>Gets the sole complete safety envelope.</summary>
    public required BaseAtomicMutationExecutionLimits Limits { get; init; }
}

/// <summary>Extends capture with one L43 selection.</summary>
public sealed record BaseSelectionMutationCaptureExtension
{
    /// <summary>Gets the installed operation profile ID.</summary>
    public required string OperationProfileId { get; init; }
    /// <summary>Gets the installed operation profile version.</summary>
    public required int OperationProfileVersion { get; init; }
    /// <summary>Gets the installed operation profile checksum.</summary>
    public required string OperationProfileChecksum { get; init; }
    /// <summary>Gets the selection request without independent limits or authority.</summary>
    public required BaseAtomicSelectionRequest Selection { get; init; }
}

/// <summary>Extends capture with one registered module mutation.</summary>
public sealed record BaseModuleMutationCaptureExtension
{
    /// <summary>Gets the installed operation ID.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the installed operation version.</summary>
    public required int OperationVersion { get; init; }
    /// <summary>Gets the installed operation checksum.</summary>
    public required string OperationChecksum { get; init; }
    /// <summary>Gets the canonical request digest.</summary>
    public required string RequestDigest { get; init; }
    /// <summary>Gets dense record captures.</summary>
    public required ImmutableArray<BaseModuleRecordCaptureRequest> Records { get; init; }
    /// <summary>Gets dense possible relation-target captures.</summary>
    public required ImmutableArray<BaseModuleRelationTargetCaptureRequest> RelationTargets { get; init; }
    /// <summary>Gets dense generation-cell captures.</summary>
    public required ImmutableArray<BaseModuleGenerationCaptureRequest> Generations { get; init; }
}

/// <summary>Requests one module-owned record capture.</summary>
public sealed record BaseModuleRecordCaptureRequest
{
    /// <summary>Gets the dense capture ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the stable capture ID.</summary>
    public required string CaptureId { get; init; }
    /// <summary>Gets the collection authority.</summary>
    public required CollectionDefinition Collection { get; init; }
    /// <summary>Gets the exact record ID.</summary>
    public required RecordId RecordId { get; init; }
    /// <summary>Gets the required presence state.</summary>
    public required BaseModuleCapturePresence Presence { get; init; }
}

/// <summary>Requests one possible relation-target capture.</summary>
public sealed record BaseModuleRelationTargetCaptureRequest
{
    /// <summary>Gets the dense capture ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the source statement ID.</summary>
    public required string SourceStatementId { get; init; }
    /// <summary>Gets the stable source field ID.</summary>
    public required string SourceFieldId { get; init; }
    /// <summary>Gets the target collection authority.</summary>
    public required CollectionDefinition TargetCollection { get; init; }
    /// <summary>Gets the exact target record ID.</summary>
    public required RecordId TargetRecordId { get; init; }
}

/// <summary>Requests one read-only generation-cell capture.</summary>
public sealed record BaseModuleGenerationCaptureRequest
{
    /// <summary>Gets the dense capture ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the stable capture ID.</summary>
    public required string CaptureId { get; init; }
    /// <summary>Gets the installed cell definition.</summary>
    public required BaseModuleGenerationCellDefinition Cell { get; init; }
    /// <summary>Gets the exact scope authority.</summary>
    public required BaseModuleGenerationScopeAuthority Scope { get; init; }
    /// <summary>Gets the canonical keyed-scope bytes.</summary>
    public ImmutableArray<byte> KeyUtf8 { get; init; }
    /// <summary>Gets the read-only absence requirement.</summary>
    public required BaseModuleGenerationAbsenceBehavior Absence { get; init; }
}

/// <summary>Contains exact scope authority for one generation cell.</summary>
public sealed record BaseModuleGenerationScopeAuthority
{
    /// <summary>Gets the scope kind.</summary>
    public required BaseModuleGenerationScope Kind { get; init; }
    /// <summary>Gets the tenant value only for tenant scope.</summary>
    public string? Tenant { get; init; }
    /// <summary>Gets the project value only for project scope.</summary>
    public string? Project { get; init; }
}

/// <summary>Contains one provider-captured current-state item bound to an open atomic session.</summary>
public sealed record BaseCapturedMutationItem
{
    /// <summary>Gets the matching intent ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the target collection ID.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the target record ID.</summary>
    public required RecordId RecordId { get; init; }
    /// <summary>Gets whether BASE Runtime, rather than the caller, assigned this create identifier.</summary>
    public bool RuntimeAssignedRecordId { get; init; }
    /// <summary>Gets the authoritative resolved disposition.</summary>
    public required BaseCapturedMutationDisposition Disposition { get; init; }
    /// <summary>Gets a deeply owned current record, or <see langword="null"/> for proven absence.</summary>
    public RecordEnvelope? Current { get; init; }
    /// <summary>Gets deeply owned state for every possible relation target declared by the matching intent.</summary>
    public required ImmutableArray<BaseCapturedRelationTarget> RelationTargets { get; init; }
}

/// <summary>Contains transaction-bound state for one possible relation target.</summary>
public sealed record BaseCapturedRelationTarget
{
    /// <summary>Gets the source field stable identity.</summary>
    public required string SourceFieldId { get; init; }
    /// <summary>Gets the exact target collection identity.</summary>
    public required string TargetCollectionId { get; init; }
    /// <summary>Gets the exact target record identity.</summary>
    public required RecordId TargetRecordId { get; init; }
    /// <summary>Gets the authoritative target record, or <see langword="null"/> for proven absence.</summary>
    public RecordEnvelope? Current { get; init; }
}

/// <summary>Contains exact provider accounting for authority capture.</summary>
public sealed record BaseAtomicCaptureAccounting
{
    /// <summary>Gets the captured record count.</summary>
    public required int Records { get; init; }
    /// <summary>Gets the relation-target read count.</summary>
    public required int RelationTargetReads { get; init; }
    /// <summary>Gets the generation-cell read count.</summary>
    public required int GenerationReads { get; init; }
    /// <summary>Gets canonical captured record bytes.</summary>
    public required long SelectedBytes { get; init; }
    /// <summary>Gets canonical relation-target bytes.</summary>
    public required long RelationTargetBytes { get; init; }
    /// <summary>Gets canonical generation evidence bytes.</summary>
    public required long GenerationBytes { get; init; }
    /// <summary>Gets normalized read-interval count.</summary>
    public required int ReadIntervals { get; init; }
    /// <summary>Gets retained evidence bytes.</summary>
    public required long EvidenceBytes { get; init; }
    /// <summary>Gets complete retained transient bytes.</summary>
    public required long TransientBytes { get; init; }
    /// <summary>Gets retirement barrier reads performed.</summary>
    public required int RetirementBarrierReads { get; init; }
    /// <summary>Gets retirement acknowledgement reads performed.</summary>
    public required int RetirementAcknowledgementReads { get; init; }
    /// <summary>Gets retirement projections processed.</summary>
    public required int RetirementProjections { get; init; }
    /// <summary>Gets retirement publications processed.</summary>
    public required int RetirementPublications { get; init; }
    /// <summary>Gets retirement evidence bytes.</summary>
    public required long RetirementEvidenceBytes { get; init; }
    /// <summary>Gets retirement publication bytes.</summary>
    public required long RetirementPublicationBytes { get; init; }
}

/// <summary>Contains immutable authority captured from one open provider transaction.</summary>
public sealed record BaseCapturedAtomicExecution
{
    /// <summary>Gets the closed execution shape.</summary>
    public required BaseAtomicMutationExecutionKind Kind { get; init; }
    /// <summary>Gets the matching intent digest.</summary>
    public required string IntentDigest { get; init; }
    /// <summary>Gets the provider-authored capture digest.</summary>
    public required string CaptureDigest { get; init; }
    /// <summary>Gets the authoritative snapshot evidence.</summary>
    public required BaseAtomicMutationAuthorityEvidence Authority { get; init; }
    /// <summary>Gets L43 selection evidence only for a selection-mutation capture.</summary>
    public BaseCapturedSelectionEvidence? Selection { get; init; }
    /// <summary>Gets dense captured items.</summary>
    public required ImmutableArray<BaseCapturedMutationItem> Items { get; init; }
    /// <summary>Gets dense module record captures.</summary>
    public required ImmutableArray<BaseCapturedModuleRecord> ModuleRecords { get; init; }
    /// <summary>Gets dense module relation-target captures.</summary>
    public required ImmutableArray<BaseCapturedModuleRelationTarget> ModuleRelationTargets { get; init; }
    /// <summary>Gets dense module generation captures.</summary>
    public required ImmutableArray<BaseCapturedModuleGeneration> Generations { get; init; }
    /// <summary>Gets current lifecycle consumer projection generations.</summary>
    public ImmutableArray<BaseCapturedSubjectLifecycleConsumerProjection> LifecycleConsumerProjections { get; init; } = [];
    /// <summary>Gets captured retirement projection authority.</summary>
    public ImmutableArray<BaseCapturedSubjectRetirementProjection> SubjectRetirement { get; init; } = [];
    /// <summary>Gets captured activation authority when activation work was requested.</summary>
    public BaseCapturedActivationExtension? Activations { get; init; }
    /// <summary>Gets transaction-captured activation-fence authority when a child operation is guarded.</summary>
    public BaseCapturedActivationGuardEvidence? ActivationGuard { get; init; }
    /// <summary>Gets captured L53 semantic activation authority.</summary>
    public BaseCapturedSemanticActivationEvidence? SemanticActivation { get; init; }
    /// <summary>Gets normalized transaction read intervals.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> ReadIntervals { get; init; }
    /// <summary>Gets exact capture accounting.</summary>
    public required BaseAtomicCaptureAccounting Accounting { get; init; }
}

/// <summary>Contains transaction-captured evidence for one same-store activation fence.</summary>
public sealed record BaseCapturedActivationGuardEvidence
{
    /// <summary>Gets the guarded activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the exact positive attempt number.</summary>
    public required int AttemptNumber { get; init; }
    /// <summary>Gets the exact positive claim epoch.</summary>
    public required long ClaimEpoch { get; init; }
    /// <summary>Gets the exact opaque 256-bit fencing token.</summary>
    public required ImmutableArray<byte> FencingToken { get; init; }
    /// <summary>Gets the exact installed worker identity.</summary>
    public required string WorkerIdentity { get; init; }
    /// <summary>Gets the exact cancellation generation.</summary>
    public required long CancellationGeneration { get; init; }
    /// <summary>Gets the exact physical store-instance identity.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the exact nonnegative restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the exact installed definition checksum.</summary>
    public required ImmutableArray<byte> DefinitionChecksum { get; init; }
    /// <summary>Gets the exact installed child-step identity.</summary>
    public required string StepId { get; init; }
    /// <summary>Gets the exact positive child ordinal.</summary>
    public required int ChildOrdinal { get; init; }
    /// <summary>Gets the exact canonical child-request fingerprint.</summary>
    public required ImmutableArray<byte> ChildRequestFingerprint { get; init; }
    /// <summary>Gets the exact current activation generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets the exact current lease revision.</summary>
    public required long LeaseRevision { get; init; }
    /// <summary>Gets the current lease expiry as Unix milliseconds.</summary>
    public required long LeaseExpiresAt { get; init; }
    /// <summary>Gets the canonical evidence checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains the bounded selected records owned by one L43 capture.</summary>
public sealed record BaseCapturedSelectionEvidence
{
    /// <summary>Gets deeply owned selected records in canonical order.</summary>
    public required ImmutableArray<BaseOwnedSelectedRecord> Records { get; init; }
    /// <summary>Gets the canonical last examined ordering boundary.</summary>
    public required ImmutableArray<byte> CanonicalOrderBoundary { get; init; }
    /// <summary>Gets exact selection accounting.</summary>
    public required BaseAtomicSelectionAccounting Accounting { get; init; }
}

/// <summary>Contains one captured L50 record.</summary>
public sealed record BaseCapturedModuleRecord
{
    /// <summary>Gets the dense ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the stable capture ID.</summary>
    public required string CaptureId { get; init; }
    /// <summary>Gets the collection ID.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the record ID.</summary>
    public required RecordId RecordId { get; init; }
    /// <summary>Gets whether the record exists.</summary>
    public required bool Exists { get; init; }
    /// <summary>Gets the exact captured record when present.</summary>
    public RecordEnvelope? Current { get; init; }
}

/// <summary>Contains one captured possible L50 relation target.</summary>
public sealed record BaseCapturedModuleRelationTarget
{
    /// <summary>Gets the dense ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the source statement ID.</summary>
    public required string SourceStatementId { get; init; }
    /// <summary>Gets the stable source field ID.</summary>
    public required string SourceFieldId { get; init; }
    /// <summary>Gets the target collection ID.</summary>
    public required string TargetCollectionId { get; init; }
    /// <summary>Gets the target record ID.</summary>
    public required RecordId TargetRecordId { get; init; }
    /// <summary>Gets the exact captured target when present.</summary>
    public RecordEnvelope? Current { get; init; }
}

/// <summary>Contains one captured L50 generation cell.</summary>
public sealed record BaseCapturedModuleGeneration
{
    /// <summary>Gets the dense ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the stable capture ID.</summary>
    public required string CaptureId { get; init; }
    /// <summary>Gets the cell ID.</summary>
    public required string CellId { get; init; }
    /// <summary>Gets the cell version.</summary>
    public required int CellVersion { get; init; }
    /// <summary>Gets the canonical key digest.</summary>
    public required string CanonicalKeyDigest { get; init; }
    /// <summary>Gets whether the cell exists.</summary>
    public required bool Exists { get; init; }
    /// <summary>Gets the captured generation when present.</summary>
    public BaseModuleGeneration? Generation { get; init; }
}

/// <summary>Contains one final canonical mutation item after transaction-bound policy evaluation.</summary>
public sealed record BaseAtomicMutationPlanItem
{
    /// <summary>Gets the dense request ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the optional batch item identity.</summary>
    public string? ItemId { get; init; }
    /// <summary>Gets the Runtime-issued stable event identity.</summary>
    public required string EventId { get; init; }
    /// <summary>Gets the target collection.</summary>
    public required CollectionDefinition Collection { get; init; }
    /// <summary>Gets the physical mutation kind.</summary>
    public required BaseCommittedRecordMutationKind Kind { get; init; }
    /// <summary>Gets the caller-requested logical mutation kind.</summary>
    public required BaseRecordMutationKind RequestedKind { get; init; }
    /// <summary>Gets the canonical record ID.</summary>
    public required RecordId RecordId { get; init; }
    /// <summary>Gets whether BASE Runtime, rather than the caller, assigned this create identifier.</summary>
    public bool RuntimeAssignedRecordId { get; init; }
    /// <summary>Gets the canonical proposed record payload for create/update.</summary>
    public RecordPayload? ProposedPayload { get; init; }
    /// <summary>Gets the canonical delete request for delete.</summary>
    public RecordDeleteRequest? Delete { get; init; }
    /// <summary>Gets the transaction-bound current record when required.</summary>
    public RecordEnvelope? Current { get; init; }
    /// <summary>Gets the canonical changed wire-field names.</summary>
    public required ImmutableArray<string> ChangedFields { get; init; }
    /// <summary>Gets Runtime-owned lifecycle projection, when this mutates an exported private source.</summary>
    public BaseSubjectLifecyclePlanItem? SubjectLifecycle { get; init; }
    /// <summary>Gets the generated exporter lifecycle CAS precondition.</summary>
    public BaseSubjectLifecycleTransitionPrecondition? SubjectLifecycleTransition { get; init; }
    /// <summary>Gets the normalized principal-bound operation context.</summary>
    public required OperationContext Operation { get; init; }
}

/// <summary>Contains one BASE-owned exported-subject lifecycle projection in a finalized mutation plan.</summary>
public sealed record BaseSubjectLifecyclePlanItem
{
    /// <summary>Gets the exported contract ID.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the normalized contract checksum.</summary>
    public required string ContractChecksum { get; init; }
    /// <summary>Gets the lifecycle disposition.</summary>
    public required BaseSubjectLifecycleMutationKind Kind { get; init; }
    /// <summary>Gets the canonical public subject ID.</summary>
    public required BaseSubjectId SubjectId { get; init; }
    /// <summary>Gets the prior lifecycle state, or null for creation.</summary>
    public BaseSubjectLifecycleState? PreviousState { get; init; }
    /// <summary>Gets the resulting lifecycle state.</summary>
    public required BaseSubjectLifecycleState ResultingState { get; init; }
    /// <summary>Gets whether a canonical lifecycle fact must be published.</summary>
    public required bool PublishFact { get; init; }
    /// <summary>Gets exact graph-selected consumer memberships.</summary>
    public required ImmutableArray<BaseSubjectLifecycleMembershipPlanItem> Memberships { get; init; }
}

/// <summary>Classifies the lifecycle sidecar effect of one private canonical mutation.</summary>
public enum BaseSubjectLifecycleMutationKind
{
    /// <summary>Creates a new subject lifetime and fresh incarnation.</summary>
    Create = 0,
    /// <summary>Preserves the current subject lifetime and incarnation.</summary>
    Preserve = 1,
    /// <summary>Retires the current subject lifetime.</summary>
    Retire = 2,
}

/// <summary>Contains one deeply owned reference validation required by a finalized mutation plan.</summary>
public sealed record BaseSubjectReferenceValidationPlanItem
{
    /// <summary>Gets the owning mutation ordinal.</summary>
    public required int MutationOrdinal { get; init; }
    /// <summary>Gets the stable source field ID.</summary>
    public required string SourceFieldId { get; init; }
    /// <summary>Gets the validation-plan ID.</summary>
    public required string ValidationPlanId { get; init; }
    /// <summary>Gets the validation-plan version.</summary>
    public required int ValidationPlanVersion { get; init; }
    /// <summary>Gets the required logical state.</summary>
    public required BaseSubjectReferenceRequirement Requirement { get; init; }
    /// <summary>Gets a deeply owned reference value.</summary>
    public required BaseOwnedSubjectReference Reference { get; init; }
    /// <summary>Gets deeply owned source-scope evidence.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
}

/// <summary>Owns one non-generic canonical subject reference across the provider SPI.</summary>
public sealed class BaseOwnedSubjectReference
{
    /// <summary>Creates a defensive immutable reference value.</summary>
    public BaseOwnedSubjectReference(BaseSubjectId subjectId, BaseSubjectAuthorityEpoch authorityEpoch, BaseSubjectIncarnation incarnation)
    { SubjectId = subjectId; AuthorityEpoch = authorityEpoch; Incarnation = incarnation; }
    /// <summary>Gets the canonical subject ID.</summary>
    public BaseSubjectId SubjectId { get; }
    /// <summary>Gets the authority epoch.</summary>
    public BaseSubjectAuthorityEpoch AuthorityEpoch { get; }
    /// <summary>Gets the subject incarnation.</summary>
    public BaseSubjectIncarnation Incarnation { get; }
}

/// <summary>Owns the source tenant or project scope used by transaction-local validation.</summary>
public sealed record BaseOwnedSubjectScopeEvidence
{
    /// <summary>Gets the scope kind.</summary>
    public required BaseSubjectScopeKind Kind { get; init; }
    /// <summary>Gets canonical scope text for tenant/project scope.</summary>
    public string? Value { get; init; }
}

/// <summary>Contains one finalized canonical atomic mutation plan.</summary>
public sealed record BaseFinalizedAtomicExecutionPlan
{
    /// <summary>Gets the closed execution shape.</summary>
    public required BaseAtomicMutationExecutionKind Kind { get; init; }
    /// <summary>Gets the canonical plan digest.</summary>
    public required string PlanDigest { get; init; }
    /// <summary>Gets the originating intent digest.</summary>
    public required string IntentDigest { get; init; }
    /// <summary>Gets the bound provider capture digest.</summary>
    public required string CaptureDigest { get; init; }
    /// <summary>Gets the Runtime-owned canonical policy-authority digest.</summary>
    public required BaseAtomicPolicyAuthorityDigest PolicyAuthorityDigest { get; init; }
    /// <summary>Gets the required authority snapshot.</summary>
    public required BaseAtomicMutationAuthorityRequirement Authority { get; init; }
    /// <summary>Gets dense canonical mutation items.</summary>
    public required ImmutableArray<BaseAtomicMutationPlanItem> Items { get; init; }
    /// <summary>Gets ordered exported-subject validation items.</summary>
    public required ImmutableArray<BaseSubjectReferenceValidationPlanItem> SubjectValidations { get; init; }
    /// <summary>Gets the L50 finalized extension only for a module mutation.</summary>
    public BaseFinalizedModuleMutationExtension? Module { get; init; }
    /// <summary>Gets the Runtime-finalized retirement projection plan.</summary>
    public BaseSubjectRetirementProjectionPlan? SubjectRetirement { get; init; }
    /// <summary>Gets Runtime-owned text projection intent.</summary>
    public BaseFinalizedTextMutationExtension? Text { get; init; }
    /// <summary>Gets ordered activation creation finalized by Runtime.</summary>
    public BaseActivationCreationExtension? Activations { get; init; }
    /// <summary>Gets the activation fence finalized by Runtime.</summary>
    public BaseActivationGuard? ActivationGuard { get; init; }
    /// <summary>Gets the Runtime-finalized L53 semantic activation operation.</summary>
    public BaseAtomicSemanticActivationExtension? SemanticActivation { get; init; }
    /// <summary>Gets the complete immutable execution limits.</summary>
    public required BaseAtomicMutationExecutionLimits Limits { get; init; }
}

/// <summary>Owns one canonical SHA-256 policy-authority digest.</summary>
public sealed class BaseAtomicPolicyAuthorityDigest : IEquatable<BaseAtomicPolicyAuthorityDigest>
{
    /// <summary>Gets the exact digest length.</summary>
    public const int Length = 32;
    private readonly byte[] _value;
    private BaseAtomicPolicyAuthorityDigest(byte[] value) => _value = value;
    /// <summary>Creates one deeply owned digest.</summary>
    public static BaseAtomicPolicyAuthorityDigest Create(ReadOnlySpan<byte> value)
    {
        if (value.Length != Length) throw new ArgumentException("An atomic policy-authority digest must contain exactly 32 bytes.", nameof(value));
        return new(value.ToArray());
    }
    /// <summary>Copies the digest.</summary>
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < Length) throw new ArgumentException("The destination is too small.", nameof(destination));
        _value.CopyTo(destination);
    }
    /// <summary>Returns a defensive copy.</summary>
    public byte[] ToArray() => _value.ToArray();
    /// <inheritdoc />
    public bool Equals(BaseAtomicPolicyAuthorityDigest? other) => other is not null && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(_value, other._value);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseAtomicPolicyAuthorityDigest other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => BitConverter.ToInt32(_value, 0);
}

/// <summary>Contains the finalized L50 program decisions and generation effects.</summary>
public sealed record BaseFinalizedModuleMutationExtension
{
    /// <summary>Gets the operation ID.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the operation version.</summary>
    public required int OperationVersion { get; init; }
    /// <summary>Gets the operation checksum.</summary>
    public required string OperationChecksum { get; init; }
    /// <summary>Gets the complete canonical decision trace.</summary>
    public required ImmutableArray<BaseModuleDecisionTraceEntry> Decisions { get; init; }
    /// <summary>Gets mutation-item to record-capture bindings.</summary>
    public required ImmutableArray<BaseModuleMutationItemCaptureBinding> ItemBindings { get; init; }
    /// <summary>Gets the complete selected relation targets authorized by Runtime.</summary>
    public required ImmutableArray<BaseAuthorizedModuleRelationTarget> RelationTargets { get; init; }
    /// <summary>Gets ordered generation comparisons.</summary>
    public required ImmutableArray<BaseModuleGenerationComparison> Comparisons { get; init; }
    /// <summary>Gets ordered generation increments.</summary>
    public required ImmutableArray<BaseModuleGenerationIncrement> Increments { get; init; }
    /// <summary>Gets the canonical result projection digest.</summary>
    public required string ResultProjectionDigest { get; init; }
}

/// <summary>Binds one selected relation target to its exact Runtime policy authority.</summary>
public sealed record BaseAuthorizedModuleRelationTarget
{
    /// <summary>Gets the original relation-target capture ordinal.</summary>
    public required int CaptureOrdinal { get; init; }
    /// <summary>Gets the source statement identity.</summary>
    public required string SourceStatementId { get; init; }
    /// <summary>Gets the stable source field identity.</summary>
    public required string SourceFieldId { get; init; }
    /// <summary>Gets the exact target collection identity.</summary>
    public required string TargetCollectionId { get; init; }
    /// <summary>Gets the exact target record identity.</summary>
    public required RecordId TargetRecordId { get; init; }
    /// <summary>Gets the exact Runtime policy-authority digest.</summary>
    public required BaseAtomicPolicyAuthorityDigest PolicyAuthorityDigest { get; init; }
}

/// <summary>Classifies one evaluated module branch decision.</summary>
public enum BaseModuleDecisionKind
{
    /// <summary>An if-statement decision.</summary>
    IfStatement = 0,
    /// <summary>A conditional-expression decision.</summary>
    ConditionalExpression = 1,
}

/// <summary>Contains one dense evaluated branch decision.</summary>
public sealed record BaseModuleDecisionTraceEntry
{
    /// <summary>Gets the dense evaluation ordinal.</summary>
    public required int EvaluationOrdinal { get; init; }
    /// <summary>Gets the decision kind.</summary>
    public required BaseModuleDecisionKind Kind { get; init; }
    /// <summary>Gets the statement or expression ID.</summary>
    public required string DecisionId { get; init; }
    /// <summary>Gets whether the true branch was selected.</summary>
    public required bool SelectedTrue { get; init; }
}

/// <summary>Binds one finalized mutation item to its captured record.</summary>
public sealed record BaseModuleMutationItemCaptureBinding
{
    /// <summary>Gets the mutation ordinal.</summary>
    public required int MutationOrdinal { get; init; }
    /// <summary>Gets the record-capture ordinal.</summary>
    public required int RecordCaptureOrdinal { get; init; }
}

/// <summary>Contains one generation comparison selected by Runtime.</summary>
public sealed record BaseModuleGenerationComparison
{
    /// <summary>Gets the generation-capture ordinal.</summary>
    public required int CaptureOrdinal { get; init; }
    /// <summary>Gets the comparison kind.</summary>
    public required BaseModuleGenerationComparisonKind Kind { get; init; }
    /// <summary>Gets the exact expected value only for MustEqual.</summary>
    public BaseModuleGeneration? Expected { get; init; }
}

/// <summary>Contains one selected generation increment.</summary>
public sealed record BaseModuleGenerationIncrement
{
    /// <summary>Gets the generation-capture ordinal.</summary>
    public required int CaptureOrdinal { get; init; }
    /// <summary>Gets whether the selected statement may create an absent cell.</summary>
    public required bool CreateIfAbsent { get; init; }
}

/// <summary>Contains the complete L45 transaction execution safety envelope.</summary>
public sealed record BaseAtomicMutationExecutionLimits
{
    /// <summary>Gets the maximum mutation item count.</summary>
    public required int MaximumItems { get; init; }
    /// <summary>Gets the maximum canonical query-node count.</summary>
    public required int MaximumQueryNodes { get; init; }
    /// <summary>Gets the maximum canonical query depth.</summary>
    public required int MaximumQueryDepth { get; init; }
    /// <summary>Gets the maximum literal-value count.</summary>
    public required int MaximumLiteralValues { get; init; }
    /// <summary>Gets the maximum selected-record count.</summary>
    public required int MaximumSelectedRecords { get; init; }
    /// <summary>Gets the maximum produced-mutation count.</summary>
    public required int MaximumProducedMutations { get; init; }
    /// <summary>Gets the maximum query execution count.</summary>
    public required int MaximumQueryExecutions { get; init; }
    /// <summary>Gets the maximum previous-state requirement count.</summary>
    public required int MaximumPreviousStateRequirements { get; init; }
    /// <summary>Gets the maximum module record-capture count.</summary>
    public required int MaximumRecordCaptures { get; init; }
    /// <summary>Gets the maximum module relation-target capture count.</summary>
    public required int MaximumRelationTargetCaptures { get; init; }
    /// <summary>Gets the maximum generation-cell read count.</summary>
    public required int MaximumGenerationReads { get; init; }
    /// <summary>Gets the maximum generation comparison count.</summary>
    public required int MaximumGenerationComparisons { get; init; }
    /// <summary>Gets the maximum generation increment count.</summary>
    public required int MaximumGenerationIncrements { get; init; }
    /// <summary>Gets the maximum guard-node count.</summary>
    public required int MaximumGuardNodes { get; init; }
    /// <summary>Gets the maximum guard depth.</summary>
    public required int MaximumGuardDepth { get; init; }
    /// <summary>Gets the maximum statement count.</summary>
    public required int MaximumStatements { get; init; }
    /// <summary>Gets the maximum branch count.</summary>
    public required int MaximumBranches { get; init; }
    /// <summary>Gets the maximum expression-node count.</summary>
    public required int MaximumExpressionNodes { get; init; }
    /// <summary>Gets the maximum canonical selected bytes.</summary>
    public required long MaximumSelectedBytes { get; init; }
    /// <summary>Gets the maximum evidence bytes.</summary>
    public required long MaximumEvidenceBytes { get; init; }
    /// <summary>Gets the maximum complete transient bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets the maximum read intervals.</summary>
    public required int MaximumReadIntervals { get; init; }
    /// <summary>Gets the maximum subject-reference validations.</summary>
    public required int MaximumSubjectValidations { get; init; }
    /// <summary>Gets the maximum authority reads.</summary>
    public required int MaximumAuthorityReads { get; init; }
    /// <summary>Gets the maximum relation-check count.</summary>
    public required int MaximumRelationChecks { get; init; }
    /// <summary>Gets the maximum unique-constraint-check count.</summary>
    public required int MaximumUniqueConstraintChecks { get; init; }
    /// <summary>Gets the maximum retirement projections.</summary>
    public required int MaximumRetirementProjections { get; init; }
    /// <summary>Gets the maximum retirement barrier reads.</summary>
    public required int MaximumRetirementBarrierReads { get; init; }
    /// <summary>Gets the maximum retirement acknowledgement reads.</summary>
    public required int MaximumRetirementAcknowledgementReads { get; init; }
    /// <summary>Gets the maximum retirement publications.</summary>
    public required int MaximumRetirementPublications { get; init; }
    /// <summary>Gets the maximum canonical request bytes.</summary>
    public required long MaximumRequestBytes { get; init; }
    /// <summary>Gets the maximum retained generation bytes.</summary>
    public required long MaximumGenerationBytes { get; init; }
    /// <summary>Gets the maximum provisional written bytes.</summary>
    public required long MaximumWrittenBytes { get; init; }
    /// <summary>Gets the maximum mutation-fact bytes.</summary>
    public required long MaximumFactBytes { get; init; }
    /// <summary>Gets the maximum journal bytes.</summary>
    public required long MaximumJournalBytes { get; init; }
    /// <summary>Gets the maximum receipt bytes.</summary>
    public required long MaximumReceiptBytes { get; init; }
    /// <summary>Gets the maximum public result bytes.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets the maximum retirement evidence bytes.</summary>
    public required long MaximumRetirementEvidenceBytes { get; init; }
    /// <summary>Gets the maximum retirement publication bytes.</summary>
    public required long MaximumRetirementPublicationBytes { get; init; }
    /// <summary>Gets the four independently owned execution deadlines.</summary>
    public required BaseAtomicMutationDeadlines Deadlines { get; init; }
}

/// <summary>Contains one final subject-state overlay entry prepared without persistent writes.</summary>
public sealed record BasePreparedSubjectOverlayEvidence
{
    /// <summary>Gets the exported contract ID.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the canonical subject ID.</summary>
    public required BaseSubjectId SubjectId { get; init; }
    /// <summary>Gets whether the subject exists in the final overlay.</summary>
    public required bool Exists { get; init; }
    /// <summary>Gets the final incarnation when present.</summary>
    public BaseSubjectIncarnation? Incarnation { get; init; }
    /// <summary>Gets the final active-state value when declared.</summary>
    public bool? Active { get; init; }
    /// <summary>Gets the final logical scope when declared.</summary>
    public string? Scope { get; init; }
    /// <summary>Gets provider-owned protected final scope.</summary>
    public required BaseProtectedSubjectScope ProtectedScope { get; init; }
    /// <summary>Gets the final lifecycle state.</summary>
    public BaseSubjectLifecycleState? LifecycleState { get; init; }
    /// <summary>Gets the final subject-local sequence.</summary>
    public long? SubjectSequence { get; init; }
}

/// <summary>Contains one provider-prepared subject-reference validation result.</summary>
public sealed record BasePreparedSubjectValidationEvidence
{
    /// <summary>Gets the matching validation ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the matching mutation ordinal.</summary>
    public required int MutationOrdinal { get; init; }
    /// <summary>Gets the stable source field ID.</summary>
    public required string SourceFieldId { get; init; }
    /// <summary>Gets the closed logical result.</summary>
    public required BaseSubjectValidationState State { get; init; }
}

/// <summary>Classifies one closed provider subject-validation result.</summary>
public enum BaseSubjectValidationState
{
    /// <summary>The reference is valid in the prepared final-state overlay.</summary>
    Valid = 0,
    /// <summary>The reference is invalid without disclosing a reason.</summary>
    Invalid = 1,
}

/// <summary>Contains provider-owned exact accounting for mutation preparation.</summary>
public sealed record BasePreparedAtomicMutationAccounting
{
    /// <summary>Gets authority reads actually performed.</summary>
    public required int AuthorityReads { get; init; }
    /// <summary>Gets generation reads performed.</summary>
    public required int GenerationReads { get; init; }
    /// <summary>Gets generation comparisons performed.</summary>
    public required int GenerationComparisons { get; init; }
    /// <summary>Gets generation increments prepared.</summary>
    public required int GenerationIncrements { get; init; }
    /// <summary>Gets normalized read intervals retained.</summary>
    public required int ReadIntervals { get; init; }
    /// <summary>Gets canonical selected bytes retained.</summary>
    public required long SelectedBytes { get; init; }
    /// <summary>Gets canonical generation bytes retained.</summary>
    public required long GenerationBytes { get; init; }
    /// <summary>Gets canonical evidence bytes retained.</summary>
    public required long EvidenceBytes { get; init; }
    /// <summary>Gets complete transient bytes retained.</summary>
    public required long TransientBytes { get; init; }
    /// <summary>Gets retirement barrier reads performed.</summary>
    public required int RetirementBarrierReads { get; init; }
    /// <summary>Gets retirement acknowledgement reads performed.</summary>
    public required int RetirementAcknowledgementReads { get; init; }
    /// <summary>Gets retirement projections processed.</summary>
    public required int RetirementProjections { get; init; }
    /// <summary>Gets retirement publications processed.</summary>
    public required int RetirementPublications { get; init; }
    /// <summary>Gets retirement evidence bytes.</summary>
    public required long RetirementEvidenceBytes { get; init; }
    /// <summary>Gets retirement publication bytes.</summary>
    public required long RetirementPublicationBytes { get; init; }
}

/// <summary>Contains current transaction-bound authority for one exported logical-subject contract.</summary>
public sealed record BaseSubjectTransactionAuthorityEvidence
{
    /// <summary>Gets the exported contract identifier.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the exported contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the normalized contract checksum.</summary>
    public required string ContractChecksum { get; init; }
    /// <summary>Gets the authoritative store-instance identifier.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the current store restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the current schema generation.</summary>
    public required long SchemaGeneration { get; init; }
    /// <summary>Gets the current exported-subject state generation.</summary>
    public required long StateGeneration { get; init; }
    /// <summary>Gets the current authority epoch.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
}

/// <summary>Contains one single-use mutation preparation bound to an open provider session.</summary>
public sealed record BasePreparedAtomicExecution
{
    /// <summary>Gets the closed execution shape.</summary>
    public required BaseAtomicMutationExecutionKind Kind { get; init; }
    /// <summary>Gets the finalized plan digest.</summary>
    public required string PlanDigest { get; init; }
    /// <summary>Gets authoritative provider snapshot evidence.</summary>
    public required BaseAtomicMutationAuthorityEvidence Authority { get; init; }
    /// <summary>Gets one ordered dynamic authority entry per referenced or lifecycle-mutated subject contract.</summary>
    public required ImmutableArray<BaseSubjectTransactionAuthorityEvidence> SubjectAuthorities { get; init; }
    /// <summary>Gets final resolved dispositions.</summary>
    public required ImmutableArray<BaseCapturedMutationDisposition> Dispositions { get; init; }
    /// <summary>Gets prepared L50 generation evidence.</summary>
    public required ImmutableArray<BasePreparedModuleGenerationEvidence> Generations { get; init; }
    /// <summary>Gets aggregate prepared activation evidence.</summary>
    public BasePreparedActivationExtension? Activations { get; init; }
    /// <summary>Gets the prepared activation-fence evidence.</summary>
    public BaseCapturedActivationGuardEvidence? ActivationGuard { get; init; }
    /// <summary>Gets prepared L53 semantic activation evidence.</summary>
    public BasePreparedSemanticActivation? SemanticActivation { get; init; }
    /// <summary>Gets provider-prepared retirement evidence.</summary>
    public BaseSubjectRetirementPreparedEvidence? SubjectRetirement { get; init; }
    /// <summary>Gets provider-prepared text projection evidence.</summary>
    public BasePreparedTextMutationEvidence? Text { get; init; }
    /// <summary>Gets canonical final subject overlays.</summary>
    public required ImmutableArray<BasePreparedSubjectOverlayEvidence> SubjectOverlay { get; init; }
    /// <summary>Gets ordered subject-validation results.</summary>
    public required ImmutableArray<BasePreparedSubjectValidationEvidence> SubjectValidations { get; init; }
    /// <summary>Gets normalized transaction-local conflict intervals covering preparation.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> ReadIntervals { get; init; }
    /// <summary>Gets exact provider preparation accounting.</summary>
    public required BasePreparedAtomicMutationAccounting Accounting { get; init; }
}

/// <summary>Classifies one prepared generation transition.</summary>
public enum BaseModuleGenerationPreparationDisposition
{
    /// <summary>The cell was absent and remains absent.</summary>
    RemainedAbsent = 0,
    /// <summary>The existing generation is preserved.</summary>
    Preserved = 1,
    /// <summary>An absent cell is created at generation one.</summary>
    Created = 2,
    /// <summary>An existing cell is incremented exactly once.</summary>
    Incremented = 3,
}

/// <summary>Contains immutable evidence for one prepared generation cell.</summary>
public sealed record BasePreparedModuleGenerationEvidence
{
    /// <summary>Gets the capture ordinal.</summary>
    public required int CaptureOrdinal { get; init; }
    /// <summary>Gets the canonical cell-key digest.</summary>
    public required string CanonicalKeyDigest { get; init; }
    /// <summary>Gets the prior generation when present.</summary>
    public BaseModuleGeneration? Previous { get; init; }
    /// <summary>Gets the resulting generation when present.</summary>
    public BaseModuleGeneration? Resulting { get; init; }
    /// <summary>Gets the closed preparation disposition.</summary>
    public required BaseModuleGenerationPreparationDisposition Disposition { get; init; }
}

/// <summary>Contains exact provider accounting for the committed attempt.</summary>
public sealed record BaseProvisionalAtomicMutationAccounting
{
    /// <summary>Gets canonical written bytes.</summary>
    public required long WrittenBytes { get; init; }
    /// <summary>Gets canonical generation-cell bytes written or retained by the attempt.</summary>
    public required long GenerationBytes { get; init; }
    /// <summary>Gets canonical mutation-fact bytes.</summary>
    public required long FactBytes { get; init; }
    /// <summary>Gets durable journal bytes.</summary>
    public required long JournalBytes { get; init; }
    /// <summary>Gets exact relation-check work.</summary>
    public required int RelationChecks { get; init; }
    /// <summary>Gets exact unique-constraint-check work.</summary>
    public required int UniqueConstraintChecks { get; init; }
    /// <summary>Gets exact subject-authority reads.</summary>
    public required int AuthorityReads { get; init; }
    /// <summary>Gets exact normalized conflict intervals.</summary>
    public required int ReadIntervals { get; init; }
    /// <summary>Gets canonical selected bytes retained by the attempt.</summary>
    public required long SelectedBytes { get; init; }
    /// <summary>Gets canonical evidence bytes retained by the attempt.</summary>
    public required long EvidenceBytes { get; init; }
    /// <summary>Gets complete retained transient bytes.</summary>
    public required long TransientBytes { get; init; }
    /// <summary>Gets retirement barrier reads performed.</summary>
    public required int RetirementBarrierReads { get; init; }
    /// <summary>Gets retirement acknowledgement reads performed.</summary>
    public required int RetirementAcknowledgementReads { get; init; }
    /// <summary>Gets retirement projections processed.</summary>
    public required int RetirementProjections { get; init; }
    /// <summary>Gets retirement publications processed.</summary>
    public required int RetirementPublications { get; init; }
    /// <summary>Gets retirement evidence bytes.</summary>
    public required long RetirementEvidenceBytes { get; init; }
    /// <summary>Gets retirement publication bytes.</summary>
    public required long RetirementPublicationBytes { get; init; }
}

/// <summary>Contains one applied but not yet externally confirmed atomic mutation.</summary>
public sealed record BaseProvisionalAtomicExecution
{
    /// <summary>Gets the execution-kind discriminator.</summary>
    public required BaseAtomicMutationExecutionKind Kind { get; init; }
    /// <summary>Gets the applied plan digest.</summary>
    public required string PlanDigest { get; init; }
    /// <summary>Gets authoritative provider snapshot evidence.</summary>
    public required BaseAtomicMutationAuthorityEvidence Authority { get; init; }
    /// <summary>Gets deeply owned mutation facts.</summary>
    public required ImmutableArray<BaseOwnedMutationFact> Facts { get; init; }
    /// <summary>Gets committed-shape generation evidence without public result authority.</summary>
    public required ImmutableArray<BaseModuleCommittedGeneration> Generations { get; init; }
    /// <summary>Gets aggregate provisional activation evidence.</summary>
    public BaseProvisionalActivationExtension? Activations { get; init; }
    /// <summary>Gets activation-fence evidence validated in the committing transaction.</summary>
    public BaseCapturedActivationGuardEvidence? ActivationGuard { get; init; }
    /// <summary>Gets provisional L53 semantic activation evidence.</summary>
    public BaseProvisionalSemanticActivation? SemanticActivation { get; init; }
    /// <summary>Gets applied retirement evidence.</summary>
    public BaseSubjectRetirementProvisionalEvidence? SubjectRetirement { get; init; }
    /// <summary>Gets applied text projection evidence.</summary>
    public BaseAppliedTextMutationEvidence? Text { get; init; }
    /// <summary>Gets exact provisional apply accounting.</summary>
    public required BaseProvisionalAtomicMutationAccounting Accounting { get; init; }
}

/// <summary>Contains Runtime-owned result and receipt authority produced before provider commit.</summary>
public sealed record BaseAtomicMutationCommitFinalization
{
    /// <summary>Gets the exact finalized plan digest.</summary>
    public required string PlanDigest { get; init; }
    /// <summary>Gets the closed receipt persisted atomically by the provider.</summary>
    public required BaseAtomicReceiptResult Receipt { get; init; }
    /// <summary>Gets exact canonical public result bytes for a module mutation.</summary>
    public required ImmutableArray<byte> CanonicalResultBytes { get; init; }
    /// <summary>Gets exact final aggregate accounting.</summary>
    public required BaseAtomicCommitAccounting Accounting { get; init; }
}

/// <summary>Contains exact accounting after Runtime adds result and receipt artifacts.</summary>
public sealed record BaseAtomicCommitAccounting
{
    /// <summary>Gets canonical written bytes.</summary>
    public required long WrittenBytes { get; init; }
    /// <summary>Gets canonical generation bytes.</summary>
    public required long GenerationBytes { get; init; }
    /// <summary>Gets canonical mutation-fact bytes.</summary>
    public required long FactBytes { get; init; }
    /// <summary>Gets canonical journal bytes.</summary>
    public required long JournalBytes { get; init; }
    /// <summary>Gets canonical receipt bytes.</summary>
    public required long ReceiptBytes { get; init; }
    /// <summary>Gets canonical public result bytes.</summary>
    public required long ResultBytes { get; init; }
    /// <summary>Gets exact relation checks.</summary>
    public required int RelationChecks { get; init; }
    /// <summary>Gets exact unique-constraint checks.</summary>
    public required int UniqueConstraintChecks { get; init; }
    /// <summary>Gets exact authority reads.</summary>
    public required int AuthorityReads { get; init; }
    /// <summary>Gets exact normalized read intervals.</summary>
    public required int ReadIntervals { get; init; }
    /// <summary>Gets canonical selected bytes.</summary>
    public required long SelectedBytes { get; init; }
    /// <summary>Gets canonical evidence bytes.</summary>
    public required long EvidenceBytes { get; init; }
    /// <summary>Gets the complete aggregate transient bytes.</summary>
    public required long TransientBytes { get; init; }
    /// <summary>Gets retirement barrier reads performed.</summary>
    public required int RetirementBarrierReads { get; init; }
    /// <summary>Gets retirement acknowledgement reads performed.</summary>
    public required int RetirementAcknowledgementReads { get; init; }
    /// <summary>Gets retirement projections processed.</summary>
    public required int RetirementProjections { get; init; }
    /// <summary>Gets retirement publications processed.</summary>
    public required int RetirementPublications { get; init; }
    /// <summary>Gets retirement evidence bytes.</summary>
    public required long RetirementEvidenceBytes { get; init; }
    /// <summary>Gets retirement publication bytes.</summary>
    public required long RetirementPublicationBytes { get; init; }
}
