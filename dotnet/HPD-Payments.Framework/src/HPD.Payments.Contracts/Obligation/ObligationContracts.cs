using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Obligation;

/// <summary>Distinguishes the economic direction of an obligation without permitting a signed scalar to hide its meaning.</summary>
public enum ObligationDirection
{
    /// <summary>Invalid default; it cannot be admitted.</summary>
    None = 0,
    /// <summary>An amount owed by the named obligor.</summary>
    Due = 1,
    /// <summary>A credit owed to the named beneficiary.</summary>
    Credit = 2,
}

/// <summary>Names an authority-local obligation fact kind; unknown wire variants must be preserved by the representation layer.</summary>
public enum ObligationFactKind
{
    /// <summary>Invalid default; it cannot be admitted.</summary>
    None = 0,
    /// <summary>An initial additive obligation.</summary>
    Initial = 1,
    /// <summary>An additive adjustment to an admitted obligation lineage.</summary>
    Adjustment = 2,
    /// <summary>An additive correction that supersedes a mistaken semantic premise without overwriting history.</summary>
    Correction = 3,
    /// <summary>A governed write-off represented as an additive fact.</summary>
    WriteOff = 4,
}

/// <summary>Represents a positive, dimensioned magnitude whose direction is carried separately.</summary>
/// <remarks>This type is local to Obligation authority. It is not universal money and makes no FX, tax, settlement, accounting, or statutory claim.</remarks>
public readonly record struct ObligationQuantity
{
    /// <summary>Gets the strictly positive decimal magnitude.</summary>
    public decimal Magnitude { get; }
    /// <summary>Gets the lowercase ASCII unit or currency token whose interpretation is fixed by the source manifest.</summary>
    public string Unit { get; }
    /// <summary>Gets whether the value is non-default, positive, finite by decimal construction, and dimensioned.</summary>
    public bool IsValid => Magnitude > 0m && Unit is not null;

    private ObligationQuantity(decimal magnitude, string unit) => (Magnitude, Unit) = (magnitude, unit);

    /// <summary>Creates a validated magnitude.</summary>
    /// <param name="magnitude">A strictly positive decimal value.</param>
    /// <param name="unit">A bounded lowercase token such as a currency or usage unit.</param>
    /// <returns>The validated immutable quantity.</returns>
    /// <exception cref="ArgumentException">The magnitude is not positive or the unit is not a valid bounded token.</exception>
    public static ObligationQuantity Create(decimal magnitude, string unit) =>
        magnitude > 0m && ScopeId.TryCreate("unit", "unit", unit, out _)
            ? new(magnitude, unit)
            : throw new ArgumentException("Obligation magnitude must be positive and its unit must be a bounded lowercase token.");
}

/// <summary>Guards an Obligation append against the exact current owner generation and optional current fact digest.</summary>
/// <remarks>The guard has no persistence behavior; adapters must perform the comparison atomically in their certified owner domain.</remarks>
public sealed record ObligationGuard
{
    /// <summary>Gets the generation that must currently own the obligation lineage.</summary>
    public OwnerGeneration ExpectedGeneration { get; }
    /// <summary>Gets the expected current semantic digest, or <see langword="null"/> only for initial creation.</summary>
    public CanonicalDigest? ExpectedDigest { get; }

    /// <summary>Creates an immutable owner guard.</summary>
    /// <param name="expectedGeneration">The valid current owner generation to compare atomically.</param>
    /// <param name="expectedDigest">The optional current fact digest; no byte storage is aliased.</param>
    /// <exception cref="ArgumentException">The generation is invalid or an initial-creation guard supplies a digest.</exception>
    public ObligationGuard(OwnerGeneration expectedGeneration, CanonicalDigest? expectedDigest)
    {
        if (!expectedGeneration.IsValid) throw new ArgumentException("Expected generation is required.", nameof(expectedGeneration));
        ExpectedGeneration = expectedGeneration;
        ExpectedDigest = expectedDigest;
    }
}

