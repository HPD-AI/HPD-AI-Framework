using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Profiles.Distributed;

/// <summary>Names the closed storage-neutral evolution state of one profile cutover.</summary>
public enum DistributedCutoverState
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>The source and target profiles are named but no dual read is active.</summary>
    Planned,
    /// <summary>Both profiles are read and compared at an exact cut.</summary>
    DualRead,
    /// <summary>The target is primary while rollback evidence remains retained.</summary>
    TargetPrimary,
    /// <summary>Authority returned to the source after an exact equality check.</summary>
    RolledBack,
    /// <summary>The cutover completed with identical authority history.</summary>
    Completed,
    /// <summary>Divergence, skew, missing history, or external residue prevents progression.</summary>
    Residual,
}

/// <summary>One canonical owner fact at an exact generation.</summary>
public sealed record CutoverHistoryEntry(SemanticId OwnerId, OwnerGeneration Generation, CanonicalDigest FactDigest);

/// <summary>Immutable cutover coordinator which changes storage topology without changing authority semantics.</summary>
public sealed record DistributedCutoverProtocol
{
    /// <summary>Gets the cutover identity.</summary>
    public SemanticId CutoverId { get; }
    /// <summary>Gets the source profile identity.</summary>
    public SemanticId SourceProfileId { get; }
    /// <summary>Gets the target profile identity.</summary>
    public SemanticId TargetProfileId { get; }
    /// <summary>Gets the latest compared generation cut.</summary>
    public OwnerGeneration ComparedThrough { get; }
    /// <summary>Gets the current cutover state.</summary>
    public DistributedCutoverState State { get; }
    /// <summary>Gets the stable residue code, when progression stopped.</summary>
    public string? ResidueCode { get; }

    private DistributedCutoverProtocol(SemanticId cutoverId, SemanticId sourceProfileId, SemanticId targetProfileId,
        OwnerGeneration comparedThrough, DistributedCutoverState state, string? residueCode)
    {
        if (!cutoverId.IsValid || !sourceProfileId.IsValid || !targetProfileId.IsValid || sourceProfileId == targetProfileId ||
            cutoverId.Scope != sourceProfileId.Scope || sourceProfileId.Scope != targetProfileId.Scope ||
            state is DistributedCutoverState.None || !Enum.IsDefined(state) ||
            (state == DistributedCutoverState.Residual) != (residueCode is not null) ||
            residueCode is not null && !ScopeId.TryCreate("code", "cutover", residueCode, out _))
            throw new ArgumentException("Distributed cutover state is invalid.");
        CutoverId = cutoverId; SourceProfileId = sourceProfileId; TargetProfileId = targetProfileId;
        ComparedThrough = comparedThrough; State = state; ResidueCode = residueCode;
    }

    /// <summary>Creates a planned cutover with no inferred target capability.</summary>
    public static DistributedCutoverProtocol Plan(SemanticId cutoverId, SemanticId sourceProfileId, SemanticId targetProfileId) =>
        new(cutoverId, sourceProfileId, targetProfileId, default, DistributedCutoverState.Planned, null);

    /// <summary>Rehydrates an exact previously stored cutover projection.</summary>
    public static DistributedCutoverProtocol Restore(SemanticId cutoverId, SemanticId sourceProfileId, SemanticId targetProfileId,
        OwnerGeneration comparedThrough, DistributedCutoverState state, string? residueCode) =>
        new(cutoverId, sourceProfileId, targetProfileId, comparedThrough, state, residueCode);

    /// <summary>Begins dual-read only when canonical histories are identical through the requested cut.</summary>
    public DistributedCutoverProtocol BeginDualRead(IReadOnlyList<CutoverHistoryEntry> source,
        IReadOnlyList<CutoverHistoryEntry> target, OwnerGeneration cut) => State == DistributedCutoverState.Planned
        ? CompareOrResidue(source, target, cut, DistributedCutoverState.DualRead, "dual-read-divergence")
        : Residue("dual-read-state-invalid");

    /// <summary>Promotes the target only after equality and bounded clock skew are established at the promotion cut.</summary>
    public DistributedCutoverProtocol Promote(IReadOnlyList<CutoverHistoryEntry> source, IReadOnlyList<CutoverHistoryEntry> target,
        OwnerGeneration cut, TimeSpan observedSkew, TimeSpan maximumSkew)
    {
        if (State != DistributedCutoverState.DualRead) return Residue("promotion-state-invalid");
        if (observedSkew.Duration() > maximumSkew || maximumSkew < TimeSpan.Zero) return Residue("promotion-clock-skew");
        return CompareOrResidue(source, target, cut, DistributedCutoverState.TargetPrimary, "promotion-divergence");
    }

    /// <summary>Rolls authority back only if the source contains the target-primary history through the rollback cut.</summary>
    public DistributedCutoverProtocol Rollback(IReadOnlyList<CutoverHistoryEntry> source, IReadOnlyList<CutoverHistoryEntry> target,
        OwnerGeneration cut) => State == DistributedCutoverState.TargetPrimary
        ? CompareOrResidue(source, target, cut, DistributedCutoverState.RolledBack, "rollback-divergence")
        : Residue("rollback-state-invalid");

    /// <summary>Completes only from target-primary after final identical replay with no named residue.</summary>
    public DistributedCutoverProtocol Complete(IReadOnlyList<CutoverHistoryEntry> source, IReadOnlyList<CutoverHistoryEntry> target,
        OwnerGeneration cut, bool residuePresent)
    {
        if (State != DistributedCutoverState.TargetPrimary) return Residue("completion-state-invalid");
        if (residuePresent) return Residue("completion-residue");
        return CompareOrResidue(source, target, cut, DistributedCutoverState.Completed, "completion-divergence");
    }

    private DistributedCutoverProtocol CompareOrResidue(IReadOnlyList<CutoverHistoryEntry> source,
        IReadOnlyList<CutoverHistoryEntry> target, OwnerGeneration cut, DistributedCutoverState success, string code)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(target);
        if (!cut.IsValid) throw new ArgumentException("Cutover comparison cut is invalid.", nameof(cut));
        static string Key(CutoverHistoryEntry entry) => $"{Convert.ToHexString(entry.OwnerId.GetCanonicalBytes())}|{entry.Generation.Value}|{entry.FactDigest.Profile.ToCanonicalText()}|{entry.FactDigest.Algorithm}|{Convert.ToHexString(entry.FactDigest.CopyBytes())}";
        var sourceCut = source.Where(x => x.Generation.Value <= cut.Value).Select(Key).Order(StringComparer.Ordinal).ToArray();
        var targetCut = target.Where(x => x.Generation.Value <= cut.Value).Select(Key).Order(StringComparer.Ordinal).ToArray();
        return sourceCut.Length != 0 && sourceCut.SequenceEqual(targetCut, StringComparer.Ordinal)
            ? new(CutoverId, SourceProfileId, TargetProfileId, cut, success, null)
            : Residue(code);
    }

    private DistributedCutoverProtocol Residue(string code) =>
        new(CutoverId, SourceProfileId, TargetProfileId, ComparedThrough, DistributedCutoverState.Residual, code);
}
