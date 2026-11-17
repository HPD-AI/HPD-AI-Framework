using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Identifies one BASE-owned durable evidence family.</summary>
public enum StudioEvidenceKind : byte
{
    /// <summary>Identified operation receipts.</summary>
    Receipt = 1,
    /// <summary>Committed record mutations.</summary>
    RecordMutation = 2,
    /// <summary>Durable activation occurrences.</summary>
    ActivationOccurrence = 3,
    /// <summary>Durable activation attempts.</summary>
    ActivationAttempt = 4,
    /// <summary>Durable at-most-once effect facts.</summary>
    ActivationEffect = 5,
    /// <summary>Search rebuild and publication facts.</summary>
    SearchRebuild = 6,
    /// <summary>Exported-subject lifecycle facts.</summary>
    Lifecycle = 7,
    /// <summary>Coordinated retirement facts.</summary>
    Retirement = 8,
    /// <summary>Schema history.</summary>
    Schema = 9,
    /// <summary>Backup and restore history.</summary>
    BackupRestore = 10,
    /// <summary>Maintenance history.</summary>
    Maintenance = 11,
    /// <summary>Quarantine history.</summary>
    Quarantine = 12,
    /// <summary>Health transitions.</summary>
    HealthTransition = 13,
}

/// <summary>Closed value-free semantic classification for retained evidence.</summary>
public enum StudioEvidenceSemanticKind : byte
{
    /// <summary>A create mutation.</summary>
    Created = 1,
    /// <summary>A patch mutation.</summary>
    Patched = 2,
    /// <summary>A replace mutation.</summary>
    Replaced = 3,
    /// <summary>A delete mutation.</summary>
    Deleted = 4,
    /// <summary>A durable state transition owned by the item variant.</summary>
    Transition = 5,
}

/// <summary>Closed safe state, phase, outcome, or disposition carried by evidence variants.</summary>
public enum StudioEvidenceState : byte
{
    /// <summary>The operation is pending.</summary>
    Pending = 1,
    /// <summary>The operation is active.</summary>
    Active = 2,
    /// <summary>The operation completed.</summary>
    Completed = 3,
    /// <summary>The operation failed.</summary>
    Failed = 4,
    /// <summary>The operation was cancelled.</summary>
    Cancelled = 5,
    /// <summary>The authority is ready or healthy.</summary>
    Ready = 6,
    /// <summary>The authority is degraded.</summary>
    Degraded = 7,
    /// <summary>The authority is unavailable.</summary>
    Unavailable = 8,
    /// <summary>The operation or artifact is quarantined.</summary>
    Quarantined = 9,
    /// <summary>The operation or artifact was released.</summary>
    Released = 10,
}

/// <summary>Identifies the BASE authority whose durable evidence is requested.</summary>
public abstract record StudioEvidenceSubject
{
    private protected StudioEvidenceSubject() { }
}

/// <summary>Identifies an installed collection without depending on Studio transport types.</summary>
public sealed record StudioCollectionEvidenceSubject : StudioEvidenceSubject
{
    /// <summary>Gets the installed collection ID.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the installed collection checksum.</summary>
    public required ImmutableArray<byte> InstalledCollectionChecksum { get; init; }
}

/// <summary>Identifies one record within an installed collection.</summary>
public sealed record StudioRecordEvidenceSubject : StudioEvidenceSubject
{
    /// <summary>Gets the installed collection ID.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the installed collection checksum.</summary>
    public required ImmutableArray<byte> InstalledCollectionChecksum { get; init; }
    /// <summary>Gets the canonical record ID.</summary>
    public required RecordId RecordId { get; init; }
}

/// <summary>Contains the effective independent limits for one evidence operation.</summary>
public sealed record StudioEvidenceLimits
{
    /// <summary>Gets the maximum returned items.</summary>
    public required int MaximumItems { get; init; }
    /// <summary>Gets the maximum provider rows examined.</summary>
    public required long MaximumRowsRead { get; init; }
    /// <summary>Gets the maximum normalized read intervals.</summary>
    public required int MaximumIntervals { get; init; }
    /// <summary>Gets the maximum canonical evidence bytes.</summary>
    public required long MaximumEvidenceBytes { get; init; }
    /// <summary>Gets the maximum provider transient bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets the authority-acquisition deadline.</summary>
    public required TimeSpan AcquisitionDeadline { get; init; }
    /// <summary>Gets the maximum session lifetime.</summary>
    public required TimeSpan SessionDeadline { get; init; }
    /// <summary>Gets the page-operation deadline.</summary>
    public required TimeSpan PageDeadline { get; init; }
}

