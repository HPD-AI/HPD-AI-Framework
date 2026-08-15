namespace HPD.Base;
/// <summary>Defines base Schema Compatibility.</summary>
public enum BaseSchemaCompatibility
{
    /// <summary>Identifies unknown.</summary>
Unknown,
    /// <summary>Identifies compatible.</summary>
Compatible,
    /// <summary>Identifies migration Required.</summary>
MigrationRequired,
    /// <summary>Identifies incompatible.</summary>
Incompatible,
    /// <summary>Identifies drifted.</summary>
Drifted
}

/// <summary>Defines base Schema Asset State.</summary>
public enum BaseSchemaAssetState
{
    /// <summary>Identifies absent.</summary>
Absent,
    /// <summary>Identifies declared.</summary>
Declared,
    /// <summary>Identifies building.</summary>
Building,
    /// <summary>Identifies ready.</summary>
Ready,
    /// <summary>Identifies failed.</summary>
Failed,
    /// <summary>Identifies retiring.</summary>
Retiring,
    /// <summary>Identifies drifted.</summary>
Drifted
}

/// <summary>Defines base Schema Migration State.</summary>
public enum BaseSchemaMigrationState
{
    /// <summary>Identifies none.</summary>
None,
    /// <summary>Identifies planning.</summary>
Planning,
    /// <summary>Identifies applying.</summary>
Applying,
    /// <summary>Identifies ready.</summary>
Ready,
    /// <summary>Identifies failed.</summary>
Failed,
    /// <summary>Identifies indeterminate.</summary>
Indeterminate
}

/// <summary>Defines base Schema Apply Outcome.</summary>
public enum BaseSchemaApplyOutcome
{
    /// <summary>Identifies applied.</summary>
Applied,
    /// <summary>Identifies no Changes.</summary>
NoChanges,
    /// <summary>Identifies rejected.</summary>
Rejected,
    /// <summary>Identifies rolled Back.</summary>
RolledBack,
    /// <summary>Identifies indeterminate.</summary>
Indeterminate
}

/// <summary>Defines base Schema Plan Classification.</summary>
public enum BaseSchemaPlanClassification
{
    /// <summary>Identifies no Changes.</summary>
NoChanges,
    /// <summary>Identifies safe Structural.</summary>
SafeStructural,
    /// <summary>Identifies destructive.</summary>
Destructive,
    /// <summary>Identifies data Migration Required.</summary>
DataMigrationRequired,
    /// <summary>Identifies unsupported.</summary>
Unsupported,
    /// <summary>Identifies drift Blocked.</summary>
DriftBlocked
}

/// <summary>Defines base Schema Structural Verification.</summary>
public enum BaseSchemaStructuralVerification
{
    /// <summary>Identifies not Applicable.</summary>
NotApplicable,
    /// <summary>Identifies verified.</summary>
Verified,
    /// <summary>Identifies failed.</summary>
Failed
}

/// <summary>Defines base External Data Migration Verification.</summary>
public enum BaseExternalDataMigrationVerification
{
    /// <summary>Identifies not Applicable.</summary>
NotApplicable,
    /// <summary>Identifies host Attested.</summary>
HostAttested
}

/// <summary>Defines base Semantic Conversion Verification.</summary>
public enum BaseSemanticConversionVerification
{
    /// <summary>Identifies not Applicable.</summary>
NotApplicable,
    /// <summary>Identifies not Verified By Base.</summary>
NotVerifiedByBase
}

