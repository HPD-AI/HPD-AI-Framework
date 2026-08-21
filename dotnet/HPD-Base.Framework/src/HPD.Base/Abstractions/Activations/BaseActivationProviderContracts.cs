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
    /// <summary>Gets whether activation-guarded children are supported.</summary>
    public required bool GuardedChildrenSupported { get; init; }
    /// <summary>Gets whether restore fencing is supported.</summary>
    public required bool RestoreFencingSupported { get; init; }
    /// <summary>Gets due invalidation behavior.</summary>
    public required BaseDueInvalidationClass DueInvalidation { get; init; }
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
    /// <summary>Gets the canonical capability checksum.</summary>
    public required ImmutableArray<byte> CanonicalChecksum { get; init; }
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
}
