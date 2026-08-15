using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.MeasuredFact;

/// <summary>Identifies how an atomic measured quantity is semantically classified.</summary>
public enum MeasurementFactKind
{
    /// <summary>Invalid default; it is never an admitted fact kind.</summary>
    None = 0,
    /// <summary>A positive or negative quantity observed for one bounded interval.</summary>
    Quantity = 1,
    /// <summary>A presence occurrence whose quantity is exactly one.</summary>
    Occurrence = 2,
    /// <summary>A tombstone that supersedes an earlier admitted measured fact.</summary>
    Tombstone = 3
}

/// <summary>Represents a bounded, unit-bearing decimal quantity without assigning economic value.</summary>
/// <remarks>The unit is a lowercase ASCII semantic token. This type is not money and performs no conversion.</remarks>
public readonly record struct MeasuredQuantity
{
    /// <summary>Gets the exact base-10 quantity.</summary>
    public decimal Value { get; }
    /// <summary>Gets the semantic unit token.</summary>
    public string Unit { get; }
    /// <summary>Gets whether this value has a valid unit; zero and negative quantities are valid.</summary>
    public bool IsValid => Unit is not null;

    private MeasuredQuantity(decimal value, string unit) => (Value, Unit) = (value, unit);

    /// <summary>Creates a quantity after validating its unit.</summary>
    /// <param name="value">The exact decimal quantity.</param>
    /// <param name="unit">A non-empty lowercase ASCII unit token.</param>
    /// <returns>A valid owned quantity.</returns>
    /// <exception cref="ArgumentException"><paramref name="unit"/> is invalid or over-bound.</exception>
    public static MeasuredQuantity Create(decimal value, string unit) =>
        ContractToken.TryValidate(unit, out var stable) ? new(value, stable) : throw new ArgumentException("A measured quantity requires a bounded lowercase-ASCII unit.", nameof(unit));
}

/// <summary>Requests admission of one atomic measured fact under an explicit source identity and semantic digest.</summary>
/// <remarks>Arrival or storage of this command does not establish admission. All retained inputs are immutable owned values.</remarks>
public sealed record AdmitMeasuredFactCommand
{
    /// <summary>Gets the identity owned by the Measured Fact authority.</summary>
    public SemanticId FactId { get; }
    /// <summary>Gets the subject whose activity was measured.</summary>
    public SemanticId SubjectId { get; }
    /// <summary>Gets the source-scoped idempotency identity.</summary>
    public SemanticId SourceId { get; }
    /// <summary>Gets the semantic digest used to distinguish replay from conflicting reuse.</summary>
    public CanonicalDigest SemanticDigest { get; }
    /// <summary>Gets the atomic quantity; it is not an aggregate or valuation.</summary>
    public MeasuredQuantity Quantity { get; }
    /// <summary>Gets the inclusive source occurrence start.</summary>
    public NamedTime OccurredFrom { get; }
    /// <summary>Gets the exclusive source occurrence end.</summary>
    public NamedTime OccurredUntil { get; }
    /// <summary>Gets the measurement definition revision that interpreted the source.</summary>
    public Revision DefinitionRevision { get; }
    /// <summary>Gets the expected owner generation used by a later authority-local compare-bind operation.</summary>
    public OwnerGeneration ExpectedGeneration { get; }

    /// <summary>Creates a fully scoped atomic measurement admission command.</summary>
    /// <param name="factId">The new Measured Fact authority identity.</param>
    /// <param name="subjectId">The identity of the measured subject.</param>
    /// <param name="sourceId">The source-scoped idempotency identity.</param>
    /// <param name="semanticDigest">The canonical semantic digest used for replay/conflict decisions.</param>
    /// <param name="quantity">The exact atomic, unit-bearing quantity.</param>
    /// <param name="occurredFrom">The inclusive source occurrence start.</param>
    /// <param name="occurredUntil">The exclusive source occurrence end.</param>
    /// <param name="definitionRevision">The measurement definition revision used.</param>
    /// <param name="expectedGeneration">The authority generation expected during compare-bind.</param>
    /// <exception cref="ArgumentException">An identity, time, quantity, revision, generation, authority scope, or interval is invalid.</exception>
    public AdmitMeasuredFactCommand(SemanticId factId, SemanticId subjectId, SemanticId sourceId, CanonicalDigest semanticDigest,
        MeasuredQuantity quantity, NamedTime occurredFrom, NamedTime occurredUntil, Revision definitionRevision, OwnerGeneration expectedGeneration)
    {
        ArgumentNullException.ThrowIfNull(semanticDigest);
        if (!factId.IsValid || !subjectId.IsValid || !sourceId.IsValid || !quantity.IsValid || !definitionRevision.IsValid || !expectedGeneration.IsValid ||
            occurredFrom.Kind != TimeKind.Source || occurredUntil.Kind != TimeKind.Source || occurredUntil.Value <= occurredFrom.Value ||
            factId.Scope.Authority != "measured-fact")
            throw new ArgumentException("Invalid measured-fact admission command.");
        (FactId, SubjectId, SourceId, SemanticDigest, Quantity, OccurredFrom, OccurredUntil, DefinitionRevision, ExpectedGeneration) =
            (factId, subjectId, sourceId, semanticDigest, quantity, occurredFrom, occurredUntil, definitionRevision, expectedGeneration);
    }
}