/// <summary>Defines base Schema Operation Kind.</summary>
public enum BaseSchemaOperationKind
{
    /// <summary>Identifies create Collection.</summary>
CreateCollection,
    /// <summary>Identifies remove Collection.</summary>
RemoveCollection,
    /// <summary>Identifies rename Collection.</summary>
RenameCollection,
    /// <summary>Identifies add Field.</summary>
AddField,
    /// <summary>Identifies rename Field.</summary>
RenameField,
    /// <summary>Identifies alter Field.</summary>
AlterField,
    /// <summary>Identifies remove Field.</summary>
RemoveField,
    /// <summary>Identifies add Relation.</summary>
AddRelation,
    /// <summary>Identifies alter Relation.</summary>
AlterRelation,
    /// <summary>Identifies remove Relation.</summary>
RemoveRelation,
    /// <summary>Identifies add Index.</summary>
AddIndex,
    /// <summary>Identifies alter Index.</summary>
AlterIndex,
    /// <summary>Identifies remove Index.</summary>
RemoveIndex,
    /// <summary>Identifies add Read.</summary>
AddRead,
    /// <summary>Identifies alter Read.</summary>
AlterRead,
    /// <summary>Identifies remove Read.</summary>
RemoveRead,
    /// <summary>Identifies provider Rebuild Required.</summary>
ProviderRebuildRequired,
    /// <summary>Identifies verify Asset.</summary>
VerifyAsset,
    /// <summary>Identifies adopt External Baseline.</summary>
AdoptExternalBaseline
}

/// <summary>Represents base Logical Collection.</summary>
public sealed record BaseLogicalCollection
{
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets whether the collection is an internal system collection.</summary>
    public bool System { get; init; }
    /// <summary>Gets the installed module identity owning a system collection.</summary>
    public string? SystemOwnerModuleId { get; init; }
    /// <summary>Gets the serializer contract checksum for this collection.</summary>
    public string? SerializerContractChecksum { get; init; }
}

/// <summary>Represents base Logical Field.</summary>
public sealed record BaseLogicalField
{
    /// <summary>Gets or sets collection Id.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the exact application-facing name.</summary>
    public required string ApplicationName { get; init; }
    /// <summary>Gets or sets stored Name.</summary>
    public required string StoredName { get; init; }
    /// <summary>Gets or sets type.</summary>
    public required string Type { get; init; }
    /// <summary>Gets or sets required.</summary>
    public bool Required { get; init; }
    /// <summary>Gets or sets nullable.</summary>
    public bool Nullable { get; init; }
    /// <summary>Gets the normalized confidentiality class.</summary>
    public BaseFieldConfidentiality Confidentiality { get; init; }
    /// <summary>Gets the normalized complete disclosure policy.</summary>
    public required BaseFieldDisclosurePolicy Disclosure { get; init; }
    /// <summary>Gets the decoded binary limit when this is a binary field.</summary>
    public int? MaximumBytes { get; init; }
    /// <summary>Gets the exported-subject reference contract when this is a reference field.</summary>
    public BaseSubjectReferenceDefinition? SubjectReference { get; init; }
}

/// <summary>Represents base Logical Index.</summary>
public sealed record BaseLogicalIndex
{
    /// <summary>Gets or sets collection Id.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets field Ids.</summary>
    public required string[] FieldIds { get; init; }
    /// <summary>Gets or sets unique.</summary>
    public bool Unique { get; init; }
}

/// <summary>Contains one canonical logical vector-index asset.</summary>
public sealed record BaseLogicalVectorIndex
{
    /// <summary>Gets the stable collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the stable vector-index identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the stable vector-field identifier.</summary>
    public required string VectorFieldId { get; init; }
    /// <summary>Gets the stable semantic vector-space identifier.</summary>
    public required string VectorSpaceId { get; init; }
    /// <summary>Gets the exact dimensions.</summary>
    public required int Dimensions { get; init; }
    /// <summary>Gets the portable comparison function.</summary>
    public required BaseVectorFunction Function { get; init; }
    /// <summary>Gets the stable pre-ranking filter-field identifiers.</summary>
    public required string[] FilterFieldIds { get; init; }
}

/// <summary>Represents base Logical Read.</summary>
public sealed record BaseLogicalRead
{
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets source Ids.</summary>
    public required string[] SourceIds { get; init; }
    /// <summary>Gets or sets projection Field Ids.</summary>
    public required string[] ProjectionFieldIds { get; init; }
    /// <summary>Gets the parameter serializer checksum.</summary>
    public required string ParameterSerializerContractChecksum { get; init; }
    /// <summary>Gets the row serializer checksum.</summary>
    public required string RowSerializerContractChecksum { get; init; }
}

