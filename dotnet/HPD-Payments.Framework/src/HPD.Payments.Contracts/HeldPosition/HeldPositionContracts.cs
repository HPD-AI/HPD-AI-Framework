using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.HeldPosition;

/// <summary>Identifies the policy-neutral capacity family governed by a held position.</summary>
/// <remarks>Payment allocation capacity and migration-opening capacity are deliberately distinct from wallet lots and quota windows, whose product policies are not admitted here.</remarks>
public enum HeldPositionKind
{
    /// <summary>Invalid default; it cannot be admitted.</summary>
    None = 0,
    /// <summary>Capacity held for applying a recorded payment to one or more obligations.</summary>
    PaymentAllocation = 1,
    /// <summary>Opening capacity whose provenance is retained during migration from an aggregate source.</summary>
    ProvenanceMigrationOpening = 2,
}

/// <summary>Names an append-only change to a policy-neutral held position.</summary>
public enum HeldPositionChangeKind
{
    /// <summary>Invalid default; it cannot be admitted.</summary>
    None = 0,
    /// <summary>Acquires new capacity.</summary>
    Acquire = 1,
    /// <summary>Consumes capacity against a typed application reference.</summary>
    Consume = 2,
    /// <summary>Releases capacity without claiming that another authority accepted it.</summary>
    Release = 3,
    /// <summary>Expires capacity under evidence supplied by a policy outside this contract.</summary>
    Expire = 4,
}

/// <summary>Represents a non-negative authority-local magnitude and its exact unit.</summary>
/// <remarks>This is not universal money, a wallet lot, or quota capacity. Arithmetic is checked and requires identical units.</remarks>
public readonly record struct HeldQuantity
{
    /// <summary>Gets the non-negative magnitude.</summary>
    public decimal Magnitude { get; }
    /// <summary>Gets the bounded lowercase unit token.</summary>
    public string Unit { get; }
    /// <summary>Gets whether this is a constructed dimensioned quantity.</summary>
    public bool IsValid => Magnitude >= 0m && Unit is not null;

    private HeldQuantity(decimal magnitude, string unit) => (Magnitude, Unit) = (magnitude, unit);

    /// <summary>Creates a validated quantity.</summary>
    /// <param name="magnitude">A non-negative decimal magnitude.</param>
    /// <param name="unit">A bounded lowercase unit token.</param>
    /// <returns>The validated immutable quantity.</returns>
    /// <exception cref="ArgumentException">The magnitude is negative or the unit is malformed.</exception>
    public static HeldQuantity Create(decimal magnitude, string unit) =>
        magnitude >= 0m && ScopeId.TryCreate("unit", "unit", unit, out _)
            ? new(magnitude, unit)
            : throw new ArgumentException("Held quantity must be non-negative and use a bounded lowercase unit.");

    /// <summary>Adds two quantities using checked decimal arithmetic.</summary>
    /// <param name="left">The first valid quantity.</param><param name="right">The second valid quantity in the same unit.</param>
    /// <returns>The exact checked sum.</returns>
    /// <exception cref="ArgumentException">Either quantity is invalid or units differ.</exception>
    /// <exception cref="OverflowException">The exact decimal sum is outside the decimal range.</exception>
    public static HeldQuantity Add(HeldQuantity left, HeldQuantity right)
    {
        EnsureCompatible(left, right);
        return new(checked(left.Magnitude + right.Magnitude), left.Unit);
    }

    /// <summary>Subtracts a quantity without permitting a negative capacity.</summary>
    /// <param name="available">The valid available quantity.</param><param name="consumed">The valid quantity to remove in the same unit.</param>
    /// <returns>The exact non-negative remainder.</returns>
    /// <exception cref="ArgumentException">Either quantity is invalid, units differ, or consumption exceeds capacity.</exception>
    public static HeldQuantity Subtract(HeldQuantity available, HeldQuantity consumed)
    {
        EnsureCompatible(available, consumed);
        if (consumed.Magnitude > available.Magnitude) throw new ArgumentException("Held capacity cannot become negative.", nameof(consumed));
        return new(checked(available.Magnitude - consumed.Magnitude), available.Unit);
    }

    private static void EnsureCompatible(HeldQuantity left, HeldQuantity right)
    {
        if (!left.IsValid || !right.IsValid || !StringComparer.Ordinal.Equals(left.Unit, right.Unit))
            throw new ArgumentException("Held quantities must be valid and have identical units.");
    }
}

