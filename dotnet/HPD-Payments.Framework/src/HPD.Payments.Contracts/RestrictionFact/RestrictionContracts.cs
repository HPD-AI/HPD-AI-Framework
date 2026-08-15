using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Results;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.RestrictionFact;

/// <summary>Names the closed Restriction Fact operations.</summary>
public enum RestrictionOperation
{
    /// <summary>Invalid default operation.</summary>
    None = 0,
    /// <summary>Appends an independently owned typed restriction.</summary>
    Restrict,
    /// <summary>Releases one named predecessor restriction by the same owner.</summary>
    Release,
    /// <summary>Supersedes one named predecessor restriction by the same owner.</summary>
    Supersede,
}

/// <summary>Requests one append-only restriction, release, or supersession fact.</summary>
/// <remarks>A restriction does not remove an entitlement, decide authorization, pause work, or release facts belonging to another owner.</remarks>
public sealed record RestrictionCommand
{
    /// <summary>Gets the identity of the new immutable restriction fact.</summary>
    public SemanticId FactId { get; }
    /// <summary>Gets the continuing subject affected by the restriction history.</summary>
    public SemanticId SubjectId { get; }
    /// <summary>Gets the stable identity of the restriction owner.</summary>
    public SemanticId RestrictionOwnerId { get; }
    /// <summary>Gets the bounded independent restriction dimension.</summary>
    public string Dimension { get; }
    /// <summary>Gets immutable evidence for the restriction or release cause.</summary>
    public SemanticId CauseEvidenceId { get; }
    /// <summary>Gets the requested owner-local operation.</summary>
    public RestrictionOperation Operation { get; }
    /// <summary>Gets the owner generation that must be current.</summary>
    public OwnerGeneration ExpectedGeneration { get; }
    /// <summary>Gets the semantic interval start.</summary>
    public NamedTime EffectiveFrom { get; }
    /// <summary>Gets the optional exclusive semantic interval end.</summary>
    public NamedTime? EffectiveTo { get; }
    /// <summary>Gets the predecessor required for release or supersession.</summary>
    public SemanticId? PredecessorFactId { get; }
    /// <summary>Gets the predecessor's owner identity, required for release or supersession owner matching.</summary>
    public SemanticId? PredecessorOwnerId { get; }

    /// <summary>Creates a scoped restriction mutation with explicit owner-only release lineage.</summary>
    /// <param name="factId">Identity of the new fact.</param><param name="subjectId">Continuing affected subject.</param>
    /// <param name="restrictionOwnerId">Owner exclusively permitted to release this history.</param><param name="dimension">Bounded restriction dimension.</param>
    /// <param name="causeEvidenceId">Immutable same-scope cause evidence.</param><param name="operation">Restrict, release, or supersede.</param>
    /// <param name="expectedGeneration">Current owner generation.</param><param name="effectiveFrom">Named effective interval start.</param>
    /// <param name="effectiveTo">Optional named effective interval end after the start.</param><param name="predecessorFactId">Required for release or supersession.</param>
    /// <param name="predecessorOwnerId">Required for release or supersession and must equal the command owner.</param>
    /// <exception cref="ArgumentException">A coordinate is invalid, scopes differ, the interval is empty, or owner lineage is inconsistent.</exception>
    public RestrictionCommand(SemanticId factId, SemanticId subjectId, SemanticId restrictionOwnerId, string dimension,
        SemanticId causeEvidenceId, RestrictionOperation operation, OwnerGeneration expectedGeneration,
        NamedTime effectiveFrom, NamedTime? effectiveTo = null, SemanticId? predecessorFactId = null,
        SemanticId? predecessorOwnerId = null)
    {
        var initial = operation == RestrictionOperation.Restrict;
        var lineagePresent = predecessorFactId is not null && predecessorOwnerId is not null;
        if (!factId.IsValid || !subjectId.IsValid || !restrictionOwnerId.IsValid || !causeEvidenceId.IsValid ||
            factId.Scope != subjectId.Scope || restrictionOwnerId.Scope != subjectId.Scope || causeEvidenceId.Scope != subjectId.Scope ||
            !ScopeId.TryCreate("dimension", "restriction", dimension, out _) || operation == RestrictionOperation.None || !Enum.IsDefined(operation) ||
            !expectedGeneration.IsValid || !effectiveFrom.IsValid || effectiveFrom.Kind != TimeKind.Effective ||
            effectiveTo is { IsValid: false } || (effectiveTo is { } end && (end.Kind != TimeKind.Effective || end.Value <= effectiveFrom.Value)) ||
            predecessorFactId is { IsValid: false } || predecessorOwnerId is { IsValid: false } ||
            (predecessorFactId is { } predecessor && predecessor.Scope != subjectId.Scope) ||
            (predecessorOwnerId is { } owner && (owner.Scope != subjectId.Scope || owner != restrictionOwnerId)) ||
            initial == lineagePresent || (!initial && !lineagePresent))
            throw new ArgumentException("Restriction admission requires valid same-scope identities, interval, guard, and same-owner lineage.");
        FactId = factId; SubjectId = subjectId; RestrictionOwnerId = restrictionOwnerId; Dimension = dimension;
        CauseEvidenceId = causeEvidenceId; Operation = operation; ExpectedGeneration = expectedGeneration;
        EffectiveFrom = effectiveFrom; EffectiveTo = effectiveTo; PredecessorFactId = predecessorFactId; PredecessorOwnerId = predecessorOwnerId;
    }
}

