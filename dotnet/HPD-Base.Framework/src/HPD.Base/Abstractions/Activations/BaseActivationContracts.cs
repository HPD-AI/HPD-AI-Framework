using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Classifies how one installed activation executes.</summary>
public enum BaseActivationExecutionClass
{
    /// <summary>Executes one installed selection or module operation in the terminal transaction.</summary>
    TransactionalOperation = 0,
    /// <summary>Executes registered worker code with at-least-once delivery and fenced child mutations.</summary>
    AtLeastOnceWorker = 1,
    /// <summary>Records effect start before invoking an external effect and never retries it automatically.</summary>
    AtMostOnceEffect = 2,
}

/// <summary>Classifies the durable lifecycle state of one activation.</summary>
public enum BaseActivationState
{
    /// <summary>The activation is eligible when its effective due time is reached.</summary>
    Pending = 0,
    /// <summary>The activation is waiting for its deterministic retry due time.</summary>
    RetryPending = 1,
    /// <summary>One current worker claim owns the activation.</summary>
    Claimed = 2,
    /// <summary>The external effect-start fact committed and automatic retry is forbidden.</summary>
    EffectStarted = 3,
    /// <summary>The activation completed successfully.</summary>
    Succeeded = 4,
    /// <summary>The activation reached a terminal failure.</summary>
    Exhausted = 5,
    /// <summary>Cancellation won the durable state transition.</summary>
    Cancelled = 6,
    /// <summary>An external effect may or may not have occurred and requires reconciliation.</summary>
    OutcomeUnknown = 7,
    /// <summary>An identified migration replaced the activation.</summary>
    Migrated = 8,
    /// <summary>Detailed state was pruned after immutable terminal evidence was retained.</summary>
    Disposed = 9,
}

