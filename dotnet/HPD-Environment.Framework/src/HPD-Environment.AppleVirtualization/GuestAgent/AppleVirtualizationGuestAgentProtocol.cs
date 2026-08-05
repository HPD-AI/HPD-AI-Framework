namespace HPD.Environment.AppleVirtualization.GuestAgent;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Environment.AppleVirtualization;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.Contracts;

public static class AppleVirtualizationGuestAgentProtocol
{
    public const string CurrentVersion = "1.0";

    public static readonly ContentType JsonContentType = new("application/json");
    public static readonly SchemaId EnvelopeSchema = new("hpd.execution.apple-virtualization.guest-agent.envelope.v1");
    public static readonly SchemaId ErrorSchema = new("hpd.execution.apple-virtualization.guest-agent.error.v1");
    public static readonly SchemaId HelloSchema = new("hpd.execution.apple-virtualization.guest-agent.hello.v1");
    public static readonly SchemaId ReadySchema = new("hpd.execution.apple-virtualization.guest-agent.ready.v1");
    public static readonly SchemaId ProjectionSchema = new("hpd.execution.apple-virtualization.guest-agent.projection.v1");
    public static readonly SchemaId ProjectionSyncSchema = new("hpd.execution.apple-virtualization.guest-agent.projection.sync.v1");
    public static readonly SchemaId ProjectionFinalizationSchema = new("hpd.execution.apple-virtualization.guest-agent.projection.finalization.v1");
    public static readonly SchemaId ProcessSchema = new("hpd.execution.apple-virtualization.guest-agent.process.v1");
    public static readonly SchemaId ProcessOutputSchema = new("hpd.execution.apple-virtualization.guest-agent.process.output.v1");
    public static readonly SchemaId NetworkSchema = new("hpd.execution.apple-virtualization.guest-agent.network.v1");
    public static readonly SchemaId AuthoritySchema = new("hpd.execution.apple-virtualization.guest-agent.authority.v1");
    public static readonly SchemaId EngineSchema = new("hpd.execution.apple-virtualization.guest-agent.engine.v1");
    public static readonly SchemaId EngineProvisioningSchema = new("hpd.execution.apple-virtualization.guest-agent.engine.provisioning.v1");
}

public enum AppleVirtualizationGuestAgentMessageType { Request, Response, Event }
public enum AppleVirtualizationGuestAgentResponseStatus { Ok, Accepted, Error }

public enum AppleVirtualizationGuestAgentOperation
{
    Hello,
    Health,
    Ready,
    Capabilities,
    ProjectionMount,
    ProjectionStatus,
    ProjectionUnmount,
    ProjectionObserve,
    ProjectionSync,
    ProjectionFinalize,
    ProjectionEnumerateChanges,
    ProjectionPromote,
    ProcessStart,
    ProcessStatus,
    ProcessStdin,
    ProcessCloseStdin,
    ProcessSignal,
    ProcessStop,
    ProcessResize,
    ProcessWait,
    ProcessReadOutput,
    NetworkStatus,
    AuthorityBind,
    AuthorityStatus,
    AuthorityRevoke,
    EngineStatus,
    EngineProvision,
    HostShutdown,
}

public enum AppleVirtualizationGuestAgentEventKind
{
    GuestAgentReady,
    GuestAgentDegraded,
    ProjectionMounted,
    ProjectionMountVerificationFailed,
    ProjectionObserved,
    ProjectionSynced,
    ProjectionFinalized,
    ProjectionPromoted,
    ProcessStarted,
    ProcessOutput,
    ProcessExited,
    NetworkObserved,
    AuthorityProjected,
    AuthorityRevoked,
    EngineObserved,
    EngineDegraded,
    EngineProvisioned,
    Diagnostic,
}

public enum AppleVirtualizationGuestAgentPtyState
{
    NotRequested,
    Requested,
    Allocated,
    Unsupported,
    Failed,
}

public enum AppleVirtualizationGuestAgentProjectionHostShareState
{
    Unknown,
    NotConfigured,
    HostShareConfigured,
}

public enum AppleVirtualizationGuestAgentProjectionFrameworkShareState
{
    Unknown,
    NotAccepted,
    Accepted,
}

public enum AppleVirtualizationGuestAgentProjectionVerificationState
{
    Unknown,
    HostShareConfigured,
    FrameworkShareAccepted,
    GuestPathVisible,
    MountedAtExpectedGuestPath,
    AccessModeVerified,
    ReadyForHpdUse,
    NotVisible,
    MountPathMissing,
    AccessMismatch,
    CoherenceUnknown,
    CoherenceDegraded,
    Failed,
}

public enum AppleVirtualizationGuestAgentProjectionSyncState
{
    Unknown,
    Succeeded,
    DryRun,
    UnsupportedMode,
    UnsupportedDirection,
    UnsupportedConflictPolicy,
    ProjectionNotVerified,
    StaleGeneration,
    Failed,
}

public enum AppleVirtualizationGuestAgentProjectionFinalizationState
{
    Unknown,
    Succeeded,
    UnsupportedKind,
    ProjectionNotVerified,
    StaleGeneration,
    Failed,
}

public enum AppleVirtualizationGuestAgentProjectionPromotionState
{
    Unknown,
    Succeeded,
    DryRun,
    Unsupported,
    ProjectionNotVerified,
    StaleGeneration,
    Failed,
}

