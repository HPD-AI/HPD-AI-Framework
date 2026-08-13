namespace HPD.Base;

/// <summary>Describes one scalar field's installed exported-subject reference contract.</summary>
public sealed record BaseSubjectReferenceDefinition
{
    /// <summary>Gets the target exported-contract identifier.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the target exported-contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the target exported-contract checksum.</summary>
    public required string ContractChecksum { get; init; }
    /// <summary>Gets the logical validity required during mutation.</summary>
    public required BaseSubjectReferenceRequirement Requirement { get; init; }
    /// <summary>Gets the required provider validation guarantee.</summary>
    public required BaseSubjectValidationGuarantee Guarantee { get; init; }
}

/// <summary>Defines one immutable exported logical-subject contract.</summary>
public sealed record BaseExportedSubjectDefinition
{
    /// <summary>Gets the stable exported-contract identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive contract version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the installed module that owns the contract.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the canonical subject-identifier grammar.</summary>
    public required BaseSubjectIdKind SubjectIdKind { get; init; }
    /// <summary>Gets the maximum canonical UTF-8 subject-identifier length.</summary>
    public required int MaximumSubjectIdUtf8Bytes { get; init; }
    /// <summary>Gets the logical scope of the exported subject.</summary>
    public required BaseSubjectScopeKind Scope { get; init; }
    /// <summary>Gets the exact L38 grant required for acquisition.</summary>
    public required string AcquisitionGrantId { get; init; }
    /// <summary>Gets the exact L38 grant required for mutation-bound validation.</summary>
    public required string ValidationGrantId { get; init; }
    /// <summary>Gets the exact L38 grant required for authority-epoch administration.</summary>
    public required string AdministrationGrantId { get; init; }
    /// <summary>Gets the audiences to which this contract may be projected.</summary>
    public required HPDBaseEndpointAudience[] Audiences { get; init; }
    /// <summary>Gets the closed transaction-local validation plan.</summary>
    public required BaseSubjectValidationPlanDefinition ValidationPlan { get; init; }
}

/// <summary>Defines the only supported subject-identifier binding.</summary>
public enum BaseSubjectIdBinding
{
    /// <summary>The private record ID is the canonical public subject ID.</summary>
    RecordId = 0,
}

/// <summary>Identifies the closed active-state binding.</summary>
public enum BaseSubjectActiveBindingKind
{
    /// <summary>No active-state field is declared.</summary>
    NotDeclared = 0,
    /// <summary>A required, non-null Boolean field carries active state.</summary>
    RequiredBooleanField = 1,
}

/// <summary>Defines how logical active state is read from private subject storage.</summary>
public sealed record BaseSubjectActiveBinding
{
    /// <summary>Gets the active-state binding kind.</summary>
    public required BaseSubjectActiveBindingKind Kind { get; init; }
    /// <summary>Gets the stable private Boolean field ID when declared.</summary>
    public string? FieldId { get; init; }
    /// <summary>Gets the Boolean value that represents active state.</summary>
    public bool ActiveValue { get; init; }
}

/// <summary>Identifies the closed logical-scope binding.</summary>
public enum BaseSubjectScopeBindingKind
{
    /// <summary>No tenant or project scope is present.</summary>
    Global = 0,
    /// <summary>A required, non-null ordinal string carries tenant scope.</summary>
    RequiredTenantField = 1,
    /// <summary>A required, non-null ordinal string carries project scope.</summary>
    RequiredProjectField = 2,
}

/// <summary>Defines how logical scope is read from private subject storage.</summary>
public sealed record BaseSubjectScopeBinding
{
    /// <summary>Gets the scope-binding kind.</summary>
    public required BaseSubjectScopeBindingKind Kind { get; init; }
    /// <summary>Gets the stable private scope-field ID when declared.</summary>
    public string? FieldId { get; init; }
}

/// <summary>Identifies the only supported provider access shape.</summary>
public enum BaseSubjectValidationAccessShape
{
    /// <summary>Uses the contract, subject, and private-record primary keys.</summary>
    ContractAndSubjectPrimaryKeys = 0,
}

/// <summary>Defines immutable bounds for one exported-subject validation plan.</summary>
public sealed record BaseSubjectValidationLimits
{
    /// <summary>Gets the maximum reference fields in one record.</summary>
    public required int MaximumReferencesPerRecord { get; init; }
    /// <summary>Gets the maximum references in one atomic mutation.</summary>
    public required int MaximumReferencesPerMutation { get; init; }
    /// <summary>Gets the maximum distinct validation plans in one mutation.</summary>
    public required int MaximumValidationPlansPerMutation { get; init; }
    /// <summary>Gets the maximum authoritative reads.</summary>
    public required int MaximumAuthorityReads { get; init; }
    /// <summary>Gets the maximum normalized read intervals.</summary>
    public required int MaximumReadIntervals { get; init; }
    /// <summary>Gets the maximum selected canonical bytes.</summary>
    public required long MaximumSelectedBytes { get; init; }
    /// <summary>Gets the maximum provider evidence bytes.</summary>
    public required long MaximumEvidenceBytes { get; init; }
    /// <summary>Gets the maximum retained transient bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets the provider-session acquisition timeout.</summary>
    public required TimeSpan AcquisitionTimeout { get; init; }
    /// <summary>Gets the complete mutation execution timeout.</summary>
    public required TimeSpan ExecutionTimeout { get; init; }