/// <summary>Records one independently owned restriction fact and its durable time coordinates.</summary>
public sealed record RestrictionFactRecord
{
    /// <summary>Gets the validated command that was admitted.</summary>
    public RestrictionCommand Command { get; }
    /// <summary>Gets the exact successor generation of the named restriction owner.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets the canonical digest of the admitted fact.</summary>
    public CanonicalDigest FactDigest { get; }
    /// <summary>Gets durable record time, distinct from effective time.</summary>
    public NamedTime RecordedAt { get; }

    /// <summary>Creates an immutable restriction fact receipt.</summary>
    /// <param name="command">The validated restriction command.</param><param name="generation">Exact successor owner generation.</param>
    /// <param name="factDigest">Canonical digest of the admitted fact.</param><param name="recordedAt">Named durable record time.</param>
    /// <exception cref="ArgumentException">Generation is not the guarded successor or record time is invalid.</exception>
    public RestrictionFactRecord(RestrictionCommand command, OwnerGeneration generation, CanonicalDigest factDigest, NamedTime recordedAt)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(factDigest);
        if (!command.ExpectedGeneration.TryNext(out var next) || generation != next || !recordedAt.IsValid || recordedAt.Kind != TimeKind.Record)
            throw new ArgumentException("A restriction fact requires the guarded successor generation and named Record time.");
        Command = command; Generation = generation; FactDigest = factDigest; RecordedAt = recordedAt;
    }
}

/// <summary>Creates closed restriction results while preserving wrong-owner, conflict, and uncertainty branches.</summary>
public static class RestrictionResults
{
    /// <summary>Returns an admitted or exact-replay restriction fact.</summary><param name="fact">The immutable fact.</param><returns>A successful typed result.</returns>
    public static PrimitiveResult<RestrictionFactRecord> Admitted(RestrictionFactRecord fact) => PrimitiveResults.Success(fact);
    /// <summary>Returns a generation, identity, digest, interval, or precedence conflict.</summary><param name="code">Bounded stable reason.</param><returns>A conflict with no fabricated fact.</returns>
    public static PrimitiveResult<RestrictionFactRecord> Conflict(string code) => PrimitiveResults.NonSuccess<RestrictionFactRecord>(ResultKind.Conflict, code);
    /// <summary>Returns explicit uncertainty when current owner or cause evidence is unavailable.</summary><param name="code">Bounded stable reason.</param><returns>An indeterminate result.</returns>
    public static PrimitiveResult<RestrictionFactRecord> Indeterminate(string code) => PrimitiveResults.NonSuccess<RestrictionFactRecord>(ResultKind.Indeterminate, code);
    /// <summary>Rejects a release attempted by anyone other than the predecessor restriction owner.</summary><param name="code">Bounded stable reason.</param><returns>A rejected result.</returns>
    public static PrimitiveResult<RestrictionFactRecord> WrongOwner(string code) => PrimitiveResults.NonSuccess<RestrictionFactRecord>(ResultKind.Failure, code);
}
