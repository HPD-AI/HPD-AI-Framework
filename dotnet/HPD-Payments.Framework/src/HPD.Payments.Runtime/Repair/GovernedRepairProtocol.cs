using HPD.Payments.Primitives.Identity;
using HPD.Payments.Supporting.Repair;

namespace HPD.Payments.Runtime.Repair;

/// <summary>Names the aggregate state of one governed repair-plan generation.</summary>
public enum GovernedRepairState
{
    /// <summary>Invalid default state.</summary>
    None = 0,
    /// <summary>The immutable plan awaits explicit approval.</summary>
    AwaitingApproval,
    /// <summary>The exact plan generation is approved for branch execution.</summary>
    Approved,
    /// <summary>At least one branch remains unresolved.</summary>
    InProgress,
    /// <summary>Every branch has a fresh verified postcondition.</summary>
    VerifiedClosed,
    /// <summary>Every branch is terminal, but failure, uncertainty, conflict, or residue remains.</summary>
    ClosedWithResidue,
}

/// <summary>Immutable adapter-neutral coordinator for a typed governed repair case.</summary>
/// <remarks>This coordinator never executes a universal mutation and never substitutes branch evidence for owner truth.</remarks>
public sealed record GovernedRepairProtocol
{
    private readonly Dictionary<SemanticId, RepairBranchReceipt> _receipts;

    /// <summary>Gets the immutable governed case.</summary>
    public GovernedCase Case { get; }
    /// <summary>Gets the current aggregate state.</summary>
    public GovernedRepairState State { get; }
    /// <summary>Gets the approval revision, invalid until approved.</summary>
    public Revision ApprovalRevision { get; }
    /// <summary>Gets a defensive read-only copy of branch receipts.</summary>
    public IReadOnlyDictionary<SemanticId, RepairBranchReceipt> Receipts => new Dictionary<SemanticId, RepairBranchReceipt>(_receipts);

    private GovernedRepairProtocol(GovernedCase @case, GovernedRepairState state, Revision approvalRevision,
        Dictionary<SemanticId, RepairBranchReceipt> receipts) =>
        (Case, State, ApprovalRevision, _receipts) = (@case, state, approvalRevision, receipts);

    /// <summary>Creates a repair protocol awaiting approval.</summary>
    public static GovernedRepairProtocol Create(GovernedCase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return new(@case, GovernedRepairState.AwaitingApproval, default, []);
    }

    /// <summary>Approves the exact immutable plan generation.</summary>
    public GovernedRepairTransition Approve(OwnerGeneration expectedPlanGeneration, Revision approvalRevision)
    {
        if (State != GovernedRepairState.AwaitingApproval)
            return Reject("approval-not-admissible");
        if (!expectedPlanGeneration.IsValid || expectedPlanGeneration != Case.PlanGeneration || !approvalRevision.IsValid)
            return Reject("stale-plan-approval");
        return Accept(new(Case, GovernedRepairState.Approved, approvalRevision, []), "approved");
    }

    /// <summary>Records one terminal branch receipt for the exact approved plan.</summary>
    public GovernedRepairTransition Record(OwnerGeneration expectedPlanGeneration, RepairBranchReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (State is not (GovernedRepairState.Approved or GovernedRepairState.InProgress))
            return Reject("plan-not-executable");
        if (expectedPlanGeneration != Case.PlanGeneration)
            return Reject("stale-plan-generation");
        if (!Case.Branches.Any(x => x.BranchId == receipt.BranchId))
            return Reject("unknown-branch");
        if (_receipts.ContainsKey(receipt.BranchId))
            return Reject("branch-already-terminal");

        var next = new Dictionary<SemanticId, RepairBranchReceipt>(_receipts) { [receipt.BranchId] = receipt };
        var state = next.Count != Case.Branches.Count
            ? GovernedRepairState.InProgress
            : next.Values.All(x => x.Terminal == RepairBranchTerminal.VerifiedPostcondition)
                ? GovernedRepairState.VerifiedClosed
                : GovernedRepairState.ClosedWithResidue;
        return Accept(new(Case, state, ApprovalRevision, next), state.ToString());
    }

    /// <summary>Creates a fresh unapproved protocol for a strictly newer plan generation.</summary>
    public GovernedRepairTransition Reopen(GovernedCase replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.CaseId != Case.CaseId || replacement.PlanGeneration.Value <= Case.PlanGeneration.Value)
            return Reject("replacement-not-newer");
        return Accept(Create(replacement), "reopened");
    }

    private GovernedRepairTransition Reject(string code) => new(this, false, code);
    private static GovernedRepairTransition Accept(GovernedRepairProtocol protocol, string code) => new(protocol, true, code);
}

/// <summary>Represents one immutable governed-repair transition.</summary>
public sealed record GovernedRepairTransition
{
    /// <summary>Gets the resulting protocol.</summary>
    public GovernedRepairProtocol Protocol { get; }
    /// <summary>Gets whether the transition was accepted.</summary>
    public bool Accepted { get; }
    /// <summary>Gets a bounded stable result code.</summary>
    public string Code { get; }

    internal GovernedRepairTransition(GovernedRepairProtocol protocol, bool accepted, string code) =>
        (Protocol, Accepted, Code) = (protocol, accepted, code);
}