/// <summary>Commands Obligation authority to append one initial, adjustment, correction, or write-off fact.</summary>
/// <remarks>All inputs are immutable references or owned primitive values. The command cannot mutate valuation, collection, movement, issuance, or application state.</remarks>
public sealed record AdmitObligationCommand
{
    /// <summary>Gets the semantic identity of the new additive fact.</summary>
    public SemanticId FactId { get; }
    /// <summary>Gets the stable lineage identity whose generation is guarded.</summary>
    public SemanticId ObligationId { get; }
    /// <summary>Gets the immutable source or valuation manifest identity.</summary>
    public SemanticId SourceManifestId { get; }
    /// <summary>Gets the exact canonical digest of the source manifest.</summary>
    public CanonicalDigest SourceManifestDigest { get; }
    /// <summary>Gets the kind of additive fact requested.</summary>
    public ObligationFactKind Kind { get; }
    /// <summary>Gets the explicit due-or-credit direction.</summary>
    public ObligationDirection Direction { get; }
    /// <summary>Gets the positive dimensioned magnitude.</summary>
    public ObligationQuantity Quantity { get; }
    /// <summary>Gets the semantic time at which the fact applies.</summary>
    public NamedTime EffectiveAt { get; }
    /// <summary>Gets the source-asserted time retained independently from effective and record time.</summary>
    public NamedTime SourceAt { get; }
    /// <summary>Gets the owner compare-bind guard.</summary>
    public ObligationGuard Guard { get; }
    /// <summary>Gets the prior fact corrected or adjusted; it is forbidden for an initial fact and required otherwise.</summary>
    public SemanticId? PredecessorFactId { get; }

    /// <summary>Creates a bounded command while preserving fact lineage and named time axes.</summary>
    /// <param name="factId">Identity of the new immutable fact.</param><param name="obligationId">Identity of its owner lineage.</param>
    /// <param name="sourceManifestId">Immutable source-manifest identity.</param><param name="sourceManifestDigest">Digest binding that source manifest.</param>
    /// <param name="kind">Initial, adjustment, correction, or write-off fact kind.</param><param name="direction">Explicit due-or-credit direction.</param>
    /// <param name="quantity">Positive authority-local magnitude and unit.</param><param name="effectiveAt">Named semantic effective time.</param>
    /// <param name="sourceAt">Named source time.</param><param name="guard">Owner generation/digest guard.</param>
    /// <param name="predecessorFactId">Required prior fact for every non-initial append.</param>
    /// <exception cref="ArgumentException">Any identity, scope, enum, quantity, time kind, lineage rule, or guard is invalid.</exception>
    public AdmitObligationCommand(SemanticId factId, SemanticId obligationId, SemanticId sourceManifestId,
        CanonicalDigest sourceManifestDigest, ObligationFactKind kind, ObligationDirection direction,
        ObligationQuantity quantity, NamedTime effectiveAt, NamedTime sourceAt, ObligationGuard guard,
        SemanticId? predecessorFactId = null)
    {
        ArgumentNullException.ThrowIfNull(sourceManifestDigest);
        ArgumentNullException.ThrowIfNull(guard);
        var sameScope = factId.IsValid && obligationId.IsValid && sourceManifestId.IsValid &&
            factId.Scope == obligationId.Scope && obligationId.Scope == sourceManifestId.Scope;
        var validKinds = kind != ObligationFactKind.None && Enum.IsDefined(kind) && direction != ObligationDirection.None && Enum.IsDefined(direction);
        var validTimes = effectiveAt.IsValid && effectiveAt.Kind == TimeKind.Effective && sourceAt.IsValid && sourceAt.Kind == TimeKind.Source;
        var needsPredecessor = kind != ObligationFactKind.Initial;
        var predecessorValid = predecessorFactId is { } p && p.IsValid && p.Scope == obligationId.Scope;
        if (!sameScope || !validKinds || !quantity.IsValid || !validTimes || needsPredecessor != predecessorValid)
            throw new ArgumentException("Invalid obligation command scope, fact kind, quantity, time axes, or predecessor lineage.");
        FactId = factId; ObligationId = obligationId; SourceManifestId = sourceManifestId;
        SourceManifestDigest = sourceManifestDigest; Kind = kind; Direction = direction; Quantity = quantity;
        EffectiveAt = effectiveAt; SourceAt = sourceAt; Guard = guard; PredecessorFactId = predecessorFactId;
    }
}

