using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Primitives.Time;

/// <summary>Represents the bounded immutable <c>TimeKind</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public enum TimeKind
{
    /// <summary>Invalid default; a generic timestamp is not admitted.</summary>
    None = 0,
    /// <summary>Time an action was requested.</summary>
    Requested,
    /// <summary>Time an action or fact was accepted.</summary>
    Accepted,
    /// <summary>Time a fact becomes semantically effective.</summary>
    Effective,
    /// <summary>Time supplied by the originating source.</summary>
    Source,
    /// <summary>Time an observation was made.</summary>
    Observed,
    /// <summary>Time reported by an external provider.</summary>
    Provider,
    /// <summary>Time associated with settlement.</summary>
    Settlement,
    /// <summary>Time associated with a billing boundary.</summary>
    Billing,
    /// <summary>Time a calculation was performed.</summary>
    Calculated,
    /// <summary>Time an obligation is due.</summary>
    Due,
    /// <summary>Time an artifact or fact was issued.</summary>
    Issue,
    /// <summary>Time dispatch crossed its named boundary.</summary>
    Dispatch,
    /// <summary>Time an acknowledgement was observed.</summary>
    Acknowledged,
    /// <summary>Time the local durable record was made.</summary>
    Record,
    /// <summary>Time evidence was verified.</summary>
    Verify,
    /// <summary>Time a revision or deployment cutover applies.</summary>
    Cutover,
    /// <summary>Time an eligibility or evidence interval expires.</summary>
    Expiry
}

/// <summary>Represents the bounded immutable <c>NamedTime</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public readonly record struct NamedTime
{
    /// <summary>Gets the validated <c>Kind</c> component; it does not imply ambient context or mutation authority.</summary>
    public TimeKind Kind { get; }
    /// <summary>Gets the validated <c>Value</c> component; it does not imply ambient context or mutation authority.</summary>
    public DateTimeOffset Value { get; }
    /// <summary>Gets whether the value satisfies its required-field and bound invariants; a default value is invalid.</summary>
    public bool IsValid => Kind != TimeKind.None && Enum.IsDefined(Kind) && Value.Offset == TimeSpan.Zero;
    private NamedTime(TimeKind kind, DateTimeOffset value) => (Kind, Value) = (kind, value);
    /// <summary>Validates the supplied components and returns a value without throwing for invalid input.</summary>
    public static bool TryCreate(TimeKind kind, DateTimeOffset value, out NamedTime result)
    {
        result = default;
        if (kind == TimeKind.None || !Enum.IsDefined(kind) || value.Offset != TimeSpan.Zero) return false;
        result = new(kind, value); return true;
    }
    /// <summary>Creates a validated value and rejects missing, unknown, or out-of-bound components.</summary>
    public static NamedTime Create(TimeKind kind, DateTimeOffset utc) => TryCreate(kind, utc, out var result) ? result : throw new ArgumentException("Named time must be UTC and use a known kind.");
    /// <summary>Returns the stable textual representation defined by the containing type, or its explicit invalid diagnostic where supported.</summary>
    public string ToCanonicalText() => IsValid ? $"{(int)Kind}:{Value:O}" : throw new InvalidOperationException("Default named time is invalid.");
}

/// <summary>Represents the bounded immutable <c>HistoricalFrameKind</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public enum HistoricalFrameKind
{
    /// <summary>Invalid default historical frame.</summary>
    None = 0,
    /// <summary>Reconstructs the inputs available when the recorded action occurred.</summary>
    AsActedUpon,
    /// <summary>Selects facts known no later than the named knowledge cut.</summary>
    AsKnownAt,
    /// <summary>Selects facts by semantic effective time.</summary>
    EffectiveAsOf,
    /// <summary>Applies admitted additive corrections visible at the cut.</summary>
    CorrectedAsOf,
    /// <summary>Selects external observations visible at the cut.</summary>
    ObservedAsOf,
    /// <summary>Selects issued artifacts visible at the cut.</summary>
    IssuedAsOf,
    /// <summary>Reconstructs a projection from its declared source cut.</summary>
    ProjectionAsOf
}

/// <summary>Represents the bounded immutable <c>OwnerCut</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public readonly record struct OwnerCut(ScopeId OwnerScope, SemanticId Subject, OwnerGeneration Through)
{
    /// <summary>Gets whether the value satisfies its required-field and bound invariants; a default value is invalid.</summary>
    public bool IsValid => OwnerScope.IsValid && Subject.IsValid && Subject.Scope == OwnerScope && Through.IsValid;
}

/// <summary>Represents the bounded immutable <c>HistoricalCut</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public sealed class HistoricalCut : IEquatable<HistoricalCut>
{
    /// <summary>Specifies the maximum admitted size or count enforced by the containing type.</summary>
    public const int MaximumOwnerCuts = 256;
    private readonly OwnerCut[] _ownerCuts;
    /// <summary>Gets the validated <c>Frame</c> component; it does not imply ambient context or mutation authority.</summary>
    public HistoricalFrameKind Frame { get; }
    /// <summary>Gets the validated <c>KnowledgeThrough</c> component; it does not imply ambient context or mutation authority.</summary>
    public NamedTime KnowledgeThrough { get; }
    /// <summary>Gets the validated <c>OwnerCuts</c> component; it does not imply ambient context or mutation authority.</summary>
    public IReadOnlyList<OwnerCut> OwnerCuts => Array.AsReadOnly(_ownerCuts);
    /// <summary>Gets the validated <c>Version</c> component; it does not imply ambient context or mutation authority.</summary>
    public ContractVersion Version { get; }

    /// <summary>Represents the bounded immutable <c>HistoricalCut</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
    public HistoricalCut(HistoricalFrameKind frame, NamedTime knowledgeThrough, IEnumerable<OwnerCut> ownerCuts, ContractVersion version)
    {
        ArgumentNullException.ThrowIfNull(ownerCuts);
        if (frame == HistoricalFrameKind.None || !Enum.IsDefined(frame) || !knowledgeThrough.IsValid || knowledgeThrough.Kind != TimeKind.Record || !version.IsValid) throw new ArgumentException("Invalid historical cut metadata.");
        _ownerCuts = ownerCuts.ToArray();
        if (_ownerCuts.Length > MaximumOwnerCuts || _ownerCuts.Any(static x => !x.IsValid)) throw new ArgumentException("Invalid or over-bound owner cuts.");
        Frame = frame; KnowledgeThrough = knowledgeThrough; Version = version;
    }

    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public bool Equals(HistoricalCut? other) => other is not null && Frame == other.Frame && KnowledgeThrough == other.KnowledgeThrough && Version == other.Version && _ownerCuts.SequenceEqual(other._ownerCuts);
    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public override bool Equals(object? obj) => Equals(obj as HistoricalCut);
    /// <summary>Returns a process-local hash consistent with equality; the hash is never a persisted identity.</summary>
    public override int GetHashCode() { var h = new HashCode(); h.Add(Frame); h.Add(KnowledgeThrough); h.Add(Version); foreach (var cut in _ownerCuts) h.Add(cut); return h.ToHashCode(); }
}