/// <summary>Contains the public schema identity of one exported logical-subject contract.</summary>
public sealed record BaseLogicalExportedSubject
{
    /// <summary>Gets the stable contract identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the owning installed module identifier.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the normalized contract checksum.</summary>
    public required string Checksum { get; init; }
    /// <summary>Gets the subject-ID grammar.</summary>
    public required BaseSubjectIdKind SubjectIdKind { get; init; }
    /// <summary>Gets the maximum canonical subject-ID byte length.</summary>
    public required int MaximumSubjectIdUtf8Bytes { get; init; }
    /// <summary>Gets the logical scope kind.</summary>
    public required BaseSubjectScopeKind Scope { get; init; }
    /// <summary>Gets the permitted endpoint audiences.</summary>
    public required HPDBaseEndpointAudience[] Audiences { get; init; }
}

/// <summary>Represents base Logical Schema.</summary>
public sealed record BaseLogicalSchema
{
    /// <summary>Gets or sets application Id.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets or sets contract Version.</summary>
    public required string ContractVersion { get; init; }
    /// <summary>Gets or sets collections.</summary>
    public required BaseLogicalCollection[] Collections { get; init; }
    /// <summary>Gets or sets fields.</summary>
    public required BaseLogicalField[] Fields { get; init; }
    /// <summary>Gets or sets relations.</summary>
    public required RelationDefinition[] Relations { get; init; }
    /// <summary>Gets or sets indexes.</summary>
    public required BaseLogicalIndex[] Indexes { get; init; }
    /// <summary>Gets the canonical vector-index assets.</summary>
    public required BaseLogicalVectorIndex[] VectorIndexes { get; init; }
    /// <summary>Gets or sets read Definitions.</summary>
    public required BaseLogicalRead[] ReadDefinitions { get; init; }
    /// <summary>Gets the installed exported logical-subject identities.</summary>
    public required BaseLogicalExportedSubject[] ExportedSubjects { get; init; }
    /// <summary>Gets or sets canonical Checksum.</summary>
    public required string CanonicalChecksum { get; init; }
}

/// <summary>Represents base Schema Observed Asset.</summary>
public sealed record BaseSchemaObservedAsset
{
    /// <summary>Gets or sets logical Id.</summary>
    public required string LogicalId { get; init; }
    /// <summary>Gets or sets state.</summary>
    public required BaseSchemaAssetState State { get; init; }
    /// <summary>Gets or sets safe Summary.</summary>
    public string? SafeSummary { get; init; }
}

/// <summary>Represents base Schema Observed State.</summary>
public sealed record BaseSchemaObservedState
{
    /// <summary>Gets or sets store Id.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets persisted Store Instance Id.</summary>
    public string? PersistedStoreInstanceId { get; init; }
    /// <summary>Gets or sets accepted Baseline Id.</summary>
    public string? AcceptedBaselineId { get; init; }
    /// <summary>Gets or sets accepted Checksum.</summary>
    public string? AcceptedChecksum { get; init; }
    /// <summary>Gets or sets generation.</summary>
    public long Generation { get; init; }
    /// <summary>Gets or sets compatibility.</summary>
    public BaseSchemaCompatibility Compatibility { get; init; }
    /// <summary>Gets or sets assets.</summary>
    public required BaseSchemaObservedAsset[] Assets { get; init; }
    /// <summary>Gets or sets migration State.</summary>
    public BaseSchemaMigrationState MigrationState { get; init; }
    /// <summary>Gets or sets last Applied Plan Id.</summary>
    public string? LastAppliedPlanId { get; init; }
}

/// <summary>Represents base Schema Logical Operation.</summary>
public sealed record BaseSchemaLogicalOperation
{
    /// <summary>Gets or sets kind.</summary>
    public required BaseSchemaOperationKind Kind { get; init; }
    /// <summary>Gets or sets logical Id.</summary>
    public required string LogicalId { get; init; }
    /// <summary>Gets or sets previous Name.</summary>
    public string? PreviousName { get; init; }
    /// <summary>Gets or sets target Name.</summary>
    public string? TargetName { get; init; }
    /// <summary>Gets or sets destructive.</summary>
    public bool Destructive { get; init; }
}