/// <summary>Records one immutable additive Obligation fact and the generation produced by its admission.</summary>
public sealed record ObligationFact
{
    /// <summary>Gets the command whose fully validated semantic inputs created this fact.</summary>
    public AdmitObligationCommand Command { get; }
    /// <summary>Gets the new monotone Obligation owner generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets the canonical digest of this exact admitted fact.</summary>
    public CanonicalDigest FactDigest { get; }
    /// <summary>Gets the local durable record time, distinct from source and effective time.</summary>
    public NamedTime RecordedAt { get; }

    /// <summary>Creates an immutable admitted fact receipt.</summary>
    /// <param name="command">The validated command retained without mutable aliases.</param><param name="generation">The exact guarded successor generation.</param>
    /// <param name="factDigest">Digest of the admitted semantic fact.</param><param name="recordedAt">Named local durable record time.</param>
    /// <exception cref="ArgumentException">The generation is not exactly the guarded successor or record time is invalid.</exception>
    public ObligationFact(AdmitObligationCommand command, OwnerGeneration generation, CanonicalDigest factDigest, NamedTime recordedAt)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(factDigest);
        if (!command.Guard.ExpectedGeneration.TryNext(out var expected) || generation != expected || !recordedAt.IsValid || recordedAt.Kind != TimeKind.Record)
            throw new ArgumentException("The fact generation must be the guarded successor and record time must be named Record time.");
        Command = command; Generation = generation; FactDigest = factDigest; RecordedAt = recordedAt;
    }
}

/// <summary>Names every closed authority-local outcome of an Obligation admission attempt.</summary>
public enum ObligationAdmissionKind
{
    /// <summary>Invalid default result.</summary>
    None = 0,
    /// <summary>The immutable fact was appended.</summary>
    Admitted,
    /// <summary>The exact semantic fact and digest were already admitted.</summary>
    Replay,
    /// <summary>The identity, digest, predecessor, or owner generation conflicts.</summary>
    Conflict,
    /// <summary>Required current owner evidence could not be established.</summary>
    Unknown,
    /// <summary>The command failed an authority-local law without owner mutation.</summary>
    Rejected,
}

/// <summary>Returns an Obligation result without flattening replay, conflict, rejection, or uncertainty.</summary>
public sealed record ObligationAdmissionResult
{
    /// <summary>Gets the exact outcome kind.</summary>
    public ObligationAdmissionKind Kind { get; }
    /// <summary>Gets the admitted or replayed fact; absent for other outcomes.</summary>
    public ObligationFact? Fact { get; }
    /// <summary>Gets a bounded stable diagnostic code for non-admitted outcomes.</summary>
    public string? Code { get; }

    private ObligationAdmissionResult(ObligationAdmissionKind kind, ObligationFact? fact, string? code) => (Kind, Fact, Code) = (kind, fact, code);

    /// <summary>Creates an admitted or exact replay result.</summary>
    /// <param name="kind">Admitted or Replay.</param><param name="fact">The immutable fact returned by that outcome.</param>
    /// <returns>A closed fact-bearing result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not Admitted or Replay.</exception>
    public static ObligationAdmissionResult WithFact(ObligationAdmissionKind kind, ObligationFact fact) =>
        kind is ObligationAdmissionKind.Admitted or ObligationAdmissionKind.Replay
            ? new(kind, fact ?? throw new ArgumentNullException(nameof(fact)), null)
            : throw new ArgumentOutOfRangeException(nameof(kind));

    /// <summary>Creates a conflict, unknown, or rejected result with no fabricated fact.</summary>
    /// <param name="kind">Conflict, Unknown, or Rejected.</param><param name="code">Bounded stable diagnostic token.</param>
    /// <returns>A closed non-fact result.</returns>
    /// <exception cref="ArgumentException">The kind or diagnostic code is invalid.</exception>
    public static ObligationAdmissionResult WithoutFact(ObligationAdmissionKind kind, string code) =>
        kind is ObligationAdmissionKind.Conflict or ObligationAdmissionKind.Unknown or ObligationAdmissionKind.Rejected && ScopeId.TryCreate("code", "code", code, out _)
            ? new(kind, null, code)
            : throw new ArgumentException("A non-fact result requires a closed non-success kind and bounded code.");
}
