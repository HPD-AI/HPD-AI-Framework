using HPD.Payments.Contracts.WorkRequirement;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Runtime.DurableWork;

/// <summary>Names a bounded observation produced by one claimed work attempt.</summary>
public enum WorkAttemptObservation
{
    /// <summary>Invalid default observation.</summary>
    None = 0,
    /// <summary>The handler returned, but the owner postcondition has not been verified.</summary>
    HandlerReturned,
    /// <summary>Fresh owner evidence verifies the required postcondition.</summary>
    OwnerPostconditionVerified,
    /// <summary>A definite retryable failure was retained.</summary>
    DefiniteFailure,
    /// <summary>The attempt outcome is unknown and requires reconciliation before retry.</summary>
    Indeterminate,
    /// <summary>The immutable work payload or handler binding is poison.</summary>
    Poison,
}

/// <summary>Represents an immutable transition result for the adapter-neutral work protocol.</summary>
public sealed record WorkProtocolTransition
{
    /// <summary>Gets the resulting protocol state.</summary>
    public WorkProtocolState State { get; }
    /// <summary>Gets whether the proposed transition was accepted.</summary>
    public bool Accepted { get; }
    /// <summary>Gets a bounded stable result code.</summary>
    public string Code { get; }

    internal WorkProtocolTransition(WorkProtocolState state, bool accepted, string code)
    {
        State = state;
        Accepted = accepted;
        Code = code;
    }
}

/// <summary>Provides an immutable, storage-independent Work Requirement lifecycle kernel.</summary>
/// <remarks>
/// The kernel owns no scheduling, clock, queue, lease, thread, or persistence mechanism. Its outputs must be
/// compare-bound and appended by the configured persistence authority.
/// </remarks>
public sealed record WorkProtocolState
{
    /// <summary>Gets the immutable work requirement.</summary>
    public WorkRequirementFact Requirement { get; }
    /// <summary>Gets the current claim epoch, invalid before the first claim.</summary>
    public OwnerGeneration ClaimEpoch { get; }
    /// <summary>Gets the current claim, or <see langword="null"/> when unclaimed.</summary>
    public WorkClaim? Claim { get; }
    /// <summary>Gets the number of retained attempt observations.</summary>
    public int AttemptCount { get; }
    /// <summary>Gets the current lifecycle disposition.</summary>
    public WorkDisposition Disposition { get; }
    /// <summary>Gets whether reconciliation is required before another execution attempt.</summary>
    public bool RequiresReconciliation { get; }

    private WorkProtocolState(WorkRequirementFact requirement, OwnerGeneration claimEpoch, WorkClaim? claim,
        int attemptCount, WorkDisposition disposition, bool requiresReconciliation)
    {
        Requirement = requirement;
        ClaimEpoch = claimEpoch;
        Claim = claim;
        AttemptCount = attemptCount;
        Disposition = disposition;
        RequiresReconciliation = requiresReconciliation;
    }

    /// <summary>Creates the initial discoverable requirement state.</summary>
    /// <param name="requirement">The immutable requirement.</param>
    /// <returns>A required, unclaimed state.</returns>
    public static WorkProtocolState Create(WorkRequirementFact requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        return new(requirement, default, null, 0, WorkDisposition.Required, false);
    }

    /// <summary>Attempts to establish the next monotone claim epoch.</summary>
    /// <param name="worker">The bounded worker identity.</param>
    /// <param name="expiresAt">The explicit claim expiry.</param>
    /// <returns>An accepted claim transition or an unchanged rejection.</returns>
    public WorkProtocolTransition TryClaim(string worker, NamedTime expiresAt)
    {
        if (Disposition is WorkDisposition.Verified or WorkDisposition.Poison or WorkDisposition.Exhausted)
            return Reject("terminal-work");
        if (RequiresReconciliation)
            return Reject("reconciliation-required");
        if (Claim is not null)
            return Reject("claim-already-active");

        var epoch = OwnerGeneration.Create(ClaimEpoch.IsValid ? checked(ClaimEpoch.Value + 1) : 1);
        var claim = new WorkClaim(Requirement.WorkId, epoch, worker, expiresAt);
        return Accept(new(Requirement, epoch, claim, AttemptCount, WorkDisposition.Claimed, false), "claimed");
    }

    /// <summary>Records an observation only when the caller presents the exact current claim epoch.</summary>
    /// <param name="epoch">The claim epoch attempting the transition.</param>
    /// <param name="observation">The retained observation.</param>
    /// <returns>An accepted disposition transition or an unchanged stale/invalid rejection.</returns>
    public WorkProtocolTransition Observe(OwnerGeneration epoch, WorkAttemptObservation observation)
    {
        if (Claim is null || !Claim.CanCommit(epoch))
            return Reject("stale-claim");
        if (observation == WorkAttemptObservation.None || !Enum.IsDefined(observation))
            return Reject("invalid-observation");

        var attempts = checked(AttemptCount + 1);
        return observation switch
        {
            WorkAttemptObservation.OwnerPostconditionVerified =>
                Accept(new(Requirement, ClaimEpoch, null, attempts, WorkDisposition.Verified, false), "owner-postcondition-verified"),
            WorkAttemptObservation.Poison =>
                Accept(new(Requirement, ClaimEpoch, null, attempts, WorkDisposition.Poison, false), "poison"),
            WorkAttemptObservation.Indeterminate or WorkAttemptObservation.HandlerReturned =>
                Accept(new(Requirement, ClaimEpoch, null, attempts,
                    attempts == Requirement.MaximumAttemptCount ? WorkDisposition.Exhausted : WorkDisposition.RetryRequired, true),
                    observation == WorkAttemptObservation.HandlerReturned ? "handler-return-unverified" : "attempt-indeterminate"),
            WorkAttemptObservation.DefiniteFailure when attempts == Requirement.MaximumAttemptCount =>
                Accept(new(Requirement, ClaimEpoch, null, attempts, WorkDisposition.Exhausted, false), "attempts-exhausted"),
            WorkAttemptObservation.DefiniteFailure =>
                Accept(new(Requirement, ClaimEpoch, null, attempts, WorkDisposition.RetryRequired, false), "retry-required"),
            _ => Reject("invalid-observation"),
        };
    }

    /// <summary>Resolves an indeterminate attempt without inferring success from worker or queue state.</summary>
    /// <param name="ownerPostconditionVerified">Whether fresh owner evidence proves the postcondition.</param>
    /// <returns>A verified terminal or retry/exhaustion transition.</returns>
    public WorkProtocolTransition Reconcile(bool ownerPostconditionVerified)
    {
        if (!RequiresReconciliation)
            return Reject("reconciliation-not-required");
        if (ownerPostconditionVerified)
            return Accept(new(Requirement, ClaimEpoch, null, AttemptCount, WorkDisposition.Verified, false), "reconciled-verified");
        var disposition = AttemptCount == Requirement.MaximumAttemptCount ? WorkDisposition.Exhausted : WorkDisposition.RetryRequired;
        return Accept(new(Requirement, ClaimEpoch, null, AttemptCount, disposition, false), "reconciled-not-verified");
    }

    private WorkProtocolTransition Reject(string code) => new(this, false, code);
    private static WorkProtocolTransition Accept(WorkProtocolState state, string code) => new(state, true, code);
}