public sealed record AppleVirtualizationGuestAgentEnvelope
{
    public string ProtocolVersion { get; init; } = AppleVirtualizationGuestAgentProtocol.CurrentVersion;
    public required AppleVirtualizationGuestAgentMessageType MessageType { get; init; }
    public required AppleVirtualizationGuestAgentOperation Operation { get; init; }
    public string? RequestId { get; init; }
    public string? EventId { get; init; }
    public long SequenceNumber { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? HostId { get; init; }
    public string? UnitId { get; init; }
    public string? ProcessId { get; init; }
    public string? ProjectionId { get; init; }
    public string? GuestBootId { get; init; }
    public ulong GuestBootGeneration { get; init; }
    public ulong GuestAgentGeneration { get; init; }
    public AppleVirtualizationGuestAgentResponseStatus? ResponseStatus { get; init; }
    public AppleVirtualizationGuestAgentEventKind? EventKind { get; init; }
    public SchemaId? PayloadSchema { get; init; }
    public AppleVirtualizationGuestAgentError? Error { get; init; }
    public AppleVirtualizationGuestAgentHello? Hello { get; init; }
    public AppleVirtualizationGuestAgentHealth? Health { get; init; }
    public AppleVirtualizationGuestAgentReady? Ready { get; init; }
    public AppleVirtualizationGuestAgentCapabilities? Capabilities { get; init; }
    public AppleVirtualizationGuestAgentProjectionMountRequest? ProjectionMountRequest { get; init; }
    public AppleVirtualizationGuestAgentProjectionMountResult? ProjectionMountResult { get; init; }
    public AppleVirtualizationGuestAgentProjectionStatusRequest? ProjectionStatusRequest { get; init; }
    public AppleVirtualizationGuestAgentProjectionStatus? ProjectionStatus { get; init; }
    public AppleVirtualizationGuestAgentProjectionUnmountRequest? ProjectionUnmountRequest { get; init; }
    public AppleVirtualizationGuestAgentProjectionUnmountResult? ProjectionUnmountResult { get; init; }
    public AppleVirtualizationGuestAgentProjectionObserveRequest? ProjectionObserveRequest { get; init; }
    public AppleVirtualizationGuestAgentProjectionObserveResult? ProjectionObserveResult { get; init; }
    public AppleVirtualizationGuestAgentProjectionSyncRequest? ProjectionSyncRequest { get; init; }
    public AppleVirtualizationGuestAgentProjectionSyncResult? ProjectionSyncResult { get; init; }
    public AppleVirtualizationGuestAgentProjectionFinalizationRequest? ProjectionFinalizationRequest { get; init; }
    public AppleVirtualizationGuestAgentProjectionFinalizationResult? ProjectionFinalizationResult { get; init; }
    public AppleVirtualizationGuestAgentProjectionChangeEnumerationRequest? ProjectionChangeEnumerationRequest { get; init; }
    public AppleVirtualizationGuestAgentProjectionChangeEnumerationResult? ProjectionChangeEnumerationResult { get; init; }
    public AppleVirtualizationGuestAgentProjectionPromotionRequest? ProjectionPromotionRequest { get; init; }
    public AppleVirtualizationGuestAgentProjectionPromotionResult? ProjectionPromotionResult { get; init; }
    public AppleVirtualizationGuestAgentProcessStartRequest? ProcessStartRequest { get; init; }
    public AppleVirtualizationGuestAgentProcessStarted? ProcessStarted { get; init; }
    public AppleVirtualizationGuestAgentProcessStatusRequest? ProcessStatusRequest { get; init; }
    public AppleVirtualizationGuestAgentProcessStatus? ProcessStatus { get; init; }
    public AppleVirtualizationGuestAgentProcessStdinRequest? ProcessStdinRequest { get; init; }
    public AppleVirtualizationGuestAgentProcessCloseStdinRequest? ProcessCloseStdinRequest { get; init; }
    public AppleVirtualizationGuestAgentProcessSignalRequest? ProcessSignalRequest { get; init; }
    public AppleVirtualizationGuestAgentProcessStopRequest? ProcessStopRequest { get; init; }
    public AppleVirtualizationGuestAgentProcessControlResult? ProcessControlResult { get; init; }
    public AppleVirtualizationGuestAgentProcessResizeRequest? ProcessResizeRequest { get; init; }
    public AppleVirtualizationGuestAgentProcessWaitRequest? ProcessWaitRequest { get; init; }
    public AppleVirtualizationGuestAgentProcessReadOutputRequest? ProcessReadOutputRequest { get; init; }
    public AppleVirtualizationGuestAgentProcessOutputReadResult? ProcessOutputReadResult { get; init; }
    public AppleVirtualizationGuestAgentProcessOutputChunk? ProcessOutputChunk { get; init; }
    public AppleVirtualizationGuestAgentProcessResult? ProcessResult { get; init; }
    public AppleVirtualizationGuestAgentNetworkStatusRequest? NetworkStatusRequest { get; init; }
    public AppleVirtualizationGuestAgentNetworkStatus? NetworkStatus { get; init; }
    public AppleVirtualizationGuestAgentAuthorityProjectionRequest? AuthorityProjectionRequest { get; init; }
    public AppleVirtualizationGuestAgentAuthorityStatusRequest? AuthorityStatusRequest { get; init; }
    public AppleVirtualizationGuestAgentAuthorityRevocationRequest? AuthorityRevocationRequest { get; init; }
    public AppleVirtualizationGuestAgentAuthorityStatus? AuthorityStatus { get; init; }
    public AppleVirtualizationGuestAgentAuthorityRevocationResult? AuthorityRevocationResult { get; init; }
    public AppleVirtualizationGuestAgentEngineStatusRequest? EngineStatusRequest { get; init; }
    public AppleVirtualizationGuestAgentEngineStatus? EngineStatus { get; init; }
    public AppleVirtualizationGuestAgentEngineProvisioningRequest? EngineProvisioningRequest { get; init; }
    public AppleVirtualizationGuestAgentEngineProvisioningResult? EngineProvisioningResult { get; init; }
    public AppleVirtualizationGuestAgentHostShutdownRequest? HostShutdownRequest { get; init; }
    public AppleVirtualizationGuestAgentHostShutdownResponse? HostShutdownResponse { get; init; }

    public static AppleVirtualizationGuestAgentEnvelope Request(
        AppleVirtualizationGuestAgentOperation operation,
        string requestId,
        long sequenceNumber,
        SchemaId? payloadSchema = null) =>
        new()
        {
            MessageType = AppleVirtualizationGuestAgentMessageType.Request,
            Operation = operation,
            RequestId = requestId,
            SequenceNumber = sequenceNumber,
            PayloadSchema = payloadSchema,
        };

    public AppleVirtualizationGuestAgentEnvelope ToResponse(long sequenceNumber) =>
        this with
        {
            MessageType = AppleVirtualizationGuestAgentMessageType.Response,
            ResponseStatus = Error is null ? AppleVirtualizationGuestAgentResponseStatus.Ok : AppleVirtualizationGuestAgentResponseStatus.Error,
            SequenceNumber = sequenceNumber,
            Timestamp = DateTimeOffset.UtcNow,
            CausationId = RequestId,
        };

    public AppleVirtualizationGuestAgentEnvelope ToErrorResponse(
        long sequenceNumber,
        AppleVirtualizationGuestAgentError error) =>
        ToResponse(sequenceNumber) with
        {
            ResponseStatus = AppleVirtualizationGuestAgentResponseStatus.Error,
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ErrorSchema,
            Error = error,
        };
}

public sealed record AppleVirtualizationGuestAgentHello
{
    public string AgentName { get; init; } = "hpd-guest-agent";
    public required string AgentVersion { get; init; }
    public required string ProtocolVersion { get; init; }
    public required string GuestBootId { get; init; }
    public ulong GuestBootGeneration { get; init; }
    public ulong GuestAgentGeneration { get; init; }
    public string? Hostname { get; init; }
    public string? RuntimeUser { get; init; }
    public bool ProtocolCompatible { get; init; } = true;
    public AppleVirtualizationGuestAgentCapabilities Capabilities { get; init; } = new();
}

public sealed record AppleVirtualizationGuestAgentHealth
{
    public bool Alive { get; init; }
    public bool Ready { get; init; }
    public string? Detail { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public sealed record AppleVirtualizationGuestAgentReady
{
    public bool IsReady { get; init; }
    public required string GuestBootId { get; init; }
    public ulong GuestBootGeneration { get; init; }
    public ulong GuestAgentGeneration { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

public sealed record AppleVirtualizationGuestAgentCapabilities
{
    public bool HostShutdown { get; init; }
    public bool ProcessStart { get; init; } = true;
    public bool ProcessStdin { get; init; } = true;
    public bool ProcessSignal { get; init; } = true;
    public bool ProcessStop { get; init; } = true;
    public bool ProcessReadOutput { get; init; } = true;
    public bool Pty { get; init; }
    public bool ProcessResize { get; init; }
    public bool ProjectionMount { get; init; } = true;
    public bool ProjectionObserve { get; init; }
    public bool ProjectionSync { get; init; }
    public bool ProjectionFinalize { get; init; }
    public bool ProjectionPromote { get; init; }
    public bool AuthorityProjection { get; init; }
    public bool AuthorityRevocation { get; init; }
    public bool EngineStatus { get; init; }
    public bool EngineProvisioning { get; init; }
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
}

public sealed record AppleVirtualizationGuestAgentHostShutdownRequest(
    string HostId,
    ulong ProviderGeneration,
    ulong HostStartGeneration,
    string? Reason = null);

public sealed record AppleVirtualizationGuestAgentHostShutdownResponse(
    bool Accepted,
    string HostId,
    ulong ProviderGeneration,
    ulong HostStartGeneration);

public sealed record AppleVirtualizationGuestAgentError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Operation { get; init; }
    public bool Retryable { get; init; }
    public string? FailedPhase { get; init; }
    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Error;
    public SchemaId? DetailSchema { get; init; }
    public ReadOnlyMemory<byte> Detail { get; init; }
}

public sealed record AppleVirtualizationGuestAgentProjectionMountRequest
{
    public required string ProjectionId { get; init; }
    public required string Tag { get; init; }
    public required string GuestPath { get; init; }
    public AccessMode AccessMode { get; init; } = AccessMode.ReadOnly;
    public bool VerifyMount { get; init; } = true;
    public AppleVirtualizationGuestAgentProjectionIdentity? Identity { get; init; }
    public AppleVirtualizationGuestAgentProjectionHostSourceIdentity? HostSource { get; init; }
    public AppleVirtualizationGuestAgentProjectionGuestPathExpectation? ExpectedGuestPath { get; init; }
    public ProjectionRealizationKind RequestedRealization { get; init; } = ProjectionRealizationKind.LiveProjection;
    public ProjectionWriteEffect RequestedWriteEffect { get; init; } = ProjectionWriteEffect.Unknown;
    public CoherenceClass RequestedCoherence { get; init; } = CoherenceClass.Unknown;
    public CacheBehavior RequestedCache { get; init; } = CacheBehavior.Unknown;
    public AppleVirtualizationGuestAgentProjectionGenerationStamp Generation { get; init; } = new();
}

public sealed record AppleVirtualizationGuestAgentProjectionMountResult
{
    public required AppleVirtualizationGuestAgentProjectionStatus Status { get; init; }
}

public sealed record AppleVirtualizationGuestAgentProjectionStatusRequest(string ProjectionId, string ExpectedGuestPath, bool VerifyAccess = true);

public sealed record AppleVirtualizationGuestAgentProjectionStatus
{
    public required string ProjectionId { get; init; }
    public required string GuestPath { get; init; }
    public required string Tag { get; init; }
    public bool Mounted { get; init; }
    public bool GuestMountVerified { get; init; }
    public AppleVirtualizationGuestAgentProjectionHostShareState HostShareState { get; init; } = AppleVirtualizationGuestAgentProjectionHostShareState.Unknown;
    public AppleVirtualizationGuestAgentProjectionFrameworkShareState FrameworkShareState { get; init; } = AppleVirtualizationGuestAgentProjectionFrameworkShareState.Unknown;
    public AppleVirtualizationGuestAgentProjectionVerificationState VerificationState { get; init; } = AppleVirtualizationGuestAgentProjectionVerificationState.Unknown;
    public string? ExpectedGuestPath { get; init; }
    public string? ActualGuestPath { get; init; }
    public AccessMode RequestedAccessMode { get; init; } = AccessMode.ReadOnly;
    public AccessMode EffectiveAccessMode { get; init; } = AccessMode.ReadOnly;
    public ContentProjectionPhase ProjectionPhase { get; init; }
    public ProjectionRealizationKind EffectiveRealization { get; init; } = ProjectionRealizationKind.ProviderDefault;
    public ProjectionWriteEffect EffectiveWriteEffect { get; init; }
    public CoherenceClass EffectiveCoherence { get; init; } = CoherenceClass.ProviderDefined;
    public CacheBehavior EffectiveCache { get; init; } = CacheBehavior.Unknown;
    public AppleVirtualizationGuestAgentProjectionGenerationStamp Generation { get; init; } = new();
    public IReadOnlyList<ContentProjectionLimitation> Limitations { get; init; } = Array.Empty<ContentProjectionLimitation>();
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
    public bool ReadyForHpdUse => GuestMountVerified &&
        Mounted &&
        VerificationState == AppleVirtualizationGuestAgentProjectionVerificationState.ReadyForHpdUse;
}

public sealed record AppleVirtualizationGuestAgentProjectionIdentity(string ProjectionId, string? HostId = null, string? UnitId = null, string? TargetName = null);
public sealed record AppleVirtualizationGuestAgentProjectionHostSourceIdentity(string HostPath, string Tag, bool HostShareConfigured, bool FrameworkShareAccepted);
public sealed record AppleVirtualizationGuestAgentProjectionGuestPathExpectation(string ExpectedGuestPath, bool RequireDirectory = true);
public sealed record AppleVirtualizationGuestAgentProjectionGuestPathObservation(string? ActualGuestPath, bool PathExists, bool IsDirectory, bool MountedAtExpectedPath);
public sealed record AppleVirtualizationGuestAgentProjectionGenerationStamp(ulong ProviderGeneration = 0, ulong HostStartGeneration = 0, string? GuestBootId = null, ulong GuestBootGeneration = 0, ulong GuestAgentGeneration = 0, ulong ProjectionGeneration = 0);

public sealed record AppleVirtualizationGuestAgentProjectionUnmountRequest(string ProjectionId, string GuestPath, bool Force = false, AppleVirtualizationGuestAgentProjectionGenerationStamp? Generation = null);
public sealed record AppleVirtualizationGuestAgentProjectionUnmountResult(string ProjectionId, bool Unmounted, bool WasMounted, string? Detail = null);
public sealed record AppleVirtualizationGuestAgentProjectionObserveRequest(string ProjectionId, string GuestPath, bool Recursive = true, long? AfterSequence = null, int? Limit = null);
public sealed record AppleVirtualizationGuestAgentProjectionObserveResult(string ProjectionId, AppleVirtualizationGuestAgentProjectionStatus Status, IReadOnlyList<ContentProjectionFileEvent>? Events = null, bool HasMore = false);

public sealed record AppleVirtualizationGuestAgentProjectionSyncRequest
{
    public required string ProjectionId { get; init; }
    public required string GuestPath { get; init; }
    public SyncMode Mode { get; init; } = SyncMode.Manual;
    public SyncDirection Direction { get; init; } = SyncDirection.TargetToSource;
    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.RecordConflict;
    public bool DryRun { get; init; }
    public int MaxChanges { get; init; } = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxChanges;
    public int MaxConflicts { get; init; } = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxConflicts;
    public AppleVirtualizationGuestAgentProjectionGenerationStamp Generation { get; init; } = new();
}

public sealed record AppleVirtualizationGuestAgentProjectionSyncResult
{
    public required string ProjectionId { get; init; }
    public AppleVirtualizationGuestAgentProjectionSyncState State { get; init; } = AppleVirtualizationGuestAgentProjectionSyncState.Unknown;
    public bool Succeeded { get; init; }
    public bool DryRun { get; init; }
    public long CheckpointVersion { get; init; }
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
    public ContentProjectionChangeSummary ChangeSummary { get; init; } = new();
    public IReadOnlyList<AppleVirtualizationGuestAgentProjectionChange> Changes { get; init; } = Array.Empty<AppleVirtualizationGuestAgentProjectionChange>();
    public IReadOnlyList<WorkspaceConflict> Conflicts { get; init; } = Array.Empty<WorkspaceConflict>();
    public bool ChangesTruncated { get; init; }
    public bool ConflictsTruncated { get; init; }
    public string? UnsupportedReason { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

public sealed record AppleVirtualizationGuestAgentProjectionFinalizationRequest
{
    public required string ProjectionId { get; init; }
    public required string GuestPath { get; init; }
    public FinalizationKind Kind { get; init; } = FinalizationKind.ManifestAndChangedContent;
    public bool IncludeProvenance { get; init; } = true;
    public bool IncludeDeletedEntries { get; init; } = true;
    public string? ProducerId { get; init; }
    public int MaxContentRefs { get; init; } = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxFinalizedContentRefs;
    public int MaxConflicts { get; init; } = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxConflicts;
    public AppleVirtualizationGuestAgentProjectionGenerationStamp Generation { get; init; } = new();
}

public sealed record AppleVirtualizationGuestAgentProjectionFinalizationResult
{
    public required string ProjectionId { get; init; }
    public AppleVirtualizationGuestAgentProjectionFinalizationState State { get; init; } = AppleVirtualizationGuestAgentProjectionFinalizationState.Unknown;
    public bool Succeeded { get; init; }
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
    public Digest? ManifestDigest { get; init; }
    public IReadOnlyList<FinalizedContentRef> Content { get; init; } = Array.Empty<FinalizedContentRef>();
    public IReadOnlyList<WorkspaceConflict> Conflicts { get; init; } = Array.Empty<WorkspaceConflict>();
    public bool ContentTruncated { get; init; }
    public bool ConflictsTruncated { get; init; }
    public string? UnsupportedReason { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

public sealed record AppleVirtualizationGuestAgentProjectionChangeEnumerationRequest
{
    public required string ProjectionId { get; init; }
    public required string GuestPath { get; init; }
    public long? AfterSequence { get; init; }
    public int Limit { get; init; } = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxChanges;
    public bool IncludeDeletedEntries { get; init; } = true;
    public AppleVirtualizationGuestAgentProjectionGenerationStamp Generation { get; init; } = new();
}

public sealed record AppleVirtualizationGuestAgentProjectionChangeEnumerationResult
{
    public required string ProjectionId { get; init; }
    public IReadOnlyList<AppleVirtualizationGuestAgentProjectionChange> Changes { get; init; } = Array.Empty<AppleVirtualizationGuestAgentProjectionChange>();
    public long? NextSequence { get; init; }
    public bool HasMore { get; init; }
    public bool Truncated { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public sealed record AppleVirtualizationGuestAgentProjectionPromotionRequest
{
    public required string ProjectionId { get; init; }
    public required string GuestPath { get; init; }
    public SyncDirection Direction { get; init; } = SyncDirection.TargetToSource;
    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.RequireExplicitPromotion;
    public bool DryRun { get; init; }
    public int MaxChanges { get; init; } = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxChanges;
    public int MaxConflicts { get; init; } = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxConflicts;
    public AppleVirtualizationGuestAgentProjectionGenerationStamp Generation { get; init; } = new();
}

public sealed record AppleVirtualizationGuestAgentProjectionPromotionResult
{
    public required string ProjectionId { get; init; }
    public AppleVirtualizationGuestAgentProjectionPromotionState State { get; init; } = AppleVirtualizationGuestAgentProjectionPromotionState.Unknown;
    public bool Succeeded { get; init; }
    public bool DryRun { get; init; }
    public ContentProjectionChangeSummary ChangeSummary { get; init; } = new();
    public IReadOnlyList<AppleVirtualizationGuestAgentProjectionChange> Changes { get; init; } = Array.Empty<AppleVirtualizationGuestAgentProjectionChange>();
    public IReadOnlyList<WorkspaceConflict> Conflicts { get; init; } = Array.Empty<WorkspaceConflict>();
    public bool ChangesTruncated { get; init; }
    public bool ConflictsTruncated { get; init; }
    public string? UnsupportedReason { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public sealed record AppleVirtualizationGuestAgentProjectionChange(
    long Sequence,
    FileEventKind Kind,
    string Path,
    ByteSize Size = default,
    Digest? Digest = null,
    DateTimeOffset? LastModifiedAt = null,
    bool Deleted = false,
    ContentProjectionRole Role = ContentProjectionRole.Workspace);

public static class AppleVirtualizationGuestAgentProjectionResultBounds
{
    public const int DefaultMaxChanges = 1024;
    public const int DefaultMaxConflicts = 128;
    public const int DefaultMaxFinalizedContentRefs = 1024;

    public static IReadOnlyList<T> Bound<T>(IReadOnlyList<T>? items, int limit, out bool truncated)
    {
        if (items is null || items.Count == 0)
        {
            truncated = false;
            return Array.Empty<T>();
        }

        int boundedLimit = Math.Clamp(limit, 0, items.Count);
        truncated = items.Count > boundedLimit;
        if (!truncated)
        {
            return items;
        }

        var bounded = new T[boundedLimit];
        for (int index = 0; index < boundedLimit; index++)
        {
            bounded[index] = items[index];
        }

        return bounded;
    }
}

public sealed record AppleVirtualizationGuestAgentProcessStartRequest
{
    public required string ProcessId { get; init; }
    public required string UnitId { get; init; }
    public required ProcessCommandSpec Command { get; init; }
    public ProcessIdentitySpec? Identity { get; init; }
    public ProcessLimitSpec? Limits { get; init; }
    public ProcessIoSpec Io { get; init; } = ProcessIoSpec.Default;
    public ProcessInvocationPolicy Policy { get; init; } = ProcessInvocationPolicy.Default;
    public ProcessIsolationPolicy Isolation { get; init; } = ProcessIsolationPolicy.Default;
    public ObservationRetentionPolicy ObservationRetention { get; init; } = ObservationRetentionPolicy.ResultAndDiagnostics;
    public string? RequiredProjectionId { get; init; }
    public string? RequiredProjectionGuestPath { get; init; }
    public bool RequireVerifiedProjection { get; init; }
    public AppleVirtualizationGuestAgentTerminalState Terminal { get; init; } = new();
    public AppleVirtualizationGuestAgentProcessGenerationStamp Generation { get; init; } = new();
}

public sealed record AppleVirtualizationGuestAgentProcessStarted
{
    public required string ProcessId { get; init; }
    public ProcessInvocationPhase ProcessPhase { get; init; } = ProcessInvocationPhase.Running;
    public int? SystemProcessId { get; init; }
    public string? ProviderProcessId { get; init; }
    public AppleVirtualizationGuestAgentPtyState PtyState { get; init; } = AppleVirtualizationGuestAgentPtyState.NotRequested;
    public AppleVirtualizationGuestAgentTerminalState Terminal { get; init; } = new();
    public ProcessIoState IoState { get; init; } = ProcessIoState.Open;
    public DateTimeOffset? StartedAt { get; init; }
    public AppleVirtualizationGuestAgentProcessGenerationStamp Generation { get; init; } = new();
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public sealed record AppleVirtualizationGuestAgentProcessStatusRequest(string ProcessId, bool IncludeResult = false);

public sealed record AppleVirtualizationGuestAgentProcessStatus
{
    public required string ProcessId { get; init; }
    public ProcessInvocationPhase ProcessPhase { get; init; } = ProcessInvocationPhase.Unknown;
    public ProcessIoState IoState { get; init; } = ProcessIoState.Unknown;
    public int? SystemProcessId { get; init; }
    public string? ProviderProcessId { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? ExitedAt { get; init; }
    public AppleVirtualizationGuestAgentTerminalState Terminal { get; init; } = new();
    public AppleVirtualizationGuestAgentProcessGenerationStamp Generation { get; init; } = new();
    public AppleVirtualizationGuestAgentProcessResult? Result { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

public sealed record AppleVirtualizationGuestAgentProcessStdinRequest
{
    public required string ProcessId { get; init; }
    public ReadOnlyMemory<byte> Bytes { get; init; }
    public bool CloseAfterWrite { get; init; }
    public long Sequence { get; init; }
}

public sealed record AppleVirtualizationGuestAgentProcessCloseStdinRequest(string ProcessId, string? Reason = null);
public sealed record AppleVirtualizationGuestAgentProcessSignalRequest(string ProcessId, ProcessSignal Signal);
public sealed record AppleVirtualizationGuestAgentProcessStopRequest(string ProcessId, StopKind Kind, TimeSpan? GracePeriod = null, string? Reason = null);
public sealed record AppleVirtualizationGuestAgentProcessControlResult(string ProcessId, bool Accepted, ProcessInvocationPhase ProcessPhase = ProcessInvocationPhase.Unknown, string? Detail = null);

public sealed record AppleVirtualizationGuestAgentProcessResizeRequest
{
    public required string ProcessId { get; init; }
    public required TerminalSpec Size { get; init; }
}

public sealed record AppleVirtualizationGuestAgentProcessWaitRequest(string ProcessId, TimeSpan? Timeout = null);
public sealed record AppleVirtualizationGuestAgentProcessReadOutputRequest(string ProcessId, long? AfterSequence = null, int? Limit = null, bool Follow = false);

public sealed record AppleVirtualizationGuestAgentProcessOutputReadResult
{
    public required string ProcessId { get; init; }
    public IReadOnlyList<AppleVirtualizationGuestAgentProcessOutputChunk> Chunks { get; init; } = Array.Empty<AppleVirtualizationGuestAgentProcessOutputChunk>();
    public long? NextSequence { get; init; }
    public bool HasMore { get; init; }
    public bool FollowActive { get; init; }
}

public sealed record AppleVirtualizationGuestAgentProcessOutputChunk
{
    public required string ProcessId { get; init; }
    public required ProcessOutputStream Stream { get; init; }
    public required long Sequence { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public ReadOnlyMemory<byte> Bytes { get; init; }
    public ProcessOutputChunkFlags Flags { get; init; }
    public AppleVirtualizationGuestAgentCaptureAccounting Capture { get; init; } = new();
}

public sealed record AppleVirtualizationGuestAgentProcessResult
{
    public required string ProcessId { get; init; }
    public int? SystemProcessId { get; init; }
    public string? ProviderProcessId { get; init; }
    public int? ExitCode { get; init; }
    public required ProcessCompletionKind CompletionKind { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? ExitedAt { get; init; }
    public TimeSpan? Duration { get; init; }
    public AppleVirtualizationGuestAgentCaptureAccounting StdoutCapture { get; init; } = new();
    public AppleVirtualizationGuestAgentCaptureAccounting StderrCapture { get; init; } = new();
    public bool MergedStandardError { get; init; }
    public bool OutputDrainTimedOut { get; init; }
    public TimeSpan OutputDrainTimeout { get; init; }
    public AppleVirtualizationGuestAgentProcessGenerationStamp Generation { get; init; } = new();
    public IReadOnlyList<ProcessViolation> Violations { get; init; } = Array.Empty<ProcessViolation>();
    public IReadOnlyList<Condition> Diagnostics { get; init; } = Array.Empty<Condition>();
}

public sealed record AppleVirtualizationGuestAgentCaptureAccounting
{
    public long BytesObserved { get; init; }
    public long BytesCaptured { get; init; }
    public long BytesDiscarded { get; init; }
    public bool Truncated { get; init; }
    public int? MaxCapturedBytes { get; init; }
}

public sealed record AppleVirtualizationGuestAgentTerminalState
{
    public AppleVirtualizationGuestAgentPtyState PtyState { get; init; } = AppleVirtualizationGuestAgentPtyState.NotRequested;
    public TerminalSpec? Size { get; init; }
    public bool ResizeSupported { get; init; }
    public string? ResizeUnsupportedReason { get; init; }
}

public sealed record AppleVirtualizationGuestAgentProcessGenerationStamp(ulong ProviderGeneration = 0, ulong HostStartGeneration = 0, string? GuestBootId = null, ulong GuestBootGeneration = 0, ulong GuestAgentGeneration = 0, ulong ProcessGeneration = 0);

public sealed record AppleVirtualizationGuestAgentNetworkStatusRequest
{
    public required string HostId { get; init; }
    public string? UnitId { get; init; }
    public bool IncludeRoutes { get; init; } = true;
    public bool IncludeListeners { get; init; } = true;
    public int MaxInterfaces { get; init; } = AppleVirtualizationGuestAgentNetworkResultBounds.DefaultMaxInterfaces;
    public int MaxRoutes { get; init; } = AppleVirtualizationGuestAgentNetworkResultBounds.DefaultMaxRoutes;
    public int MaxListeners { get; init; } = AppleVirtualizationGuestAgentNetworkResultBounds.DefaultMaxListeners;
}

public sealed record AppleVirtualizationGuestAgentNetworkStatus
{
    public required string HostId { get; init; }
    public string? UnitId { get; init; }
    public bool GuestAgentReady { get; init; }
    public IReadOnlyList<AppleVirtualizationGuestAgentNetworkInterfaceStatus> Interfaces { get; init; } = Array.Empty<AppleVirtualizationGuestAgentNetworkInterfaceStatus>();
    public IReadOnlyList<AppleVirtualizationGuestAgentNetworkRouteObservation> Routes { get; init; } = Array.Empty<AppleVirtualizationGuestAgentNetworkRouteObservation>();
    public IReadOnlyList<AppleVirtualizationGuestAgentNetworkListenerObservation> Listeners { get; init; } = Array.Empty<AppleVirtualizationGuestAgentNetworkListenerObservation>();
    public bool InterfacesTruncated { get; init; }
    public bool RoutesTruncated { get; init; }
    public bool ListenersTruncated { get; init; }
    public AppleVirtualizationGuestAgentNetworkGenerationStamp Generation { get; init; } = new();
    public IReadOnlyList<NetworkLimitation> Limitations { get; init; } = Array.Empty<NetworkLimitation>();
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public sealed record AppleVirtualizationGuestAgentNetworkInterfaceStatus
{
    public required string Name { get; init; }
    public ushort? Mtu { get; init; }
    public MacAddressValue? MacAddress { get; init; }
    public bool IsUp { get; init; }
    public IReadOnlyList<NetworkAddressAssignment> Addresses { get; init; } = Array.Empty<NetworkAddressAssignment>();
}

public sealed record AppleVirtualizationGuestAgentNetworkRouteObservation
{
    public IpCidr? Destination { get; init; }
    public IpAddressValue? Gateway { get; init; }
    public string? InterfaceName { get; init; }
    public bool IsDefault { get; init; }
}

public sealed record AppleVirtualizationGuestAgentNetworkListenerObservation
{
    public required string Name { get; init; }
    public NetworkTransport Transport { get; init; } = NetworkTransport.ProviderDefined;
    public IpAddressValue? Address { get; init; }
    public NetworkPort? Port { get; init; }
    public UnixSocketPath? SocketPath { get; init; }
    public bool GuestVisibleOnly { get; init; } = true;
    public bool HpdPublished { get; init; }
}

public sealed record AppleVirtualizationGuestAgentNetworkGenerationStamp(ulong ProviderGeneration = 0, ulong HostStartGeneration = 0, string? GuestBootId = null, ulong GuestBootGeneration = 0, ulong GuestAgentGeneration = 0);

public static class AppleVirtualizationGuestAgentNetworkResultBounds
{
    public const int DefaultMaxInterfaces = 16;
    public const int DefaultMaxRoutes = 64;
    public const int DefaultMaxListeners = 128;

    public static IReadOnlyList<T> Bound<T>(IReadOnlyList<T>? items, int limit, out bool truncated)
    {
        if (items is null || items.Count == 0)
        {
            truncated = false;
            return Array.Empty<T>();
        }

        int boundedLimit = Math.Clamp(limit, 0, items.Count);
        truncated = items.Count > boundedLimit;
        if (!truncated)
        {
            return items;
        }

        var bounded = new T[boundedLimit];
        for (int index = 0; index < boundedLimit; index++)
        {
            bounded[index] = items[index];
        }

        return bounded;
    }
}

public sealed record AppleVirtualizationGuestAgentEngineGenerationStamp(ulong ProviderGeneration = 0, ulong HostStartGeneration = 0, string? GuestBootId = null, ulong GuestBootGeneration = 0, ulong GuestAgentGeneration = 0, ulong EngineGeneration = 0);

public sealed record AppleVirtualizationGuestAgentEngineStatusRequest
{
    public required string HostId { get; init; }
    public ulong ProviderGeneration { get; init; }
    public ulong HostStartGeneration { get; init; }
    public string? EngineId { get; init; }
    public EngineControlPlaneKind Kind { get; init; } = EngineControlPlaneKind.DockerCompatible;
    public EngineApiKind Api { get; init; } = EngineApiKind.DockerCompatible;
    public EngineAuthorityMode AuthorityMode { get; init; } = EngineAuthorityMode.Rootless;
    public EngineImageStoreMode ImageStore { get; init; } = EngineImageStoreMode.ProviderManaged;
    public EngineWorkloadAdoptionMode WorkloadAdoption { get; init; } = EngineWorkloadAdoptionMode.None;
    public BoundaryLocus ObservationLocus { get; init; } = BoundaryLocus.RuntimeHost;
    public string? ObservedSocketPath { get; init; }
    public string? ObservedVersion { get; init; }
    public string? ObservedStatus { get; init; }
    public IReadOnlyList<AppleVirtualizationGuestAgentContainerObservation> ObservedContainers { get; init; } =
        Array.Empty<AppleVirtualizationGuestAgentContainerObservation>();
    public IReadOnlyList<Diagnostic> ObservedDiagnostics { get; init; } = Array.Empty<Diagnostic>();
    public int MaxEndpoints { get; init; } = AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxEndpoints;
    public int MaxContainers { get; init; } = AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxContainers;
    public int MaxDiagnostics { get; init; } = AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxDiagnostics;
    public int MaxVersionLength { get; init; } = AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxVersionLength;
    public int MaxStatusLength { get; init; } = AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxStatusLength;
    public bool IncludeContainers { get; init; }
    public AppleVirtualizationEngineObservationState? ScriptedObservationState { get; init; }
}

public enum AppleVirtualizationGuestAgentEngineProbeReadiness
{
    Unknown,
    Ready,
    Degraded,
    NotInstalled,
    Unavailable,
}

public enum AppleVirtualizationGuestAgentEngineProbeIssue
{
    None,
    SocketMissing,
    PermissionDenied,
    SystemdMissing,
    TransportError,
    ProbeFailed,
}

public sealed record AppleVirtualizationGuestAgentEngineProbeCandidate
{
    public required UnixSocketPath SocketPath { get; init; }
    public EngineControlPlaneKind Kind { get; init; } = EngineControlPlaneKind.DockerCompatible;
    public EngineApiKind Api { get; init; } = EngineApiKind.DockerCompatible;
    public EngineAuthorityMode AuthorityMode { get; init; } = EngineAuthorityMode.Rootless;
    public bool GuestVisibleOnly { get; init; } = true;
}

public sealed record AppleVirtualizationGuestAgentEngineProbeObservation
{
    public AppleVirtualizationGuestAgentEngineProbeReadiness Readiness { get; init; } =
        AppleVirtualizationGuestAgentEngineProbeReadiness.Unknown;
    public AppleVirtualizationGuestAgentEngineProbeIssue Issue { get; init; } =
        AppleVirtualizationGuestAgentEngineProbeIssue.None;
    public UnixSocketPath? SocketPath { get; init; }
    public bool SocketExists { get; init; }
    public bool SocketAccessible { get; init; } = true;
    public bool SystemdAvailable { get; init; } = true;
    public string? VersionOutput { get; init; }
    public string? StatusOutput { get; init; }
    public IReadOnlyList<AppleVirtualizationGuestAgentContainerObservation> Containers { get; init; } =
        Array.Empty<AppleVirtualizationGuestAgentContainerObservation>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

public interface IAppleVirtualizationGuestAgentEngineProbe
{
    AppleVirtualizationGuestAgentEngineProbeObservation Probe(AppleVirtualizationGuestAgentEngineStatusRequest request);
}

public sealed class FakeAppleVirtualizationGuestAgentEngineProbe : IAppleVirtualizationGuestAgentEngineProbe
{
    private readonly ConcurrentQueue<AppleVirtualizationGuestAgentEngineProbeObservation> _observations = new();

    public FakeAppleVirtualizationGuestAgentEngineProbe(params AppleVirtualizationGuestAgentEngineProbeObservation[] observations)
    {
        for (int index = 0; index < observations.Length; index++)
        {
            _observations.Enqueue(observations[index]);
        }
    }

    public void Enqueue(AppleVirtualizationGuestAgentEngineProbeObservation observation) =>
        _observations.Enqueue(observation);

    public AppleVirtualizationGuestAgentEngineProbeObservation Probe(AppleVirtualizationGuestAgentEngineStatusRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _observations.TryDequeue(out AppleVirtualizationGuestAgentEngineProbeObservation? observation)
            ? observation
            : AppleVirtualizationGuestAgentEngineProbeMapper.NotInstalled(request);
    }
}

public static class AppleVirtualizationGuestAgentEngineProbeMapper
{
    private const string RootlessDockerSocketPrefix = "/run/user/";
    private const string RootlessDockerSocketSuffix = "/docker.sock";
    private const string DefaultRootlessDockerSocket = "/run/user/1000/docker.sock";
    private const string RootfulDockerSocket = "/var/run/docker.sock";
    private const string AlternateRootfulDockerSocket = "/run/docker.sock";
    private const string ContainerdSocket = "/run/containerd/containerd.sock";
    private const string AlternateContainerdSocket = "/var/run/containerd/containerd.sock";
    private const string DefaultRootlessPodmanSocket = "/run/user/1000/podman/podman.sock";
    private const string RootfulPodmanSocket = "/run/podman/podman.sock";
    private const string DefaultRootlessBuildKitSocket = "/run/user/1000/buildkit-default/buildkitd.sock";
    private const string RootfulBuildKitSocket = "/run/buildkit/buildkitd.sock";

    public static AppleVirtualizationGuestAgentEngineProbeObservation NotInstalled(
        AppleVirtualizationGuestAgentEngineStatusRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new AppleVirtualizationGuestAgentEngineProbeObservation
        {
            Readiness = AppleVirtualizationGuestAgentEngineProbeReadiness.NotInstalled,
            Issue = AppleVirtualizationGuestAgentEngineProbeIssue.SocketMissing,
            SocketPath = CandidateSocketPaths(request).FirstOrDefault()?.SocketPath,
            SocketExists = false,
            SocketAccessible = false,
            StatusOutput = "guest engine socket was not found inside the runtime host",
        };
    }

    public static IReadOnlyList<AppleVirtualizationGuestAgentEngineProbeCandidate> CandidateSocketPaths(
        AppleVirtualizationGuestAgentEngineStatusRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Api == EngineApiKind.ContainerdApi || request.Kind == EngineControlPlaneKind.Containerd)
        {
            return [
                Candidate(ContainerdSocket, EngineControlPlaneKind.Containerd, EngineApiKind.ContainerdApi, EngineAuthorityMode.Rootful),
                Candidate(AlternateContainerdSocket, EngineControlPlaneKind.Containerd, EngineApiKind.ContainerdApi, EngineAuthorityMode.Rootful),
            ];
        }

        if (request.Api == EngineApiKind.PodmanApi || request.Kind == EngineControlPlaneKind.Podman)
        {
            return request.AuthorityMode switch
            {
                EngineAuthorityMode.Rootful =>
                [
                    Candidate(RootfulPodmanSocket, EngineControlPlaneKind.Podman, EngineApiKind.PodmanApi, EngineAuthorityMode.Rootful),
                ],
                EngineAuthorityMode.Mixed or EngineAuthorityMode.ProviderDefined =>
                [
                    Candidate(DefaultRootlessPodmanSocket, EngineControlPlaneKind.Podman, EngineApiKind.PodmanApi, EngineAuthorityMode.Rootless),
                    Candidate(RootfulPodmanSocket, EngineControlPlaneKind.Podman, EngineApiKind.PodmanApi, EngineAuthorityMode.Rootful),
                ],
                _ =>
                [
                    Candidate(DefaultRootlessPodmanSocket, EngineControlPlaneKind.Podman, EngineApiKind.PodmanApi, EngineAuthorityMode.Rootless),
                ],
            };
        }

        if (request.Api == EngineApiKind.BuildKitApi || request.Kind == EngineControlPlaneKind.BuildKit)
        {
            return request.AuthorityMode switch
            {
                EngineAuthorityMode.Rootless =>
                [
                    Candidate(DefaultRootlessBuildKitSocket, EngineControlPlaneKind.BuildKit, EngineApiKind.BuildKitApi, EngineAuthorityMode.Rootless),
                ],
                EngineAuthorityMode.Mixed or EngineAuthorityMode.ProviderDefined =>
                [
                    Candidate(DefaultRootlessBuildKitSocket, EngineControlPlaneKind.BuildKit, EngineApiKind.BuildKitApi, EngineAuthorityMode.Rootless),
                    Candidate(RootfulBuildKitSocket, EngineControlPlaneKind.BuildKit, EngineApiKind.BuildKitApi, EngineAuthorityMode.Rootful),
                ],
                _ =>
                [
                    Candidate(RootfulBuildKitSocket, EngineControlPlaneKind.BuildKit, EngineApiKind.BuildKitApi, EngineAuthorityMode.Rootful),
                ],
            };
        }

        return request.AuthorityMode switch
            {
                EngineAuthorityMode.Rootful =>
                [
                    Candidate(RootfulDockerSocket, EngineControlPlaneKind.DockerCompatible, EngineApiKind.DockerCompatible, EngineAuthorityMode.Rootful),
                    Candidate(AlternateRootfulDockerSocket, EngineControlPlaneKind.DockerCompatible, EngineApiKind.DockerCompatible, EngineAuthorityMode.Rootful),
                ],
                EngineAuthorityMode.Mixed or EngineAuthorityMode.ProviderDefined =>
                [
                    Candidate(DefaultRootlessDockerSocket, EngineControlPlaneKind.DockerCompatible, EngineApiKind.DockerCompatible, EngineAuthorityMode.Rootless),
                    Candidate(RootfulDockerSocket, EngineControlPlaneKind.DockerCompatible, EngineApiKind.DockerCompatible, EngineAuthorityMode.Rootful),
                    Candidate(ContainerdSocket, EngineControlPlaneKind.Containerd, EngineApiKind.ContainerdApi, EngineAuthorityMode.Rootful),
                ],
                _ =>
                [
                    Candidate(DefaultRootlessDockerSocket, EngineControlPlaneKind.DockerCompatible, EngineApiKind.DockerCompatible, EngineAuthorityMode.Rootless),
                ],
            };
    }

    public static AppleVirtualizationGuestAgentEngineStatus ToStatus(
        AppleVirtualizationGuestAgentEngineStatusRequest request,
        AppleVirtualizationGuestAgentEngineProbeObservation observation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observation);

        string? socketPath = observation.SocketPath?.Value;
        EngineApiKind api = ResolveApi(request.Api, socketPath);
        EngineControlPlaneKind kind = ResolveKind(request.Kind, api);
        EngineAuthorityMode authorityMode = ResolveAuthorityMode(request.AuthorityMode, socketPath, api);
        AppleVirtualizationEngineObservationState state = ObservationStateFor(observation);
        IReadOnlyList<Diagnostic> diagnostics = DiagnosticsFor(observation);

        return AppleVirtualizationGuestAgentEngineStatus.FromObservation(
            request.HostId,
            request.EngineId ?? "engine-" + kind,
            state,
            kind,
            api,
            authorityMode,
            request.ImageStore,
            request.WorkloadAdoption,
            request.MaxEndpoints,
            request.MaxContainers,
            BoundaryLocus.RuntimeHost,
            socketPath,
            ExtractBoundedLine(observation.VersionOutput, request.MaxVersionLength),
            ExtractBoundedLine(observation.StatusOutput, request.MaxStatusLength) ?? StatusFor(observation),
            request.IncludeContainers ? observation.Containers : null,
            diagnostics,
            request.MaxDiagnostics,
            request.MaxVersionLength,
            request.MaxStatusLength);
    }

    public static bool IsRootlessDockerSocket(string socketPath) =>
        socketPath.StartsWith(RootlessDockerSocketPrefix, StringComparison.Ordinal) &&
        socketPath.EndsWith(RootlessDockerSocketSuffix, StringComparison.Ordinal);

    public static bool IsRootfulDockerSocket(string socketPath) =>
        string.Equals(socketPath, RootfulDockerSocket, StringComparison.Ordinal) ||
        string.Equals(socketPath, AlternateRootfulDockerSocket, StringComparison.Ordinal);

    public static bool IsContainerdSocket(string socketPath) =>
        string.Equals(socketPath, ContainerdSocket, StringComparison.Ordinal) ||
        string.Equals(socketPath, AlternateContainerdSocket, StringComparison.Ordinal);

    public static bool IsRootlessPodmanSocket(string socketPath) =>
        string.Equals(socketPath, DefaultRootlessPodmanSocket, StringComparison.Ordinal) ||
        (socketPath.StartsWith(RootlessDockerSocketPrefix, StringComparison.Ordinal) &&
         socketPath.EndsWith("/podman/podman.sock", StringComparison.Ordinal));

    public static bool IsRootfulPodmanSocket(string socketPath) =>
        string.Equals(socketPath, RootfulPodmanSocket, StringComparison.Ordinal);

    public static bool IsRootlessBuildKitSocket(string socketPath) =>
        string.Equals(socketPath, DefaultRootlessBuildKitSocket, StringComparison.Ordinal) ||
        (socketPath.StartsWith(RootlessDockerSocketPrefix, StringComparison.Ordinal) &&
         socketPath.EndsWith("/buildkit-default/buildkitd.sock", StringComparison.Ordinal));

    public static bool IsRootfulBuildKitSocket(string socketPath) =>
        string.Equals(socketPath, RootfulBuildKitSocket, StringComparison.Ordinal);

    private static AppleVirtualizationGuestAgentEngineProbeCandidate Candidate(
        string path,
        EngineControlPlaneKind kind,
        EngineApiKind api,
        EngineAuthorityMode authorityMode) =>
        new()
        {
            SocketPath = new UnixSocketPath(path),
            Kind = kind,
            Api = api,
            AuthorityMode = authorityMode,
        };

    private static AppleVirtualizationEngineObservationState ObservationStateFor(
        AppleVirtualizationGuestAgentEngineProbeObservation observation) =>
        observation.Readiness switch
        {
            AppleVirtualizationGuestAgentEngineProbeReadiness.Ready
                when observation.SocketExists && observation.SocketAccessible => AppleVirtualizationEngineObservationState.Ready,
            AppleVirtualizationGuestAgentEngineProbeReadiness.Ready => AppleVirtualizationEngineObservationState.Degraded,
            AppleVirtualizationGuestAgentEngineProbeReadiness.Degraded => AppleVirtualizationEngineObservationState.Degraded,
            AppleVirtualizationGuestAgentEngineProbeReadiness.NotInstalled => AppleVirtualizationEngineObservationState.NotInstalled,
            AppleVirtualizationGuestAgentEngineProbeReadiness.Unavailable => AppleVirtualizationEngineObservationState.Failed,
            _ => AppleVirtualizationEngineObservationState.Unknown,
        };

    private static IReadOnlyList<Diagnostic> DiagnosticsFor(
        AppleVirtualizationGuestAgentEngineProbeObservation observation)
    {
        var diagnostics = new List<Diagnostic>(observation.Diagnostics.Count + 2);
        switch (observation.Issue)
        {
            case AppleVirtualizationGuestAgentEngineProbeIssue.SocketMissing:
                diagnostics.Add(Diagnostic(
                    DiagnosticSeverity.Warning,
                    "AppleVirtualization.EngineProbe.NotInstalled",
                    "No guest-visible Docker or containerd engine socket was observed inside the runtime host.",
                    "engine.probe.socket"));
                break;
            case AppleVirtualizationGuestAgentEngineProbeIssue.PermissionDenied:
                diagnostics.Add(Diagnostic(
                    DiagnosticSeverity.Warning,
                    "AppleVirtualization.EngineProbe.PermissionDenied",
                    "The guest engine socket exists, but the HPD guest agent could not access it.",
                    "engine.probe.socket"));
                break;
            case AppleVirtualizationGuestAgentEngineProbeIssue.SystemdMissing:
                diagnostics.Add(Diagnostic(
                    DiagnosticSeverity.Warning,
                    "AppleVirtualization.EngineProbe.SystemdMissing",
                    "The guest engine service could not be verified because systemd is unavailable inside the guest.",
                    "engine.probe.systemd"));
                break;
            case AppleVirtualizationGuestAgentEngineProbeIssue.TransportError:
                diagnostics.Add(Diagnostic(
                    DiagnosticSeverity.Error,
                    "AppleVirtualization.EngineProbe.TransportError",
                    "The guest-agent engine probe transport failed before engine status could be observed.",
                    "engine.probe.transport"));
                break;
            case AppleVirtualizationGuestAgentEngineProbeIssue.ProbeFailed:
                diagnostics.Add(Diagnostic(
                    DiagnosticSeverity.Error,
                    "AppleVirtualization.EngineProbe.Failed",
                    "The guest-agent engine probe failed while reading bounded engine status.",
                    "engine.probe"));
                break;
        }

        if (!observation.SystemdAvailable &&
            observation.Issue != AppleVirtualizationGuestAgentEngineProbeIssue.SystemdMissing)
        {
            diagnostics.Add(Diagnostic(
                DiagnosticSeverity.Warning,
                "AppleVirtualization.EngineProbe.SystemdMissing",
                "The guest engine service could not be verified because systemd is unavailable inside the guest.",
                "engine.probe.systemd"));
        }

        for (int index = 0; index < observation.Diagnostics.Count; index++)
        {
            diagnostics.Add(observation.Diagnostics[index]);
        }

        return diagnostics.Count == 0 ? Array.Empty<Diagnostic>() : diagnostics;
    }

    private static Diagnostic Diagnostic(DiagnosticSeverity severity, string code, string message, string targetPath) =>
        new()
        {
            Severity = severity,
            Code = new DiagnosticCode(code),
            Message = message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };

    private static string? ExtractBoundedLine(string? output, int maxLength)
    {
        if (string.IsNullOrEmpty(output))
        {
            return output;
        }

        int lineLength = 0;
        while (lineLength < output.Length && output[lineLength] is not '\r' and not '\n')
        {
            lineLength++;
        }

        int boundedLength = Math.Clamp(maxLength, 0, lineLength);
        return output[..boundedLength];
    }

    private static string StatusFor(AppleVirtualizationGuestAgentEngineProbeObservation observation) =>
        observation.Readiness switch
        {
            AppleVirtualizationGuestAgentEngineProbeReadiness.Ready => "guest engine socket and service are ready inside the runtime host",
            AppleVirtualizationGuestAgentEngineProbeReadiness.Degraded => "guest engine is present but degraded inside the runtime host",
            AppleVirtualizationGuestAgentEngineProbeReadiness.NotInstalled => "guest engine is not installed inside the runtime host",
            AppleVirtualizationGuestAgentEngineProbeReadiness.Unavailable => "guest engine probe is unavailable inside the runtime host",
            _ => "guest engine probe status is unknown",
        };

    private static EngineApiKind ResolveApi(EngineApiKind requestedApi, string? socketPath)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
        {
            return requestedApi;
        }

        return IsContainerdSocket(socketPath) || socketPath.Contains("containerd", StringComparison.OrdinalIgnoreCase)
            ? EngineApiKind.ContainerdApi
            : socketPath.EndsWith("/docker.sock", StringComparison.OrdinalIgnoreCase)
                ? EngineApiKind.DockerCompatible
                : requestedApi;
    }

    private static EngineControlPlaneKind ResolveKind(EngineControlPlaneKind requestedKind, EngineApiKind api) =>
        api == EngineApiKind.ContainerdApi ? EngineControlPlaneKind.Containerd :
        api == EngineApiKind.DockerCompatible ? EngineControlPlaneKind.DockerCompatible :
        requestedKind;

    private static EngineAuthorityMode ResolveAuthorityMode(
        EngineAuthorityMode requestedMode,
        string? socketPath,
        EngineApiKind api)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
        {
            return requestedMode;
        }

        if (IsRootlessDockerSocket(socketPath))
        {
            return EngineAuthorityMode.Rootless;
        }

        if (IsRootfulDockerSocket(socketPath) ||
            IsContainerdSocket(socketPath) ||
            api == EngineApiKind.ContainerdApi)
        {
            return EngineAuthorityMode.Rootful;
        }

        return requestedMode;
    }
}

public sealed record AppleVirtualizationGuestAgentEngineProvisioningRequest
{
    public required string HostId { get; init; }
    public string? EngineId { get; init; }
    public EngineControlPlaneKind Kind { get; init; } = EngineControlPlaneKind.DockerCompatible;
    public EngineApiKind Api { get; init; } = EngineApiKind.DockerCompatible;
    public EngineAuthorityMode AuthorityMode { get; init; } = EngineAuthorityMode.Rootless;
    public EngineImageStoreMode ImageStore { get; init; } = EngineImageStoreMode.ProviderManaged;
    public EngineWorkloadAdoptionMode WorkloadAdoption { get; init; } = EngineWorkloadAdoptionMode.None;
    public bool AllowPackageInstall { get; init; }
    public bool AllowServiceEnablement { get; init; }
    public int ProvisioningTimeoutMilliseconds { get; init; } = 120_000;
    public int MaxCapturedOutputBytes { get; init; } = AppleVirtualizationEngineProvisioningOptions.DefaultMaxCapturedOutputBytes;
    public AppleVirtualizationEngineProvisioningPackageManager PackageManager { get; init; } =
        AppleVirtualizationEngineProvisioningPackageManager.Auto;
    public AppleVirtualizationEngineProvisioningExecutionState ScriptedExecutionState { get; init; } =
        AppleVirtualizationEngineProvisioningExecutionState.NotRequested;
    public AppleVirtualizationEngineProvisioningPrerequisiteStatus ScriptedPrerequisites { get; init; } =
        AppleVirtualizationEngineProvisioningPrerequisiteStatus.Supported;
    public string? ScriptedOutput { get; init; }
    public string? ScriptedStdout { get; init; }
    public string? ScriptedStderr { get; init; }
}

public sealed record AppleVirtualizationGuestAgentEngineProvisioningResult
{
    public required string HostId { get; init; }
    public string? EngineId { get; init; }
    public AppleVirtualizationEngineProvisioningPhase Phase { get; init; } =
        AppleVirtualizationEngineProvisioningPhase.NotRequested;
    public EngineControlPlaneKind Kind { get; init; } = EngineControlPlaneKind.DockerCompatible;
    public EngineApiKind Api { get; init; } = EngineApiKind.DockerCompatible;
    public EngineAuthorityMode AuthorityMode { get; init; } = EngineAuthorityMode.Rootless;
    public EngineImageStoreMode ImageStore { get; init; } = EngineImageStoreMode.ProviderManaged;
    public bool ExistingEngineObserved { get; init; }
    public bool InstallAttempted { get; init; }
    public string? GuestSocketPath { get; init; }
    public AppleVirtualizationEngineProvisioningPrerequisiteStatus Prerequisites { get; init; } =
        AppleVirtualizationEngineProvisioningPrerequisiteStatus.Supported;
    public IReadOnlyList<AppleVirtualizationEngineProvisioningPlanStep> Plan { get; init; } =
        Array.Empty<AppleVirtualizationEngineProvisioningPlanStep>();
    public AppleVirtualizationEngineProvisioningOutputCapture Output { get; init; } = new();
    public AppleVirtualizationEngineProvisioningEvidence Evidence { get; init; } = new();
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

public interface IAppleVirtualizationGuestAgentEngineProvisioner
{
    AppleVirtualizationGuestAgentEngineProvisioningResult Provision(
        AppleVirtualizationGuestAgentEngineProvisioningRequest request);
}

public sealed class AppleVirtualizationGuestAgentEngineProvisioningExecutor :
    IAppleVirtualizationGuestAgentEngineProvisioner
{
    public AppleVirtualizationGuestAgentEngineProvisioningResult Provision(
        AppleVirtualizationGuestAgentEngineProvisioningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return AppleVirtualizationEngineProvisioningPlanner.PlanGuest(request);
    }
}

public sealed record AppleVirtualizationGuestAgentEngineStatus
{
    public required string HostId { get; init; }
    public required string EngineId { get; init; }
    public AppleVirtualizationEngineObservationState ObservationState { get; init; } = AppleVirtualizationEngineObservationState.Unknown;
    public EngineControlPlaneKind Kind { get; init; } = EngineControlPlaneKind.DockerCompatible;
    public EngineApiKind Api { get; init; } = EngineApiKind.DockerCompatible;
    public EngineAuthorityMode AuthorityMode { get; init; } = EngineAuthorityMode.Rootless;
    public EngineImageStoreMode ImageStore { get; init; } = EngineImageStoreMode.ProviderManaged;
    public EngineWorkloadAdoptionMode WorkloadAdoption { get; init; } = EngineWorkloadAdoptionMode.None;
    public EngineControlPlanePhase EnginePhase { get; init; } = EngineControlPlanePhase.Pending;
    public ResourcePhase Phase { get; init; } = ResourcePhase.Pending;
    public bool Installed { get; init; }
    public bool Running { get; init; }
    public bool Ready { get; init; }
    public string? Version { get; init; }
    public string? Status { get; init; }
    public IReadOnlyList<AppleVirtualizationGuestAgentEngineApiEndpoint> Endpoints { get; init; } = Array.Empty<AppleVirtualizationGuestAgentEngineApiEndpoint>();
    public IReadOnlyList<AppleVirtualizationGuestAgentContainerObservation> Containers { get; init; } = Array.Empty<AppleVirtualizationGuestAgentContainerObservation>();
    public bool EndpointsTruncated { get; init; }
    public bool ContainersTruncated { get; init; }
    public bool DiagnosticsTruncated { get; init; }
    public AppleVirtualizationGuestAgentEngineGenerationStamp Generation { get; init; } = new();
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();

    public static AppleVirtualizationGuestAgentEngineStatus FromRequest(
        AppleVirtualizationGuestAgentEngineStatusRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        AppleVirtualizationGuestAgentEngineStatus status = FromObservation(
            request.HostId,
            request.EngineId ?? "engine-" + request.Kind,
            request.ScriptedObservationState ?? AppleVirtualizationEngineObservationState.NotInstalled,
            request.Kind,
            request.Api,
            request.AuthorityMode,
            request.ImageStore,
            request.WorkloadAdoption,
            request.MaxEndpoints,
            request.MaxContainers,
            request.ObservationLocus,
            request.ObservedSocketPath,
            request.ObservedVersion,
            request.ObservedStatus,
            request.IncludeContainers ? request.ObservedContainers : null,
            request.ObservedDiagnostics,
            request.MaxDiagnostics,
            request.MaxVersionLength,
            request.MaxStatusLength);
        return status with
        {
            Generation = status.Generation with
            {
                ProviderGeneration = request.ProviderGeneration,
                HostStartGeneration = request.HostStartGeneration,
            },
        };
    }

    public static AppleVirtualizationGuestAgentEngineStatus FromObservation(
        string hostId,
        string engineId,
        AppleVirtualizationEngineObservationState state,
        EngineControlPlaneKind kind,
        EngineApiKind api,
        EngineAuthorityMode authorityMode,
        EngineImageStoreMode imageStore,
        EngineWorkloadAdoptionMode workloadAdoption,
        int maxEndpoints,
        int maxContainers,
        BoundaryLocus observationLocus = BoundaryLocus.RuntimeHost,
        string? observedSocketPath = null,
        string? observedVersion = null,
        string? observedStatus = null,
        IReadOnlyList<AppleVirtualizationGuestAgentContainerObservation>? observedContainers = null,
        IReadOnlyList<Diagnostic>? observedDiagnostics = null,
        int maxDiagnostics = AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxDiagnostics,
        int maxVersionLength = AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxVersionLength,
        int maxStatusLength = AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxStatusLength)
    {
        EngineApiKind observedApi = ResolveApi(api, observedSocketPath);
        EngineControlPlaneKind observedKind = ResolveKind(kind, observedApi);
        EngineAuthorityMode observedAuthorityMode = ResolveAuthorityMode(authorityMode, observedSocketPath, observedApi);
        bool hostSocketRejected = observationLocus == BoundaryLocus.Host && IsEngineAuthorityObservation(observedKind, observedApi, observedSocketPath);
        AppleVirtualizationEngineObservationState effectiveState = hostSocketRejected
            ? AppleVirtualizationEngineObservationState.Unsupported
            : state;
        bool ready = !hostSocketRejected && effectiveState == AppleVirtualizationEngineObservationState.Ready;
        bool degraded = effectiveState == AppleVirtualizationEngineObservationState.Degraded;
        bool installed = ready || degraded ||
            effectiveState is AppleVirtualizationEngineObservationState.Installed or AppleVirtualizationEngineObservationState.Starting;
        IReadOnlyList<AppleVirtualizationGuestAgentEngineApiEndpoint> endpoints =
            AppleVirtualizationGuestAgentEngineResultBounds.Bound(
                ready || degraded
                    ? [DefaultEndpoint(observedKind, observedApi, observedAuthorityMode, observedSocketPath)]
                    : Array.Empty<AppleVirtualizationGuestAgentEngineApiEndpoint>(),
                maxEndpoints,
                out bool endpointsTruncated);
        IReadOnlyList<AppleVirtualizationGuestAgentContainerObservation> containers =
            AppleVirtualizationGuestAgentEngineResultBounds.Bound(
                observedContainers,
                maxContainers,
                out bool containersTruncated);
        IReadOnlyList<Diagnostic> diagnostics =
            AppleVirtualizationGuestAgentEngineResultBounds.Bound(
                DiagnosticsFor(effectiveState, engineId, hostSocketRejected, observedDiagnostics),
                maxDiagnostics,
                out bool diagnosticsTruncated);

        return new AppleVirtualizationGuestAgentEngineStatus
        {
            HostId = hostId,
            EngineId = engineId,
            ObservationState = effectiveState,
            Kind = observedKind,
            Api = observedApi,
            AuthorityMode = observedAuthorityMode,
            ImageStore = imageStore,
            WorkloadAdoption = workloadAdoption,
            EnginePhase = EnginePhaseFor(effectiveState),
            Phase = ResourcePhaseFor(effectiveState),
            Installed = installed,
            Running = ready || degraded,
            Ready = ready,
            Version = AppleVirtualizationGuestAgentEngineResultBounds.BoundString(
                ready ? observedVersion ?? "fake-engine-0.1" : observedVersion,
                maxVersionLength),
            Status = AppleVirtualizationGuestAgentEngineResultBounds.BoundString(
                observedStatus ?? EngineMessageFor(effectiveState),
                maxStatusLength),
            Endpoints = endpoints,
            EndpointsTruncated = endpointsTruncated,
            Containers = containers,
            ContainersTruncated = containersTruncated,
            DiagnosticsTruncated = diagnosticsTruncated,
            Conditions =
            [
                new Condition(
                    "AppleVirtualization.EngineObserved",
                    ready ? ConditionStatus.True : ConditionStatus.False,
                    effectiveState.ToString(),
                    EngineMessageFor(effectiveState),
                    DateTimeOffset.UtcNow,
                    default,
                    ready ? DiagnosticSeverity.Info : diagnostics.Any(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Error)
                        ? DiagnosticSeverity.Error
                        : DiagnosticSeverity.Warning),
            ],
            Diagnostics = diagnostics,
            Generation = new AppleVirtualizationGuestAgentEngineGenerationStamp(
                GuestBootId: "boot-1",
                GuestBootGeneration: 1,
                GuestAgentGeneration: 1,
                EngineGeneration: ready ? 1UL : 0UL),
        };
    }

    private static AppleVirtualizationGuestAgentEngineApiEndpoint DefaultEndpoint(
        EngineControlPlaneKind kind,
        EngineApiKind api,
        EngineAuthorityMode authorityMode,
        string? observedSocketPath)
    {
        string socketPath = !string.IsNullOrWhiteSpace(observedSocketPath)
            ? observedSocketPath
            : api switch
        {
            EngineApiKind.ContainerdApi => "/run/containerd/containerd.sock",
            EngineApiKind.PodmanApi when authorityMode == EngineAuthorityMode.Rootful => "/run/podman/podman.sock",
            EngineApiKind.PodmanApi => "/run/user/1000/podman/podman.sock",
            EngineApiKind.BuildKitApi when authorityMode == EngineAuthorityMode.Rootless => "/run/user/1000/buildkit-default/buildkitd.sock",
            EngineApiKind.BuildKitApi => "/run/buildkit/buildkitd.sock",
            _ when authorityMode == EngineAuthorityMode.Rootful => "/var/run/docker.sock",
            _ => "/run/user/1000/docker.sock",
        };

        SensitiveAuthorityClass authorityClass = authorityMode == EngineAuthorityMode.Rootful
            ? SensitiveAuthorityClass.RootfulEngineControl
            : SensitiveAuthorityClass.RootlessEngineControl;

        return new AppleVirtualizationGuestAgentEngineApiEndpoint
        {
            Name = kind.ToString(),
            Api = api,
            Transport = NetworkTransport.UnixStream,
            SocketPath = new UnixSocketPath(socketPath),
            GuestVisibleOnly = true,
            HpdPublished = false,
            RequiresAuthorityBinding = true,
            SensitivePolicy = new SensitiveEndpointPolicy
            {
                Kind = SensitiveEndpointKind.EngineSocket,
                AuthorityClass = authorityClass,
                Redaction = SensitiveRedactionLevel.RedactIdentifiers,
                RequireAudit = true,
            },
        };
    }

    private static EngineControlPlanePhase EnginePhaseFor(AppleVirtualizationEngineObservationState state) =>
        state switch
        {
            AppleVirtualizationEngineObservationState.Ready => EngineControlPlanePhase.Ready,
            AppleVirtualizationEngineObservationState.Starting => EngineControlPlanePhase.Starting,
            AppleVirtualizationEngineObservationState.Degraded => EngineControlPlanePhase.Degraded,
            AppleVirtualizationEngineObservationState.Failed => EngineControlPlanePhase.Failed,
            AppleVirtualizationEngineObservationState.Installed => EngineControlPlanePhase.Stopped,
            AppleVirtualizationEngineObservationState.Unsupported => EngineControlPlanePhase.Failed,
            _ => EngineControlPlanePhase.Pending,
        };

    private static ResourcePhase ResourcePhaseFor(AppleVirtualizationEngineObservationState state) =>
        state switch
        {
            AppleVirtualizationEngineObservationState.Ready => ResourcePhase.Ready,
            AppleVirtualizationEngineObservationState.Degraded => ResourcePhase.Degraded,
            AppleVirtualizationEngineObservationState.Failed => ResourcePhase.Failed,
            AppleVirtualizationEngineObservationState.Unsupported => ResourcePhase.Failed,
            AppleVirtualizationEngineObservationState.Starting => ResourcePhase.Reconciling,
            _ => ResourcePhase.Pending,
        };

    private static string EngineMessageFor(AppleVirtualizationEngineObservationState state) =>
        state switch
        {
            AppleVirtualizationEngineObservationState.Ready => "Guest engine API is ready inside the VM and requires authority binding for access.",
            AppleVirtualizationEngineObservationState.Degraded => "Guest engine is present but degraded; API access remains authority-bound.",
            AppleVirtualizationEngineObservationState.NotInstalled => "Guest engine is not installed or not configured.",
            AppleVirtualizationEngineObservationState.RequiresConfiguration => "Guest engine observation requires explicit opt-in configuration.",
            AppleVirtualizationEngineObservationState.Unsupported => "Guest engine status cannot be satisfied by host engine sockets.",
            _ => "Guest engine status was observed.",
        };

    private static Diagnostic DiagnosticFor(AppleVirtualizationEngineObservationState state, string engineId) =>
        new()
        {
            Severity = state == AppleVirtualizationEngineObservationState.Failed ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            Code = new DiagnosticCode("AppleVirtualization.Engine" + state),
            Message = EngineMessageFor(state),
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = engineId,
        };

    private static IReadOnlyList<Diagnostic> DiagnosticsFor(
        AppleVirtualizationEngineObservationState state,
        string engineId,
        bool hostSocketRejected,
        IReadOnlyList<Diagnostic>? observedDiagnostics)
    {
        var diagnostics = new List<Diagnostic>();
        if (state is AppleVirtualizationEngineObservationState.Degraded or AppleVirtualizationEngineObservationState.Failed)
        {
            diagnostics.Add(DiagnosticFor(state, engineId));
        }

        if (hostSocketRejected)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Code = new DiagnosticCode("AppleVirtualization.EngineStatusHostSocketRejected"),
                Message = "Host Docker, Podman, containerd, and BuildKit sockets cannot satisfy Apple Virtualization engine status; status must be observed inside the guest.",
                ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                TargetPath = "engine.status.socket",
            });
        }

        if (observedDiagnostics is { Count: > 0 })
        {
            diagnostics.AddRange(observedDiagnostics);
        }

        return diagnostics.Count == 0 ? Array.Empty<Diagnostic>() : diagnostics;
    }

    private static EngineApiKind ResolveApi(EngineApiKind requestedApi, string? socketPath)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
        {
            return requestedApi;
        }

        if (socketPath.Contains("containerd", StringComparison.OrdinalIgnoreCase))
        {
            return EngineApiKind.ContainerdApi;
        }

        if (socketPath.Contains("podman", StringComparison.OrdinalIgnoreCase))
        {
            return EngineApiKind.PodmanApi;
        }

        if (socketPath.Contains("buildkit", StringComparison.OrdinalIgnoreCase))
        {
            return EngineApiKind.BuildKitApi;
        }

        if (socketPath.EndsWith("/docker.sock", StringComparison.OrdinalIgnoreCase))
        {
            return EngineApiKind.DockerCompatible;
        }

        return requestedApi;
    }

    private static EngineControlPlaneKind ResolveKind(EngineControlPlaneKind requestedKind, EngineApiKind api) =>
        api switch
        {
            EngineApiKind.ContainerdApi => EngineControlPlaneKind.Containerd,
            EngineApiKind.PodmanApi => EngineControlPlaneKind.Podman,
            EngineApiKind.BuildKitApi => EngineControlPlaneKind.BuildKit,
            EngineApiKind.DockerCompatible => EngineControlPlaneKind.DockerCompatible,
            _ => requestedKind,
        };

    private static EngineAuthorityMode ResolveAuthorityMode(
        EngineAuthorityMode requestedMode,
        string? socketPath,
        EngineApiKind api)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
        {
            return requestedMode;
        }

        if (socketPath.StartsWith("/run/user/", StringComparison.OrdinalIgnoreCase))
        {
            return EngineAuthorityMode.Rootless;
        }

        if (socketPath is "/var/run/docker.sock" or "/run/docker.sock" ||
            string.Equals(socketPath, "/run/podman/podman.sock", StringComparison.OrdinalIgnoreCase) ||
            socketPath.StartsWith("/run/containerd/", StringComparison.OrdinalIgnoreCase) ||
            socketPath.StartsWith("/var/run/containerd/", StringComparison.OrdinalIgnoreCase) ||
            socketPath.StartsWith("/run/buildkit/", StringComparison.OrdinalIgnoreCase) ||
            api is EngineApiKind.ContainerdApi or EngineApiKind.BuildKitApi)
        {
            return EngineAuthorityMode.Rootful;
        }

        return requestedMode;
    }

    private static bool IsEngineAuthorityObservation(
        EngineControlPlaneKind kind,
        EngineApiKind api,
        string? socketPath) =>
        kind is EngineControlPlaneKind.DockerCompatible or EngineControlPlaneKind.Podman or EngineControlPlaneKind.Containerd or EngineControlPlaneKind.BuildKit ||
        api is EngineApiKind.DockerCompatible or EngineApiKind.PodmanApi or EngineApiKind.ContainerdApi or EngineApiKind.BuildKitApi ||
        (!string.IsNullOrWhiteSpace(socketPath) &&
            (socketPath.Contains("docker.sock", StringComparison.OrdinalIgnoreCase) ||
             socketPath.Contains("podman", StringComparison.OrdinalIgnoreCase) ||
             socketPath.Contains("containerd", StringComparison.OrdinalIgnoreCase) ||
             socketPath.Contains("buildkit", StringComparison.OrdinalIgnoreCase)));
}

public sealed record AppleVirtualizationGuestAgentEngineApiEndpoint
{
    public required string Name { get; init; }
    public EngineApiKind Api { get; init; } = EngineApiKind.ProviderDefined;
    public NetworkTransport Transport { get; init; } = NetworkTransport.UnixStream;
    public UnixSocketPath? SocketPath { get; init; }
    public string? Address { get; init; }
    public NetworkPort? Port { get; init; }
    public bool GuestVisibleOnly { get; init; } = true;
    public bool HpdPublished { get; init; }
    public bool RequiresAuthorityBinding { get; init; } = true;
    public string? AuthorityBindingId { get; init; }
    public SensitiveEndpointPolicy SensitivePolicy { get; init; } = new()
    {
        Kind = SensitiveEndpointKind.EngineSocket,
        AuthorityClass = SensitiveAuthorityClass.RootlessEngineControl,
        Redaction = SensitiveRedactionLevel.RedactIdentifiers,
        RequireAudit = true,
    };
}

public sealed record AppleVirtualizationGuestAgentContainerObservation
{
    public required string ContainerId { get; init; }
    public string? Name { get; init; }
    public ResourcePhase Phase { get; init; } = ResourcePhase.Unknown;
    public bool HpdOwned { get; init; }
    public string? ImageRef { get; init; }
}

public static class AppleVirtualizationGuestAgentEngineResultBounds
{
    public const int DefaultMaxEndpoints = 8;
    public const int DefaultMaxContainers = 32;
    public const int DefaultMaxDiagnostics = 16;
    public const int DefaultMaxVersionLength = 128;
    public const int DefaultMaxStatusLength = 512;

    public static IReadOnlyList<T> Bound<T>(IReadOnlyList<T>? items, int limit, out bool truncated)
    {
        if (items is null || items.Count == 0)
        {
            truncated = false;
            return Array.Empty<T>();
        }

        int boundedLimit = Math.Clamp(limit, 0, items.Count);
        truncated = items.Count > boundedLimit;
        if (!truncated)
        {
            return items;
        }

        var bounded = new T[boundedLimit];
        for (int index = 0; index < boundedLimit; index++)
        {
            bounded[index] = items[index];
        }

        return bounded;
    }

    public static string? BoundString(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        int boundedLength = Math.Clamp(maxLength, 0, value.Length);
        return value.Length <= boundedLength ? value : value[..boundedLength];
    }
}

public sealed record AppleVirtualizationGuestAgentAuthorityGenerationStamp(ulong ProviderGeneration = 0, ulong HostStartGeneration = 0, string? GuestBootId = null, ulong GuestBootGeneration = 0, ulong GuestAgentGeneration = 0, ulong BindingGeneration = 0);

public sealed record AppleVirtualizationGuestAgentAuthoritySource
{
    public AuthoritySourceKind Kind { get; init; } = AuthoritySourceKind.ProviderDefined;
    public BoundaryLocus Locus { get; init; } = BoundaryLocus.Host;
    public HostServiceKind? HostService { get; init; }
    public UnixSocketPath? SocketPath { get; init; }
    public CredentialRef? Credential { get; init; }
    public SensitiveEndpointKind? SensitiveEndpointKind { get; init; }
    public SensitiveAuthorityClass AuthorityClass { get; init; } = SensitiveAuthorityClass.ProviderDefined;
    public string? RedactedDisplayName { get; init; }
}

public sealed record AppleVirtualizationGuestAgentAuthorityTarget
{
    public AuthorityTargetKind Kind { get; init; } = AuthorityTargetKind.ProviderDefined;
    public string? UnitId { get; init; }
    public string? ProcessId { get; init; }
    public string? ServiceName { get; init; }
    public BoundaryLocus Locus { get; init; } = BoundaryLocus.ExecutionUnit;
}

public sealed record AppleVirtualizationGuestAgentAuthorityProjection
{
    public AuthorityProjectionKind Kind { get; init; } = AuthorityProjectionKind.ProviderDefined;
    public UnixSocketPath? TargetSocketPath { get; init; }
    public string? EnvironmentVariableName { get; init; }
    public UnixSocketPermissions? SocketPermissions { get; init; }
    public bool ReadOnly { get; init; } = true;
}

public sealed record AppleVirtualizationGuestAgentAuthorityLease
{
    public BindingLifetime Lifetime { get; init; } = BindingLifetime.ExecutionUnit;
    public DateTimeOffset BoundAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool RevokeOnTargetStop { get; init; } = true;
    public bool SurviveTargetRestart { get; init; }
}

public sealed record AppleVirtualizationGuestAgentAuthorityProjectionRequest
{
    public required string BindingId { get; init; }
    public AppleVirtualizationGuestAgentAuthoritySource Source { get; init; } = new();
    public AppleVirtualizationGuestAgentAuthorityTarget Target { get; init; } = new();
    public AppleVirtualizationGuestAgentAuthorityProjection Projection { get; init; } = new();
    public AuthorityBindingDirection Direction { get; init; } = AuthorityBindingDirection.HostToGuest;
    public SensitiveAuthorityClass EffectiveAuthorityClass { get; init; } = SensitiveAuthorityClass.ProviderDefined;
    public SensitiveRedactionLevel Redaction { get; init; } = SensitiveRedactionLevel.RedactSecretValues;
    public bool RequireAudit { get; init; } = true;
    public string? AuditCorrelationId { get; init; }
    public AppleVirtualizationGuestAgentAuthorityLease Lease { get; init; } = new();
    public AppleVirtualizationGuestAgentAuthorityGenerationStamp Generation { get; init; } = new();
}

public sealed record AppleVirtualizationGuestAgentAuthorityStatusRequest(string BindingId);
public sealed record AppleVirtualizationGuestAgentAuthorityRevocationRequest(string BindingId, string? Reason = null, bool Verify = true, AppleVirtualizationGuestAgentAuthorityGenerationStamp? Generation = null);

public sealed record AppleVirtualizationGuestAgentBoundAuthority
{
    public required string BindingId { get; init; }
    public AuthoritySourceKind SourceKind { get; init; }
    public AuthorityProjectionKind ProjectionKind { get; init; }
    public AuthorityBindingDirection Direction { get; init; }
    public SensitiveAuthorityClass EffectiveAuthorityClass { get; init; }
    public SensitiveRedactionLevel Redaction { get; init; } = SensitiveRedactionLevel.RedactSecretValues;
    public UnixSocketPath? TargetSocketPath { get; init; }
    public string? EnvironmentVariableName { get; init; }
    public HostFunctionName? HostFunctionName { get; init; }
    public DateTimeOffset BoundAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public ulong RotationGeneration { get; init; }
    public RevocationVerificationStatus RevocationStatus { get; init; }
    public string? AuditCorrelationId { get; init; }
    public SensitiveEndpointKind? SensitiveEndpointKind { get; init; }
    public AppleVirtualizationGuestAgentAuthorityGenerationStamp Generation { get; init; } = new();
}

public sealed record AppleVirtualizationGuestAgentAuthorityStatus
{
    public required string BindingId { get; init; }
    public AuthorityBindingPhase BindingPhase { get; init; } = AuthorityBindingPhase.Pending;
    public AppleVirtualizationGuestAgentBoundAuthority? BoundAuthority { get; init; }
    public IReadOnlyList<AuthorityAuditEvent> AuditEvents { get; init; } = Array.Empty<AuthorityAuditEvent>();
    public bool AuditEventsTruncated { get; init; }
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

public sealed record AppleVirtualizationGuestAgentAuthorityRevocationResult
{
    public required string BindingId { get; init; }
    public bool Accepted { get; init; }
    public RevocationVerificationStatus RevocationStatus { get; init; } = RevocationVerificationStatus.Unknown;
    public string? Reason { get; init; }
    public IReadOnlyList<AuthorityAuditEvent> AuditEvents { get; init; } = Array.Empty<AuthorityAuditEvent>();
    public IReadOnlyList<Condition> Conditions { get; init; } = Array.Empty<Condition>();
}

public sealed record AppleVirtualizationGuestAgentFrameResult
{
    public AppleVirtualizationGuestAgentEnvelope? Envelope { get; init; }
    public AppleVirtualizationGuestAgentError? Error { get; init; }
    public bool IsMalformed => Error is not null;
}

public static class AppleVirtualizationGuestAgentJsonCodec
{
    public static byte[] Encode(AppleVirtualizationGuestAgentEnvelope envelope) =>
        JsonSerializer.SerializeToUtf8Bytes(envelope, AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentEnvelope);

    public static AppleVirtualizationGuestAgentEnvelope Decode(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize(utf8Json, AppleVirtualizationJsonContext.Default.AppleVirtualizationGuestAgentEnvelope)
        ?? throw new JsonException("Decoded Apple Virtualization guest-agent envelope was null.");

    public static AppleVirtualizationGuestAgentFrameResult DecodeFrame(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            return new AppleVirtualizationGuestAgentFrameResult { Envelope = Decode(utf8Json) };
        }
        catch (JsonException ex)
        {
            return new AppleVirtualizationGuestAgentFrameResult
            {
                Error = new AppleVirtualizationGuestAgentError
                {
                    Code = "AppleVirtualization.GuestAgentMalformedFrame",
                    Message = "Guest-agent frame could not be decoded as a protocol envelope.",
                    Operation = "guest-agent.frame.decode",
                    FailedPhase = "Decode",
                    Retryable = true,
                    Severity = DiagnosticSeverity.Error,
                    Detail = System.Text.Encoding.UTF8.GetBytes(ex.GetType().Name),
                    DetailSchema = AppleVirtualizationGuestAgentProtocol.ErrorSchema,
                },
            };
        }
    }

    public static AppleVirtualizationGuestAgentError Unsupported(string operation, string message) =>
        new()
        {
            Code = "AppleVirtualization.GuestAgentUnsupported",
            Message = message,
            Operation = operation,
            FailedPhase = "Dispatch",
            Retryable = false,
            Severity = DiagnosticSeverity.Error,
        };
}

public sealed class FakeAppleVirtualizationGuestAgentToolHarness
{
    private readonly ConcurrentQueue<AppleVirtualizationGuestAgentEnvelope> _responses = new();
    private readonly ConcurrentQueue<AppleVirtualizationGuestAgentEnvelope> _events = new();
    private readonly Dictionary<string, BoundedCapture> _captures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AppleVirtualizationGuestAgentProjectionGenerationStamp> _verifiedProjections = new(StringComparer.Ordinal);
    private readonly int _maxEvents;
    private IAppleVirtualizationGuestAgentEngineProbe? _engineProbe;
    private IAppleVirtualizationGuestAgentEngineProvisioner _engineProvisioner =
        new AppleVirtualizationGuestAgentEngineProvisioningExecutor();
    private long _sequence;
    private bool _ptyResizeSupported;

    public FakeAppleVirtualizationGuestAgentToolHarness(int maxEvents = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEvents);
        _maxEvents = maxEvents;
    }

    public IReadOnlyList<AppleVirtualizationGuestAgentEnvelope> Requests => _requests;
    private readonly List<AppleVirtualizationGuestAgentEnvelope> _requests = [];

    public FakeAppleVirtualizationGuestAgentToolHarness WithEngineProbe(IAppleVirtualizationGuestAgentEngineProbe probe)
    {
        _engineProbe = probe ?? throw new ArgumentNullException(nameof(probe));
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithEngineProvisioner(
        IAppleVirtualizationGuestAgentEngineProvisioner provisioner)
    {
        _engineProvisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithHandshake(
        string guestBootId = "guest-boot-1",
        ulong guestBootGeneration = 1,
        ulong guestAgentGeneration = 1,
        bool ptyResizeSupported = false,
        string agentVersion = "0.1.0-test",
        string protocolVersion = AppleVirtualizationGuestAgentProtocol.CurrentVersion)
    {
        _ptyResizeSupported = ptyResizeSupported;
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.Hello) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.HelloSchema,
            GuestBootId = guestBootId,
            GuestBootGeneration = guestBootGeneration,
            GuestAgentGeneration = guestAgentGeneration,
            Hello = new AppleVirtualizationGuestAgentHello
            {
                AgentVersion = agentVersion,
                ProtocolVersion = protocolVersion,
                GuestBootId = guestBootId,
                GuestBootGeneration = guestBootGeneration,
                GuestAgentGeneration = guestAgentGeneration,
                Hostname = "hpd-guest",
                RuntimeUser = "hpd",
                Capabilities = new AppleVirtualizationGuestAgentCapabilities
                {
                    Pty = ptyResizeSupported,
                    ProcessResize = ptyResizeSupported,
                    ProjectionSync = true,
                    ProjectionFinalize = true,
                    ProjectionPromote = true,
                    AuthorityProjection = true,
                    AuthorityRevocation = true,
                    EngineStatus = true,
                    EngineProvisioning = true,
                },
            },
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithWrongProtocolVersion(string protocolVersion = "0.0")
    {
        WithHandshake(protocolVersion: protocolVersion);
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithWrongAgentVersion(string agentVersion = "wrong-version")
    {
        WithHandshake(agentVersion: agentVersion);
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithMissingCapability(string capability = "process.start")
    {
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.Capabilities) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.HelloSchema,
            Capabilities = new AppleVirtualizationGuestAgentCapabilities
            {
                ProcessStart = capability != "process.start",
                ProjectionMount = capability != "projection.mount",
                ProcessReadOutput = capability != "process.readOutput",
                Limitations = ["missing:" + capability],
            },
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithGuestAgentError(string code = "AppleVirtualization.GuestAgentScriptedError")
    {
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.Ready) with
        {
            ResponseStatus = AppleVirtualizationGuestAgentResponseStatus.Error,
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ErrorSchema,
            Error = new AppleVirtualizationGuestAgentError
            {
                Code = code,
                Message = "Scripted fake guest-agent error.",
                Operation = "guest.ready",
                FailedPhase = "Ready",
                Retryable = true,
            },
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithDisconnectDuringHandshake()
    {
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.Health) with
        {
            ResponseStatus = AppleVirtualizationGuestAgentResponseStatus.Error,
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ErrorSchema,
            Error = new AppleVirtualizationGuestAgentError
            {
                Code = "AppleVirtualization.GuestAgentDisconnected",
                Message = "Scripted fake guest-agent disconnect during handshake.",
                Operation = "guest.health",
                FailedPhase = "Handshake",
                Retryable = true,
            },
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithReady(
        bool ready = true,
        string guestBootId = "guest-boot-1",
        ulong guestBootGeneration = 1,
        ulong guestAgentGeneration = 1)
    {
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.Ready) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ReadySchema,
            GuestBootId = guestBootId,
            GuestBootGeneration = guestBootGeneration,
            GuestAgentGeneration = guestAgentGeneration,
            Ready = new AppleVirtualizationGuestAgentReady
            {
                IsReady = ready,
                GuestBootId = guestBootId,
                GuestBootGeneration = guestBootGeneration,
                GuestAgentGeneration = guestAgentGeneration,
                Conditions =
                [
                    new Condition("AppleVirtualization.GuestAgentReady", ready ? ConditionStatus.True : ConditionStatus.False, ready ? "Ready" : "NotReady", ready ? "Guest agent is ready." : "Guest agent is not ready.", DateTimeOffset.UtcNow, default, ready ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning),
                ],
            },
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithProjectionMount(
        string projectionId,
        string tag,
        string guestPath,
        bool verified)
    {
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.ProjectionMount) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProjectionSchema,
            ProjectionId = projectionId,
            ProjectionMountResult = new AppleVirtualizationGuestAgentProjectionMountResult
            {
                Status = ProjectionStatus(projectionId, tag, guestPath, verified),
            },
            ProjectionStatus = ProjectionStatus(projectionId, tag, guestPath, verified),
        });
        TrackProjection(projectionId, verified);
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithProjectionStatus(
        string projectionId,
        string tag,
        string guestPath,
        bool verified)
    {
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.ProjectionStatus) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProjectionSchema,
            ProjectionId = projectionId,
            ProjectionStatus = ProjectionStatus(projectionId, tag, guestPath, verified),
        });
        TrackProjection(projectionId, verified);
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithProjectionSync(
        string projectionId,
        ContentProjectionChangeSummary? changeSummary = null,
        IReadOnlyList<AppleVirtualizationGuestAgentProjectionChange>? changes = null,
        IReadOnlyList<WorkspaceConflict>? conflicts = null,
        int maxChanges = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxChanges,
        int maxConflicts = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxConflicts)
    {
        IReadOnlyList<AppleVirtualizationGuestAgentProjectionChange> boundedChanges =
            AppleVirtualizationGuestAgentProjectionResultBounds.Bound(changes, maxChanges, out bool changesTruncated);
        IReadOnlyList<WorkspaceConflict> boundedConflicts =
            AppleVirtualizationGuestAgentProjectionResultBounds.Bound(conflicts, maxConflicts, out bool conflictsTruncated);
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.ProjectionSync) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProjectionSyncSchema,
            ProjectionId = projectionId,
            ProjectionSyncResult = new AppleVirtualizationGuestAgentProjectionSyncResult
            {
                ProjectionId = projectionId,
                State = AppleVirtualizationGuestAgentProjectionSyncState.Succeeded,
                Succeeded = true,
                CheckpointVersion = Interlocked.Increment(ref _sequence),
                ChangeSummary = changeSummary ?? new ContentProjectionChangeSummary(),
                Changes = boundedChanges,
                Conflicts = boundedConflicts,
                ChangesTruncated = changesTruncated,
                ConflictsTruncated = conflictsTruncated,
            },
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithProjectionFinalization(
        string projectionId,
        Digest? manifestDigest = null,
        IReadOnlyList<FinalizedContentRef>? content = null,
        IReadOnlyList<WorkspaceConflict>? conflicts = null,
        int maxContentRefs = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxFinalizedContentRefs,
        int maxConflicts = AppleVirtualizationGuestAgentProjectionResultBounds.DefaultMaxConflicts)
    {
        IReadOnlyList<FinalizedContentRef> boundedContent =
            AppleVirtualizationGuestAgentProjectionResultBounds.Bound(content, maxContentRefs, out bool contentTruncated);
        IReadOnlyList<WorkspaceConflict> boundedConflicts =
            AppleVirtualizationGuestAgentProjectionResultBounds.Bound(conflicts, maxConflicts, out bool conflictsTruncated);
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.ProjectionFinalize) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProjectionFinalizationSchema,
            ProjectionId = projectionId,
            ProjectionFinalizationResult = new AppleVirtualizationGuestAgentProjectionFinalizationResult
            {
                ProjectionId = projectionId,
                State = AppleVirtualizationGuestAgentProjectionFinalizationState.Succeeded,
                Succeeded = true,
                ManifestDigest = manifestDigest,
                Content = boundedContent,
                Conflicts = boundedConflicts,
                ContentTruncated = contentTruncated,
                ConflictsTruncated = conflictsTruncated,
            },
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithProjectionConfiguredOnly(
        string projectionId,
        string tag,
        string guestPath)
    {
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.ProjectionStatus) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProjectionSchema,
            ProjectionId = projectionId,
            ProjectionStatus = ProjectionStatus(
                projectionId,
                tag,
                guestPath,
                verified: false,
                verificationState: AppleVirtualizationGuestAgentProjectionVerificationState.HostShareConfigured,
                hostShareState: AppleVirtualizationGuestAgentProjectionHostShareState.HostShareConfigured,
                frameworkShareState: AppleVirtualizationGuestAgentProjectionFrameworkShareState.Unknown),
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithProjectionAccessMismatch(
        string projectionId,
        string tag,
        string guestPath,
        AccessMode requested = AccessMode.ReadWrite,
        AccessMode effective = AccessMode.ReadOnly)
    {
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.ProjectionStatus) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProjectionSchema,
            ProjectionId = projectionId,
            ProjectionStatus = ProjectionStatus(
                projectionId,
                tag,
                guestPath,
                verified: false,
                verificationState: AppleVirtualizationGuestAgentProjectionVerificationState.AccessMismatch,
                requestedAccess: requested,
                effectiveAccess: effective,
                limitations:
                [
                    new ContentProjectionLimitation(
                        ContentProjectionDegradedFeature.ReadOnlyEnforcement,
                        CapabilityDegradationMode.PartiallyAvailable,
                        "AppleVirtualization.ProjectionAccessMismatch",
                        "Guest projection effective access does not match requested access."),
                ]),
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithProjectionCoherence(
        string projectionId,
        string tag,
        string guestPath,
        CoherenceClass coherence)
    {
        AppleVirtualizationGuestAgentProjectionVerificationState state = coherence == CoherenceClass.Unknown
            ? AppleVirtualizationGuestAgentProjectionVerificationState.CoherenceUnknown
            : AppleVirtualizationGuestAgentProjectionVerificationState.CoherenceDegraded;
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.ProjectionStatus) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProjectionSchema,
            ProjectionId = projectionId,
            ProjectionStatus = ProjectionStatus(
                projectionId,
                tag,
                guestPath,
                verified: false,
                verificationState: state,
                coherence: coherence,
                limitations:
                [
                    new ContentProjectionLimitation(
                        ContentProjectionDegradedFeature.Coherence,
                        coherence == CoherenceClass.Unknown ? CapabilityDegradationMode.Unsupported : CapabilityDegradationMode.PartiallyAvailable,
                        "AppleVirtualization.ProjectionCoherenceUnverified",
                        "Guest projection coherence is not verified as ready for HPD use."),
                ]),
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithProjectionUnmount(
        string projectionId,
        bool unmounted,
        bool wasMounted)
    {
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.ProjectionUnmount) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProjectionSchema,
            ProjectionId = projectionId,
            ProjectionUnmountResult = new AppleVirtualizationGuestAgentProjectionUnmountResult(projectionId, unmounted, wasMounted),
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithProcessStarted(
        string processId,
        string unitId,
        bool pty = false)
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.ProcessStart) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProcessSchema,
            UnitId = unitId,
            ProcessId = processId,
            ProcessStarted = new AppleVirtualizationGuestAgentProcessStarted
            {
                ProcessId = processId,
                ProviderProcessId = "guest-" + processId,
                SystemProcessId = 42,
                PtyState = pty ? AppleVirtualizationGuestAgentPtyState.Allocated : AppleVirtualizationGuestAgentPtyState.NotRequested,
                Terminal = new AppleVirtualizationGuestAgentTerminalState
                {
                    PtyState = pty ? AppleVirtualizationGuestAgentPtyState.Allocated : AppleVirtualizationGuestAgentPtyState.NotRequested,
                    ResizeSupported = pty && _ptyResizeSupported,
                    ResizeUnsupportedReason = pty && _ptyResizeSupported ? null : "PTY resize is not enabled in this fake toolharness.",
                },
                IoState = ProcessIoState.Open,
                StartedAt = startedAt,
            },
            ProcessStatus = RunningStatus(processId, startedAt, pty),
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithProcessStatus(
        string processId,
        ProcessInvocationPhase phase,
        ProcessIoState ioState = ProcessIoState.Open)
    {
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.ProcessStatus) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProcessSchema,
            ProcessId = processId,
            ProcessStatus = new AppleVirtualizationGuestAgentProcessStatus
            {
                ProcessId = processId,
                ProcessPhase = phase,
                IoState = ioState,
                ProviderProcessId = "guest-" + processId,
                SystemProcessId = 42,
            },
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithProcessFailed(
        string processId,
        ProcessCompletionKind completionKind = ProcessCompletionKind.Faulted,
        string code = "AppleVirtualization.GuestProcessFailed")
    {
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.ProcessWait) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProcessSchema,
            ProcessId = processId,
            ProcessStatus = FailedStatus(processId, completionKind, code),
            ProcessResult = FailedStatus(processId, completionKind, code).Result,
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithProcessControlResult(
        AppleVirtualizationGuestAgentOperation operation,
        string processId,
        bool accepted = true)
    {
        EnqueueResponse(Response(operation) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProcessSchema,
            ProcessId = processId,
            ProcessControlResult = new AppleVirtualizationGuestAgentProcessControlResult(
                processId,
                accepted,
                accepted ? ProcessInvocationPhase.Running : ProcessInvocationPhase.Unknown,
                accepted ? null : "Scripted process control rejection."),
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithProcessOutput(
        string processId,
        ProcessOutputStream stream,
        ReadOnlyMemory<byte> bytes,
        bool final = false,
        bool truncated = false,
        int maxCapturedBytes = 64 * 1024)
    {
        BoundedCapture capture = CaptureFor(processId, stream, maxCapturedBytes);
        AppleVirtualizationGuestAgentCaptureAccounting accounting = capture.Record(bytes.Length, truncated);
        EnqueueEvent(Event(AppleVirtualizationGuestAgentOperation.ProcessReadOutput) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProcessOutputSchema,
            EventKind = AppleVirtualizationGuestAgentEventKind.ProcessOutput,
            ProcessId = processId,
            ProcessOutputChunk = new AppleVirtualizationGuestAgentProcessOutputChunk
            {
                ProcessId = processId,
                Stream = stream,
                Sequence = Interlocked.Increment(ref _sequence),
                ObservedAt = DateTimeOffset.UtcNow,
                Bytes = bytes,
                Flags = (final ? ProcessOutputChunkFlags.Final : ProcessOutputChunkFlags.None) |
                    (truncated ? ProcessOutputChunkFlags.Truncated : ProcessOutputChunkFlags.None),
                Capture = accounting,
            },
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithProcessExited(string processId, int exitCode)
    {
        DateTimeOffset exitedAt = DateTimeOffset.UtcNow;
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.ProcessWait) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProcessSchema,
            ProcessId = processId,
            ProcessStatus = new AppleVirtualizationGuestAgentProcessStatus
            {
                ProcessId = processId,
                ProcessPhase = ProcessInvocationPhase.Exited,
                IoState = ProcessIoState.Closed,
                ProviderProcessId = "guest-" + processId,
                SystemProcessId = 42,
                ExitedAt = exitedAt,
            },
            ProcessResult = new AppleVirtualizationGuestAgentProcessResult
            {
                ProcessId = processId,
                ProviderProcessId = "guest-" + processId,
                SystemProcessId = 42,
                ExitCode = exitCode,
                CompletionKind = ProcessCompletionKind.Exited,
                ExitedAt = exitedAt,
                StdoutCapture = CaptureFor(processId, ProcessOutputStream.Stdout, 0).Snapshot,
                StderrCapture = CaptureFor(processId, ProcessOutputStream.Stderr, 0).Snapshot,
            },
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithNetworkStatus(
        string hostId = "host-network",
        string interfaceName = "en0",
        ushort mtu = 1500)
    {
        var address = new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x0a000002);
        var gateway = new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x0a000001);
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.NetworkStatus) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.NetworkSchema,
            EventKind = AppleVirtualizationGuestAgentEventKind.NetworkObserved,
            NetworkStatus = new AppleVirtualizationGuestAgentNetworkStatus
            {
                HostId = hostId,
                GuestAgentReady = true,
                Interfaces =
                [
                    new AppleVirtualizationGuestAgentNetworkInterfaceStatus
                    {
                        Name = interfaceName,
                        Mtu = mtu,
                        IsUp = true,
                        Addresses =
                        [
                            new NetworkAddressAssignment(address, 24, AddressAssignmentKind.ProviderAssigned, IsPrimary: true),
                        ],
                    },
                ],
                Routes =
                [
                    new AppleVirtualizationGuestAgentNetworkRouteObservation
                    {
                        Gateway = gateway,
                        InterfaceName = interfaceName,
                        IsDefault = true,
                    },
                ],
                Listeners =
                [
                    new AppleVirtualizationGuestAgentNetworkListenerObservation
                    {
                        Name = "guest-process-listener",
                        Transport = NetworkTransport.Tcp,
                        Address = address,
                        Port = new NetworkPort(8080),
                        GuestVisibleOnly = true,
                        HpdPublished = false,
                    },
                ],
                Generation = new AppleVirtualizationGuestAgentNetworkGenerationStamp(
                    GuestBootId: "guest-boot-1",
                    GuestBootGeneration: 1,
                    GuestAgentGeneration: 1),
            },
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithEngineStatus(
        string hostId = "host-engine",
        string engineId = "engine-docker",
        AppleVirtualizationEngineObservationState state = AppleVirtualizationEngineObservationState.Ready,
        EngineControlPlaneKind kind = EngineControlPlaneKind.DockerCompatible,
        EngineApiKind api = EngineApiKind.DockerCompatible,
        EngineAuthorityMode authorityMode = EngineAuthorityMode.Rootless,
        EngineImageStoreMode imageStore = EngineImageStoreMode.ProviderManaged,
        EngineWorkloadAdoptionMode workloadAdoption = EngineWorkloadAdoptionMode.None)
    {
        AppleVirtualizationGuestAgentEngineStatus status =
            AppleVirtualizationGuestAgentEngineStatus.FromObservation(
                hostId,
                engineId,
                state,
                kind,
                api,
                authorityMode,
                imageStore,
                workloadAdoption,
                AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxEndpoints,
                AppleVirtualizationGuestAgentEngineResultBounds.DefaultMaxContainers);
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.EngineStatus) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.EngineSchema,
            EventKind = state == AppleVirtualizationEngineObservationState.Degraded
                ? AppleVirtualizationGuestAgentEventKind.EngineDegraded
                : AppleVirtualizationGuestAgentEventKind.EngineObserved,
            HostId = hostId,
            EngineStatus = status,
        });
        return this;
    }

    public FakeAppleVirtualizationGuestAgentToolHarness WithMalformedFrame(ReadOnlyMemory<byte> frame)
    {
        AppleVirtualizationGuestAgentFrameResult result = AppleVirtualizationGuestAgentJsonCodec.DecodeFrame(frame.Span);
        EnqueueResponse(Response(AppleVirtualizationGuestAgentOperation.Health) with
        {
            ResponseStatus = AppleVirtualizationGuestAgentResponseStatus.Error,
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ErrorSchema,
            Error = result.Error ?? new AppleVirtualizationGuestAgentError
            {
                Code = "AppleVirtualization.GuestAgentMalformedFrame",
                Message = "Guest-agent frame was marked malformed by the scripted toolharness.",
                Operation = "guest-agent.frame.decode",
                FailedPhase = "Decode",
                Retryable = true,
            },
        });
        return this;
    }

    public ValueTask<AppleVirtualizationGuestAgentEnvelope> SendAsync(
        AppleVirtualizationGuestAgentEnvelope request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _requests.Add(request);

        if (request.Operation == AppleVirtualizationGuestAgentOperation.ProcessResize && !_ptyResizeSupported)
        {
            return ValueTask.FromResult(request.ToErrorResponse(
                Interlocked.Increment(ref _sequence),
                AppleVirtualizationGuestAgentJsonCodec.Unsupported(
                    "process.resize",
                    "Guest-agent process resize requires scripted PTY support in this fake toolharness.")));
        }

        if (request.Operation == AppleVirtualizationGuestAgentOperation.ProjectionSync)
        {
            return ValueTask.FromResult(HandleProjectionSync(request));
        }

        if (request.Operation == AppleVirtualizationGuestAgentOperation.ProjectionFinalize)
        {
            return ValueTask.FromResult(HandleProjectionFinalization(request));
        }

        if (request.Operation == AppleVirtualizationGuestAgentOperation.ProjectionEnumerateChanges)
        {
            return ValueTask.FromResult(HandleProjectionChangeEnumeration(request));
        }

        if (request.Operation == AppleVirtualizationGuestAgentOperation.ProjectionPromote)
        {
            return ValueTask.FromResult(HandleProjectionPromotion(request));
        }

        if (request.Operation == AppleVirtualizationGuestAgentOperation.NetworkStatus)
        {
            return ValueTask.FromResult(HandleNetworkStatus(request));
        }

        if (request.Operation == AppleVirtualizationGuestAgentOperation.AuthorityBind)
        {
            return ValueTask.FromResult(HandleAuthorityBind(request));
        }

        if (request.Operation == AppleVirtualizationGuestAgentOperation.AuthorityStatus)
        {
            return ValueTask.FromResult(HandleAuthorityStatus(request));
        }

        if (request.Operation == AppleVirtualizationGuestAgentOperation.AuthorityRevoke)
        {
            return ValueTask.FromResult(HandleAuthorityRevoke(request));
        }

        if (request.Operation == AppleVirtualizationGuestAgentOperation.EngineStatus)
        {
            return ValueTask.FromResult(HandleEngineStatus(request));
        }

        if (request.Operation == AppleVirtualizationGuestAgentOperation.EngineProvision)
        {
            return ValueTask.FromResult(HandleEngineProvision(request));
        }

        if (_responses.TryDequeue(out AppleVirtualizationGuestAgentEnvelope? response))
        {
            return ValueTask.FromResult(response);
        }

        return ValueTask.FromResult(request.ToResponse(Interlocked.Increment(ref _sequence)));
    }

    public async IAsyncEnumerable<AppleVirtualizationGuestAgentEnvelope> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        while (_events.TryDequeue(out AppleVirtualizationGuestAgentEnvelope? helperEvent))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return helperEvent;
        }
    }

    private void EnqueueResponse(AppleVirtualizationGuestAgentEnvelope response) => _responses.Enqueue(response);

    private void EnqueueEvent(AppleVirtualizationGuestAgentEnvelope guestEvent)
    {
        if (_events.Count >= _maxEvents)
        {
            throw new InvalidOperationException("Fake guest-agent event queue capacity was exceeded.");
        }

        _events.Enqueue(guestEvent);
    }

    private void TrackProjection(string projectionId, bool verified)
    {
        if (verified)
        {
            _verifiedProjections[projectionId] = new AppleVirtualizationGuestAgentProjectionGenerationStamp(
                GuestBootId: "guest-boot-1",
                GuestBootGeneration: 1,
                GuestAgentGeneration: 1,
                ProjectionGeneration: 1);
        }
    }

    private AppleVirtualizationGuestAgentEnvelope HandleEngineStatus(AppleVirtualizationGuestAgentEnvelope request)
    {
        if (_responses.TryDequeue(out AppleVirtualizationGuestAgentEnvelope? scripted))
        {
            return scripted;
        }

        AppleVirtualizationGuestAgentEngineStatusRequest? engine = request.EngineStatusRequest;
        if (engine is null)
        {
            return request.ToErrorResponse(Interlocked.Increment(ref _sequence), MissingPayload("engine.status"));
        }

        AppleVirtualizationGuestAgentEngineStatus status = _engineProbe is null
            ? AppleVirtualizationGuestAgentEngineStatus.FromRequest(engine)
            : AppleVirtualizationGuestAgentEngineProbeMapper.ToStatus(engine, _engineProbe.Probe(engine));

        return request.ToResponse(Interlocked.Increment(ref _sequence)) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.EngineSchema,
            EventKind = status.ObservationState == AppleVirtualizationEngineObservationState.Degraded
                ? AppleVirtualizationGuestAgentEventKind.EngineDegraded
                : AppleVirtualizationGuestAgentEventKind.EngineObserved,
            HostId = engine.HostId,
            EngineStatus = status,
        };
    }

    private AppleVirtualizationGuestAgentEnvelope HandleEngineProvision(AppleVirtualizationGuestAgentEnvelope request)
    {
        if (_responses.TryDequeue(out AppleVirtualizationGuestAgentEnvelope? scripted))
        {
            return scripted;
        }

        AppleVirtualizationGuestAgentEngineProvisioningRequest? engine = request.EngineProvisioningRequest;
        if (engine is null)
        {
            return request.ToErrorResponse(Interlocked.Increment(ref _sequence), MissingPayload("engine.provision"));
        }

        AppleVirtualizationGuestAgentEngineProvisioningResult result = _engineProvisioner.Provision(engine);
        return request.ToResponse(Interlocked.Increment(ref _sequence)) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.EngineProvisioningSchema,
            EventKind = result.Phase == AppleVirtualizationEngineProvisioningPhase.Degraded
                ? AppleVirtualizationGuestAgentEventKind.EngineDegraded
                : AppleVirtualizationGuestAgentEventKind.EngineProvisioned,
            HostId = engine.HostId,
            EngineProvisioningResult = result,
        };
    }

    private AppleVirtualizationGuestAgentEnvelope HandleProjectionSync(AppleVirtualizationGuestAgentEnvelope request)
    {
        AppleVirtualizationGuestAgentProjectionSyncRequest? sync = request.ProjectionSyncRequest;
        if (sync is null)
        {
            return request.ToErrorResponse(Interlocked.Increment(ref _sequence), MissingPayload("projection.sync"));
        }

        if (ValidateProjectionReady(sync.ProjectionId, sync.Generation, out AppleVirtualizationGuestAgentError? error))
        {
            return request.ToErrorResponse(Interlocked.Increment(ref _sequence), error);
        }

        AppleVirtualizationGuestAgentProjectionSyncState unsupported = UnsupportedSync(sync);
        if (unsupported != AppleVirtualizationGuestAgentProjectionSyncState.Unknown)
        {
            return request.ToResponse(Interlocked.Increment(ref _sequence)) with
            {
                PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProjectionSyncSchema,
                ProjectionId = sync.ProjectionId,
                ProjectionSyncResult = new AppleVirtualizationGuestAgentProjectionSyncResult
                {
                    ProjectionId = sync.ProjectionId,
                    State = unsupported,
                    Succeeded = false,
                    DryRun = sync.DryRun,
                    UnsupportedReason = unsupported.ToString(),
                    Conditions = [Condition("AppleVirtualization.ProjectionSyncUnsupported", ConditionStatus.True, unsupported.ToString(), "Projection sync mode is not supported by the fake guest-agent contract toolharness.", DiagnosticSeverity.Warning)],
                },
            };
        }

        if (_responses.TryDequeue(out AppleVirtualizationGuestAgentEnvelope? scripted))
        {
            return scripted;
        }

        return request.ToResponse(Interlocked.Increment(ref _sequence)) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProjectionSyncSchema,
            ProjectionId = sync.ProjectionId,
            ProjectionSyncResult = new AppleVirtualizationGuestAgentProjectionSyncResult
            {
                ProjectionId = sync.ProjectionId,
                State = sync.DryRun ? AppleVirtualizationGuestAgentProjectionSyncState.DryRun : AppleVirtualizationGuestAgentProjectionSyncState.Succeeded,
                Succeeded = true,
                DryRun = sync.DryRun,
                CheckpointVersion = sync.DryRun ? 0 : Interlocked.Increment(ref _sequence),
                Conditions = [Condition("AppleVirtualization.ProjectionSyncCompleted", ConditionStatus.True, "Completed", "Projection sync completed in fake guest-agent toolharness.", DiagnosticSeverity.Info)],
            },
        };
    }

    private AppleVirtualizationGuestAgentEnvelope HandleAuthorityBind(AppleVirtualizationGuestAgentEnvelope request)
    {
        AppleVirtualizationGuestAgentAuthorityProjectionRequest? bind = request.AuthorityProjectionRequest;
        if (bind is null)
        {
            return request.ToErrorResponse(Interlocked.Increment(ref _sequence), MissingPayload("authority.bind"));
        }

        DateTimeOffset boundAt = bind.Lease.BoundAt == default ? DateTimeOffset.UtcNow : bind.Lease.BoundAt;
        string auditCorrelationId = bind.AuditCorrelationId ?? "authority-" + bind.BindingId;
        var boundAuthority = new AppleVirtualizationGuestAgentBoundAuthority
        {
            BindingId = bind.BindingId,
            SourceKind = bind.Source.Kind,
            ProjectionKind = bind.Projection.Kind,
            Direction = bind.Direction,
            EffectiveAuthorityClass = bind.EffectiveAuthorityClass,
            Redaction = bind.Redaction,
            TargetSocketPath = bind.Projection.TargetSocketPath,
            EnvironmentVariableName = bind.Projection.EnvironmentVariableName,
            BoundAt = boundAt,
            ExpiresAt = bind.Lease.ExpiresAt,
            RevocationStatus = RevocationVerificationStatus.Pending,
            AuditCorrelationId = auditCorrelationId,
            SensitiveEndpointKind = bind.Source.SensitiveEndpointKind,
            Generation = bind.Generation,
        };
        var audit = new AuthorityAuditEvent
        {
            Kind = AuthorityAuditKind.Projected,
            SourceKind = bind.Source.Kind,
            TargetKind = bind.Target.Kind,
            Timestamp = boundAt,
            CorrelationId = auditCorrelationId,
        };

        return request.ToResponse(Interlocked.Increment(ref _sequence)) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.AuthoritySchema,
            EventKind = AppleVirtualizationGuestAgentEventKind.AuthorityProjected,
            AuthorityStatus = new AppleVirtualizationGuestAgentAuthorityStatus
            {
                BindingId = bind.BindingId,
                BindingPhase = AuthorityBindingPhase.Projected,
                BoundAuthority = boundAuthority,
                AuditEvents = [audit],
                Conditions =
                [
                    Condition("AppleVirtualization.AuthorityProjected", ConditionStatus.True, "Projected", "Authority binding was accepted by the fake guest-agent toolharness.", DiagnosticSeverity.Info),
                ],
            },
        };
    }

    private AppleVirtualizationGuestAgentEnvelope HandleAuthorityStatus(AppleVirtualizationGuestAgentEnvelope request)
    {
        AppleVirtualizationGuestAgentAuthorityStatusRequest? status = request.AuthorityStatusRequest;
        if (status is null)
        {
            return request.ToErrorResponse(Interlocked.Increment(ref _sequence), MissingPayload("authority.status"));
        }

        return request.ToResponse(Interlocked.Increment(ref _sequence)) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.AuthoritySchema,
            AuthorityStatus = new AppleVirtualizationGuestAgentAuthorityStatus
            {
                BindingId = status.BindingId,
                BindingPhase = AuthorityBindingPhase.Projected,
                Conditions =
                [
                    Condition("AppleVirtualization.AuthorityStatusObserved", ConditionStatus.True, "Observed", "Authority binding status was observed by the fake guest-agent toolharness.", DiagnosticSeverity.Info),
                ],
            },
        };
    }

    private AppleVirtualizationGuestAgentEnvelope HandleAuthorityRevoke(AppleVirtualizationGuestAgentEnvelope request)
    {
        AppleVirtualizationGuestAgentAuthorityRevocationRequest? revoke = request.AuthorityRevocationRequest;
        if (revoke is null)
        {
            return request.ToErrorResponse(Interlocked.Increment(ref _sequence), MissingPayload("authority.revoke"));
        }

        var audit = new AuthorityAuditEvent
        {
            Kind = AuthorityAuditKind.Revoked,
            SourceKind = AuthoritySourceKind.ProviderDefined,
            TargetKind = AuthorityTargetKind.ProviderDefined,
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = "authority-" + revoke.BindingId,
        };

        return request.ToResponse(Interlocked.Increment(ref _sequence)) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.AuthoritySchema,
            EventKind = AppleVirtualizationGuestAgentEventKind.AuthorityRevoked,
            AuthorityRevocationResult = new AppleVirtualizationGuestAgentAuthorityRevocationResult
            {
                BindingId = revoke.BindingId,
                Accepted = true,
                RevocationStatus = revoke.Verify ? RevocationVerificationStatus.Verified : RevocationVerificationStatus.Pending,
                Reason = revoke.Reason,
                AuditEvents = [audit],
                Conditions =
                [
                    Condition("AppleVirtualization.AuthorityRevoked", ConditionStatus.True, "Revoked", "Authority binding revocation was accepted by the fake guest-agent toolharness.", DiagnosticSeverity.Info),
                ],
            },
        };
    }

    private AppleVirtualizationGuestAgentEnvelope HandleNetworkStatus(AppleVirtualizationGuestAgentEnvelope request)
    {
        if (_responses.TryDequeue(out AppleVirtualizationGuestAgentEnvelope? response))
        {
            return response;
        }

        AppleVirtualizationGuestAgentNetworkStatusRequest? status = request.NetworkStatusRequest;
        int maxInterfaces = status?.MaxInterfaces ?? AppleVirtualizationGuestAgentNetworkResultBounds.DefaultMaxInterfaces;
        int maxRoutes = status?.MaxRoutes ?? AppleVirtualizationGuestAgentNetworkResultBounds.DefaultMaxRoutes;
        int maxListeners = status?.MaxListeners ?? AppleVirtualizationGuestAgentNetworkResultBounds.DefaultMaxListeners;
        var address = new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x0a000002);
        var gateway = new IpAddressValue(NetworkAddressFamily.IPv4, 0, 0x0a000001);
        AppleVirtualizationGuestAgentNetworkInterfaceStatus[] interfaces =
        [
            new()
            {
                Name = "en0",
                Mtu = 1500,
                IsUp = true,
                Addresses =
                [
                    new NetworkAddressAssignment(address, 24, AddressAssignmentKind.ProviderAssigned, IsPrimary: true),
                ],
            },
        ];
        AppleVirtualizationGuestAgentNetworkRouteObservation[] routes =
        [
            new()
            {
                Gateway = gateway,
                InterfaceName = "en0",
                IsDefault = true,
            },
        ];
        AppleVirtualizationGuestAgentNetworkListenerObservation[] listeners =
        [
            new()
            {
                Name = "guest-observed-tcp-listener",
                Transport = NetworkTransport.Tcp,
                Address = address,
                Port = new NetworkPort(8080),
                GuestVisibleOnly = true,
                HpdPublished = false,
            },
        ];

        IReadOnlyList<AppleVirtualizationGuestAgentNetworkInterfaceStatus> boundedInterfaces =
            AppleVirtualizationGuestAgentNetworkResultBounds.Bound(interfaces, maxInterfaces, out bool interfacesTruncated);
        IReadOnlyList<AppleVirtualizationGuestAgentNetworkRouteObservation> boundedRoutes =
            AppleVirtualizationGuestAgentNetworkResultBounds.Bound(status?.IncludeRoutes == false ? [] : routes, maxRoutes, out bool routesTruncated);
        IReadOnlyList<AppleVirtualizationGuestAgentNetworkListenerObservation> boundedListeners =
            AppleVirtualizationGuestAgentNetworkResultBounds.Bound(status?.IncludeListeners == false ? [] : listeners, maxListeners, out bool listenersTruncated);

        return request.ToResponse(Interlocked.Increment(ref _sequence)) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.NetworkSchema,
            EventKind = AppleVirtualizationGuestAgentEventKind.NetworkObserved,
            NetworkStatus = new AppleVirtualizationGuestAgentNetworkStatus
            {
                HostId = status?.HostId ?? "unknown",
                UnitId = status?.UnitId,
                GuestAgentReady = true,
                Interfaces = boundedInterfaces,
                Routes = boundedRoutes,
                Listeners = boundedListeners,
                InterfacesTruncated = interfacesTruncated,
                RoutesTruncated = routesTruncated,
                ListenersTruncated = listenersTruncated,
                Generation = new AppleVirtualizationGuestAgentNetworkGenerationStamp(
                    GuestBootId: "guest-boot-1",
                    GuestBootGeneration: 1,
                    GuestAgentGeneration: 1),
            },
        };
    }

    private AppleVirtualizationGuestAgentEnvelope HandleProjectionFinalization(AppleVirtualizationGuestAgentEnvelope request)
    {
        AppleVirtualizationGuestAgentProjectionFinalizationRequest? finalization = request.ProjectionFinalizationRequest;
        if (finalization is null)
        {
            return request.ToErrorResponse(Interlocked.Increment(ref _sequence), MissingPayload("projection.finalize"));
        }

        if (ValidateProjectionReady(finalization.ProjectionId, finalization.Generation, out AppleVirtualizationGuestAgentError? error))
        {
            return request.ToErrorResponse(Interlocked.Increment(ref _sequence), error);
        }

        if (finalization.Kind is FinalizationKind.PublishArtifacts or FinalizationKind.CommitWorkspace or FinalizationKind.PromoteMemory)
        {
            return request.ToResponse(Interlocked.Increment(ref _sequence)) with
            {
                PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProjectionFinalizationSchema,
                ProjectionId = finalization.ProjectionId,
                ProjectionFinalizationResult = new AppleVirtualizationGuestAgentProjectionFinalizationResult
                {
                    ProjectionId = finalization.ProjectionId,
                    State = AppleVirtualizationGuestAgentProjectionFinalizationState.UnsupportedKind,
                    Succeeded = false,
                    UnsupportedReason = finalization.Kind.ToString(),
                    Conditions = [Condition("AppleVirtualization.ProjectionFinalizationUnsupported", ConditionStatus.True, "UnsupportedKind", "Projection finalization kind is not supported by the fake guest-agent contract toolharness.", DiagnosticSeverity.Warning)],
                },
            };
        }

        if (_responses.TryDequeue(out AppleVirtualizationGuestAgentEnvelope? scripted))
        {
            return scripted;
        }

        return request.ToResponse(Interlocked.Increment(ref _sequence)) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProjectionFinalizationSchema,
            ProjectionId = finalization.ProjectionId,
            ProjectionFinalizationResult = new AppleVirtualizationGuestAgentProjectionFinalizationResult
            {
                ProjectionId = finalization.ProjectionId,
                State = AppleVirtualizationGuestAgentProjectionFinalizationState.Succeeded,
                Succeeded = true,
                ManifestDigest = new Digest("sha256", "fake-manifest"),
                Conditions = [Condition("AppleVirtualization.ProjectionFinalized", ConditionStatus.True, "Completed", "Projection finalization completed in fake guest-agent toolharness.", DiagnosticSeverity.Info)],
            },
        };
    }