/// <summary>Commands one guarded append to a policy-neutral Held Position lineage.</summary>
public sealed record ChangeHeldPositionCommand
{
    /// <summary>Gets the immutable fact identity.</summary>
    public SemanticId FactId { get; }
    /// <summary>Gets the Held Position owner identity.</summary>
    public SemanticId PositionId { get; }
    /// <summary>Gets the exact capacity family.</summary>
    public HeldPositionKind PositionKind { get; }
    /// <summary>Gets the requested append-only change.</summary>
    public HeldPositionChangeKind ChangeKind { get; }
    /// <summary>Gets the strictly positive change quantity.</summary>
    public HeldQuantity Quantity { get; }
    /// <summary>Gets the exact source or application evidence identity.</summary>
    public SemanticId EvidenceId { get; }
    /// <summary>Gets the digest binding the supplied evidence.</summary>
    public CanonicalDigest EvidenceDigest { get; }
    /// <summary>Gets the expected current owner generation.</summary>
    public OwnerGeneration ExpectedGeneration { get; }
    /// <summary>Gets the expected currently available quantity.</summary>
    public HeldQuantity ExpectedAvailable { get; }
    /// <summary>Gets when the change is semantically effective.</summary>
    public NamedTime EffectiveAt { get; }
    /// <summary>Gets the predecessor fact for every non-acquire change.</summary>
    public SemanticId? PredecessorFactId { get; }

    /// <summary>Creates a bounded guarded held-position command.</summary>
    /// <param name="factId">New immutable fact identity.</param><param name="positionId">Held Position lineage identity.</param>
    /// <param name="positionKind">Policy-neutral capacity family.</param><param name="changeKind">Acquire, consume, release, or expire.</param>
    /// <param name="quantity">Strictly positive change quantity.</param><param name="evidenceId">Exact immutable evidence reference.</param>
    /// <param name="evidenceDigest">Digest binding the evidence.</param><param name="expectedGeneration">Current generation guard.</param>
    /// <param name="expectedAvailable">Current capacity guard in the same unit.</param><param name="effectiveAt">Named effective time.</param>
    /// <param name="predecessorFactId">Required predecessor for non-acquire changes and forbidden for acquisition.</param>
    /// <exception cref="ArgumentException">Scope, authority, kind, quantity, guard, time, or lineage is invalid.</exception>
    public ChangeHeldPositionCommand(SemanticId factId, SemanticId positionId, HeldPositionKind positionKind,
        HeldPositionChangeKind changeKind, HeldQuantity quantity, SemanticId evidenceId, CanonicalDigest evidenceDigest,
        OwnerGeneration expectedGeneration, HeldQuantity expectedAvailable, NamedTime effectiveAt, SemanticId? predecessorFactId = null)
    {
        ArgumentNullException.ThrowIfNull(evidenceDigest);
        var scoped = factId.IsValid && positionId.IsValid && evidenceId.IsValid && factId.Scope == positionId.Scope && positionId.Scope == evidenceId.Scope;
        var kinds = positionKind != HeldPositionKind.None && Enum.IsDefined(positionKind) && changeKind != HeldPositionChangeKind.None && Enum.IsDefined(changeKind);
        var quantities = quantity.IsValid && quantity.Magnitude > 0m && expectedAvailable.IsValid && StringComparer.Ordinal.Equals(quantity.Unit, expectedAvailable.Unit);
        var predecessorValid = predecessorFactId is { } predecessor && predecessor.IsValid && predecessor.Scope == positionId.Scope;
        if (!scoped || positionId.Scope.Authority != "held-position" || !kinds || !quantities || !expectedGeneration.IsValid ||
            !effectiveAt.IsValid || effectiveAt.Kind != TimeKind.Effective || (changeKind == HeldPositionChangeKind.Acquire) == predecessorValid)
            throw new ArgumentException("Invalid held-position command.");
        if (changeKind != HeldPositionChangeKind.Acquire) _ = HeldQuantity.Subtract(expectedAvailable, quantity);
        (FactId, PositionId, PositionKind, ChangeKind, Quantity, EvidenceId, EvidenceDigest, ExpectedGeneration, ExpectedAvailable, EffectiveAt, PredecessorFactId) =
            (factId, positionId, positionKind, changeKind, quantity, evidenceId, evidenceDigest, expectedGeneration, expectedAvailable, effectiveAt, predecessorFactId);
    }
}