    /// <summary>Creates the normative L45 host defaults.</summary>
    public static BaseSubjectValidationLimits Default { get; } = new()
    {
        MaximumReferencesPerRecord = 8,
        MaximumReferencesPerMutation = 256,
        MaximumValidationPlansPerMutation = 16,
        MaximumAuthorityReads = 256,
        MaximumReadIntervals = 256,
        MaximumSelectedBytes = 1_048_576,
        MaximumEvidenceBytes = 1_048_576,
        MaximumTransientBytes = 8_388_608,
        AcquisitionTimeout = TimeSpan.FromSeconds(5),
        ExecutionTimeout = TimeSpan.FromSeconds(15),
    };
}

/// <summary>Defines one closed provider-neutral subject-validation plan.</summary>
public sealed record BaseSubjectValidationPlanDefinition
{
    /// <summary>Gets the stable plan identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive plan version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the exported contract identifier.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the exported contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the lowercase canonical contract checksum.</summary>
    public required string ContractChecksum { get; init; }
    /// <summary>Gets the exact private system collection identifier.</summary>
    public required string PrivateCollectionId { get; init; }
    /// <summary>Gets the subject-ID binding.</summary>
    public required BaseSubjectIdBinding SubjectId { get; init; }
    /// <summary>Gets the active-state binding.</summary>
    public required BaseSubjectActiveBinding Active { get; init; }
    /// <summary>Gets the logical-scope binding.</summary>
    public required BaseSubjectScopeBinding Scope { get; init; }
    /// <summary>Gets the required provider access shape.</summary>
    public required BaseSubjectValidationAccessShape Access { get; init; }
    /// <summary>Gets the immutable validation limits.</summary>
    public required BaseSubjectValidationLimits Limits { get; init; }
}

/// <summary>Proves how one immutable exported-subject validation plan was lowered by the selected store.</summary>
public sealed record BaseSubjectValidationPlanReceipt
{
    /// <summary>Gets the stable validation-plan identifier.</summary>
    public required string PlanId { get; init; }
    /// <summary>Gets the positive validation-plan version.</summary>
    public required int PlanVersion { get; init; }
    /// <summary>Gets the lowercase canonical plan checksum.</summary>
    public required string PlanChecksum { get; init; }
    /// <summary>Gets the exact authoritative store-instance identifier.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the schema generation against which the plan was lowered.</summary>
    public required long SchemaGeneration { get; init; }
    /// <summary>Gets the closed provider access shape.</summary>
    public required BaseSubjectValidationAccessShape Access { get; init; }
    /// <summary>Gets the provider lowering format version.</summary>
    public required int LoweringFormatVersion { get; init; }
}