/// <summary>Represents base Schema Safe Physical Summary.</summary>
public sealed record BaseSchemaSafePhysicalSummary
{
    /// <summary>Gets or sets logical Id.</summary>
    public required string LogicalId { get; init; }
    /// <summary>Gets or sets summary.</summary>
    public required string Summary { get; init; }
}

/// <summary>Represents base Schema Prepared Plan.</summary>
public sealed record BaseSchemaPreparedPlan
{
    /// <summary>Gets a stricter classification established from provider-observed physical state.</summary>
    public BaseSchemaPlanClassification? RefinedClassification { get; init; }
    /// <summary>Gets or sets safe Physical Summary.</summary>
    public required BaseSchemaSafePhysicalSummary[] SafePhysicalSummary { get; init; }
    /// <summary>Gets or sets provider Id.</summary>
    public required string ProviderId { get; init; }
    /// <summary>Gets or sets provider Version.</summary>
    public required string ProviderVersion { get; init; }
    /// <summary>Gets or sets planner Version.</summary>
    public required string PlannerVersion { get; init; }
    /// <summary>Gets or sets persisted Store Instance Id.</summary>
    public required string PersistedStoreInstanceId { get; init; }
    /// <summary>Gets or sets provider Apply Artifact.</summary>
    public required byte[] ProviderApplyArtifact { get; init; }
    /// <summary>Gets or sets provider Apply Artifact Digest.</summary>
    public required string ProviderApplyArtifactDigest { get; init; }
}

/// <summary>Represents base Schema Plan.</summary>
public sealed record BaseSchemaPlan
{
    /// <summary>Gets or sets plan Id.</summary>
    public required string PlanId { get; init; }
    /// <summary>Gets or sets application Id.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets or sets store Id.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets persisted Store Instance Id.</summary>
    public required string PersistedStoreInstanceId { get; init; }
    /// <summary>Gets or sets provider Id.</summary>
    public required string ProviderId { get; init; }
    /// <summary>Gets or sets provider Version.</summary>
    public required string ProviderVersion { get; init; }
    /// <summary>Gets or sets planner Version.</summary>
    public required string PlannerVersion { get; init; }
    /// <summary>Gets or sets expected Generation.</summary>
    public long ExpectedGeneration { get; init; }
    /// <summary>Gets or sets baseline Id.</summary>
    public string? BaselineId { get; init; }
    /// <summary>Gets or sets baseline Checksum.</summary>
    public string? BaselineChecksum { get; init; }
    /// <summary>Gets or sets target Baseline Id.</summary>
    public required string TargetBaselineId { get; init; }
    /// <summary>Gets or sets target Checksum.</summary>
    public required string TargetChecksum { get; init; }
    /// <summary>Gets or sets classification.</summary>
    public BaseSchemaPlanClassification Classification { get; init; }
    /// <summary>Gets or sets operations.</summary>
    public required BaseSchemaLogicalOperation[] Operations { get; init; }
    /// <summary>Gets or sets warnings.</summary>
    public OperationWarning[]? Warnings { get; init; }
    /// <summary>Gets or sets requires External Data Migration.</summary>
    public bool RequiresExternalDataMigration { get; init; }
    /// <summary>Gets or sets external Migration Attestation.</summary>
    public BaseExternalMigrationAttestation? ExternalMigrationAttestation { get; init; }
    /// <summary>Gets or sets created At.</summary>
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>Gets or sets expires At.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
    /// <summary>Gets or sets logical Plan Digest.</summary>
    public required string LogicalPlanDigest { get; init; }
    /// <summary>Gets or sets provider Apply Artifact Digest.</summary>
    public required string ProviderApplyArtifactDigest { get; init; }
    /// <summary>Gets or sets protected Artifact.</summary>
    public required byte[] ProtectedArtifact { get; init; }
}

/// <summary>Represents base Schema Execution Capability.</summary>
public sealed record BaseSchemaExecutionCapability
{
    /// <summary>Gets or sets inspect.</summary>
    public bool Inspect { get; init; }
    /// <summary>Gets or sets prepare.</summary>
    public bool Prepare { get; init; }
    /// <summary>Gets or sets apply.</summary>
    public bool Apply { get; init; }
    /// <summary>Gets or sets history.</summary>
    public bool History { get; init; }
    /// <summary>Gets or sets classifications.</summary>
    public required BaseSchemaPlanClassification[] Classifications { get; init; }
}