    private AppleVirtualizationGuestAgentEnvelope HandleProjectionChangeEnumeration(AppleVirtualizationGuestAgentEnvelope request)
    {
        AppleVirtualizationGuestAgentProjectionChangeEnumerationRequest? enumeration = request.ProjectionChangeEnumerationRequest;
        if (enumeration is null)
        {
            return request.ToErrorResponse(Interlocked.Increment(ref _sequence), MissingPayload("projection.enumerateChanges"));
        }

        if (ValidateProjectionReady(enumeration.ProjectionId, enumeration.Generation, out AppleVirtualizationGuestAgentError? error))
        {
            return request.ToErrorResponse(Interlocked.Increment(ref _sequence), error);
        }

        return request.ToResponse(Interlocked.Increment(ref _sequence)) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProjectionSyncSchema,
            ProjectionId = enumeration.ProjectionId,
            ProjectionChangeEnumerationResult = new AppleVirtualizationGuestAgentProjectionChangeEnumerationResult
            {
                ProjectionId = enumeration.ProjectionId,
                Changes = Array.Empty<AppleVirtualizationGuestAgentProjectionChange>(),
                HasMore = false,
            },
        };
    }

    private AppleVirtualizationGuestAgentEnvelope HandleProjectionPromotion(AppleVirtualizationGuestAgentEnvelope request)
    {
        AppleVirtualizationGuestAgentProjectionPromotionRequest? promotion = request.ProjectionPromotionRequest;
        if (promotion is null)
        {
            return request.ToErrorResponse(Interlocked.Increment(ref _sequence), MissingPayload("projection.promote"));
        }

        if (ValidateProjectionReady(promotion.ProjectionId, promotion.Generation, out AppleVirtualizationGuestAgentError? error))
        {
            return request.ToErrorResponse(Interlocked.Increment(ref _sequence), error);
        }

        if (promotion.ConflictPolicy != ConflictPolicy.RequireExplicitPromotion)
        {
            return request.ToResponse(Interlocked.Increment(ref _sequence)) with
            {
                PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProjectionSyncSchema,
                ProjectionId = promotion.ProjectionId,
                ProjectionPromotionResult = new AppleVirtualizationGuestAgentProjectionPromotionResult
                {
                    ProjectionId = promotion.ProjectionId,
                    State = AppleVirtualizationGuestAgentProjectionPromotionState.Unsupported,
                    Succeeded = false,
                    DryRun = promotion.DryRun,
                    UnsupportedReason = promotion.ConflictPolicy.ToString(),
                    Conditions = [Condition("AppleVirtualization.ProjectionPromotionUnsupported", ConditionStatus.True, "UnsupportedConflictPolicy", "Projection promotion requires explicit promotion policy in the fake guest-agent toolharness.", DiagnosticSeverity.Warning)],
                },
            };
        }

        return request.ToResponse(Interlocked.Increment(ref _sequence)) with
        {
            PayloadSchema = AppleVirtualizationGuestAgentProtocol.ProjectionSyncSchema,
            ProjectionId = promotion.ProjectionId,
            ProjectionPromotionResult = new AppleVirtualizationGuestAgentProjectionPromotionResult
            {
                ProjectionId = promotion.ProjectionId,
                State = promotion.DryRun ? AppleVirtualizationGuestAgentProjectionPromotionState.DryRun : AppleVirtualizationGuestAgentProjectionPromotionState.Succeeded,
                Succeeded = true,
                DryRun = promotion.DryRun,
            },
        };
    }