/// <summary>Advertises the exact evidence kinds and independent provider ceilings.</summary>
public sealed record StudioEvidenceCapability
{
    /// <summary>Gets the supported closed evidence kinds.</summary>
    public required ImmutableArray<StudioEvidenceKind> SupportedKinds { get; init; }
    /// <summary>Gets the provider's independent maximum items per page.</summary>
    public required int MaximumItems { get; init; }
    /// <summary>Gets the provider's independent maximum rows examined.</summary>
    public required long MaximumRowsRead { get; init; }
    /// <summary>Gets the provider's independent maximum normalized intervals.</summary>
    public required int MaximumIntervals { get; init; }
    /// <summary>Gets the provider's independent maximum evidence bytes.</summary>
    public required long MaximumEvidenceBytes { get; init; }
    /// <summary>Gets the provider's independent maximum transient bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets the maximum authority-acquisition deadline.</summary>
    public required TimeSpan AcquisitionDeadline { get; init; }
    /// <summary>Gets the maximum finite session lifetime.</summary>
    public required TimeSpan SessionDeadline { get; init; }
    /// <summary>Gets the maximum page deadline.</summary>
    public required TimeSpan PageDeadline { get; init; }
    /// <summary>Gets kinds included by ordinary whole-store backup.</summary>
    public required ImmutableArray<StudioEvidenceKind> BackupIncludedKinds { get; init; }
    /// <summary>Gets kinds whose authority is invalidated and validated across restore.</summary>
    public required ImmutableArray<StudioEvidenceKind> RestoreValidatedKinds { get; init; }
    /// <summary>Gets the provider certification checksum.</summary>
    public required ImmutableArray<byte> CertificationChecksum { get; init; }
}

/// <summary>Describes the Runtime-owned durable evidence authority requirement.</summary>
public sealed record StudioEvidenceRequirement
{
    /// <summary>Gets the installed application ID.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the requested closed evidence kind.</summary>
    public required StudioEvidenceKind Kind { get; init; }
    /// <summary>Gets the BASE-owned parent authority.</summary>
    public required StudioEvidenceSubject Parent { get; init; }
    /// <summary>Gets the exact currently authorized tenant, project, or global scope.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets the purpose-protected scope/seek checksum.</summary>
    public required ImmutableArray<byte> ProtectedScopeSeekChecksum { get; init; }
    /// <summary>Gets the exact effective limits.</summary>
    public required StudioEvidenceLimits Limits { get; init; }
}

/// <summary>Contains the immutable provider capture receipt.</summary>
public sealed record StudioEvidenceCaptureReceipt
{
    /// <summary>Gets the application ID.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the evidence kind.</summary>
    public required StudioEvidenceKind Kind { get; init; }
    /// <summary>Gets the provider store identity.</summary>
    public required string StoreIdentity { get; init; }
    /// <summary>Gets the restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the captured evidence index generation.</summary>
    public required long IndexGeneration { get; init; }
    /// <summary>Gets the fixed logical access-path ID.</summary>
    public required string LogicalAccessPathId { get; init; }
    /// <summary>Gets the protected scope/seek checksum.</summary>
    public required ImmutableArray<byte> ProtectedScopeSeekChecksum { get; init; }
    /// <summary>Gets the purpose-bound authority checksum.</summary>
    public required ImmutableArray<byte> AuthorityChecksum { get; init; }
}

/// <summary>Provider-instance-bound, nonserializable evidence authority.</summary>
public abstract class BaseCapturedStudioEvidenceAuthority
{
    /// <summary>Initializes a provider-owned captured authority.</summary>
    protected BaseCapturedStudioEvidenceAuthority(StudioEvidenceCaptureReceipt receipt) => Receipt = receipt with
    { ApplicationId = new string(receipt.ApplicationId.AsSpan()), StoreIdentity = new string(receipt.StoreIdentity.AsSpan()),
      LogicalAccessPathId = new string(receipt.LogicalAccessPathId.AsSpan()), ProtectedScopeSeekChecksum = [.. receipt.ProtectedScopeSeekChecksum],
      AuthorityChecksum = [.. receipt.AuthorityChecksum] };
    /// <summary>Gets its immutable Runtime-verifiable capture receipt.</summary>
    public StudioEvidenceCaptureReceipt Receipt { get; }
}