/// <summary>Represents base Schema Inspection Request.</summary>
public sealed record BaseSchemaInspectionRequest
{
    /// <summary>Gets or sets application Id.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets or sets expected Logical Checksum.</summary>
    public required string ExpectedLogicalChecksum { get; init; }
    /// <summary>Gets or sets visibility.</summary>
    public VisibilityLevel Visibility { get; init; }
    /// <summary>Gets or sets inspection Timeout.</summary>
    public TimeSpan InspectionTimeout { get; init; }
}

/// <summary>Represents base Schema Preparation Request.</summary>
public sealed record BaseSchemaPreparationRequest
{
    /// <summary>Gets or sets application Id.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets or sets logical Delta.</summary>
    public required BaseSchemaLogicalOperation[] LogicalDelta { get; init; }
    /// <summary>Gets or sets observed State.</summary>
    public required BaseSchemaObservedState ObservedState { get; init; }
    /// <summary>Gets or sets classification.</summary>
    public BaseSchemaPlanClassification Classification { get; init; }
    /// <summary>Gets or sets expected Generation.</summary>
    public long ExpectedGeneration { get; init; }
    /// <summary>Gets or sets baseline Checksum.</summary>
    public string? BaselineChecksum { get; init; }
    /// <summary>Gets or sets target Checksum.</summary>
    public required string TargetChecksum { get; init; }
    /// <summary>Gets or sets preparation Timeout.</summary>
    public TimeSpan PreparationTimeout { get; init; }
}

/// <summary>Represents base Schema Provider Apply Request.</summary>
public sealed record BaseSchemaProviderApplyRequest
{
    /// <summary>Gets or sets verified Plan Envelope.</summary>
    public required byte[] VerifiedPlanEnvelope { get; init; }
    /// <summary>Gets or sets provider Apply Artifact.</summary>
    public required byte[] ProviderApplyArtifact { get; init; }
    /// <summary>Gets or sets expected Generation.</summary>
    public long ExpectedGeneration { get; init; }
    /// <summary>Gets or sets expected Baseline Checksum.</summary>
    public string? ExpectedBaselineChecksum { get; init; }
    /// <summary>Gets or sets expected Target Checksum.</summary>
    public required string ExpectedTargetChecksum { get; init; }
    /// <summary>Gets or sets allow Destructive.</summary>
    public bool AllowDestructive { get; init; }
    /// <summary>Gets or sets lease Timeout.</summary>
    public TimeSpan LeaseTimeout { get; init; }
    /// <summary>Gets or sets apply Timeout.</summary>
    public TimeSpan ApplyTimeout { get; init; }
    /// <summary>Gets or sets commit Completion Timeout.</summary>
    public TimeSpan CommitCompletionTimeout { get; init; }
}

/// <summary>Represents base Schema History Request.</summary>
public sealed record BaseSchemaHistoryRequest
{
    /// <summary>Gets or sets before Generation.</summary>
    public long? BeforeGeneration { get; init; }
    /// <summary>Gets or sets limit.</summary>
    public int Limit { get; init; }
    /// <summary>Gets or sets visibility.</summary>
    public VisibilityLevel Visibility { get; init; }
}

/// <summary>Represents base Schema Apply Result.</summary>
public sealed record BaseSchemaApplyResult
{
    /// <summary>Gets or sets outcome.</summary>
    public BaseSchemaApplyOutcome Outcome { get; init; }
    /// <summary>Gets or sets generation.</summary>
    public long Generation { get; init; }
    /// <summary>Gets or sets baseline Id.</summary>
    public required string BaselineId { get; init; }
    /// <summary>Gets or sets checksum.</summary>
    public required string Checksum { get; init; }
    /// <summary>Gets or sets state.</summary>
    public BaseSchemaMigrationState State { get; init; }
}

