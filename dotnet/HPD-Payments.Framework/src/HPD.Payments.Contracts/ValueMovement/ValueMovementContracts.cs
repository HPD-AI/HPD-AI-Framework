using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.ValueMovement;

/// <summary>Identifies a policy-neutral movement family with its own conservation equation.</summary>
public enum ValueMovementFamily
{
    /// <summary>Invalid default family.</summary>
    None = 0,
    /// <summary>Applies collected value between exact economic endpoints.</summary>
    ObligationApplication = 1,
    /// <summary>Records a correction as an additive compensating transition.</summary>
    Compensation = 2,
    /// <summary>Records provisional and settled allocation endpoints without claiming external settlement truth.</summary>
    Allocation = 3,
}

/// <summary>Represents a strictly positive movement magnitude and exact unit.</summary>
/// <remarks>This authority-local value is neither universal money nor held capacity. It cannot be substituted for an endpoint balance.</remarks>
public readonly record struct MovementQuantity
{
    /// <summary>Gets the strictly positive magnitude.</summary>
    public decimal Magnitude { get; }
    /// <summary>Gets the bounded lowercase unit token.</summary>
    public string Unit { get; }
    /// <summary>Gets whether this is a constructed positive dimensioned value.</summary>
    public bool IsValid => Magnitude > 0m && Unit is not null;
    private MovementQuantity(decimal magnitude, string unit) => (Magnitude, Unit) = (magnitude, unit);

    /// <summary>Creates a validated movement quantity.</summary>
    /// <param name="magnitude">A strictly positive decimal magnitude.</param><param name="unit">A bounded lowercase unit token.</param>
    /// <returns>The immutable movement quantity.</returns>
    /// <exception cref="ArgumentException">Magnitude or unit is invalid.</exception>
    public static MovementQuantity Create(decimal magnitude, string unit) =>
        magnitude > 0m && ScopeId.TryCreate("unit", "unit", unit, out _)
            ? new(magnitude, unit)
            : throw new ArgumentException("Movement quantity must be positive and dimensioned.");

    /// <summary>Computes a checked sum for a sequence of same-unit movement quantities.</summary>
    /// <param name="values">A bounded caller-owned sequence that is read synchronously and not retained.</param>
    /// <returns>The exact checked positive total.</returns>
    /// <exception cref="ArgumentException">The sequence is empty, invalid, or mixes units.</exception>
    /// <exception cref="OverflowException">The exact decimal total is outside the decimal range.</exception>
    public static MovementQuantity Sum(IEnumerable<MovementQuantity> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext() || !enumerator.Current.IsValid) throw new ArgumentException("At least one valid movement quantity is required.", nameof(values));
        var unit = enumerator.Current.Unit;
        var total = enumerator.Current.Magnitude;
        while (enumerator.MoveNext())
        {
            if (!enumerator.Current.IsValid || !StringComparer.Ordinal.Equals(unit, enumerator.Current.Unit))
                throw new ArgumentException("Movement quantities must use one exact unit.", nameof(values));
            total = checked(total + enumerator.Current.Magnitude);
        }
        return new(total, unit);
    }
}

/// <summary>Names one immutable endpoint and the generation guarded by a movement.</summary>
public readonly record struct MovementEndpoint
{
    /// <summary>Gets the stable endpoint identity.</summary>
    public SemanticId Id { get; }
    /// <summary>Gets the expected endpoint generation.</summary>
    public OwnerGeneration ExpectedGeneration { get; }
    /// <summary>Gets whether both endpoint coordinates are valid.</summary>
    public bool IsValid => Id.IsValid && ExpectedGeneration.IsValid;

    /// <summary>Creates a guarded immutable endpoint reference.</summary>
    /// <param name="id">Stable endpoint identity.</param><param name="expectedGeneration">Generation that must be compared by the endpoint owner.</param>
    /// <exception cref="ArgumentException">Identity or generation is invalid.</exception>
    public MovementEndpoint(SemanticId id, OwnerGeneration expectedGeneration)
    {
        if (!id.IsValid || !expectedGeneration.IsValid) throw new ArgumentException("Movement endpoint must be valid and generation-guarded.");
        (Id, ExpectedGeneration) = (id, expectedGeneration);
    }
}

