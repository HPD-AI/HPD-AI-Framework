using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Classifies how an activation provider invalidates a due observation.</summary>
public enum BaseDueInvalidationClass
{
    /// <summary>The provider supplies native finite-token invalidation.</summary>
    Native = 0,
    /// <summary>The provider supplies certified bounded polling.</summary>
    BoundedPolling = 1,
}

/// <summary>Classifies one provider-supported durable schedule expression.</summary>
public enum BaseScheduleKind
{
    /// <summary>One exact instant.</summary>
    Once,
    /// <summary>One fixed UTC interval.</summary>
    Interval,
    /// <summary>One six-field calendar cron expression.</summary>
    Cron,
    /// <summary>One closed calendar recurrence.</summary>
    Calendar,
}

/// <summary>Classifies the result of waiting on one finite due observation.</summary>
public enum BaseDueWaitOutcome
{
    /// <summary>The observed due authority changed.</summary>
    Changed = 0,
    /// <summary>The requested finite deadline elapsed.</summary>
    Deadline = 1,
    /// <summary>The provider is shutting down.</summary>
    Shutdown = 2,
    /// <summary>The observation token is no longer valid.</summary>
    TokenInvalid = 3,
}

/// <summary>Provides protected authority for one exact activation scope seek.</summary>
public sealed record BaseOwnedScopeSeekAuthority
{
    /// <summary>Gets the semantic scope kind.</summary>
    public required BaseSubjectScopeKind Kind { get; init; }
    /// <summary>Gets the purpose-bound protected index digest.</summary>
    public required ImmutableArray<byte> ProtectedIndexDigest { get; init; }
}

/// <summary>Contains a Runtime-issued, provider-verifiable accepted-time observation.</summary>
public sealed class BaseAcceptedTimeReceipt
{
    internal BaseAcceptedTimeReceipt(
        string applicationId,
        long clockGeneration,
        long capturedUtc,
        long monotonicTimestamp,
        long captureSequence,
        long maximumForwardSkewMilliseconds,
        byte[] checksum)
    {
        ApplicationId = applicationId;
        ClockGeneration = clockGeneration;
        CapturedUtc = capturedUtc;
        MonotonicTimestamp = monotonicTimestamp;
        CaptureSequence = captureSequence;
        MaximumForwardSkewMilliseconds = maximumForwardSkewMilliseconds;
        Checksum = checksum.ToArray();
    }

    /// <summary>Gets the installed application identity.</summary>
    public string ApplicationId { get; }
    /// <summary>Gets the positive installed clock generation.</summary>
    public long ClockGeneration { get; }
    /// <summary>Gets accepted UTC time as Unix milliseconds.</summary>
    public long CapturedUtc { get; }
    /// <summary>Gets the process-monotonic timestamp used for validation.</summary>
    public long MonotonicTimestamp { get; }
    /// <summary>Gets the positive capture sequence.</summary>
    public long CaptureSequence { get; }
    /// <summary>Gets the maximum accepted forward skew in milliseconds.</summary>
    public long MaximumForwardSkewMilliseconds { get; }
    /// <summary>Gets the canonical receipt checksum.</summary>
    public ReadOnlyMemory<byte> Checksum { get; }
}

/// <summary>Defines the complete effective safety envelope for one activation-provider operation.</summary>
public sealed record BaseActivationExecutionLimits
{
    /// <summary>Gets the maximum candidates a due seek may inspect.</summary>
    public required int MaximumCandidates { get; init; }
    /// <summary>Gets the maximum canonical input bytes retained.</summary>
    public required long MaximumInputBytes { get; init; }
    /// <summary>Gets the maximum canonical result bytes retained.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets the maximum evidence bytes returned.</summary>
    public required long MaximumEvidenceBytes { get; init; }
    /// <summary>Gets the maximum aggregate transient bytes retained.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets the maximum read intervals returned.</summary>
    public required int MaximumReadIntervals { get; init; }
    /// <summary>Gets the maximum index operations performed.</summary>
    public required int MaximumIndexOperations { get; init; }
    /// <summary>Gets the acquisition deadline.</summary>
    public required TimeSpan AcquisitionTimeout { get; init; }
    /// <summary>Gets the transaction deadline.</summary>
    public required TimeSpan TransactionTimeout { get; init; }
    /// <summary>Gets the commit-observation deadline.</summary>
    public required TimeSpan CommitObservationTimeout { get; init; }
    /// <summary>Gets the receipt-resolution deadline.</summary>
    public required TimeSpan ReceiptResolutionTimeout { get; init; }
}