/// <summary>Contains authenticated plan facts passed to a provider after Runtime verification.</summary>
public sealed record BaseSchemaProviderVerifiedEnvelope
{
    /// <summary>Gets or sets plan Id.</summary>
    public required string PlanId { get; init; }
    /// <summary>Gets or sets target Baseline Id.</summary>
    public required string TargetBaselineId { get; init; }
    /// <summary>Gets or sets application Id.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets or sets store Id.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets persisted Store Instance Id.</summary>
    public required string PersistedStoreInstanceId { get; init; }
    /// <summary>Gets or sets provider Id.</summary>
    public required string ProviderId { get; init; }
    /// <summary>Gets or sets provider Version.</summary>
    public required string ProviderVersion { get; init; }
    /// <summary>Gets or sets planner Version.</summary>
    public required string PlannerVersion { get; init; }
    /// <summary>Gets or sets classification.</summary>
    public BaseSchemaPlanClassification Classification { get; init; }
    /// <summary>Gets or sets logical Plan Digest.</summary>
    public required string LogicalPlanDigest { get; init; }
    /// <summary>Gets or sets provider Apply Artifact Digest.</summary>
    public required string ProviderApplyArtifactDigest { get; init; }
    /// <summary>Gets or sets created At.</summary>
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>Gets or sets expires At.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
    /// <summary>Gets or sets structural Verification.</summary>
    public BaseSchemaStructuralVerification StructuralVerification { get; init; }
    /// <summary>Gets or sets external Data Migration.</summary>
    public BaseExternalDataMigrationVerification ExternalDataMigration { get; init; }
    /// <summary>Gets or sets semantic Conversion.</summary>
    public BaseSemanticConversionVerification SemanticConversion { get; init; }
    /// <summary>Gets or sets external Attestation Id.</summary>
    public string? ExternalAttestationId { get; init; }
    /// <summary>Gets or sets external Signer Id.</summary>
    public string? ExternalSignerId { get; init; }
}

/// <summary>Represents base Schema History Entry.</summary>
public sealed record BaseSchemaHistoryEntry
{
    /// <summary>Gets or sets generation.</summary>
    public long Generation { get; init; }
    /// <summary>Gets or sets baseline Id.</summary>
    public required string BaselineId { get; init; }
    /// <summary>Gets or sets checksum.</summary>
    public required string Checksum { get; init; }
    /// <summary>Gets or sets plan Id.</summary>
    public required string PlanId { get; init; }
    /// <summary>Gets or sets classification.</summary>
    public BaseSchemaPlanClassification Classification { get; init; }
    /// <summary>Gets or sets outcome.</summary>
    public BaseSchemaApplyOutcome Outcome { get; init; }
    /// <summary>Gets or sets provider Version.</summary>
    public required string ProviderVersion { get; init; }
    /// <summary>Gets or sets applied At.</summary>
    public DateTimeOffset AppliedAt { get; init; }
    /// <summary>Gets or sets structural Verification.</summary>
    public BaseSchemaStructuralVerification StructuralVerification { get; init; }
    /// <summary>Gets or sets external Data Migration.</summary>
    public BaseExternalDataMigrationVerification ExternalDataMigration { get; init; }
    /// <summary>Gets or sets semantic Conversion.</summary>
    public BaseSemanticConversionVerification SemanticConversion { get; init; }
    /// <summary>Gets or sets external Attestation Id.</summary>
    public string? ExternalAttestationId { get; init; }
    /// <summary>Gets or sets external Signer Id.</summary>
    public string? ExternalSignerId { get; init; }
}

/// <summary>Represents base Schema History Page.</summary>
public sealed record BaseSchemaHistoryPage
{
    /// <summary>Gets or sets items.</summary>
    public required BaseSchemaHistoryEntry[] Items { get; init; }
    /// <summary>Gets or sets before Generation.</summary>
    public long? BeforeGeneration { get; init; }
}

/// <summary>Requests a bounded schema plan for the installed logical application contract.</summary>
public sealed record BaseSchemaPlanRequest
{
    /// <summary>Gets the target store registration identity.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets an optional authenticated attestation for an externally completed data migration.</summary>
    public BaseExternalMigrationAttestation? ExternalMigrationAttestation { get; init; }
}