/// <summary>Commands admission of one typed, dual-endpoint, conserved value transition.</summary>
/// <remarks>The command references endpoint guards but cannot mutate Held Position, Obligation, Valuation, provider, settlement, or accounting authorities.</remarks>
public sealed record AdmitValueMovementCommand
{
    /// <summary>Gets the new movement identity.</summary>
    public SemanticId MovementId { get; }
    /// <summary>Gets the exact conserved movement family.</summary>
    public ValueMovementFamily Family { get; }
    /// <summary>Gets the debited endpoint guard.</summary>
    public MovementEndpoint Debit { get; }
    /// <summary>Gets the credited endpoint guard.</summary>
    public MovementEndpoint Credit { get; }
    /// <summary>Gets the strictly positive amount removed and added.</summary>
    public MovementQuantity Quantity { get; }
    /// <summary>Gets the exact source-manifest identity.</summary>
    public SemanticId SourceManifestId { get; }
    /// <summary>Gets the canonical digest binding the source manifest.</summary>
    public CanonicalDigest SourceManifestDigest { get; }
    /// <summary>Gets the optional policy-neutral held receipt reference required by the caller's family contract.</summary>
    public SemanticId? HeldReceiptId { get; }
    /// <summary>Gets the expected Value Movement owner generation.</summary>
    public OwnerGeneration ExpectedGeneration { get; }
    /// <summary>Gets the semantic effective time.</summary>
    public NamedTime EffectiveAt { get; }
    /// <summary>Gets the prior movement compensated by this append.</summary>
    public SemanticId? CompensatesMovementId { get; }

    /// <summary>Creates a complete conserved movement request.</summary>
    /// <param name="movementId">New authority identity.</param><param name="family">Exact movement family.</param>
    /// <param name="debit">Guarded debit endpoint.</param><param name="credit">Guarded credit endpoint.</param>
    /// <param name="quantity">Positive same-unit debit and credit magnitude.</param><param name="sourceManifestId">Immutable source-manifest identity.</param>
    /// <param name="sourceManifestDigest">Digest binding the source manifest.</param><param name="expectedGeneration">Movement owner guard.</param>
    /// <param name="effectiveAt">Named effective time.</param><param name="heldReceiptId">Optional immutable held-capacity receipt reference.</param>
    /// <param name="compensatesMovementId">Required only for the Compensation family.</param>
    /// <exception cref="ArgumentException">Scope, family, endpoints, quantity, guard, time, or compensation lineage is invalid.</exception>
    public AdmitValueMovementCommand(SemanticId movementId, ValueMovementFamily family, MovementEndpoint debit,
        MovementEndpoint credit, MovementQuantity quantity, SemanticId sourceManifestId, CanonicalDigest sourceManifestDigest,
        OwnerGeneration expectedGeneration, NamedTime effectiveAt, SemanticId? heldReceiptId = null, SemanticId? compensatesMovementId = null)
    {
        ArgumentNullException.ThrowIfNull(sourceManifestDigest);
        var scoped = movementId.IsValid && movementId.Scope.Authority == "value-movement" && debit.IsValid && credit.IsValid &&
            sourceManifestId.IsValid && movementId.Scope == debit.Id.Scope && debit.Id.Scope == credit.Id.Scope && credit.Id.Scope == sourceManifestId.Scope;
        var distinct = debit.Id != credit.Id;
        var heldValid = heldReceiptId is null || heldReceiptId is { IsValid: true } held &&
            held.Scope.Authority == "held-position" &&
            StringComparer.Ordinal.Equals(held.Scope.Tenant, movementId.Scope.Tenant) &&
            StringComparer.Ordinal.Equals(held.Scope.Environment, movementId.Scope.Environment);
        var compensationValid = compensatesMovementId is { IsValid: true } prior && prior.Scope == movementId.Scope;
        if (!scoped || !distinct || family == ValueMovementFamily.None || !Enum.IsDefined(family) || !quantity.IsValid ||
            !expectedGeneration.IsValid || !effectiveAt.IsValid || effectiveAt.Kind != TimeKind.Effective || !heldValid ||
            (family == ValueMovementFamily.Compensation) != compensationValid)
            throw new ArgumentException("Invalid value-movement command.");
        (MovementId, Family, Debit, Credit, Quantity, SourceManifestId, SourceManifestDigest, HeldReceiptId, ExpectedGeneration, EffectiveAt, CompensatesMovementId) =
            (movementId, family, debit, credit, quantity, sourceManifestId, sourceManifestDigest, heldReceiptId, expectedGeneration, effectiveAt, compensatesMovementId);
    }
}

