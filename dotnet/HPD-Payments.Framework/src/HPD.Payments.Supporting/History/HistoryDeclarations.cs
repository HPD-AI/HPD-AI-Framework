using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Supporting.Ownership;

namespace HPD.Payments.Supporting.History;

/// <summary>Names the declared interpretation of an immutable historical read.</summary>
public enum HistoricalFrameKind
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>Facts as known at the declared knowledge cut.</summary>
    AsKnownAt,
    /// <summary>Facts effective by the declared semantic time and known by the knowledge cut.</summary>
    EffectiveAsOf,
    /// <summary>Correction-aware facts as of both effective and knowledge cuts.</summary>
    CorrectedAsOf,
    /// <summary>Projection output bound to an immutable source cut.</summary>
    ProjectionAsOf,
}

/// <summary>Declares one authority-local cut in a multi-owner historical frame.</summary>
public readonly record struct OwnerCut
{
    /// <summary>Gets the exact owner subject and terminal generation included.</summary>
    public OwnerReference Owner { get; }
    /// <summary>Creates an owner cut from a valid owner reference.</summary>
    /// <exception cref="ArgumentException">The owner reference is invalid.</exception>
    public OwnerCut(OwnerReference owner) { if (!owner.IsValid) throw new ArgumentException("A valid owner cut is required.", nameof(owner)); Owner = owner; }
}

/// <summary>Declares a bounded immutable historical frame without substituting current state.</summary>
public sealed record HistoricalFrame
{
    /// <summary>Maximum number of independent owner cuts.</summary>
    public const int MaximumOwnerCuts = 32;
    private readonly OwnerCut[] _ownerCuts;
    /// <summary>Gets the frame interpretation.</summary>
    public HistoricalFrameKind Kind { get; }
    /// <summary>Gets the knowledge cut.</summary>
    public NamedTime KnowledgeThrough { get; }
    /// <summary>Gets the optional effective cut.</summary>
    public NamedTime? EffectiveThrough { get; }
    /// <summary>Gets a read-only view of the owned owner-cut array.</summary>
    public IReadOnlyList<OwnerCut> OwnerCuts => _ownerCuts;

    /// <summary>Copies and validates a deterministic historical frame.</summary>
    /// <exception cref="ArgumentException">Kind, named times, cuts, cardinality, or duplicate owner subjects are invalid.</exception>
    public HistoricalFrame(HistoricalFrameKind kind, NamedTime knowledgeThrough, ReadOnlySpan<OwnerCut> ownerCuts, NamedTime? effectiveThrough = null)
    {
        if (kind == HistoricalFrameKind.None || !Enum.IsDefined(kind) || !knowledgeThrough.IsValid || knowledgeThrough.Kind != TimeKind.Record || ownerCuts.Length is 0 or > MaximumOwnerCuts ||
            effectiveThrough is { } e && (!e.IsValid || e.Kind != TimeKind.Effective) ||
            kind is HistoricalFrameKind.EffectiveAsOf or HistoricalFrameKind.CorrectedAsOf && effectiveThrough is null)
            throw new ArgumentException("Historical frame kind, time axes, or owner cuts are invalid.");
        _ownerCuts = ownerCuts.ToArray();
        if (_ownerCuts.Any(static x => !x.Owner.IsValid) || _ownerCuts.Select(static x => x.Owner.SubjectId).Distinct().Count() != _ownerCuts.Length)
            throw new ArgumentException("Owner cuts must be valid and unique by subject.", nameof(ownerCuts));
        Kind = kind; KnowledgeThrough = knowledgeThrough; EffectiveThrough = effectiveThrough;
    }
}

/// <summary>Declares an immutable projection generation and its exact source frame.</summary>
public sealed record ProjectionDeclaration
{
    /// <summary>Gets the projection identity.</summary>
    public SemanticId ProjectionId { get; }
    /// <summary>Gets the projection generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets the exact immutable source frame.</summary>
    public HistoricalFrame SourceFrame { get; }
    /// <summary>Gets the digest of the produced projection generation.</summary>
    public CanonicalDigest Digest { get; }

    /// <summary>Creates a projection declaration that owns no source mutation truth.</summary>
    /// <exception cref="ArgumentException">Identity or generation is invalid.</exception>
    public ProjectionDeclaration(SemanticId projectionId, OwnerGeneration generation, HistoricalFrame sourceFrame, CanonicalDigest digest)
    {
        ArgumentNullException.ThrowIfNull(sourceFrame); ArgumentNullException.ThrowIfNull(digest);
        if (!projectionId.IsValid || !generation.IsValid) throw new ArgumentException("Projection identity and generation are required.");
        ProjectionId = projectionId; Generation = generation; SourceFrame = sourceFrame; Digest = digest;
    }
}