/// <summary>Attests that an external tool completed application-owned data conversion before baseline adoption.</summary>
public sealed record BaseExternalMigrationAttestation
{
    /// <summary>Gets or sets attestation Id.</summary>
    public required string AttestationId { get; init; }
    /// <summary>Gets or sets application Id.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets or sets store Id.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets source Checksum.</summary>
    public required string SourceChecksum { get; init; }
    /// <summary>Gets or sets target Checksum.</summary>
    public required string TargetChecksum { get; init; }
    /// <summary>Gets or sets completed At.</summary>
    public DateTimeOffset CompletedAt { get; init; }
    /// <summary>Gets or sets tool.</summary>
    public required string Tool { get; init; }
    /// <summary>Gets or sets tool Version.</summary>
    public required string ToolVersion { get; init; }
    /// <summary>Gets or sets signer Id.</summary>
    public required string SignerId { get; init; }
    /// <summary>Gets or sets authentication Tag.</summary>
    public required byte[] AuthenticationTag { get; init; }
}

/// <summary>Creates authentication tags for bounded external-migration attestations.</summary>
public static class BaseExternalMigrationAttestationAuthenticator
{
    /// <summary>Performs compute Authentication Tag.</summary>
    public static byte[] ComputeAuthenticationTag(BaseExternalMigrationAttestation attestation, ReadOnlySpan<byte> key)
    {
        ArgumentNullException.ThrowIfNull(attestation);
        if (key.Length != 32)
            throw new ArgumentException("The attestation key must contain exactly 32 bytes.", nameof(key));
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        Write(writer, attestation.AttestationId);
        Write(writer, attestation.ApplicationId);
        Write(writer, attestation.StoreId);
        Write(writer, attestation.SourceChecksum);
        Write(writer, attestation.TargetChecksum);
        writer.Write(attestation.CompletedAt.ToUnixTimeMilliseconds());
        Write(writer, attestation.Tool);
        Write(writer, attestation.ToolVersion);
        Write(writer, attestation.SignerId);
        writer.Flush();
        return System.Security.Cryptography.HMACSHA256.HashData(key, stream.ToArray());
    }