/// <summary>Records one immutable conserved Value Movement fact.</summary>
public sealed record ValueMovementFact
{
    /// <summary>Gets the exact admitted command.</summary>
    public AdmitValueMovementCommand Command { get; }
    /// <summary>Gets the guarded successor movement generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets the canonical digest of the admitted movement.</summary>
    public CanonicalDigest FactDigest { get; }
    /// <summary>Gets the local record time, distinct from effective time.</summary>
    public NamedTime RecordedAt { get; }

    /// <summary>Creates an admitted append-only movement fact.</summary>
    /// <param name="command">Validated exact transition.</param><param name="generation">Expected successor owner generation.</param>
    /// <param name="factDigest">Canonical digest of the fact.</param><param name="recordedAt">Named local record time.</param>
    /// <exception cref="ArgumentException">Generation or record time is invalid.</exception>
    public ValueMovementFact(AdmitValueMovementCommand command, OwnerGeneration generation, CanonicalDigest factDigest, NamedTime recordedAt)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(factDigest);
        if (!command.ExpectedGeneration.TryNext(out var next) || generation != next || !recordedAt.IsValid || recordedAt.Kind != TimeKind.Record)
            throw new ArgumentException("Invalid movement fact generation or record time.");
        (Command, Generation, FactDigest, RecordedAt) = (command, generation, factDigest, recordedAt);
    }
}

/// <summary>Names closed Value Movement admission outcomes.</summary>
public enum ValueMovementResultKind
{
    /// <summary>Invalid default result.</summary>
    None = 0,
    /// <summary>The movement fact was admitted.</summary>
    Admitted,
    /// <summary>The exact movement fact was already admitted.</summary>
    Replay,
    /// <summary>An identity, digest, endpoint, or generation conflicted.</summary>
    Conflict,
    /// <summary>An endpoint or occurrence question could not be established.</summary>
    Unknown,
    /// <summary>The family or conservation guard rejected the command.</summary>
    Rejected,
    /// <summary>Some external or cross-owner consequence remains after local termination.</summary>
    Residual,
}

/// <summary>Returns an authority-specific movement result with explicit residue and uncertainty.</summary>
public sealed record ValueMovementResult
{
    /// <summary>Gets the exact closed outcome.</summary>
    public ValueMovementResultKind Kind { get; }
    /// <summary>Gets the fact only for admitted and replay outcomes.</summary>
    public ValueMovementFact? Fact { get; }
    /// <summary>Gets the bounded code for all non-fact outcomes.</summary>
    public string? Code { get; }
    private ValueMovementResult(ValueMovementResultKind kind, ValueMovementFact? fact, string? code) => (Kind, Fact, Code) = (kind, fact, code);

    /// <summary>Creates an admitted or replay result.</summary>
    /// <param name="kind">Admitted or replay.</param><param name="fact">The exact immutable fact.</param>
    /// <returns>A fact-bearing result.</returns>
    /// <exception cref="ArgumentException">The kind is not fact-bearing.</exception>
    public static ValueMovementResult WithFact(ValueMovementResultKind kind, ValueMovementFact fact) =>
        kind is ValueMovementResultKind.Admitted or ValueMovementResultKind.Replay
            ? new(kind, fact ?? throw new ArgumentNullException(nameof(fact)), null)
            : throw new ArgumentException("Movement result kind cannot carry a fact.", nameof(kind));

    /// <summary>Creates a conflict, unknown, rejected, or residual result.</summary>
    /// <param name="kind">A supported non-fact outcome.</param><param name="code">Bounded stable diagnostic or residue token.</param>
    /// <returns>A non-fact result.</returns>
    /// <exception cref="ArgumentException">The kind or token is invalid.</exception>
    public static ValueMovementResult WithoutFact(ValueMovementResultKind kind, string code) =>
        kind is ValueMovementResultKind.Conflict or ValueMovementResultKind.Unknown or ValueMovementResultKind.Rejected or ValueMovementResultKind.Residual &&
        ScopeId.TryCreate("code", "code", code, out _)
            ? new(kind, null, code)
            : throw new ArgumentException("Invalid movement non-fact result.");
}