    private bool ValidateProjectionReady(
        string projectionId,
        AppleVirtualizationGuestAgentProjectionGenerationStamp generation,
        out AppleVirtualizationGuestAgentError? error)
    {
        if (!_verifiedProjections.TryGetValue(projectionId, out AppleVirtualizationGuestAgentProjectionGenerationStamp observed))
        {
            error = new AppleVirtualizationGuestAgentError
            {
                Code = "AppleVirtualization.ProjectionNotVerified",
                Message = "Projection sync/finalization requires a guest-verified projection.",
                Operation = "projection.lifecycle",
                FailedPhase = "ProjectionVerification",
                Retryable = true,
                Severity = DiagnosticSeverity.Error,
            };
            return true;
        }

        if (generation.ProjectionGeneration != 0 &&
            observed.ProjectionGeneration != 0 &&
            generation.ProjectionGeneration != observed.ProjectionGeneration)
        {
            error = new AppleVirtualizationGuestAgentError
            {
                Code = "AppleVirtualization.ProjectionStaleGeneration",
                Message = "Projection generation is stale for sync/finalization.",
                Operation = "projection.lifecycle",
                FailedPhase = "GenerationCheck",
                Retryable = true,
                Severity = DiagnosticSeverity.Error,
            };
            return true;
        }

        error = null;
        return false;
    }

