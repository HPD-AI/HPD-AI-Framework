using HPD.Payments.Primitives.Identity;
using HPD.Payments.Supporting.Evidence;
using HPD.Payments.Supporting.Operations;
using HPD.Payments.Supporting.Ownership;

namespace HPD.Payments.Supporting.Repair;

/// <summary>Names a governed repair branch terminal without claiming authority mutation.</summary>
public enum RepairBranchTerminal
{
    /// <summary>Invalid or non-terminal default.</summary>
    None = 0,
    /// <summary>The exact branch postcondition was freshly verified.</summary>
    VerifiedPostcondition,
    /// <summary>A definite branch failure was established.</summary>
    DefiniteFailure,
    /// <summary>The external effect remains unknown.</summary>
    UnknownExternalEffect,
    /// <summary>Authorization or evidence remains indeterminate.</summary>
    Indeterminate,
    /// <summary>The branch conflicts with immutable owner state.</summary>
    Conflict,
    /// <summary>Owned residue remains and is declared.</summary>
    Residual,
    /// <summary>The postcondition cannot be established with available evidence.</summary>
    Unverifiable,
}

/// <summary>Declares one governed repair branch routed to exactly one frozen owner.</summary>
/// <remarks>A branch coordinates a typed owner command; it neither contains nor executes a universal mutation.</remarks>
public sealed record RepairBranch
{
    /// <summary>Gets the branch identity.</summary>
    public SemanticId BranchId { get; }
    /// <summary>Gets the exact frozen owner target.</summary>
    public OwnerReference Owner { get; }
    /// <summary>Gets the stable operation declaration for the typed owner command.</summary>
    public OperationDeclaration Operation { get; }
    /// <summary>Gets the bounded postcondition question token.</summary>
    public string Postcondition { get; }

    /// <summary>Creates a single-owner repair branch.</summary>
    /// <exception cref="ArgumentException">Identity, scope, owner routing, or postcondition is invalid.</exception>
    public RepairBranch(SemanticId branchId, OwnerReference owner, OperationDeclaration operation, string postcondition)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!branchId.IsValid || !owner.IsValid || branchId.Scope != owner.SubjectId.Scope || operation.Owner != owner ||
            !ScopeId.TryCreate("question", "question", postcondition, out _))
            throw new ArgumentException("Repair branch must route one bounded postcondition to one same-scope owner operation.");
        BranchId = branchId; Owner = owner; Operation = operation; Postcondition = postcondition;
    }
}

/// <summary>Groups immutable repair branches into a plan generation without becoming a business authority.</summary>
public sealed record GovernedCase
{
    /// <summary>Maximum branches in one immutable plan generation.</summary>
    public const int MaximumBranches = 64;
    private readonly RepairBranch[] _branches;
    /// <summary>Gets the case identity.</summary>
    public SemanticId CaseId { get; }
    /// <summary>Gets the immutable plan generation.</summary>
    public OwnerGeneration PlanGeneration { get; }
    /// <summary>Gets a read-only view of copied branches.</summary>
    public IReadOnlyList<RepairBranch> Branches => _branches;

    /// <summary>Copies a bounded, unique set of typed single-owner branches.</summary>
    /// <exception cref="ArgumentException">Case, generation, branch count, scope, or branch identity is invalid.</exception>
    public GovernedCase(SemanticId caseId, OwnerGeneration planGeneration, ReadOnlySpan<RepairBranch> branches)
    {
        if (!caseId.IsValid || !planGeneration.IsValid || branches.Length is 0 or > MaximumBranches)
            throw new ArgumentException("A governed case requires identity, generation, and bounded branches.");
        _branches = branches.ToArray();
        if (_branches.Any(x => x is null || x.BranchId.Scope != caseId.Scope) || _branches.Select(static x => x.BranchId).Distinct().Count() != _branches.Length)
            throw new ArgumentException("Branches must be non-null, same-scope, and unique.", nameof(branches));
        CaseId = caseId; PlanGeneration = planGeneration;
    }
}

/// <summary>Records a branch terminal and question-scoped evidence or explicit residue.</summary>
public sealed record RepairBranchReceipt
{
    /// <summary>Gets the branch identity.</summary>
    public SemanticId BranchId { get; }
    /// <summary>Gets the terminal outcome.</summary>
    public RepairBranchTerminal Terminal { get; }
    /// <summary>Gets fresh verification evidence, required only for VerifiedPostcondition.</summary>
    public EvidenceDescriptor? Verification { get; }
    /// <summary>Gets the bounded residue code, required only for Residual or Unverifiable.</summary>
    public string? ResidueCode { get; }

    /// <summary>Creates an honest branch terminal receipt.</summary>
    /// <exception cref="ArgumentException">Terminal, verification, or residue rules are invalid.</exception>
    public RepairBranchReceipt(SemanticId branchId, RepairBranchTerminal terminal, EvidenceDescriptor? verification = null, string? residueCode = null)
    {
        var needsVerification = terminal == RepairBranchTerminal.VerifiedPostcondition;
        var needsResidue = terminal is RepairBranchTerminal.Residual or RepairBranchTerminal.Unverifiable;
        var validResidue = residueCode is not null && ScopeId.TryCreate("residue", "residue", residueCode, out _);
        if (!branchId.IsValid || terminal == RepairBranchTerminal.None || !Enum.IsDefined(terminal) ||
            needsVerification != (verification is { Role: EvidenceRole.Verification }) || needsResidue != validResidue)
            throw new ArgumentException("Repair receipt must preserve verification and residue truth.");
        BranchId = branchId; Terminal = terminal; Verification = verification; ResidueCode = residueCode;
    }
}