/// <summary>Reports canonical provider work for one activation operation.</summary>
public sealed record BaseActivationAccounting
{
    /// <summary>Gets the number of candidate rows inspected.</summary>
    public required int Candidates { get; init; }
    /// <summary>Gets the number of comparisons performed.</summary>
    public required int Comparisons { get; init; }
    /// <summary>Gets the number of index operations performed.</summary>
    public required int IndexOperations { get; init; }
    /// <summary>Gets the number of returned read intervals.</summary>
    public required int ReadIntervals { get; init; }
    /// <summary>Gets the canonical evidence byte count.</summary>
    public required long EvidenceBytes { get; init; }
    /// <summary>Gets the aggregate transient byte count.</summary>
    public required long TransientBytes { get; init; }
}

/// <summary>Requests resolution of one identified durable activation receipt.</summary>
public sealed record BaseActivationReceiptResolutionRequest
{
    /// <summary>Gets the exact historical request identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets current trusted time used for claim-authority replay.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the exact effective provider limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains one checksum-validated, provider-neutral receipt resolution.</summary>
public sealed record BaseActivationReceiptResolution
{
    /// <summary>Gets the closed provider operation kind.</summary>
    public required string OperationKind { get; init; }
    /// <summary>Gets the exact stored request fingerprint.</summary>
    public required ImmutableArray<byte> Fingerprint { get; init; }
    /// <summary>Gets canonical source-generated result bytes after current claim resolution.</summary>
    public required ImmutableArray<byte> CanonicalResult { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
}

/// <summary>Contains the canonical total-order boundary for a due activation.</summary>
public sealed record BaseActivationDueBoundary
{
    /// <summary>Gets the effective aged priority.</summary>
    public required int EffectiveAgedPriority { get; init; }
    /// <summary>Gets the effective due instant as Unix milliseconds.</summary>
    public required long EffectiveDueAt { get; init; }
    /// <summary>Gets the optional schedule occurrence identity.</summary>
    public string? OccurrenceId { get; init; }
    /// <summary>Gets the stable activation identity.</summary>
    public required string ActivationId { get; init; }
}

/// <summary>Contains one opaque, finite due-observation token.</summary>
public sealed record BaseDueObservationToken
{
    /// <summary>Gets the purpose-bound authenticated token bytes.</summary>
    public required ImmutableArray<byte> Value { get; init; }
}

/// <summary>Requests an exact protected due observation.</summary>
public sealed record BaseActivationDueObservationRequest
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the installed worker module identity.</summary>
    public required string WorkerModuleId { get; init; }
    /// <summary>Gets the canonical authorized definition set.</summary>
    public required ImmutableArray<BaseActivationDefinitionKey> Definitions { get; init; }
    /// <summary>Gets the protected exact scope seek.</summary>
    public required BaseOwnedScopeSeekAuthority Scope { get; init; }
    /// <summary>Gets the accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the maximum candidates to inspect.</summary>
    public required int MaximumCandidates { get; init; }
    /// <summary>Gets the optional exclusive continuation boundary.</summary>
    public BaseActivationDueBoundary? After { get; init; }
    /// <summary>Gets the effective operation limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains an authoritative finite due observation.</summary>
public sealed record BaseActivationDueObservation
{
    /// <summary>Gets the earliest matching due boundary, or null for an empty observation.</summary>
    public BaseActivationDueBoundary? Earliest { get; init; }
    /// <summary>Gets the finite observation token.</summary>
    public required BaseDueObservationToken Token { get; init; }
    /// <summary>Gets normalized covering read intervals.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> Intervals { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
}

/// <summary>Contains the result of waiting on a due observation.</summary>
public sealed record BaseDueWaitResult
{
    /// <summary>Gets the closed wait outcome.</summary>
    public required BaseDueWaitOutcome Outcome { get; init; }
}

/// <summary>Contains exact installed authority for one worker.</summary>
public sealed record BaseActivationWorkerAuthority
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the worker module identity.</summary>
    public required string ModuleId { get; init; }
    /// <summary>Gets the worker identity.</summary>
    public required string WorkerIdentity { get; init; }
    /// <summary>Gets the authorized definitions in canonical order.</summary>
    public required ImmutableArray<BaseActivationDefinitionKey> Definitions { get; init; }
    /// <summary>Gets the protected exact scope.</summary>
    public required BaseOwnedScopeSeekAuthority Scope { get; init; }
    /// <summary>Gets the canonical authority checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains one durable activation-attempt observation.</summary>
public sealed record BaseActivationAttemptEvidence
{
    /// <summary>Gets the stable attempt identity.</summary>
    public required string AttemptId { get; init; }
    /// <summary>Gets the positive attempt number.</summary>
    public required int AttemptNumber { get; init; }
    /// <summary>Gets the accepted start instant as Unix milliseconds.</summary>
    public required long StartedAt { get; init; }
    /// <summary>Gets the canonical evidence checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Requests one atomic due seek and claim.</summary>
public sealed record BaseActivationClaimRequest
{
    /// <summary>Gets the current due-observation token.</summary>
    public required BaseDueObservationToken Observation { get; init; }
    /// <summary>Gets the installed worker authority.</summary>
    public required BaseActivationWorkerAuthority Worker { get; init; }
    /// <summary>Gets accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the requested lease duration in milliseconds.</summary>
    public required long LeaseMilliseconds { get; init; }
    /// <summary>Gets the identified claim request.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets effective operation limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Represents the closed result of an atomic claim operation.</summary>
[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationClaimedResult), "claimed")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationClaimEmptyResult), "empty")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationObservationChangedResult), "observationChanged")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationClaimCapacityResult), "capacity")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationRecoveredClaimResult), "recovered")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationClaimExpiredResult), "expired")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationClaimSupersededResult), "superseded")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationClaimCancelledResult), "cancelled")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BaseActivationClaimTerminalResult), "terminal")]
public abstract record BaseActivationClaimResult;

