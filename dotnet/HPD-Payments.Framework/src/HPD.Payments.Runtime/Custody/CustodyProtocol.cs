using HPD.Payments.Primitives.Identity;
using HPD.Payments.Supporting.Custody;

namespace HPD.Payments.Runtime.Custody;

/// <summary>Immutable per-instance custody transition kernel.</summary>
/// <remarks>It never infers global deletion from one controller's observation.</remarks>
public sealed record CustodyProtocol
{
    /// <summary>Gets the latest observation for the exact named instance.</summary>
    public CustodyInstance Current { get; }

    private CustodyProtocol(CustodyInstance current) => Current = current;

    /// <summary>Creates a protocol from an admitted inventory observation.</summary>
    public static CustodyProtocol Create(CustodyInstance initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        return new(initial);
    }

    /// <summary>Attempts to append the next exact instance-generation observation.</summary>
    public CustodyTransition Observe(CustodyInstance next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (next.InstanceId != Current.InstanceId || next.Subject.SubjectId != Current.Subject.SubjectId ||
            next.ControllerId != Current.ControllerId)
            return Reject("instance-mismatch");
        if (next.InventoryGeneration.Value != checked(Current.InventoryGeneration.Value + 1))
            return Reject("inventory-generation-conflict");
        if (next.ObservedAt.Value <= Current.ObservedAt.Value)
            return Reject("custody-observation-not-monotone");
        if ((next.PolicyRevision != Current.PolicyRevision || next.HoldRevision != Current.HoldRevision) &&
            next.State is CustodyState.Requested or CustodyState.VerifiedAbsent)
            return Reject("custody-policy-drift");
        if (Current.State is CustodyState.Held or CustodyState.RetentionRequired &&
            next.State is CustodyState.Requested or CustodyState.VerifiedAbsent)
            return Reject("hold-or-retention-blocks-disposition");
        if (next.State == CustodyState.VerifiedAbsent && Current.State != CustodyState.Requested)
            return Reject("absence-not-requested");
        if (Current.State == CustodyState.VerifiedAbsent && next.State != CustodyState.KnownPresent)
            return Reject("restore-must-reopen-present");
        return Accept(new(next), next.State.ToString());
    }

    /// <summary>Gets whether local cleanup may sweep this exact instance observation.</summary>
    public bool MaySweep(OwnerGeneration verifiedThrough) =>
        verifiedThrough.IsValid && Current.State == CustodyState.VerifiedAbsent &&
        Current.InventoryGeneration.Value <= verifiedThrough.Value;

    private CustodyTransition Reject(string code) => new(this, false, code);
    private static CustodyTransition Accept(CustodyProtocol protocol, string code) => new(protocol, true, code);
}

/// <summary>Represents one immutable per-instance custody transition.</summary>
public sealed record CustodyTransition
{
    /// <summary>Gets the resulting protocol.</summary>
    public CustodyProtocol Protocol { get; }
    /// <summary>Gets whether the transition was accepted.</summary>
    public bool Accepted { get; }
    /// <summary>Gets a bounded stable result code.</summary>
    public string Code { get; }

    internal CustodyTransition(CustodyProtocol protocol, bool accepted, string code) =>
        (Protocol, Accepted, Code) = (protocol, accepted, code);
}