    /// <summary>Performs write.</summary>
    private static void Write(BinaryWriter writer, string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
        if (bytes.Length is 0 or > 4_096)
            throw new ArgumentException("Attestation text is invalid.");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}

/// <summary>Requests read-only verification of the installed logical application contract.</summary>
public sealed record BaseSchemaVerifyRequest
{
    /// <summary>Gets the target store registration identity.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets the visibility allowed for bounded diagnostics.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
}

/// <summary>Requests application of one authenticated schema plan.</summary>
public sealed record BaseSchemaApplyRequest
{
    /// <summary>Gets the encrypted authenticated plan artifact.</summary>
    public required byte[] ProtectedArtifact { get; init; }
    /// <summary>Gets fresh operator authorization for destructive work.</summary>
    public bool AllowDestructive { get; init; }
}

/// <summary>Contains an authenticated schema plan and its exact provider artifact.</summary>
public sealed record BaseSchemaVerifiedPlan
{
    /// <summary>Gets the authenticated public-safe plan.</summary>
    public required BaseSchemaPlan Plan { get; init; }
    /// <summary>Gets the exact provider-owned apply artifact recovered from encryption.</summary>
    public required byte[] ProviderApplyArtifact { get; init; }
}

/// <summary>Protects portable schema plans with host-owned authenticated encryption.</summary>
public interface IBaseSchemaPlanProtector
{
    /// <summary>Encrypts and authenticates the complete plan and provider artifact.</summary>
    byte[] Protect(BaseSchemaPlan plan, byte[] providerApplyArtifact);
    /// <summary>Authenticates and decrypts a complete plan artifact.</summary>
    OperationResult<BaseSchemaVerifiedPlan> Unprotect(byte[] protectedArtifact);
}

/// <summary>Plans, verifies, applies, and reads history for installed application schemas.</summary>
public interface IBaseSchemaManager
{
    /// <summary>Creates a bounded authenticated plan without mutating storage.</summary>
    ValueTask<OperationResult<BaseSchemaPlan>> PlanAsync(BaseSchemaPlanRequest request, CancellationToken cancellationToken = default);
    /// <summary>Verifies logical, baseline, and observed physical state without mutation.</summary>
    ValueTask<OperationResult<BaseSchemaObservedState>> VerifyAsync(BaseSchemaVerifyRequest request, CancellationToken cancellationToken = default);
    /// <summary>Applies an authenticated provider-prepared plan.</summary>
    ValueTask<OperationResult<BaseSchemaApplyResult>> ApplyAsync(BaseSchemaApplyRequest request, CancellationToken cancellationToken = default);
    /// <summary>Reads bounded safe schema history.</summary>
    ValueTask<OperationResult<BaseSchemaHistoryPage>> ReadHistoryAsync(string storeId, BaseSchemaHistoryRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Stable schema lifecycle error identifiers.</summary>
public static class BaseSchemaErrorCodes
{
    /// <summary>Provides invalid.</summary>
    public const string Invalid = "base.schema.invalid";
    /// <summary>Provides baseline Missing.</summary>
    public const string BaselineMissing = "base.schema.baseline.missing";
    /// <summary>Provides baseline Mismatch.</summary>
    public const string BaselineMismatch = "base.schema.baseline.mismatch";
    /// <summary>Provides drift Detected.</summary>
    public const string DriftDetected = "base.schema.driftDetected";
    /// <summary>Provides asset Not Ready.</summary>
    public const string AssetNotReady = "base.schema.asset.notReady";
    /// <summary>Provides plan Invalid.</summary>
    public const string PlanInvalid = "base.schema.plan.invalid";
    /// <summary>Provides plan Expired.</summary>
    public const string PlanExpired = "base.schema.plan.expired";
    /// <summary>Provides plan Stale.</summary>
    public const string PlanStale = "base.schema.plan.stale";
    /// <summary>Provides plan Limit Exceeded.</summary>
    public const string PlanLimitExceeded = "base.schema.plan.limitExceeded";
    /// <summary>Provides migration Required.</summary>
    public const string MigrationRequired = "base.schema.migration.required";
    /// <summary>Provides migration Unsupported.</summary>
    public const string MigrationUnsupported = "base.schema.migration.unsupported";
    /// <summary>Provides migration Busy.</summary>
    public const string MigrationBusy = "base.schema.migration.busy";
    /// <summary>Provides migration Failed.</summary>
    public const string MigrationFailed = "base.schema.migration.failed";
    /// <summary>Provides migration Rolled Back.</summary>
    public const string MigrationRolledBack = "base.schema.migration.rolledBack";
    /// <summary>Provides migration Indeterminate.</summary>
    public const string MigrationIndeterminate = "base.schema.migration.indeterminate";
    /// <summary>Provides verify Failed.</summary>
    public const string VerifyFailed = "base.schema.verify.failed";
}

/// <summary>Defines iBase Schema Store.</summary>
public interface IBaseSchemaStore
{
    /// <summary>Gets schema Execution.</summary>
    BaseSchemaExecutionCapability SchemaExecution { get; }

    /// <summary>Performs inspect Schema Async.</summary>
    ValueTask<OperationResult<BaseSchemaObservedState>> InspectSchemaAsync(BaseSchemaInspectionRequest request, CancellationToken cancellationToken = default);
    /// <summary>Performs prepare Schema Plan Async.</summary>
    ValueTask<OperationResult<BaseSchemaPreparedPlan>> PrepareSchemaPlanAsync(BaseSchemaPreparationRequest request, CancellationToken cancellationToken = default);
    /// <summary>Performs apply Schema Async.</summary>
    ValueTask<OperationResult<BaseSchemaApplyResult>> ApplySchemaAsync(BaseSchemaProviderApplyRequest request, CancellationToken cancellationToken = default);
    /// <summary>Performs read Schema History Async.</summary>
    ValueTask<OperationResult<BaseSchemaHistoryPage>> ReadSchemaHistoryAsync(BaseSchemaHistoryRequest request, CancellationToken cancellationToken = default);
}