/// <summary>Records one admitted atomic measurement without aggregate, pricing, or obligation authority.</summary>
public sealed record MeasuredFactRecord
{
    /// <summary>Gets the admitted command whose semantic content is preserved.</summary>
    public AdmitMeasuredFactCommand Admission { get; }
    /// <summary>Gets the resulting authority generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets when the authority accepted the fact.</summary>
    public NamedTime AcceptedAt { get; }
    /// <summary>Gets the contract representation version.</summary>
    public ContractVersion ContractVersion { get; }

    /// <summary>Creates an immutable admitted measurement record.</summary>
    /// <param name="admission">The validated atomic admission command.</param>
    /// <param name="generation">The newly accepted owner generation.</param>
    /// <param name="acceptedAt">The UTC acceptance time.</param>
    /// <param name="contractVersion">The representation contract version.</param>
    /// <exception cref="ArgumentException">Generation, acceptance time, or contract version is invalid.</exception>
    public MeasuredFactRecord(AdmitMeasuredFactCommand admission, OwnerGeneration generation, NamedTime acceptedAt, ContractVersion contractVersion)
    {
        ArgumentNullException.ThrowIfNull(admission);
        if (!generation.IsValid || acceptedAt.Kind != TimeKind.Accepted || !contractVersion.IsValid) throw new ArgumentException("Invalid measured-fact record metadata.");
        (Admission, Generation, AcceptedAt, ContractVersion) = (admission, generation, acceptedAt, contractVersion);
    }
}

/// <summary>Requests an append-only correction or tombstone of a prior measured fact.</summary>
public sealed record CorrectMeasuredFactCommand
{
    /// <summary>Gets the predecessor fact identity; it is never overwritten.</summary>
    public SemanticId PredecessorFactId { get; }
    /// <summary>Gets the predecessor generation that must still be current for this correction.</summary>
    public OwnerGeneration ExpectedPredecessorGeneration { get; }
    /// <summary>Gets the successor admission, or <see langword="null"/> for a tombstone.</summary>
    public AdmitMeasuredFactCommand? Successor { get; }
    /// <summary>Gets a stable reason token explaining the correction.</summary>
    public string Reason { get; }

    /// <summary>Creates a correction that retains predecessor lineage and never encodes aggregate subtraction.</summary>
    /// <param name="predecessorFactId">The immutable predecessor identity.</param>
    /// <param name="expectedPredecessorGeneration">The authority generation that must still be current.</param>
    /// <param name="successor">A complete successor admission, or <see langword="null"/> for a tombstone.</param>
    /// <param name="reason">A stable bounded reason token.</param>
    /// <exception cref="ArgumentException">Identity, generation, reason, or successor lineage is invalid.</exception>
    public CorrectMeasuredFactCommand(SemanticId predecessorFactId, OwnerGeneration expectedPredecessorGeneration, AdmitMeasuredFactCommand? successor, string reason)
    {
        if (!predecessorFactId.IsValid || !expectedPredecessorGeneration.IsValid || !ContractToken.TryValidate(reason, out var stable) ||
            (successor is not null && successor.FactId == predecessorFactId)) throw new ArgumentException("Invalid measured-fact correction.");
        (PredecessorFactId, ExpectedPredecessorGeneration, Successor, Reason) = (predecessorFactId, expectedPredecessorGeneration, successor, stable);
    }
}

internal static class ContractToken
{
    internal static bool TryValidate(string? candidate, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrEmpty(candidate) || candidate.Length > ScopeId.MaximumComponentUtf8Bytes) return false;
        foreach (var c in candidate) if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.' or '_')) return false;
        value = candidate;
        return true;
    }
}