    private static AppleVirtualizationGuestAgentProjectionSyncState UnsupportedSync(AppleVirtualizationGuestAgentProjectionSyncRequest sync)
    {
        if (sync.Mode is SyncMode.Continuous or SyncMode.None)
        {
            return AppleVirtualizationGuestAgentProjectionSyncState.UnsupportedMode;
        }

        if (sync.Direction is SyncDirection.Bidirectional or SyncDirection.None)
        {
            return AppleVirtualizationGuestAgentProjectionSyncState.UnsupportedDirection;
        }

        if (sync.ConflictPolicy is ConflictPolicy.PreferSource or ConflictPolicy.PreferTarget)
        {
            return AppleVirtualizationGuestAgentProjectionSyncState.UnsupportedConflictPolicy;
        }

        return AppleVirtualizationGuestAgentProjectionSyncState.Unknown;
    }

    private static AppleVirtualizationGuestAgentError MissingPayload(string operation) =>
        new()
        {
            Code = "AppleVirtualization.GuestAgentMissingPayload",
            Message = "Guest-agent request did not include the required operation payload.",
            Operation = operation,
            FailedPhase = "Decode",
            Retryable = false,
            Severity = DiagnosticSeverity.Error,
        };

    private AppleVirtualizationGuestAgentEnvelope Response(AppleVirtualizationGuestAgentOperation operation) =>
        new()
        {
            MessageType = AppleVirtualizationGuestAgentMessageType.Response,
            Operation = operation,
            ResponseStatus = AppleVirtualizationGuestAgentResponseStatus.Ok,
            SequenceNumber = Interlocked.Increment(ref _sequence),
        };