/// <summary>Supplies deeply owned exported-subject validation-plan receipts from one authoritative store.</summary>
public interface IBaseSubjectValidationPlanReceiptStore
{
    /// <summary>Reads every installed validation-plan lowering receipt.</summary>
    ValueTask<OperationResult<BaseSubjectValidationPlanReceipt[]>> ReadSubjectValidationPlanReceiptsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Defines one authorized registered-read acquisition projection.</summary>
public sealed record BaseSubjectAcquisitionDefinition
{
    /// <summary>Gets the stable acquisition identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive acquisition version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the exported contract identifier.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the exported contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the registered-read identifier.</summary>
    public required string RegisteredReadId { get; init; }
    /// <summary>Gets the exact L38 acquisition grant.</summary>
    public required string RequiredGrantId { get; init; }
    /// <summary>Gets the acquisition audience.</summary>
    public required HPDBaseEndpointAudience Audience { get; init; }
    /// <summary>Gets the maximum returned references.</summary>
    public required int MaximumResults { get; init; }
}

/// <summary>Contains provider-owned current publication integrity evidence for one exported subject contract.</summary>
public sealed record BaseSubjectCurrentPublicationReceipt
{
    /// <summary>Gets the preceding state generation.</summary>
    public required long PreviousStateGeneration { get; init; }
    /// <summary>Gets the published state generation.</summary>
    public required long PublishedStateGeneration { get; init; }
    /// <summary>Gets the current store restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the publication kind.</summary>
    public required BaseSubjectAuthorityPublicationKind Kind { get; init; }
    /// <summary>Gets the original shared journal position.</summary>
    public required BaseMutationJournalPosition OriginalPublicationPosition { get; init; }
    /// <summary>Gets the lowercase SHA-256 corruption-detection checksum.</summary>
    public required string PublicationDigest { get; init; }
}

/// <summary>Requests an explicit exported-subject authority epoch rotation.</summary>
public sealed record BaseSubjectEpochRotationRequest
{
    /// <summary>Gets the target contract ID.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the target contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the expected current state generation.</summary>
    public required long ExpectedStateGeneration { get; init; }
    /// <summary>Gets the exact destructive-intent confirmation.</summary>
    public required string DestructiveIntent { get; init; }
}

/// <summary>Reports a completed exported-subject authority epoch rotation.</summary>
public sealed record BaseSubjectEpochRotationResult
{
    /// <summary>Gets the target contract ID.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the target contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the preceding state generation.</summary>
    public required long PreviousStateGeneration { get; init; }
    /// <summary>Gets the published state generation.</summary>
    public required long PublishedStateGeneration { get; init; }
    /// <summary>Gets the exact shared publication position.</summary>
    public required BaseMutationJournalPosition PublicationPosition { get; init; }
    /// <summary>Gets the number of examined current records.</summary>
    public required long ExaminedRecords { get; init; }
    /// <summary>Gets the number of rewritten subject references.</summary>
    public required long RewrittenReferences { get; init; }
}

/// <summary>Provides ControlPlane-only administration of exported-subject authority state.</summary>
public interface IBaseSubjectAdministration
{
    /// <summary>Rotates one authority epoch under the exclusive maintenance boundary.</summary>
    ValueTask<OperationResult<BaseSubjectEpochRotationResult>> RotateEpochAsync(
        BaseSubjectEpochRotationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Reports one provider-owned current exported-subject publication state.</summary>
public sealed record BaseSubjectCurrentPublicationState
{
    /// <summary>Gets the exported-contract identifier.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the exported-contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the current contract checksum.</summary>
    public required string ContractChecksum { get; init; }
    /// <summary>Gets the current authority epoch.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
    /// <summary>Gets the exact current receipt.</summary>
    public required BaseSubjectCurrentPublicationReceipt Receipt { get; init; }
}

/// <summary>Supplies bounded current-publication inspection to BASE's internal control dispatcher.</summary>
public interface IBaseSubjectPublicationStore
{
    /// <summary>Reads every installed current publication as deeply owned provider evidence.</summary>
    ValueTask<OperationResult<BaseSubjectCurrentPublicationState[]>> ReadCurrentSubjectPublicationsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Describes a provider's certified exported-subject validation envelope.</summary>
public sealed record BaseSubjectReferenceCapability
{
    /// <summary>Gets whether same-transaction snapshot validation is supported.</summary>
    public required bool TransactionSnapshotValidationSupported { get; init; }
    /// <summary>Gets the maximum references in one record.</summary>
    public required int MaximumReferencesPerRecord { get; init; }
    /// <summary>Gets the maximum references in one atomic mutation.</summary>
    public required int MaximumReferencesPerMutation { get; init; }
    /// <summary>Gets the maximum canonical subject-ID byte length.</summary>
    public required int MaximumSubjectIdUtf8Bytes { get; init; }
    /// <summary>Gets the maximum distinct validation plans in one mutation.</summary>
    public required int MaximumValidationPlansPerMutation { get; init; }
    /// <summary>Gets the maximum authoritative reads.</summary>
    public required int MaximumAuthorityReads { get; init; }
    /// <summary>Gets the maximum normalized read intervals.</summary>
    public required int MaximumReadIntervals { get; init; }
    /// <summary>Gets the maximum selected canonical bytes.</summary>
    public required long MaximumSelectedBytes { get; init; }
    /// <summary>Gets the maximum provider evidence bytes.</summary>
    public required long MaximumEvidenceBytes { get; init; }
    /// <summary>Gets the maximum retained transient bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets the maximum complete operation time.</summary>
    public required TimeSpan MaximumExecutionTime { get; init; }
}

/// <summary>Provides the normative certified envelope for BASE's built-in authoritative stores.</summary>
public static class BaseSubjectProviderCapabilities
{
    /// <summary>Gets the closed L45 envelope implemented by InMemory and SQLite.</summary>
    public static BaseSubjectReferenceCapability BuiltIn { get; } = new()
    {
        TransactionSnapshotValidationSupported = true,
        MaximumReferencesPerRecord = 32,
        MaximumReferencesPerMutation = 1_024,
        MaximumSubjectIdUtf8Bytes = 256,
        MaximumValidationPlansPerMutation = 64,
        MaximumAuthorityReads = 1_024,
        MaximumReadIntervals = 1_024,
        MaximumSelectedBytes = 8_388_608,
        MaximumEvidenceBytes = 8_388_608,
        MaximumTransientBytes = 67_108_864,
        MaximumExecutionTime = TimeSpan.FromMinutes(2),
    };
}
