using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Host-only BASE provider administration.</summary>
public interface IHPDBaseAdministration
{
    /// <summary>Gets the selected provider's administration capability.</summary>
    BaseAdministrationCapability Capability { get; }

    /// <summary>Creates one consistent provider backup artifact.</summary>
    ValueTask<BaseResult<BaseBackupManifest>> CreateBackupAsync(
        Stream destination,
        BaseBackupRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Validates one complete provider backup artifact without installing it.</summary>
    ValueTask<BaseResult<BaseBackupManifest>> ValidateBackupAsync(
        Stream source,
        BaseBackupValidationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Destructively restores one validated provider backup artifact.</summary>
    ValueTask<BaseResult<BaseRestoreResult>> RestoreAsync(
        Stream source,
        BaseRestoreRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Canonically purges bounded records from a purge-enabled append-only collection.</summary>
    ValueTask<BaseResult<BasePurgeResult>> PurgeAsync(
        BasePurgeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Rebuilds and atomically publishes one vector-index generation.</summary>
    ValueTask<BaseResult<BaseVectorRebuildResult>> RebuildVectorIndexAsync(
        BaseVectorRebuildRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Rotates one exported-subject authority epoch through the selected ControlPlane store.</summary>
    ValueTask<BaseResult<BaseSubjectEpochRotationResult>> RotateSubjectEpochAsync(
        string storeId,
        PrincipalContext principal,
        BaseSubjectEpochRotationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Executes one exact identified durable subject-lifecycle maintenance operation.</summary>
    ValueTask<BaseResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteSubjectAuthorityMaintenanceAsync(
        string storeId,
        PrincipalContext principal,
        BaseSubjectAuthorityMaintenanceExecutionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one grant-authorized sanitized lifecycle authority inspection.</summary>
    ValueTask<BaseResult<BaseSubjectLifecycleInspectionResult>> InspectSubjectLifecycleAsync(
        string storeId,
        PrincipalContext principal,
        BaseSubjectLifecycleInspectionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels one exact activation generation under current ControlPlane authority.</summary>
    ValueTask<BaseResult<BaseActivationTransitionResult>> CancelActivationAsync(
        BaseActivationAdministrationCancelRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Retries one exact exhausted activation under current ControlPlane authority.</summary>
    ValueTask<BaseResult<BaseActivationTransitionResult>> RetryActivationAsync(
        BaseActivationAdministrationRetryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reconciles one ambiguous external effect under current ControlPlane authority.</summary>
    ValueTask<BaseResult<BaseActivationTransitionResult>> ReconcileActivationAsync(
        BaseActivationAdministrationReconcileRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Disposes one exact retained terminal activation under current ControlPlane authority.</summary>
    ValueTask<BaseResult<BaseActivationTransitionResult>> DisposeActivationAsync(
        BaseActivationAdministrationDisposeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one exact-scope bounded activation administration page.</summary>
    ValueTask<BaseResult<BaseActivationAdministrationPage>> ReadActivationsAsync(
        BaseActivationAdministrationReadRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Advances one crash-recovery activation-maintenance page.</summary>
    ValueTask<BaseResult<BaseActivationMaintenancePage>> AdvanceActivationMaintenanceAsync(
        BaseActivationAdministrationMaintenanceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Prunes one dependency-free disposed-activation page.</summary>
    ValueTask<BaseResult<BaseActivationPrunePage>> PruneActivationsAsync(
        BaseActivationAdministrationPruneRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Compacts one authorized page of expired activation-instance receipts.</summary>
    ValueTask<BaseResult<BaseActivationReceiptCompactionResult>> CompactActivationReceiptsAsync(
        BaseActivationAdministrationReceiptCompactionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically migrates one activation through an installed closed projection.</summary>
    ValueTask<BaseResult<BaseActivationMigrationResult>> MigrateActivationAsync(
        BaseActivationAdministrationMigrationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Executes one closed, independently authorized activation repair plan.</summary>
    ValueTask<BaseResult<BaseActivationQuarantinePage>> ExecuteActivationRepairAsync(
        BaseActivationAdministrationRepairRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Explicitly releases completed external semantic-recovery quarantine under ControlPlane authority.</summary>
    ValueTask<BaseResult<BaseSemanticRecoveryQuarantineRecoveryResult>> RecoverSemanticRecoveryQuarantineAsync(
        BaseSemanticRecoveryQuarantineRecoveryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one exact-definition sanitized semantic activation inspection page.</summary>
    ValueTask<BaseResult<BaseSemanticActivationInspectionPage>> InspectSemanticActivationsAsync(
        string storeId,
        PrincipalContext principal,
        BaseSemanticActivationInspectionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads sanitized semantic definition health and Runtime-minted command authority.</summary>
    ValueTask<BaseResult<BaseSemanticActivationControlDescriptor>> ReadSemanticActivationControlAsync(
        string storeId, PrincipalContext principal, BaseSemanticActivationDefinitionKey definition,
        CancellationToken cancellationToken = default);

    /// <summary>Executes one Runtime-minted semantic maintenance command.</summary>
    ValueTask<BaseResult<BaseSemanticActivationControlResult>> ExecuteSemanticActivationControlAsync(
        string storeId, PrincipalContext principal, BaseSemanticActivationControlCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves one Runtime-minted indeterminate semantic maintenance command.</summary>
    ValueTask<BaseResult<BaseSemanticActivationControlResult>> ResolveSemanticActivationControlAsync(
        string storeId, PrincipalContext principal, BaseSemanticActivationControlResolution resolution,
        CancellationToken cancellationToken = default);
}

/// <summary>Opaque Runtime-minted semantic maintenance authority.</summary>
public sealed class BaseSemanticActivationControlToken
{
    internal BaseSemanticActivationControlToken(string value) => Value = value;
    internal static BaseSemanticActivationControlToken FromWire(string value)
    {
        if (value.Length is < 1 or > 4096 || value.Any(static character =>
                character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z')
                    and not (>= '0' and <= '9') and not '-' and not '_'))
            throw new FormatException(BaseSemanticActivationErrorCodes.Invalid);
        return new(new string(value.AsSpan()));
    }
    /// <summary>Gets canonical unpadded base64url token text.</summary>
    public string Value { get; }
}

/// <summary>Contains sanitized health and available exact maintenance commands.</summary>
public sealed record BaseSemanticActivationControlDescriptor
{
    /// <summary>Gets the stable definition identifier.</summary>
    public required string DefinitionId { get; init; }
    /// <summary>Gets its installed positive version.</summary>
    public required int DefinitionVersion { get; init; }
    /// <summary>Gets the current semantic authority generation, or null while authority is unavailable.</summary>
    public required long? AuthorityGeneration { get; init; }
    /// <summary>Gets the current live count, or null while authority is unavailable.</summary>
    public required long? LiveCount { get; init; }
    /// <summary>Gets the current retired count, or null while authority is unavailable.</summary>
    public required long? RetiredCount { get; init; }
    /// <summary>Gets the current compacted-absence count, or null while authority is unavailable.</summary>
    public required long? AbsenceCount { get; init; }
    /// <summary>Gets current provider readiness.</summary>
    public required bool Ready { get; init; }
    /// <summary>Gets current provider quarantine state.</summary>
    public required bool Quarantined { get; init; }
    /// <summary>Gets compact command authority when currently eligible.</summary>
    public BaseSemanticActivationControlToken? Compact { get; init; }
    /// <summary>Gets removal command authority when currently eligible.</summary>
    public BaseSemanticActivationControlToken? Remove { get; init; }
}

/// <summary>Executes or resumes one opaque semantic maintenance command.</summary>
public sealed record BaseSemanticActivationControlCommand
{
    /// <summary>Gets opaque Runtime-minted authority.</summary>
    public required BaseSemanticActivationControlToken Token { get; init; }
    /// <summary>Gets the normalized caller idempotency key.</summary>
    public required string IdempotencyKey { get; init; }
    /// <summary>Gets the exact destructive confirmation literal.</summary>
    public required string Confirmation { get; init; }
}

/// <summary>Resolves one opaque indeterminate semantic maintenance command.</summary>
public sealed record BaseSemanticActivationControlResolution
{
    /// <summary>Gets opaque Runtime-minted resolution authority.</summary>
    public required BaseSemanticActivationControlToken Token { get; init; }
}

/// <summary>Contains only sanitized semantic maintenance outcome authority.</summary>
public sealed record BaseSemanticActivationControlResult
{
    /// <summary>Gets the closed maintenance disposition.</summary>
    public required BaseSemanticActivationMaintenanceDisposition Disposition { get; init; }
    /// <summary>Gets the published semantic authority generation.</summary>
    public required long AuthorityGeneration { get; init; }
    /// <summary>Gets examined rows.</summary>
    public required long ExaminedRows { get; init; }
    /// <summary>Gets changed rows.</summary>
    public required long ChangedRows { get; init; }
    /// <summary>Gets examined canonical bytes.</summary>
    public required long CanonicalBytes { get; init; }
    /// <summary>Gets receipt disposition when an outcome was confirmed.</summary>
    public required BaseMutationRequestDisposition? ReceiptDisposition { get; init; }
    /// <summary>Gets an opaque resume command when bounded work remains.</summary>
    public BaseSemanticActivationControlToken? Resume { get; init; }
    /// <summary>Gets an opaque resolution command when commit observation is indeterminate.</summary>
    public BaseSemanticActivationControlToken? Resolution { get; init; }
    /// <summary>Gets a sanitized Runtime-authored outcome checksum.</summary>
    public required ImmutableArray<byte> SanitizedChecksum { get; init; }
}

/// <summary>Requests sanitized lifecycle authority inspection after exact ControlPlane authorization.</summary>
public sealed record BaseSubjectLifecycleInspectionRequest
{
    /// <summary>Gets the exported contract identity.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the exported contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the optional exact consumer identity.</summary>
    public string? ConsumerId { get; init; }
    /// <summary>Gets the requested scope-query mode.</summary>
    public required BaseSubjectScopeQueryMode ScopeMode { get; init; }
    /// <summary>Gets exact scope authority when exact-scope inspection is requested.</summary>
    public BaseOwnedSubjectScopeEvidence? ExactScope { get; init; }
    /// <summary>Gets the optional subject identity for exact-scope terminal inspection.</summary>
    public BaseSubjectId? SubjectId { get; init; }
    /// <summary>Gets whether exact-scope terminal evidence is requested.</summary>
    public required bool IncludeTerminalReceipt { get; init; }
    /// <summary>Gets the maximum canonical result bytes.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets the bounded inspection timeout.</summary>
    public required TimeSpan Timeout { get; init; }
}

/// <summary>Contains sanitized terminal lifetime evidence without protected provider scope material.</summary>
public sealed record BaseSubjectTerminalLifetimeInspection
{
    /// <summary>Gets the contract identity.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the retired subject identity.</summary>
    public required BaseSubjectId SubjectId { get; init; }
    /// <summary>Gets the retired authority epoch.</summary>
    public required BaseSubjectAuthorityEpoch RetiredAuthorityEpoch { get; init; }
    /// <summary>Gets the retired incarnation.</summary>
    public required BaseSubjectIncarnation RetiredIncarnation { get; init; }
    /// <summary>Gets the retired lifetime generation.</summary>
    public required long RetiredLifetimeGeneration { get; init; }
    /// <summary>Gets the retired subject sequence.</summary>
    public required long RetiredSubjectSequence { get; init; }
    /// <summary>Gets the retirement journal position.</summary>
    public required BaseMutationJournalPosition RetiredPosition { get; init; }
    /// <summary>Gets the contract state generation.</summary>
    public required long ContractStateGeneration { get; init; }
    /// <summary>Gets the restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the corruption checksum.</summary>
    public required string ReceiptChecksum { get; init; }
}

/// <summary>Contains one sanitized ControlPlane lifecycle inspection.</summary>
public sealed record BaseSubjectLifecycleInspectionResult
{
    private BaseSubjectLifecycleConsumerInspection[] _consumers = [];
    /// <summary>Gets the current delivery epoch.</summary>
    public required long DeliveryEpoch { get; init; }
    /// <summary>Gets the earliest retained lifecycle boundary.</summary>
    public BaseSubjectLifecycleOrderingBoundary? EarliestRetained { get; init; }
    /// <summary>Gets the current lifecycle high-water boundary.</summary>
    public BaseSubjectLifecycleOrderingBoundary? HighWater { get; init; }
    /// <summary>Gets sanitized installed-consumer authority.</summary>
    public required IReadOnlyList<BaseSubjectLifecycleConsumerInspection> Consumers
    {
        get => Array.AsReadOnly(_consumers.ToArray());
        init => _consumers = value?.Select(static item => item with { }).ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
    /// <summary>Gets sanitized terminal evidence when exact-scope inspection requested it.</summary>
    public BaseSubjectTerminalLifetimeInspection? TerminalReceipt { get; init; }
}

/// <summary>Describes provider administration guarantees.</summary>
public sealed record BaseAdministrationCapability
{
    /// <summary>Gets whether backup creation is supported.</summary>
    public required bool Backup { get; init; }
    /// <summary>Gets whether backup validation is supported.</summary>
    public required bool Validate { get; init; }
    /// <summary>Gets whether restore is supported.</summary>
    public required bool Restore { get; init; }
    /// <summary>Gets whether administrative purge is supported.</summary>
    public required bool AdministrativePurge { get; init; }
    /// <summary>Gets whether an installed vector provider supports generation-safe rebuild.</summary>
    public required bool VectorRebuild { get; init; }
    /// <summary>Gets whether backup can run while the store is online.</summary>
    public required bool OnlineBackup { get; init; }
    /// <summary>Gets whether backup blocks writers.</summary>
    public required bool WritersBlockedDuringBackup { get; init; }
    /// <summary>Gets whether backup blocks readers.</summary>
    public required bool ReadersBlockedDuringBackup { get; init; }
    /// <summary>Gets whether restore requires exclusive maintenance.</summary>
    public required bool RestoreRequiresExclusiveMaintenance { get; init; }
    /// <summary>Gets whether administration operates on durable storage.</summary>
    public required bool Durable { get; init; }
    /// <summary>Gets the maximum accepted artifact size.</summary>
    public required long MaxArtifactBytes { get; init; }
}

/// <summary>Requests a backup for one configured store.</summary>
public sealed record BaseBackupRequest
{
    /// <summary>Gets the configured store identifier.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets the principal requesting the operation.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets the optional expected store-identity digest.</summary>
    public string? ExpectedStoreIdentityDigest { get; init; }
}

/// <summary>Requests validation of one artifact for a configured store.</summary>
public sealed record BaseBackupValidationRequest
{
    /// <summary>Gets the configured store identifier.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets the principal requesting the operation.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets the optional expected artifact store-identity digest.</summary>
    public string? ExpectedArtifactStoreIdentityDigest { get; init; }
}

/// <summary>Controls logical store identity during restore.</summary>
public enum BaseRestoreIdentityMode
{
    /// <summary>Requires the artifact to match the current store identity.</summary>
    RequireCurrentStoreIdentity,
    /// <summary>Adopts the artifact's logical store identity.</summary>
    AdoptArtifactStoreIdentity
}

/// <summary>Controls provider recovery-image retention after successful restore.</summary>
public enum BaseRecoveryImageRetention
{
    /// <summary>Deletes the recovery image after confirmed restoration.</summary>
    DeleteAfterSuccessfulRestore,
    /// <summary>Retains the recovery image until the host removes it.</summary>
    RetainUntilHostRemoves
}

/// <summary>Controls durable schedule-occurrence authority across restoration.</summary>
public enum BaseScheduleRestoreDomain
{
    /// <summary>Preserves the pre-restore non-prunable occurrence floor in the same disaster domain.</summary>
    InPlaceRecovery,
    /// <summary>Begins a new disaster domain from one authenticated external occurrence floor.</summary>
    NewDisasterDomain
}

/// <summary>Requests destructive restoration of one configured store.</summary>
public sealed record BaseRestoreRequest
{
    /// <summary>Gets the configured store identifier.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets the principal requesting the operation.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets the expected current store-identity digest.</summary>
    public required string ExpectedCurrentStoreIdentityDigest { get; init; }
    /// <summary>Gets the expected artifact store-identity digest.</summary>
    public required string ExpectedArtifactStoreIdentityDigest { get; init; }
    /// <summary>Gets the requested identity treatment.</summary>
    public required BaseRestoreIdentityMode IdentityMode { get; init; }
    /// <summary>Gets the recovery-image retention treatment.</summary>
    public required BaseRecoveryImageRetention RecoveryImageRetention { get; init; }
    /// <summary>Gets explicit confirmation of destructive replacement.</summary>
    public required bool ConfirmDestructiveReplacement { get; init; }
    /// <summary>Gets the schedule-occurrence recovery domain.</summary>
    public required BaseScheduleRestoreDomain ScheduleRestoreDomain { get; init; }
    /// <summary>Gets the authenticated external floor required for a new disaster domain.</summary>
    public BaseScheduleRecoveryManifest? ScheduleRecoveryManifest { get; init; }
    internal string? RecoveryApplicationId { get; init; }
    internal ImmutableArray<BaseScheduleRecoveryVerificationKey> RecoveryVerificationKeys { get; init; }
    internal long RecoveryAcceptedNow { get; init; }
    /// <summary>Gets Runtime-validated external semantic recovery authority for a selected new disaster domain.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public BaseSemanticRecoveryRestoreAuthority? SemanticRecoveryAuthority { get; init; }
}

/// <summary>Binds one authenticated external semantic recovery suffix to its backup artifact prefix.</summary>
public sealed record BaseSemanticRecoveryRestoreAuthority
{
    /// <summary>Gets the exact graph-owned external authority used to authenticate this suffix.</summary>
    public required BaseSemanticRecoveryAuthorityDefinition Definition { get; init; }
    /// <summary>Gets the Runtime-issued accepted restore instant in Unix milliseconds.</summary>
    public required long AcceptedNow { get; init; }
    /// <summary>Gets the exact number of external pages read.</summary>
    public required int PageCount { get; init; }
    /// <summary>Gets the checked canonical publication bytes retained for restore.</summary>
    public required long CanonicalBytes { get; init; }
    /// <summary>Gets the checked transient bytes retained for restore.</summary>
    public required long TransientBytes { get; init; }
    /// <summary>Gets the exact effective external operation limits.</summary>
    public required BaseSemanticRecoveryOperationLimits Limits { get; init; }
    /// <summary>Gets the artifact-time contiguous terminal publication sequence.</summary>
    public required long ArtifactSequence { get; init; }
    /// <summary>Gets the artifact-time ordered publication checksum.</summary>
    public required ImmutableArray<byte> ArtifactOrderedChecksum { get; init; }
    /// <summary>Gets the exact artifact-bound request authenticated by the returned head.</summary>
    public required BaseSemanticRecoveryHeadRequest HeadRequest { get; init; }
    /// <summary>Gets the authenticated current external head.</summary>
    public required BaseSemanticRecoveryPublishedHead Head { get; init; }
    /// <summary>Gets every contiguous publication after the artifact sequence through the head.</summary>
    public required ImmutableArray<BaseSemanticRecoveryPublicationEntry> Publications { get; init; }
    /// <summary>Gets the canonical complete authority checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Describes a successful restore result.</summary>
public enum BaseRestoreStatus
{
    /// <summary>The artifact was installed and validated.</summary>
    Restored
}

/// <summary>Classifies the state left by a failed restore attempt.</summary>
public enum BaseRestoreFailureDisposition
{
    /// <summary>The request was rejected before store state changed.</summary>
    RejectedBeforeChange,
    /// <summary>The original store image was preserved.</summary>
    OriginalPreserved,
    /// <summary>The recovery image restored the original store.</summary>
    RecoveryRestoredOriginal,
    /// <summary>The resulting store state cannot be confirmed.</summary>
    IndeterminateUnavailable
}

/// <summary>Returns a confirmed successful restore.</summary>
public sealed record BaseRestoreResult
{
    /// <summary>Gets the configured store identifier.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets the confirmed restore status.</summary>
    public required BaseRestoreStatus Status { get; init; }
    /// <summary>Gets the installed store-identity digest.</summary>
    public required string InstalledStoreIdentityDigest { get; init; }
    /// <summary>Gets the installed restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets whether a recovery image remains retained.</summary>
    public required bool RecoveryImageRetained { get; init; }
}

/// <summary>Safe authenticated metadata for one complete backup artifact.</summary>
public sealed record BaseBackupManifest
{
    private string[] _logicalPartitions = [];
    /// <summary>Gets the gap-free activation-instance receipt sequence captured by this artifact.</summary>
    public required long ActivationInstanceReceiptSequence { get; init; }
    /// <summary>Gets the ordered activation-instance receipt checksum through that sequence.</summary>
    public required ImmutableArray<byte> ActivationInstanceReceiptOrderedChecksum { get; init; }
    /// <summary>Gets the contiguous external semantic-terminal publication sequence captured by this artifact.</summary>
    public required long SemanticTerminalPublicationSequence { get; init; }
    /// <summary>Gets the canonical ordered semantic-terminal publication checksum through that sequence.</summary>
    public required ImmutableArray<byte> SemanticTerminalPublicationChecksum { get; init; }
    /// <summary>Gets the authenticated artifact-envelope version.</summary>
    public required ushort EnvelopeVersion { get; init; }
    /// <summary>Gets the provider kind.</summary>
    public required string ProviderKind { get; init; }
    /// <summary>Gets the provider version.</summary>
    public required string ProviderVersion { get; init; }
    /// <summary>Gets the native SQLite version, when applicable.</summary>
    public required string NativeSqliteVersion { get; init; }
    /// <summary>Gets the BASE contract version.</summary>
    public required string BaseContractVersion { get; init; }
    /// <summary>Gets the logical store-identity digest.</summary>
    public required string StoreIdentityDigest { get; init; }
    /// <summary>Gets the captured schema generation.</summary>
    public required long SchemaGeneration { get; init; }
    /// <summary>Gets the captured schema baseline identifier.</summary>
    public required string SchemaBaselineId { get; init; }
    /// <summary>Gets the captured schema checksum.</summary>
    public required string SchemaChecksum { get; init; }
    /// <summary>Gets the captured restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the artifact creation time.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
    /// <summary>Gets the provider payload length.</summary>
    public required long ProviderPayloadLength { get; init; }
    /// <summary>Gets the provider payload SHA-256 digest.</summary>
    public required string ProviderPayloadSha256 { get; init; }
    /// <summary>Gets the included logical partitions.</summary>
    public required IReadOnlyList<string> LogicalPartitions
    {
        get => Array.AsReadOnly(_logicalPartitions);
        init => _logicalPartitions = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
    /// <summary>Gets the receipt representation version.</summary>
    public required int ReceiptFormatVersion { get; init; }
    /// <summary>Gets the journal representation version.</summary>
    public required int JournalFormatVersion { get; init; }
    /// <summary>Gets the collection-history representation version.</summary>
    public required int CollectionHistoryFormatVersion { get; init; }
    /// <summary>Gets whether the provider payload is encrypted at rest.</summary>
    public required bool PayloadEncryptedAtRest { get; init; }
    /// <summary>Gets the external key-reference kind, when one is required.</summary>
    public required string? ExternalKeyReferenceKind { get; init; }
}

/// <summary>Requests one bounded canonical administrative purge.</summary>
public sealed record BasePurgeRequest
{
    private RecordId[] _recordIds = [];
    /// <summary>Gets the purge-enabled collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the bounded record identifiers to purge.</summary>
    public required RecordId[] RecordIds
    {
        get => [.. _recordIds];
        init => _recordIds = value is null ? throw new ArgumentNullException(nameof(value)) : [.. value];
    }
    /// <summary>Gets the principal requesting the purge.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets the bounded audit reason code.</summary>
    public required string ReasonCode { get; init; }
    /// <summary>Gets the host audit reference.</summary>
    public required string AuditReference { get; init; }
    /// <summary>Gets the authorization evaluation time.</summary>
    public required DateTimeOffset EvaluatedAt { get; init; }
    /// <summary>Gets the optional compare-and-swap purge generation.</summary>
    public long? ExpectedPurgeGeneration { get; init; }
}

/// <summary>Returns bounded facts for a committed administrative purge.</summary>
public sealed record BasePurgeResult
{
    /// <summary>Gets the purged collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the number of requested record identifiers.</summary>
    public required int RequestedCount { get; init; }
    /// <summary>Gets the number of records physically purged.</summary>
    public required int PurgedCount { get; init; }
    /// <summary>Gets the collection purge generation after commit.</summary>
    public required long PurgeGeneration { get; init; }
    /// <summary>Gets the confirmed commit time.</summary>
    public required DateTimeOffset CommittedAt { get; init; }
}

/// <summary>Stable L37 administration error codes.</summary>
public static class BaseAdministrationErrorCodes
{
    /// <summary>Administration is disabled.</summary>
    public const string Disabled = "base.admin.disabled";
    /// <summary>The administration request is invalid.</summary>
    public const string Invalid = "base.admin.invalid";
    /// <summary>The administration request is unauthorized.</summary>
    public const string Unauthorized = "base.admin.unauthorized";
    /// <summary>The selected store is unavailable.</summary>
    public const string StoreUnavailable = "base.admin.storeUnavailable";
    /// <summary>The selected provider lacks the required capability.</summary>
    public const string CapabilityUnavailable = "base.admin.capabilityUnavailable";
    /// <summary>The artifact is invalid.</summary>
    public const string ArtifactInvalid = "base.admin.artifact.invalid";
    /// <summary>The artifact exceeds its configured size bound.</summary>
    public const string ArtifactTooLarge = "base.admin.artifact.tooLarge";
    /// <summary>The artifact key is unavailable.</summary>
    public const string ArtifactKeyUnavailable = "base.admin.artifact.keyUnavailable";
    /// <summary>The artifact store identity does not match.</summary>
    public const string ArtifactIdentityMismatch = "base.admin.artifact.identityMismatch";
    /// <summary>Backup could not acquire required capacity.</summary>
    public const string BackupBusy = "base.admin.backup.busy";
    /// <summary>Backup exceeded its deadline.</summary>
    public const string BackupTimeout = "base.admin.backup.timeout";
    /// <summary>Backup failed before producing a confirmed artifact.</summary>
    public const string BackupFailed = "base.admin.backup.failed";
    /// <summary>Backup completion is indeterminate.</summary>
    public const string BackupIndeterminate = "base.admin.backup.indeterminate";
    /// <summary>Artifact validation exceeded its deadline.</summary>
    public const string ValidationTimeout = "base.admin.validation.timeout";
    /// <summary>Artifact validation failed.</summary>
    public const string ValidationFailed = "base.admin.validation.failed";
    /// <summary>Artifact validation completion is indeterminate.</summary>
    public const string ValidationIndeterminate = "base.admin.validation.indeterminate";
    /// <summary>Restore lacks explicit destructive confirmation.</summary>
    public const string RestoreConfirmationRequired = "base.admin.restore.confirmationRequired";
    /// <summary>Restore identity requirements were not met.</summary>
    public const string RestoreIdentityMismatch = "base.admin.restore.identityMismatch";
    /// <summary>Restore could not acquire exclusive maintenance.</summary>
    public const string RestoreBusy = "base.admin.restore.busy";
    /// <summary>Restore exceeded its deadline.</summary>
    public const string RestoreTimeout = "base.admin.restore.timeout";
    /// <summary>Restore failed with a classified recoverable disposition.</summary>
    public const string RestoreFailed = "base.admin.restore.failed";
    /// <summary>The restored store state is indeterminate and unavailable.</summary>
    public const string RestoreIndeterminate = "base.admin.restore.indeterminate";
}