/// <summary>Records one admitted immutable Held Position change and its resulting capacity.</summary>
public sealed record HeldPositionFact
{
    /// <summary>Gets the exact admitted command.</summary>
    public ChangeHeldPositionCommand Command { get; }
    /// <summary>Gets the guarded successor owner generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets the checked capacity after this fact.</summary>
    public HeldQuantity AvailableAfter { get; }
    /// <summary>Gets the canonical digest of this fact.</summary>
    public CanonicalDigest FactDigest { get; }
    /// <summary>Gets the local durable record time.</summary>
    public NamedTime RecordedAt { get; }

    /// <summary>Creates and verifies an admitted fact receipt.</summary>
    /// <param name="command">The validated command.</param><param name="generation">Its exact successor generation.</param>
    /// <param name="availableAfter">The claimed checked resulting capacity.</param><param name="factDigest">Digest of this fact.</param>
    /// <param name="recordedAt">Named local record time.</param>
    /// <exception cref="ArgumentException">Generation, equation, digest, or time is invalid.</exception>
    public HeldPositionFact(ChangeHeldPositionCommand command, OwnerGeneration generation, HeldQuantity availableAfter,
        CanonicalDigest factDigest, NamedTime recordedAt)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(factDigest);
        if (!command.ExpectedGeneration.TryNext(out var next) || generation != next || !recordedAt.IsValid || recordedAt.Kind != TimeKind.Record)
            throw new ArgumentException("Invalid held-position fact generation or record time.");
        var expected = command.ChangeKind == HeldPositionChangeKind.Acquire
            ? HeldQuantity.Add(command.ExpectedAvailable, command.Quantity)
            : HeldQuantity.Subtract(command.ExpectedAvailable, command.Quantity);
        if (availableAfter != expected) throw new ArgumentException("Held-position fact violates its kind-specific capacity equation.", nameof(availableAfter));
        (Command, Generation, AvailableAfter, FactDigest, RecordedAt) = (command, generation, availableAfter, factDigest, recordedAt);
    }
}

/// <summary>Names every closed local result of a held-position change attempt.</summary>
public enum HeldPositionResultKind
{
    /// <summary>Invalid default result.</summary>
    None = 0,
    /// <summary>The fact was admitted.</summary>
    Admitted,
    /// <summary>The exact fact was already admitted.</summary>
    Replay,
    /// <summary>The identity, digest, guard, or lineage conflicted.</summary>
    Conflict,
    /// <summary>The current guarded state could not be established.</summary>
    Unknown,
    /// <summary>The requested change exceeded or violated authority-local capacity.</summary>
    Rejected,
}

/// <summary>Returns a subject-specific held-position outcome without flattening uncertainty.</summary>
public sealed record HeldPositionResult
{
    /// <summary>Gets the exact closed outcome.</summary>
    public HeldPositionResultKind Kind { get; }
    /// <summary>Gets the fact for admitted and replay outcomes.</summary>
    public HeldPositionFact? Fact { get; }
    /// <summary>Gets a bounded diagnostic code for outcomes without a fact.</summary>
    public string? Code { get; }
    private HeldPositionResult(HeldPositionResultKind kind, HeldPositionFact? fact, string? code) => (Kind, Fact, Code) = (kind, fact, code);

    /// <summary>Creates an admitted or replay result.</summary>
    /// <param name="kind">Admitted or replay.</param><param name="fact">The exact immutable fact.</param>
    /// <returns>A fact-bearing closed result.</returns>
    /// <exception cref="ArgumentException">The result kind is not fact-bearing.</exception>
    public static HeldPositionResult WithFact(HeldPositionResultKind kind, HeldPositionFact fact) =>
        kind is HeldPositionResultKind.Admitted or HeldPositionResultKind.Replay
            ? new(kind, fact ?? throw new ArgumentNullException(nameof(fact)), null)
            : throw new ArgumentException("Held-position result kind cannot carry a fact.", nameof(kind));

    /// <summary>Creates a conflict, unknown, or rejected result.</summary>
    /// <param name="kind">A supported non-fact outcome.</param><param name="code">Bounded stable diagnostic token.</param>
    /// <returns>A closed result carrying no fabricated fact.</returns>
    /// <exception cref="ArgumentException">The kind or diagnostic token is invalid.</exception>
    public static HeldPositionResult WithoutFact(HeldPositionResultKind kind, string code) =>
        kind is HeldPositionResultKind.Conflict or HeldPositionResultKind.Unknown or HeldPositionResultKind.Rejected && ScopeId.TryCreate("code", "code", code, out _)
            ? new(kind, null, code)
            : throw new ArgumentException("Invalid held-position non-fact result.");
}