/// <summary>Contains a newly committed activation claim.</summary>
public sealed record BaseActivationClaimedResult(
    BaseActivationPayload Payload,
    BaseActivationClaimAuthority Claim,
    BaseActivationLeaseObservation Lease,
    BaseActivationAttemptEvidence Attempt,
    ImmutableArray<BaseAtomicReadIntervalEvidence> Intervals,
    BaseActivationAccounting Accounting) : BaseActivationClaimResult;

/// <summary>Reports that no eligible activation exists under a replacement observation.</summary>
public sealed record BaseActivationClaimEmptyResult(BaseDueObservationToken Replacement) : BaseActivationClaimResult;

/// <summary>Reports that the supplied observation changed before claiming.</summary>
public sealed record BaseActivationObservationChangedResult(BaseDueObservationToken Replacement) : BaseActivationClaimResult;

/// <summary>Reports bounded provider capacity exhaustion.</summary>
public sealed record BaseActivationClaimCapacityResult(TimeSpan RetryAfter) : BaseActivationClaimResult;

/// <summary>Reports one recovered expired claim; callers must observe again.</summary>
public sealed record BaseActivationRecoveredClaimResult(string ActivationId, long ResultingGeneration) : BaseActivationClaimResult;

/// <summary>Reports that a historically committed claim is no longer leased.</summary>
public sealed record BaseActivationClaimExpiredResult(string ActivationId) : BaseActivationClaimResult;

/// <summary>Reports that a later claim epoch superseded the historical claim.</summary>
public sealed record BaseActivationClaimSupersededResult(string ActivationId) : BaseActivationClaimResult;

/// <summary>Reports that the activation was cancelled after the historical claim.</summary>
public sealed record BaseActivationClaimCancelledResult(string ActivationId) : BaseActivationClaimResult;

/// <summary>Reports that the activation is already terminal.</summary>
public sealed record BaseActivationClaimTerminalResult(string ActivationId, BaseActivationState State) : BaseActivationClaimResult;