/// <summary>Contains an exclusive canonical ordering boundary.</summary>
public sealed record StudioEvidenceBoundary
{
    /// <summary>Gets the evidence kind whose tuple is represented.</summary>
    public required StudioEvidenceKind Kind { get; init; }
    /// <summary>Gets the exact canonical tuple bytes.</summary>
    public required ImmutableArray<byte> CanonicalTuple { get; init; }
    /// <summary>Gets the checksum over the kind and tuple.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains one normalized provider read interval.</summary>
public sealed record StudioEvidenceReadInterval
{
    /// <summary>Gets the fixed logical provider access path.</summary>
    public required string LogicalAccessPathId { get; init; }
    /// <summary>Gets the purpose-protected scope seek represented by this interval.</summary>
    public required ImmutableArray<byte> ProtectedScopeSeekChecksum { get; init; }
    /// <summary>Gets the inclusive lower tuple bytes.</summary>
    public required ImmutableArray<byte> LowerInclusive { get; init; }
    /// <summary>Gets the exclusive upper tuple bytes.</summary>
    public required ImmutableArray<byte> UpperExclusive { get; init; }
    /// <summary>Gets the checksum over the complete interval.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains provider-owned evidence accounting.</summary>
public sealed record StudioEvidenceProviderAccounting
{
    /// <summary>Gets rows examined.</summary>
    public required long RowsRead { get; init; }
    /// <summary>Gets intervals returned.</summary>
    public required int Intervals { get; init; }
    /// <summary>Gets canonical evidence bytes returned.</summary>
    public required long EvidenceBytes { get; init; }
    /// <summary>Gets provider transient bytes retained while executing.</summary>
    public required long TransientBytes { get; init; }
}

/// <summary>Contains one committed, value-free durable evidence item.</summary>
public abstract record StudioEvidenceItem
{
    private protected StudioEvidenceItem() { }
    /// <summary>Gets its closed evidence kind.</summary>
    public required StudioEvidenceKind Kind { get; init; }
    /// <summary>Gets its canonical strictly ordered tuple.</summary>
    public required ImmutableArray<byte> OrderingTuple { get; init; }
    /// <summary>Gets its committed or observed UTC time.</summary>
    public required DateTimeOffset ObservedAtUtc { get; init; }
    /// <summary>Gets a stable safe semantic kind.</summary>
    public required StudioEvidenceSemanticKind SemanticKind { get; init; }
    /// <summary>Gets the checksum over the complete canonical item.</summary>
    public required ImmutableArray<byte> EvidenceChecksum { get; init; }
}

/// <summary>Contains one committed record-mutation evidence item.</summary>
public sealed record StudioRecordMutationEvidenceItem : StudioEvidenceItem
{
    /// <summary>Gets the affected collection ID.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the affected record ID.</summary>
    public required RecordId RecordId { get; init; }
    /// <summary>Gets the committed revision when retained.</summary>
    public RevisionToken? Revision { get; init; }
    /// <summary>Gets the stable event identity.</summary>
    public required string EvidenceId { get; init; }
    /// <summary>Gets the identified receipt when the mutation retained one.</summary>
    public string? ReceiptIdentity { get; init; }
}

/// <summary>Contains one durable operation-receipt evidence item.</summary>
public sealed record StudioReceiptEvidenceItem : StudioEvidenceItem
{ /// <summary>Gets the receipt identity.</summary>
  public required string ReceiptIdentity { get; init; }
  /// <summary>Gets the receipt kind.</summary>
  public required string ReceiptKind { get; init; }
  /// <summary>Gets the disposition.</summary>
  public required StudioEvidenceState Disposition { get; init; }
  /// <summary>Gets affected resource identities.</summary>
  public required ImmutableArray<string> AffectedResourceIdentities { get; init; } }
/// <summary>Contains one durable activation-occurrence evidence item.</summary>
public sealed record StudioActivationOccurrenceEvidenceItem : StudioEvidenceItem
{ /// <summary>Gets the schedule ID.</summary>
  public required string ScheduleId { get; init; }
  /// <summary>Gets the occurrence ID.</summary>
  public required string OccurrenceId { get; init; }
  /// <summary>Gets the activation ID.</summary>
  public required string ActivationId { get; init; }
  /// <summary>Gets the disposition.</summary>
  public required StudioEvidenceState Disposition { get; init; } }
/// <summary>Contains one durable activation-attempt evidence item.</summary>
public sealed record StudioActivationAttemptEvidenceItem : StudioEvidenceItem
{ /// <summary>Gets the activation ID.</summary>
  public required string ActivationId { get; init; }
  /// <summary>Gets the attempt number.</summary>
  public required long AttemptNumber { get; init; }
  /// <summary>Gets the event sequence.</summary>
  public required long EventSequence { get; init; }
  /// <summary>Gets the state.</summary>
  public required StudioEvidenceState State { get; init; } }
/// <summary>Contains one durable activation-effect evidence item.</summary>
public sealed record StudioActivationEffectEvidenceItem : StudioEvidenceItem
{ /// <summary>Gets the activation ID.</summary>
  public required string ActivationId { get; init; }
  /// <summary>Gets the attempt number.</summary>
  public required long AttemptNumber { get; init; }
  /// <summary>Gets the effect ID.</summary>
  public required string EffectId { get; init; }
  /// <summary>Gets the event sequence.</summary>
  public required long EventSequence { get; init; }
  /// <summary>Gets the outcome.</summary>
  public required StudioEvidenceState Outcome { get; init; } }
/// <summary>Contains one search rebuild/publication evidence item.</summary>
public sealed record StudioSearchRebuildEvidenceItem : StudioEvidenceItem
{ /// <summary>Gets the index ID.</summary>
  public required string IndexId { get; init; }
  /// <summary>Gets the rebuild generation.</summary>
  public required long RebuildGeneration { get; init; }
  /// <summary>Gets the phase sequence.</summary>
  public required long PhaseSequence { get; init; }
  /// <summary>Gets the phase.</summary>
  public required StudioEvidenceState Phase { get; init; }
  /// <summary>Gets the probe outcome.</summary>
  public required StudioEvidenceState ProbeOutcome { get; init; } }
/// <summary>Contains one subject-lifecycle evidence item.</summary>
public sealed record StudioLifecycleEvidenceItem : StudioEvidenceItem
{ /// <summary>Gets the contract ID.</summary>
  public required string ContractId { get; init; }
  /// <summary>Gets the protected scope order.</summary>
  public required ImmutableArray<byte> ProtectedScopeOrder { get; init; }
  /// <summary>Gets the epoch.</summary>
  public required long Epoch { get; init; }
  /// <summary>Gets the incarnation.</summary>
  public required string Incarnation { get; init; }
  /// <summary>Gets the sequence.</summary>
  public required long Sequence { get; init; }
  /// <summary>Gets the state.</summary>
  public required StudioEvidenceState State { get; init; } }
/// <summary>Contains one coordinated-retirement evidence item.</summary>
public sealed record StudioRetirementEvidenceItem : StudioEvidenceItem
{ /// <summary>Gets the contract ID.</summary>
  public required string ContractId { get; init; }
  /// <summary>Gets the protected scope order.</summary>
  public required ImmutableArray<byte> ProtectedScopeOrder { get; init; }
  /// <summary>Gets the epoch.</summary>
  public required long Epoch { get; init; }
  /// <summary>Gets the incarnation.</summary>
  public required string Incarnation { get; init; }
  /// <summary>Gets the publication sequence.</summary>
  public required long PublicationSequence { get; init; }
  /// <summary>Gets the disposition.</summary>
  public required StudioEvidenceState Disposition { get; init; } }
/// <summary>Contains one schema-history evidence item.</summary>
public sealed record StudioSchemaEvidenceItem : StudioEvidenceItem
{ /// <summary>Gets the store ID.</summary>
  public required string StoreId { get; init; }
  /// <summary>Gets the schema generation.</summary>
  public required long SchemaGeneration { get; init; }
  /// <summary>Gets the history sequence.</summary>
  public required long HistorySequence { get; init; }
  /// <summary>Gets the authority checksum.</summary>
  public required ImmutableArray<byte> AuthorityChecksum { get; init; }
  /// <summary>Gets the state.</summary>
  public required StudioEvidenceState State { get; init; } }
/// <summary>Contains one backup/restore-history evidence item.</summary>
public sealed record StudioBackupRestoreEvidenceItem : StudioEvidenceItem
{ /// <summary>Gets the store ID.</summary>
  public required string StoreId { get; init; }
  /// <summary>Gets the operation identity.</summary>
  public required string OperationIdentity { get; init; }
  /// <summary>Gets the restore epoch.</summary>
  public required long RestoreEpoch { get; init; }
  /// <summary>Gets the artifact authority checksum.</summary>
  public required ImmutableArray<byte> ArtifactAuthorityChecksum { get; init; }
  /// <summary>Gets the state.</summary>
  public required StudioEvidenceState State { get; init; } }
/// <summary>Contains one maintenance-history evidence item.</summary>
public sealed record StudioMaintenanceEvidenceItem : StudioEvidenceItem
{ /// <summary>Gets the store ID.</summary>
  public required string StoreId { get; init; }
  /// <summary>Gets the maintenance kind.</summary>
  public required string MaintenanceKind { get; init; }
  /// <summary>Gets the generation.</summary>
  public required long Generation { get; init; }
  /// <summary>Gets the page sequence.</summary>
  public required long PageSequence { get; init; }
  /// <summary>Gets the state.</summary>
  public required StudioEvidenceState State { get; init; } }
/// <summary>Contains one quarantine-history evidence item.</summary>
public sealed record StudioQuarantineEvidenceItem : StudioEvidenceItem
{ /// <summary>Gets the store ID.</summary>
  public required string StoreId { get; init; }
  /// <summary>Gets the subsystem ID.</summary>
  public required string SubsystemId { get; init; }
  /// <summary>Gets the quarantine identity.</summary>
  public required string QuarantineIdentity { get; init; }
  /// <summary>Gets the state.</summary>
  public required StudioEvidenceState State { get; init; } }
/// <summary>Contains one contributor-health transition evidence item.</summary>
public sealed record StudioHealthTransitionEvidenceItem : StudioEvidenceItem
{ /// <summary>Gets the contributor ID.</summary>
  public required string ContributorId { get; init; }
  /// <summary>Gets the observation position.</summary>
  public required long ObservationPosition { get; init; }
  /// <summary>Gets the contributor generation.</summary>
  public required long ContributorGeneration { get; init; }
  /// <summary>Gets the state.</summary>
  public required StudioEvidenceState State { get; init; } }

/// <summary>Requests one finite page from an already captured authority.</summary>
public sealed record StudioEvidencePageRequest
{
    /// <summary>Gets the exclusive boundary.</summary>
    public StudioEvidenceBoundary? After { get; init; }
    /// <summary>Gets the requested page size.</summary>
    public required int Take { get; init; }
}

/// <summary>Contains one provider evidence page.</summary>
public sealed record StudioEvidencePage
{
    /// <summary>Gets the exact ordered items.</summary>
    public required ImmutableArray<StudioEvidenceItem> Items { get; init; }
    /// <summary>Gets the next exclusive boundary, if more evidence remains.</summary>
    public StudioEvidenceBoundary? Next { get; init; }
    /// <summary>Gets the captured index generation.</summary>
    public required long IndexGeneration { get; init; }
    /// <summary>Gets normalized covering read intervals.</summary>
    public required ImmutableArray<StudioEvidenceReadInterval> Intervals { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required StudioEvidenceProviderAccounting Accounting { get; init; }
    /// <summary>Gets the checksum over the complete page.</summary>
    public required ImmutableArray<byte> PageChecksum { get; init; }
}

/// <summary>Reads finite evidence pages from one provider-bound capture.</summary>
public interface IStudioEvidenceSession : IAsyncDisposable
{
    /// <summary>Reads the next bounded evidence page.</summary>
    ValueTask<OperationResult<StudioEvidencePage>> ReadPageAsync(StudioEvidencePageRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Provides provider-neutral durable inspection evidence.</summary>
public interface IStudioEvidenceStore
{
    /// <summary>Gets the immutable capability of this exact provider instance.</summary>
    StudioEvidenceCapability EvidenceCapability { get; }
    /// <summary>Captures immutable provider authority for an authorized scope.</summary>
    ValueTask<OperationResult<BaseCapturedStudioEvidenceAuthority>> CaptureAuthorityAsync(StudioEvidenceRequirement request,
        BaseOwnedScopeSeekAuthority scope, CancellationToken cancellationToken = default);
    /// <summary>Opens the one finite provider-bound session for a captured authority.</summary>
    ValueTask<OperationResult<IStudioEvidenceSession>> OpenSessionAsync(BaseCapturedStudioEvidenceAuthority authority,
        CancellationToken cancellationToken = default);
}