    private AppleVirtualizationGuestAgentEnvelope Event(AppleVirtualizationGuestAgentOperation operation) =>
        new()
        {
            MessageType = AppleVirtualizationGuestAgentMessageType.Event,
            Operation = operation,
            SequenceNumber = Interlocked.Increment(ref _sequence),
        };

    private static AppleVirtualizationGuestAgentProcessStatus RunningStatus(
        string processId,
        DateTimeOffset startedAt,
        bool pty) =>
        new()
        {
            ProcessId = processId,
            ProcessPhase = ProcessInvocationPhase.Running,
            IoState = ProcessIoState.Open,
            ProviderProcessId = "guest-" + processId,
            SystemProcessId = 42,
            StartedAt = startedAt,
            Terminal = new AppleVirtualizationGuestAgentTerminalState
            {
                PtyState = pty ? AppleVirtualizationGuestAgentPtyState.Allocated : AppleVirtualizationGuestAgentPtyState.NotRequested,
                ResizeSupported = false,
                ResizeUnsupportedReason = "PTY resize is not enabled in this fake toolharness.",
            },
        };

    private static AppleVirtualizationGuestAgentProcessStatus FailedStatus(
        string processId,
        ProcessCompletionKind completionKind,
        string code)
    {
        var condition = new Condition(
            "AppleVirtualization.GuestProcessFailed",
            ConditionStatus.True,
            code,
            "Scripted fake guest-agent process failure.",
            DateTimeOffset.UtcNow,
            default,
            DiagnosticSeverity.Error);

        return new AppleVirtualizationGuestAgentProcessStatus
        {
            ProcessId = processId,
            ProcessPhase = ProcessInvocationPhase.Failed,
            IoState = ProcessIoState.Closed,
            ProviderProcessId = "guest-" + processId,
            SystemProcessId = 42,
            ExitedAt = DateTimeOffset.UtcNow,
            Conditions = [condition],
            Result = new AppleVirtualizationGuestAgentProcessResult
            {
                ProcessId = processId,
                ProviderProcessId = "guest-" + processId,
                SystemProcessId = 42,
                CompletionKind = completionKind,
                ExitedAt = DateTimeOffset.UtcNow,
                Diagnostics = [condition],
            },
        };
    }