/// <summary>Requests renewal of one current activation lease.</summary>
public sealed record BaseActivationRenewRequest
{
    /// <summary>Gets stable claim authority.</summary>
    public required BaseActivationClaimAuthority Claim { get; init; }
    /// <summary>Gets the expected positive lease revision.</summary>
    public required long ExpectedLeaseRevision { get; init; }
    /// <summary>Gets accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the requested lease extension in milliseconds.</summary>
    public required long ExtensionMilliseconds { get; init; }
    /// <summary>Gets the identified renewal request.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets effective operation limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains a committed lease renewal.</summary>
public sealed record BaseActivationRenewResult
{
    /// <summary>Gets the byte-identical stable claim authority.</summary>
    public required BaseActivationClaimAuthority Claim { get; init; }
    /// <summary>Gets the replacement lease observation.</summary>
    public required BaseActivationLeaseObservation Lease { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets duplicate-resolution disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

/// <summary>Classifies a failed activation attempt.</summary>
public enum BaseActivationFailureDisposition
{
    /// <summary>Schedule another deterministic retry.</summary>
    Retry = 0,
    /// <summary>Terminalize the activation as exhausted.</summary>
    Exhaust = 1,
}

/// <summary>Classifies cancellation propagation.</summary>
public enum BaseCancellationPropagation
{
    /// <summary>Cancel only the selected activation.</summary>
    None = 0,
    /// <summary>Cancel all currently unstarted descendants in bounded maintenance.</summary>
    Descendants = 1,
}

/// <summary>Base contract for one identified activation state transition.</summary>
public abstract record BaseActivationTransitionRequest
{
    /// <summary>Gets the activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the identified transition request.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets effective operation limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Requests successful completion by the current claim.</summary>
public sealed record BaseActivationCompleteRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets current stable claim authority.</summary>
    public required BaseActivationClaimAuthority Claim { get; init; }
    /// <summary>Gets canonical result bytes.</summary>
    public required ImmutableArray<byte> CanonicalResult { get; init; }
    /// <summary>Gets the canonical result checksum.</summary>
    public required ImmutableArray<byte> ResultChecksum { get; init; }
}

/// <summary>Requests failed-attempt handling by the current claim.</summary>
public sealed record BaseActivationFailRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets current stable claim authority.</summary>
    public required BaseActivationClaimAuthority Claim { get; init; }
    /// <summary>Gets the stable safe failure code.</summary>
    public required string StableFailureCode { get; init; }
    /// <summary>Gets retry or exhaustion disposition.</summary>
    public required BaseActivationFailureDisposition Disposition { get; init; }
    /// <summary>Gets the exact Runtime-computed retry due instant, required only for retry.</summary>
    public long? RetryDueAt { get; init; }
}

/// <summary>Requests cancellation using an exact control generation.</summary>
public sealed record BaseActivationCancelRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets the expected activation generation.</summary>
    public required long ExpectedGeneration { get; init; }
    /// <summary>Gets cancellation propagation.</summary>
    public required BaseCancellationPropagation Propagation { get; init; }
}

/// <summary>Requests durable effect-start before one external side effect.</summary>
public sealed record BaseActivationBeginEffectRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets the current activation claim.</summary>
    public required BaseActivationClaimAuthority Claim { get; init; }
    /// <summary>Gets the complete current executor incarnation.</summary>
    public required BaseExecutorIncarnationAuthority Executor { get; init; }
    /// <summary>Gets the current executor heartbeat observation.</summary>
    public required BaseExecutorHeartbeatObservation ExecutorHeartbeat { get; init; }
    /// <summary>Gets the requested effect-heartbeat lifetime.</summary>
    public required long HeartbeatMilliseconds { get; init; }
}

/// <summary>Requests renewal of one started external-effect heartbeat.</summary>
public sealed record BaseActivationEffectHeartbeatRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets current effect authority.</summary>
    public required BaseEffectExecutionAuthority Effect { get; init; }
    /// <summary>Gets the expected effect-heartbeat revision.</summary>
    public required long ExpectedHeartbeatRevision { get; init; }
    /// <summary>Gets the requested heartbeat extension.</summary>
    public required long ExtensionMilliseconds { get; init; }
}

/// <summary>Requests successful terminalization of one started external effect.</summary>
public sealed record BaseActivationCompleteEffectRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets current effect authority.</summary>
    public required BaseEffectExecutionAuthority Effect { get; init; }
    /// <summary>Gets canonical result bytes.</summary>
    public required ImmutableArray<byte> CanonicalResult { get; init; }
    /// <summary>Gets the canonical result checksum.</summary>
    public required ImmutableArray<byte> ResultChecksum { get; init; }
}

/// <summary>Requests recovery of an expired effect whose external outcome is unknowable.</summary>
public sealed record BaseActivationRecoverEffectRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets the exact effect authority expected to be abandoned.</summary>
    public required BaseEffectExecutionAuthority Effect { get; init; }
}

/// <summary>Classifies an operator-verified resolution of an ambiguous external effect.</summary>
public enum BaseEffectReconciliationDisposition
{
    /// <summary>The external effect is verified successful.</summary>
    Succeeded = 0,
    /// <summary>The external effect is verified failed and terminal.</summary>
    Exhausted = 1,
    /// <summary>The ambiguous activation is administratively disposed.</summary>
    Disposed = 2,
}

/// <summary>Requests identified operator reconciliation of one outcome-unknown effect.</summary>
public sealed record BaseActivationReconcileEffectRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets the expected effect-start generation retained with ambiguity.</summary>
    public required long ExpectedEffectStartGeneration { get; init; }
    /// <summary>Gets the expected retained effect-authority checksum.</summary>
    public required ImmutableArray<byte> ExpectedEffectChecksum { get; init; }
    /// <summary>Gets the expected activation generation in the outcome-unknown state.</summary>
    public required long ExpectedGeneration { get; init; }
    /// <summary>Gets the selected terminal disposition.</summary>
    public required BaseEffectReconciliationDisposition Disposition { get; init; }
    /// <summary>Gets bounded canonical external verification evidence.</summary>
    public required ImmutableArray<byte> VerificationEvidence { get; init; }
    /// <summary>Gets the SHA-256 checksum of the verification evidence.</summary>
    public required ImmutableArray<byte> VerificationChecksum { get; init; }
}

