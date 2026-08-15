using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.WorkRequirement;

/// <summary>Names the durable lifecycle disposition of one required work item.</summary>
public enum WorkDisposition
{
    /// <summary>Invalid default disposition.</summary>
    None = 0,
    /// <summary>The work is durably required and discoverable.</summary>
    Required,
    /// <summary>A current claim epoch owns an attempt lease.</summary>
    Claimed,
    /// <summary>The owner postcondition was freshly verified.</summary>
    Verified,
    /// <summary>The work remains retryable after a retained failed attempt.</summary>
    RetryRequired,
    /// <summary>The immutable original is poison and requires governed replacement.</summary>
    Poison,
    /// <summary>The retry budget is exhausted with a retained terminal history.</summary>
    Exhausted,
}

/// <summary>Defines immutable durable work, its compatibility pins, and bounded retry policy.</summary>
public sealed record WorkRequirementFact
{
    /// <summary>Maximum attempts admitted by one requirement.</summary>
    public const int MaximumAttempts = 1024;
    /// <summary>Gets the durable work identity.</summary>
    public SemanticId WorkId { get; }
    /// <summary>Gets the owner fact whose postcondition requires this work.</summary>
    public SemanticId OwnerFactId { get; }
    /// <summary>Gets the exact immutable work payload digest.</summary>
    public CanonicalDigest PayloadDigest { get; }
    /// <summary>Gets the handler schema version pinned at creation.</summary>
    public ContractVersion HandlerVersion { get; }
    /// <summary>Gets the deployment generation required at activation.</summary>
    public Revision DeploymentGeneration { get; }
    /// <summary>Gets the earliest requested activation time.</summary>
    public NamedTime RequestedAt { get; }
    /// <summary>Gets the bounded total attempt limit.</summary>
    public int MaximumAttemptCount { get; }

    /// <summary>Creates an immutable, discoverable work requirement.</summary>
    /// <exception cref="ArgumentException">Identity, version, generation, time, or retry bound is invalid.</exception>
    public WorkRequirementFact(SemanticId workId, SemanticId ownerFactId, CanonicalDigest payloadDigest,
        ContractVersion handlerVersion, Revision deploymentGeneration, NamedTime requestedAt, int maximumAttemptCount)
    {
        ArgumentNullException.ThrowIfNull(payloadDigest);
        if (!workId.IsValid || !ownerFactId.IsValid || workId.Scope != ownerFactId.Scope || !handlerVersion.IsValid ||
            !deploymentGeneration.IsValid || !requestedAt.IsValid || requestedAt.Kind != TimeKind.Requested || maximumAttemptCount is < 1 or > MaximumAttempts)
            throw new ArgumentException("Work requirement requires one scope, pinned compatibility, Requested time, and bounded retries.");
        WorkId = workId; OwnerFactId = ownerFactId; PayloadDigest = payloadDigest; HandlerVersion = handlerVersion;
        DeploymentGeneration = deploymentGeneration; RequestedAt = requestedAt; MaximumAttemptCount = maximumAttemptCount;
    }
}

/// <summary>Records a fenced work claim; stale epochs cannot commit an attempt or completion.</summary>
public sealed record WorkClaim
{
    /// <summary>Gets the claimed work identity.</summary>
    public SemanticId WorkId { get; }
    /// <summary>Gets the monotone claim epoch.</summary>
    public OwnerGeneration Epoch { get; }
    /// <summary>Gets the bounded worker identity.</summary>
    public string Worker { get; }
    /// <summary>Gets the claim expiry time.</summary>
    public NamedTime ExpiresAt { get; }

    /// <summary>Creates a claim that owns no work outcome by itself.</summary>
    /// <exception cref="ArgumentException">Identity, epoch, worker, or expiry time is invalid.</exception>
    public WorkClaim(SemanticId workId, OwnerGeneration epoch, string worker, NamedTime expiresAt)
    {
        if (!workId.IsValid || !epoch.IsValid || !ScopeId.TryCreate("token", "worker", worker, out _) || !expiresAt.IsValid || expiresAt.Kind != TimeKind.Expiry)
            throw new ArgumentException("Work claim requires valid identity, epoch, worker, and Expiry time.");
        WorkId = workId; Epoch = epoch; Worker = worker; ExpiresAt = expiresAt;
    }

    /// <summary>Returns whether the supplied epoch may commit for this claim.</summary>
    /// <param name="currentEpoch">The authoritative current epoch read in the commit domain.</param>
    /// <returns><see langword="true"/> only for an exact current-epoch match.</returns>
    public bool CanCommit(OwnerGeneration currentEpoch) => currentEpoch.IsValid && currentEpoch == Epoch;
}

/// <summary>Records one immutable work disposition without treating handler return or acknowledgement as owner success.</summary>
public sealed record WorkDispositionFact
{
    /// <summary>Gets the requirement being dispositioned.</summary>
    public WorkRequirementFact Requirement { get; }
    /// <summary>Gets the claim epoch that produced this fact, absent only for Required.</summary>
    public OwnerGeneration ClaimEpoch { get; }
    /// <summary>Gets the explicit lifecycle disposition.</summary>
    public WorkDisposition Disposition { get; }
    /// <summary>Gets the number of retained attempts.</summary>
    public int AttemptCount { get; }
    /// <summary>Gets the bounded reason or verification code.</summary>
    public string Code { get; }
    /// <summary>Gets the result or owner-postcondition evidence digest.</summary>
    public CanonicalDigest EvidenceDigest { get; }

    /// <summary>Creates an immutable work disposition.</summary>
    /// <exception cref="ArgumentException">Disposition, epoch, count, or code is invalid.</exception>
    public WorkDispositionFact(WorkRequirementFact requirement, OwnerGeneration claimEpoch, WorkDisposition disposition,
        int attemptCount, string code, CanonicalDigest evidenceDigest)
    {
        ArgumentNullException.ThrowIfNull(requirement); ArgumentNullException.ThrowIfNull(evidenceDigest);
        var needsClaim = disposition != WorkDisposition.Required;
        if (disposition == WorkDisposition.None || !Enum.IsDefined(disposition) || needsClaim != claimEpoch.IsValid ||
            attemptCount < 0 || attemptCount > requirement.MaximumAttemptCount ||
            disposition == WorkDisposition.Exhausted && attemptCount != requirement.MaximumAttemptCount ||
            !ScopeId.TryCreate("token", "code", code, out _))
            throw new ArgumentException("Work disposition requires explicit state, matching claim epoch, bounded attempts, and code.");
        Requirement = requirement; ClaimEpoch = claimEpoch; Disposition = disposition; AttemptCount = attemptCount; Code = code; EvidenceDigest = evidenceDigest;
    }
}