    private BoundedCapture CaptureFor(string processId, ProcessOutputStream stream, int maxCapturedBytes)
    {
        string key = processId + ":" + stream;
        if (!_captures.TryGetValue(key, out BoundedCapture? capture))
        {
            capture = new BoundedCapture(maxCapturedBytes);
            _captures.Add(key, capture);
        }

        return capture;
    }

    private static AppleVirtualizationGuestAgentProjectionStatus ProjectionStatus(
        string projectionId,
        string tag,
        string guestPath,
        bool verified,
        AppleVirtualizationGuestAgentProjectionVerificationState? verificationState = null,
        AppleVirtualizationGuestAgentProjectionHostShareState hostShareState = AppleVirtualizationGuestAgentProjectionHostShareState.HostShareConfigured,
        AppleVirtualizationGuestAgentProjectionFrameworkShareState frameworkShareState = AppleVirtualizationGuestAgentProjectionFrameworkShareState.Accepted,
        AccessMode requestedAccess = AccessMode.ReadOnly,
        AccessMode effectiveAccess = AccessMode.ReadOnly,
        CoherenceClass coherence = CoherenceClass.CloseToOpen,
        IReadOnlyList<ContentProjectionLimitation>? limitations = null) =>
        new()
        {
            ProjectionId = projectionId,
            Tag = tag,
            GuestPath = guestPath,
            Mounted = verified,
            GuestMountVerified = verified,
            HostShareState = hostShareState,
            FrameworkShareState = frameworkShareState,
            VerificationState = verificationState ?? (verified
                ? AppleVirtualizationGuestAgentProjectionVerificationState.ReadyForHpdUse
                : AppleVirtualizationGuestAgentProjectionVerificationState.FrameworkShareAccepted),
            ExpectedGuestPath = guestPath,
            ActualGuestPath = verified ? guestPath : null,
            RequestedAccessMode = requestedAccess,
            EffectiveAccessMode = effectiveAccess,
            ProjectionPhase = verified ? ContentProjectionPhase.Projected : ContentProjectionPhase.Projecting,
            EffectiveRealization = ProjectionRealizationKind.LiveProjection,
            EffectiveWriteEffect = ProjectionWriteEffect.NoWrites,
            EffectiveCoherence = coherence,
            EffectiveCache = CacheBehavior.Unknown,
            Limitations = limitations ?? Array.Empty<ContentProjectionLimitation>(),
            Conditions =
            [
                new Condition("AppleVirtualization.GuestMountVerified", verified ? ConditionStatus.True : ConditionStatus.False, verified ? "GuestMountVerified" : "GuestMountPending", verified ? "Guest mount was verified." : "Guest mount has not been verified.", DateTimeOffset.UtcNow, default, verified ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning),
            ],
        };