/// <summary>Requests an identified operator retry of one exhausted activation.</summary>
public sealed record BaseActivationOperatorRetryRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets the exact exhausted activation generation.</summary>
    public required long ExpectedGeneration { get; init; }
    /// <summary>Gets the Runtime-accepted retry due instant.</summary>
    public required long RetryDueAt { get; init; }
}

/// <summary>Requests identified disposal of retained terminal activation authority.</summary>
public sealed record BaseActivationDisposeRequest : BaseActivationTransitionRequest
{
    /// <summary>Gets the exact terminal activation generation.</summary>
    public required long ExpectedGeneration { get; init; }
}

/// <summary>Contains one committed activation transition.</summary>
public sealed record BaseActivationTransitionResult
{
    /// <summary>Gets the resulting state.</summary>
    public required BaseActivationState State { get; init; }
    /// <summary>Gets the resulting activation generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets the resulting control checksum.</summary>
    public required ImmutableArray<byte> ControlChecksum { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets duplicate-resolution disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
    /// <summary>Gets current effect authority for effect-start and heartbeat transitions.</summary>
    public BaseEffectExecutionAuthority? Effect { get; init; }
    /// <summary>Gets canonical graph-owned result bytes when this transition commits a result.</summary>
    public ImmutableArray<byte> CanonicalResult { get; init; }
}

/// <summary>Requests registration of one durable worker-process incarnation.</summary>
public sealed record BaseExecutorRegistrationRequest
{
    /// <summary>Gets the application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the stable host identity.</summary>
    public required string HostId { get; init; }
    /// <summary>Gets the unique process-incarnation identity.</summary>
    public required string ProcessIncarnationId { get; init; }
    /// <summary>Gets the installed worker-definition-set checksum.</summary>
    public required ImmutableArray<byte> WorkerDefinitionSetChecksum { get; init; }
    /// <summary>Gets requested heartbeat lifetime in milliseconds.</summary>
    public required long RequestedHeartbeatMilliseconds { get; init; }
    /// <summary>Gets trusted accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the identified operation identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets effective limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Requests renewal of one exact executor heartbeat.</summary>
public sealed record BaseExecutorHeartbeatRequest
{
    /// <summary>Gets stable executor authority.</summary>
    public required BaseExecutorIncarnationAuthority Executor { get; init; }
    /// <summary>Gets expected heartbeat revision.</summary>
    public required long ExpectedHeartbeatRevision { get; init; }
    /// <summary>Gets requested extension in milliseconds.</summary>
    public required long ExtensionMilliseconds { get; init; }
    /// <summary>Gets trusted accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets identified operation identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets effective limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Requests retirement of one exact executor incarnation.</summary>
public sealed record BaseExecutorRetirementRequest
{
    /// <summary>Gets stable executor authority.</summary>
    public required BaseExecutorIncarnationAuthority Executor { get; init; }
    /// <summary>Gets expected heartbeat revision.</summary>
    public required long ExpectedHeartbeatRevision { get; init; }
    /// <summary>Gets trusted accepted-time authority.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets identified operation identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets effective limits.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains a newly registered executor incarnation.</summary>
public sealed record BaseExecutorRegistrationResult
{
    /// <summary>Gets stable incarnation authority.</summary>
    public required BaseExecutorIncarnationAuthority Executor { get; init; }
    /// <summary>Gets initial heartbeat observation.</summary>
    public required BaseExecutorHeartbeatObservation Heartbeat { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets request disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

/// <summary>Contains a committed executor heartbeat replacement.</summary>
public sealed record BaseExecutorHeartbeatResult
{
    /// <summary>Gets byte-identical stable incarnation authority.</summary>
    public required BaseExecutorIncarnationAuthority Executor { get; init; }
    /// <summary>Gets replacement heartbeat observation.</summary>
    public required BaseExecutorHeartbeatObservation Heartbeat { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets request disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

/// <summary>Contains committed terminal executor-registry evidence.</summary>
public sealed record BaseExecutorRetirementResult
{
    /// <summary>Gets stable retired incarnation authority.</summary>
    public required BaseExecutorIncarnationAuthority Executor { get; init; }
    /// <summary>Gets the terminal heartbeat revision.</summary>
    public required long HeartbeatRevision { get; init; }
    /// <summary>Gets terminal evidence checksum.</summary>
    public required ImmutableArray<byte> RetirementChecksum { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets request disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

/// <summary>Describes one installed activation provider.</summary>
public sealed record BaseActivationProviderDescriptor
{
    /// <summary>Gets the stable provider identity.</summary>
    public required string ProviderId { get; init; }
    /// <summary>Gets the provider implementation version.</summary>
    public required string ProviderVersion { get; init; }
    /// <summary>Gets the protocol version.</summary>
    public required int ProtocolVersion { get; init; }
    /// <summary>Gets provider capability.</summary>
    public required BaseActivationProviderCapability Capability { get; init; }
}

/// <summary>Declares certified activation-provider features and maxima.</summary>
public sealed record BaseActivationProviderCapability
{
    /// <summary>Gets whether atomic activation creation is supported.</summary>
    public required bool AtomicCreationSupported { get; init; }
    /// <summary>Gets whether transaction-bound selection targets are supported.</summary>
    public required bool SelectionTargetSupported { get; init; }
    /// <summary>Gets whether registered module-mutation targets are supported.</summary>
    public required bool ModuleTargetSupported { get; init; }
    /// <summary>Gets whether activation-guarded children are supported.</summary>
    public required bool GuardedChildrenSupported { get; init; }
    /// <summary>Gets whether restore fencing is supported.</summary>
    public required bool RestoreFencingSupported { get; init; }
    /// <summary>Gets due invalidation behavior.</summary>
    public required BaseDueInvalidationClass DueInvalidation { get; init; }
    /// <summary>Gets the closed supported schedule kinds.</summary>
    public required ImmutableArray<BaseScheduleKind> ScheduleKinds { get; init; }
    /// <summary>Gets the closed supported execution classes.</summary>
    public required ImmutableArray<BaseActivationExecutionClass> ExecutionClasses { get; init; }
    /// <summary>Gets maximum activations created in one transaction.</summary>
    public required int MaximumActivationsPerTransaction { get; init; }
    /// <summary>Gets maximum due candidates per seek.</summary>
    public required int MaximumDueCandidates { get; init; }
    /// <summary>Gets maximum canonical input bytes.</summary>
    public required long MaximumInputBytes { get; init; }
    /// <summary>Gets maximum canonical result bytes.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets maximum evidence bytes.</summary>
    public required long MaximumEvidenceBytes { get; init; }
    /// <summary>Gets maximum transient bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets maximum receipt bytes.</summary>
    public required long MaximumReceiptBytes { get; init; }
    /// <summary>Gets maximum pending rows.</summary>
    public required int MaximumPendingRows { get; init; }
    /// <summary>Gets maximum claimed rows.</summary>
    public required int MaximumClaimedRows { get; init; }
    /// <summary>Gets maximum terminal rows.</summary>
    public required int MaximumTerminalRows { get; init; }
    /// <summary>Gets maximum attempts.</summary>
    public required int MaximumAttempts { get; init; }
    /// <summary>Gets maximum renewals per attempt.</summary>
    public required int MaximumRenewalsPerAttempt { get; init; }
    /// <summary>Gets maximum guarded children per attempt.</summary>
    public required int MaximumChildrenPerAttempt { get; init; }
    /// <summary>Gets maximum lineage depth.</summary>
    public required int MaximumLineageDepth { get; init; }
    /// <summary>Gets maximum occurrence page size.</summary>
    public required int MaximumOccurrencePage { get; init; }
    /// <summary>Gets maximum installed time-zone authority bytes.</summary>
    public required long MaximumTimeZoneBytes { get; init; }
    /// <summary>Gets maximum acquisition deadline.</summary>
    public required TimeSpan AcquisitionDeadline { get; init; }
    /// <summary>Gets maximum transaction deadline.</summary>
    public required TimeSpan TransactionDeadline { get; init; }
    /// <summary>Gets maximum observation wait deadline.</summary>
    public required TimeSpan ObservationWaitDeadline { get; init; }
    /// <summary>Gets maximum renewal deadline.</summary>
    public required TimeSpan RenewalDeadline { get; init; }
    /// <summary>Gets maximum commit-observation deadline.</summary>
    public required TimeSpan CommitObservationDeadline { get; init; }
    /// <summary>Gets maximum receipt-resolution deadline.</summary>
    public required TimeSpan ReceiptResolutionDeadline { get; init; }
    /// <summary>Gets maximum maintenance deadline.</summary>
    public required TimeSpan MaintenanceDeadline { get; init; }
    /// <summary>Gets maximum shutdown-drain deadline.</summary>
    public required TimeSpan ShutdownDrainDeadline { get; init; }
    /// <summary>Gets retained non-cooperative provider capacity.</summary>
    public required int ProviderQuarantineSlots { get; init; }
    /// <summary>Gets retained non-cooperative handler capacity.</summary>
    public required int HandlerQuarantineSlots { get; init; }
    /// <summary>Gets the canonical capability checksum.</summary>
    public required ImmutableArray<byte> CanonicalChecksum { get; init; }
}

/// <summary>Validates and supplies the built-in durable-activation capability contract.</summary>
public static class BaseActivationCapabilityContract
{
    /// <summary>Creates the certified built-in semantic envelope.</summary>
    public static BaseActivationProviderCapability BuiltIn(string checksumPurpose) => new()
    {
        AtomicCreationSupported = true, SelectionTargetSupported = true, ModuleTargetSupported = true,
        GuardedChildrenSupported = true, RestoreFencingSupported = true,
        DueInvalidation = BaseDueInvalidationClass.BoundedPolling,
        ScheduleKinds = [BaseScheduleKind.Once, BaseScheduleKind.Interval, BaseScheduleKind.Cron, BaseScheduleKind.Calendar],
        ExecutionClasses = [BaseActivationExecutionClass.TransactionalOperation, BaseActivationExecutionClass.AtLeastOnceWorker, BaseActivationExecutionClass.AtMostOnceEffect],
        MaximumActivationsPerTransaction = 256, MaximumDueCandidates = 256,
        MaximumInputBytes = 4L * 1024 * 1024, MaximumResultBytes = 4L * 1024 * 1024,
        MaximumEvidenceBytes = 16L * 1024 * 1024, MaximumTransientBytes = 16L * 1024 * 1024,
        MaximumReceiptBytes = 16L * 1024 * 1024, MaximumPendingRows = 1_000_000,
        MaximumClaimedRows = 1_000_000, MaximumTerminalRows = 1_000_000,
        MaximumAttempts = 1024, MaximumRenewalsPerAttempt = 4096, MaximumChildrenPerAttempt = 4096,
        MaximumLineageDepth = 256, MaximumOccurrencePage = 256, MaximumTimeZoneBytes = 64L * 1024 * 1024,
        AcquisitionDeadline = TimeSpan.FromSeconds(5), TransactionDeadline = TimeSpan.FromSeconds(30),
        ObservationWaitDeadline = TimeSpan.FromMinutes(5), RenewalDeadline = TimeSpan.FromSeconds(5),
        CommitObservationDeadline = TimeSpan.FromSeconds(30), ReceiptResolutionDeadline = TimeSpan.FromSeconds(30),
        MaintenanceDeadline = TimeSpan.FromMinutes(5), ShutdownDrainDeadline = TimeSpan.FromSeconds(60),
        ProviderQuarantineSlots = 32, HandlerQuarantineSlots = 32,
        CanonicalChecksum = ImmutableArray.CreateRange(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(checksumPurpose))),
    };

    /// <summary>Returns whether the capability is a closed valid safety envelope.</summary>
    public static bool IsValid(BaseActivationProviderCapability? value) => value is not null
        && value.AtomicCreationSupported && value.GuardedChildrenSupported && value.RestoreFencingSupported
        && Enum.IsDefined(value.DueInvalidation)
        && !value.ScheduleKinds.IsDefaultOrEmpty && value.ScheduleKinds.Distinct().Count() == value.ScheduleKinds.Length
        && !value.ExecutionClasses.IsDefaultOrEmpty && value.ExecutionClasses.Distinct().Count() == value.ExecutionClasses.Length
        && value.MaximumActivationsPerTransaction is >= 1 and <= 256
        && value.MaximumDueCandidates is >= 1 and <= 256
        && value.MaximumInputBytes is >= 1 and <= 4L * 1024 * 1024
        && value.MaximumResultBytes is >= 1 and <= 4L * 1024 * 1024
        && value.MaximumReceiptBytes is >= 1 and <= 16L * 1024 * 1024
        && value.MaximumEvidenceBytes is >= 1 and <= 16L * 1024 * 1024
        && value.MaximumTransientBytes is >= 1 and <= 16L * 1024 * 1024
        && value.MaximumAttempts is >= 1 and <= 1024
        && value.MaximumRenewalsPerAttempt is >= 1 and <= 4096
        && value.MaximumChildrenPerAttempt is >= 1 and <= 4096
        && value.MaximumLineageDepth is >= 1 and <= 256
        && value.MaximumOccurrencePage is >= 1 and <= 256
        && value.AcquisitionDeadline > TimeSpan.Zero && value.AcquisitionDeadline <= TimeSpan.FromSeconds(5)
        && value.TransactionDeadline > TimeSpan.Zero && value.TransactionDeadline <= TimeSpan.FromSeconds(30)
        && value.ShutdownDrainDeadline > TimeSpan.Zero && value.ShutdownDrainDeadline <= TimeSpan.FromSeconds(60)
        && value.ProviderQuarantineSlots > 0 && value.HandlerQuarantineSlots > 0
        && value.CanonicalChecksum.Length == 32;

    internal static void Require(BaseActivationProviderCapability capability, BaseActivationDefinition definition)
    {
        if (!IsValid(capability) || !capability.ExecutionClasses.Contains(definition.ExecutionClass)
            || definition.TransactionalTarget is BaseSelectionMutationActivationTarget && !capability.SelectionTargetSupported
            || definition.TransactionalTarget is BaseModuleMutationActivationTarget && !capability.ModuleTargetSupported
            || definition.Limits.MaximumInputBytes > capability.MaximumInputBytes
            || definition.Limits.MaximumResultBytes > capability.MaximumResultBytes
            || definition.Limits.MaximumAttempts > capability.MaximumAttempts
            || definition.Limits.MaximumRenewalsPerAttempt > capability.MaximumRenewalsPerAttempt
            || definition.Limits.MaximumChildrenPerAttempt > capability.MaximumChildrenPerAttempt
            || definition.Limits.MaximumLineageDepth > capability.MaximumLineageDepth)
            throw new InvalidOperationException("base.activation.capabilityUnavailable");
    }

    internal static void Require(BaseActivationProviderCapability capability, BaseScheduleDefinition definition)
    {
        BaseScheduleKind kind = definition.Expression switch
        {
            BaseOnceSchedule => BaseScheduleKind.Once, BaseIntervalSchedule => BaseScheduleKind.Interval,
            BaseCronSchedule => BaseScheduleKind.Cron, BaseCalendarSchedule => BaseScheduleKind.Calendar,
            _ => throw new InvalidOperationException("base.activation.scheduleInvalid"),
        };
        if (!IsValid(capability) || !capability.ScheduleKinds.Contains(kind))
            throw new InvalidOperationException("base.activation.capabilityUnavailable");
    }
}

/// <summary>Defines provider-neutral durable activation operations.</summary>
public interface IBaseActivationProvider
{
    /// <summary>Gets the immutable provider descriptor.</summary>
    BaseActivationProviderDescriptor Descriptor { get; }

    /// <summary>Observes the earliest due activation under exact authority.</summary>
    ValueTask<OperationResult<BaseActivationDueObservation>> ObserveDueAsync(
        BaseActivationDueObservationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Waits for a finite due observation to change.</summary>
    ValueTask<BaseDueWaitResult> WaitForDueChangeAsync(
        BaseDueObservationToken token,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically seeks and claims the first eligible activation.</summary>
    ValueTask<OperationResult<BaseActivationClaimResult>> TryClaimNextAsync(
        BaseActivationClaimRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the exact handler-free activation selected for transactional execution.</summary>
    ValueTask<OperationResult<BaseTransactionalActivationCandidate>> ReadTransactionalCandidateAsync(
        BaseTransactionalActivationCandidateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Renews one current stable claim.</summary>
    ValueTask<OperationResult<BaseActivationRenewResult>> RenewAsync(
        BaseActivationRenewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Registers one durable executor incarnation.</summary>
    ValueTask<OperationResult<BaseExecutorRegistrationResult>> RegisterExecutorAsync(
        BaseExecutorRegistrationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Renews one current executor heartbeat.</summary>
    ValueTask<OperationResult<BaseExecutorHeartbeatResult>> HeartbeatExecutorAsync(
        BaseExecutorHeartbeatRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Retires one exact executor incarnation.</summary>
    ValueTask<OperationResult<BaseExecutorRetirementResult>> RetireExecutorAsync(
        BaseExecutorRetirementRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Applies one closed activation state transition.</summary>
    ValueTask<OperationResult<BaseActivationTransitionResult>> TransitionAsync(
        BaseActivationTransitionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one exact current durable schedule authority.</summary>
    ValueTask<OperationResult<BaseScheduleAuthority>> ReadScheduleAsync(
        string scheduleId,
        int scheduleVersion,
        CancellationToken cancellationToken = default);

    /// <summary>Applies one identified schedule create/update/state mutation.</summary>
    ValueTask<OperationResult<BaseScheduleMutationResult>> MutateScheduleAsync(
        BaseScheduleMutationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically applies one Runtime-computed occurrence page.</summary>
    ValueTask<OperationResult<BaseScheduleMaintenancePage>> AdvanceSchedulesAsync(
        BaseScheduleMaintenanceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Advances one crash-recoverable cancel-previous maintenance page.</summary>
    ValueTask<OperationResult<BaseScheduleCancellationMaintenancePage>> AdvanceScheduleCancellationAsync(
        BaseScheduleCancellationMaintenanceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves one durable receipt without re-executing its operation.</summary>
    ValueTask<OperationResult<BaseActivationReceiptResolution>> ResolveReceiptAsync(
        BaseActivationReceiptResolutionRequest request,
        CancellationToken cancellationToken = default);
}