/// <summary>Identifies one installed activation definition.</summary>
public sealed record BaseActivationDefinitionKey
{
    /// <summary>Gets the stable definition identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive definition version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the Runtime-owned canonical 256-bit definition checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains stable claim authority for one activation attempt.</summary>
public sealed record BaseActivationClaimAuthority
{
    /// <summary>Gets the stable activation identifier.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the positive attempt number.</summary>
    public required int AttemptNumber { get; init; }
    /// <summary>Gets the positive claim epoch.</summary>
    public required long ClaimEpoch { get; init; }
    /// <summary>Gets the opaque 256-bit fencing token.</summary>
    public required ImmutableArray<byte> FencingToken { get; init; }
    /// <summary>Gets the installed worker identity.</summary>
    public required string WorkerIdentity { get; init; }
    /// <summary>Gets the cancellation generation captured by the claim.</summary>
    public required long CancellationGeneration { get; init; }
    /// <summary>Gets the physical store-instance identity.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the nonnegative restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the exact installed definition checksum.</summary>
    public required ImmutableArray<byte> DefinitionChecksum { get; init; }
}

/// <summary>Contains the replaceable lease observation for one stable activation claim.</summary>
public sealed record BaseActivationLeaseObservation
{
    /// <summary>Gets the positive lease revision.</summary>
    public required long LeaseRevision { get; init; }
    /// <summary>Gets the lease expiry as canonical Unix milliseconds.</summary>
    public required long LeaseExpiresAt { get; init; }
    /// <summary>Gets the canonical 256-bit observation checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains stable authority for one registered worker-process incarnation.</summary>
public sealed record BaseExecutorIncarnationAuthority
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the stable host identity.</summary>
    public required string HostId { get; init; }
    /// <summary>Gets the unique process-incarnation identity.</summary>
    public required string ProcessIncarnationId { get; init; }
    /// <summary>Gets the positive executor generation.</summary>
    public required long ExecutorGeneration { get; init; }
    /// <summary>Gets the physical store-instance identity.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the nonnegative restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the installed worker-definition-set checksum.</summary>
    public required ImmutableArray<byte> WorkerDefinitionSetChecksum { get; init; }
    /// <summary>Gets the canonical incarnation-authority checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains the replaceable heartbeat observation for one stable executor incarnation.</summary>
public sealed record BaseExecutorHeartbeatObservation
{
    /// <summary>Gets the positive heartbeat revision.</summary>
    public required long HeartbeatRevision { get; init; }
    /// <summary>Gets the heartbeat expiry as canonical Unix milliseconds.</summary>
    public required long HeartbeatExpiresAt { get; init; }
    /// <summary>Gets the stable executor-authority checksum.</summary>
    public required ImmutableArray<byte> ExecutorAuthorityChecksum { get; init; }
    /// <summary>Gets the canonical heartbeat-observation checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains the immutable ownership authority for one started external effect.</summary>
public sealed record BaseEffectExecutionAuthority
{
    /// <summary>Gets the exact activation claim that began the effect.</summary>
    public required BaseActivationClaimAuthority Claim { get; init; }
    /// <summary>Gets the complete stable executor incarnation that owns the effect.</summary>
    public required BaseExecutorIncarnationAuthority Executor { get; init; }
    /// <summary>Gets the activation generation at effect start.</summary>
    public required long EffectStartGeneration { get; init; }
    /// <summary>Gets the positive effect-heartbeat revision.</summary>
    public required long HeartbeatRevision { get; init; }
    /// <summary>Gets the effect-heartbeat expiry as canonical Unix milliseconds.</summary>
    public required long HeartbeatExpiresAt { get; init; }
    /// <summary>Gets the canonical effect-authority checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Guards one same-store child operation with a current activation claim.</summary>
public sealed record BaseActivationGuard
{
    /// <summary>Gets the stable claim authority.</summary>
    public required BaseActivationClaimAuthority Claim { get; init; }
    /// <summary>Gets the stable installed child-step identifier.</summary>
    public required string StepId { get; init; }
    /// <summary>Gets the positive child ordinal within the step.</summary>
    public required int ChildOrdinal { get; init; }
    /// <summary>Gets the canonical child-request fingerprint.</summary>
    public required ImmutableArray<byte> ChildRequestFingerprint { get; init; }
}

/// <summary>Contains immutable activation input stored separately from hot control authority.</summary>
public sealed record BaseActivationPayload
{
    /// <summary>Gets the activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the installed definition.</summary>
    public required BaseActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the canonical input bytes.</summary>
    public required ImmutableArray<byte> CanonicalInput { get; init; }
    /// <summary>Gets the canonical input checksum.</summary>
    public required ImmutableArray<byte> InputChecksum { get; init; }
    /// <summary>Gets the protected semantic scope captured at creation.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets the immutable payload checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains one activation creation projected into a shared atomic execution.</summary>
public sealed record BaseActivationCreateIntent
{
    /// <summary>Gets the dense zero-based activation ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the exact installed definition.</summary>
    public required BaseActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the canonical activation input.</summary>
    public required ImmutableArray<byte> CanonicalInput { get; init; }
    /// <summary>Gets the canonical input checksum.</summary>
    public required ImmutableArray<byte> InputChecksum { get; init; }
    /// <summary>Gets the protected semantic scope evidence.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets the caller-requested due instant as Unix milliseconds.</summary>
    public required long RequestedDueAt { get; init; }
    /// <summary>Gets the effective due instant after deterministic scheduling policy.</summary>
    public long? EffectiveDueAt { get; init; }
    /// <summary>Gets the immutable schedule occurrence identity when scheduled.</summary>
    public string? OccurrenceId { get; init; }
    /// <summary>Gets declared priority in the closed -32..32 range.</summary>
    public int Priority { get; init; }
    /// <summary>Gets the canonical overlap-key digest; empty means no overlap group.</summary>
    public ImmutableArray<byte> OverlapKey { get; init; }
    /// <summary>Gets the schedule overlap policy.</summary>
    public BaseScheduleOverlapPolicy OverlapPolicy { get; init; } = BaseScheduleOverlapPolicy.Allow;
    /// <summary>Gets whether the activation may enter the due index immediately.</summary>
    public bool InitiallyEligible { get; init; } = true;
    /// <summary>Gets the identified request that owns duplicate resolution.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
}

/// <summary>Extends one shared atomic request with ordered activation creation.</summary>
public sealed record BaseActivationCreationExtension
{
    /// <summary>Gets the dense ordered activation intents.</summary>
    public required ImmutableArray<BaseActivationCreateIntent> Items { get; init; }
    /// <summary>Gets the Runtime-owned structural digest.</summary>
    public required ImmutableArray<byte> StructuralDigest { get; init; }
}

/// <summary>Requests the exact due handler-free activation selected by a finite observation.</summary>
public sealed record BaseTransactionalActivationCandidateRequest
{
    /// <summary>Gets the installed application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the exact installed definition authority.</summary>
    public required BaseActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the finite due observation token.</summary>
    public required BaseDueObservationToken Observation { get; init; }
    /// <summary>Gets the protected worker scope.</summary>
    public required BaseOwnedScopeSeekAuthority Scope { get; init; }
    /// <summary>Gets provider-accepted time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the effective provider limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains immutable authority for one due handler-free activation.</summary>
public sealed record BaseTransactionalActivationCandidate
{
    /// <summary>Gets the authoritative activation payload.</summary>
    public required BaseActivationPayload Payload { get; init; }
    /// <summary>Gets the exact current activation generation.</summary>
    public required long ActivationGeneration { get; init; }
    /// <summary>Gets provider-accepted discovery time as Unix milliseconds.</summary>
    public required long AcceptedAt { get; init; }
    /// <summary>Gets the current control checksum.</summary>
    public required ImmutableArray<byte> ControlChecksum { get; init; }
    /// <summary>Gets the finite due-index interval evidence.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> ReadIntervals { get; init; }
    /// <summary>Gets provider accounting for candidate discovery.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets the effective limits retained through transaction-local terminalization.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Requests transaction-local terminalization of one handler-free activation.</summary>
public sealed record BaseTransactionalActivationFinalization
{
    /// <summary>Gets the captured candidate authority.</summary>
    public required BaseTransactionalActivationCandidate Candidate { get; init; }
    /// <summary>Gets canonical target result bytes projected by Runtime.</summary>
    public required ImmutableArray<byte> CanonicalResult { get; init; }
    /// <summary>Gets the SHA-256 checksum of the canonical result.</summary>
    public required ImmutableArray<byte> ResultChecksum { get; init; }
}

/// <summary>Contains provisional terminal evidence committed with the target operation.</summary>
public sealed record BaseTransactionalActivationCommitEvidence
{
    /// <summary>Gets the terminal activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the resulting positive activation generation.</summary>
    public required long ActivationGeneration { get; init; }
    /// <summary>Gets the resulting terminal state.</summary>
    public required BaseActivationState State { get; init; }
    /// <summary>Gets the resulting control checksum.</summary>
    public required ImmutableArray<byte> ControlChecksum { get; init; }
    /// <summary>Gets the provider-owned work charged for transaction-local terminalization.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
}

/// <summary>Contains one transaction-captured activation uniqueness result.</summary>
public sealed record BaseCapturedActivationItem
{
    /// <summary>Gets the matching creation ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the deterministic activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets whether the activation already exists.</summary>
    public required bool Exists { get; init; }
    /// <summary>Gets the existing canonical fingerprint when present.</summary>
    public ImmutableArray<byte> ExistingFingerprint { get; init; }
}

/// <summary>Contains aggregate transaction-captured activation authority.</summary>
public sealed record BaseCapturedActivationExtension
{
    /// <summary>Gets one item for every requested activation in request order.</summary>
    public required ImmutableArray<BaseCapturedActivationItem> Items { get; init; }
    /// <summary>Gets normalized uniqueness and schedule read intervals.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> ReadIntervals { get; init; }
    /// <summary>Gets the canonical aggregate evidence checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains one prepared activation creation.</summary>
public sealed record BasePreparedActivationItem
{
    /// <summary>Gets the matching request ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the resulting activation generation.</summary>
    public required long ResultingGeneration { get; init; }
    /// <summary>Gets the immutable payload checksum.</summary>
    public required ImmutableArray<byte> PayloadChecksum { get; init; }
    /// <summary>Gets the hot-control checksum.</summary>
    public required ImmutableArray<byte> ControlChecksum { get; init; }
}

/// <summary>Contains aggregate prepared activation evidence.</summary>
public sealed record BasePreparedActivationExtension
{
    /// <summary>Gets prepared items in exact request order.</summary>
    public required ImmutableArray<BasePreparedActivationItem> Items { get; init; }
    /// <summary>Gets the aggregate preparation checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains one provisionally applied activation creation.</summary>
public sealed record BaseProvisionalActivationItem
{
    /// <summary>Gets the matching request ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the provisional control generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets the canonical committed-evidence checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains aggregate provisional activation evidence.</summary>
public sealed record BaseProvisionalActivationExtension
{
    /// <summary>Gets provisional items in exact request order.</summary>
    public required ImmutableArray<BaseProvisionalActivationItem> Items { get; init; }
    /// <summary>Gets the aggregate provisional checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}