    private static Condition Condition(
        string type,
        ConditionStatus status,
        string reason,
        string message,
        DiagnosticSeverity severity) =>
        new(type, status, reason, message, DateTimeOffset.UtcNow, default, severity);

    private sealed class BoundedCapture
    {
        private readonly int _maxCapturedBytes;
        private long _bytesObserved;
        private long _bytesCaptured;
        private long _bytesDiscarded;
        private bool _truncated;

        public BoundedCapture(int maxCapturedBytes)
        {
            _maxCapturedBytes = Math.Max(0, maxCapturedBytes);
        }

        public AppleVirtualizationGuestAgentCaptureAccounting Snapshot =>
            new()
            {
                BytesObserved = _bytesObserved,
                BytesCaptured = _bytesCaptured,
                BytesDiscarded = _bytesDiscarded,
                Truncated = _truncated,
                MaxCapturedBytes = _maxCapturedBytes,
            };

        public AppleVirtualizationGuestAgentCaptureAccounting Record(int byteCount, bool chunkTruncated)
        {
            _bytesObserved += byteCount;
            long remaining = Math.Max(0, _maxCapturedBytes - _bytesCaptured);
            long captured = Math.Min(byteCount, remaining);
            _bytesCaptured += captured;
            _bytesDiscarded += byteCount - captured;
            _truncated |= chunkTruncated || captured < byteCount;
            return Snapshot;
        }
    }
}
