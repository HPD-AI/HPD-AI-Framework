using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Results;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.EntitlementGrantRemovalFact;

/// <summary>Names the closed Entitlement Grant/Removal Fact operations.</summary>
public enum EntitlementOperation
{
    /// <summary>Invalid default operation.</summary>
    None = 0,
    /// <summary>Appends a typed grant without authorizing an action by itself.</summary>
    Grant,
    /// <summary>Appends a higher-precedence typed override with explicit lineage.</summary>
    Override,
    /// <summary>Appends removal of a named predecessor grant or override.</summary>
    Remove,
    /// <summary>Corrects a named predecessor while retaining both historical records.</summary>
    Correct,
}

/// <summary>Names typed precedence for facts at the same effective coordinate.</summary>
public enum EntitlementPrecedence
{
    /// <summary>Invalid or unresolved precedence.</summary>
    None = 0,
    /// <summary>An initial fact with no predecessor.</summary>
    Initial,
    /// <summary>An explicit override of a named predecessor.</summary>
    ExplicitOverride,
    /// <summary>An explicit removal of a named predecessor.</summary>
    Removal,
    /// <summary>An explicit correction of a named predecessor.</summary>
    Correction,
}

/// <summary>Requests one append-only entitlement grant, override, removal, or correction.</summary>
/// <remarks>The command carries provenance and owner guards but is neither an authorization decision nor a quota-position mutation.</remarks>
public sealed record EntitlementCommand
{
    /// <summary>Gets the immutable identity of the fact to append.</summary>
    public SemanticId FactId { get; }
    /// <summary>Gets the continuing subject whose entitlement history changes.</summary>
    public SemanticId SubjectId { get; }
    /// <summary>Gets the bounded feature discriminator.</summary>
    public string Feature { get; }
    /// <summary>Gets the canonical digest of the typed entitlement value; removal still retains the removed value's digest.</summary>
    public CanonicalDigest ValueDigest { get; }
    /// <summary>Gets immutable evidence establishing the fact's provenance.</summary>
    public SemanticId ProvenanceId { get; }
    /// <summary>Gets the requested append operation.</summary>
    public EntitlementOperation Operation { get; }
    /// <summary>Gets the typed equal-time precedence rule.</summary>
    public EntitlementPrecedence Precedence { get; }
    /// <summary>Gets the owner generation that must be current at admission.</summary>
    public OwnerGeneration ExpectedGeneration { get; }
    /// <summary>Gets the semantic start of the fact's interval.</summary>
    public NamedTime EffectiveFrom { get; }
    /// <summary>Gets the optional exclusive semantic end of the interval.</summary>
    public NamedTime? EffectiveTo { get; }
    /// <summary>Gets the predecessor required for override, removal, and correction.</summary>
    public SemanticId? PredecessorFactId { get; }

    /// <summary>Creates a fully scoped, bounded entitlement append command.</summary>
    /// <param name="factId">Identity of the new immutable fact.</param>
    /// <param name="subjectId">Continuing entitlement subject.</param>
    /// <param name="feature">Bounded lowercase feature token.</param>
    /// <param name="valueDigest">Canonical digest of the typed feature value.</param>
    /// <param name="provenanceId">Immutable same-scope provenance evidence.</param>
    /// <param name="operation">Grant, override, removal, or correction.</param>
    /// <param name="precedence">Precedence consistent with the operation.</param>
    /// <param name="expectedGeneration">Current owner generation to compare at admission.</param>
    /// <param name="effectiveFrom">Named effective interval start.</param>
    /// <param name="effectiveTo">Optional named effective interval end, strictly after the start.</param>
    /// <param name="predecessorFactId">Absent for an initial grant and required otherwise.</param>
    /// <exception cref="ArgumentException">A value is invalid, scopes differ, the interval is empty, or lineage and operation disagree.</exception>
    public EntitlementCommand(SemanticId factId, SemanticId subjectId, string feature, CanonicalDigest valueDigest,
        SemanticId provenanceId, EntitlementOperation operation, EntitlementPrecedence precedence,
        OwnerGeneration expectedGeneration, NamedTime effectiveFrom, NamedTime? effectiveTo = null,
        SemanticId? predecessorFactId = null)
    {
        ArgumentNullException.ThrowIfNull(valueDigest);
        var initial = operation == EntitlementOperation.Grant && precedence == EntitlementPrecedence.Initial;
        var matched = operation switch
        {
            EntitlementOperation.Grant => precedence == EntitlementPrecedence.Initial,
            EntitlementOperation.Override => precedence == EntitlementPrecedence.ExplicitOverride,
            EntitlementOperation.Remove => precedence == EntitlementPrecedence.Removal,
            EntitlementOperation.Correct => precedence == EntitlementPrecedence.Correction,
            _ => false,
        };
        if (!factId.IsValid || !subjectId.IsValid || !provenanceId.IsValid || factId.Scope != subjectId.Scope || provenanceId.Scope != subjectId.Scope ||
            !ScopeId.TryCreate("feature", "entitlement", feature, out _) || !matched || !expectedGeneration.IsValid ||
            !effectiveFrom.IsValid || effectiveFrom.Kind != TimeKind.Effective ||
            effectiveTo is { IsValid: false } || (effectiveTo is { } end && (end.Kind != TimeKind.Effective || end.Value <= effectiveFrom.Value)) ||
            predecessorFactId is { IsValid: false } || (predecessorFactId is { } predecessor && predecessor.Scope != subjectId.Scope) ||
            initial != (predecessorFactId is null))
            throw new ArgumentException("Entitlement admission requires valid same-scope identities, bounded feature, interval, guard, and explicit lineage.");
        FactId = factId; SubjectId = subjectId; Feature = feature; ValueDigest = valueDigest; ProvenanceId = provenanceId;
        Operation = operation; Precedence = precedence; ExpectedGeneration = expectedGeneration; EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo; PredecessorFactId = predecessorFactId;
    }
}

/// <summary>Records one admitted entitlement fact at its owner generation and durable record time.</summary>
public sealed record EntitlementFact
{
    /// <summary>Gets the validated command whose semantics were admitted.</summary>
    public EntitlementCommand Command { get; }
    /// <summary>Gets the exact successor owner generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets the canonical digest of the admitted fact.</summary>
    public CanonicalDigest FactDigest { get; }
    /// <summary>Gets durable record time, distinct from effective time.</summary>
    public NamedTime RecordedAt { get; }

    /// <summary>Creates an immutable entitlement fact receipt.</summary>
    /// <param name="command">The validated command.</param><param name="generation">The exact successor of its expected generation.</param>
    /// <param name="factDigest">Canonical digest of the admitted fact.</param><param name="recordedAt">Named durable record time.</param>
    /// <exception cref="ArgumentException">Generation is not the guarded successor or record time is invalid.</exception>
    public EntitlementFact(EntitlementCommand command, OwnerGeneration generation, CanonicalDigest factDigest, NamedTime recordedAt)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(factDigest);
        if (!command.ExpectedGeneration.TryNext(out var next) || generation != next || !recordedAt.IsValid || recordedAt.Kind != TimeKind.Record)
            throw new ArgumentException("An entitlement fact requires the guarded successor generation and named Record time.");
        Command = command; Generation = generation; FactDigest = factDigest; RecordedAt = recordedAt;
    }
}

/// <summary>Creates closed entitlement results without flattening conflict, rejection, or uncertainty.</summary>
public static class EntitlementResults
{
    /// <summary>Returns an admitted or exact-replay fact.</summary><param name="fact">The immutable fact.</param><returns>A successful typed result.</returns>
    public static PrimitiveResult<EntitlementFact> Admitted(EntitlementFact fact) => PrimitiveResults.Success(fact);
    /// <summary>Returns a generation, identity, digest, or precedence conflict.</summary><param name="code">Bounded stable reason.</param><returns>A conflict with no fabricated fact.</returns>
    public static PrimitiveResult<EntitlementFact> Conflict(string code) => PrimitiveResults.NonSuccess<EntitlementFact>(ResultKind.Conflict, code);
    /// <summary>Returns explicit uncertainty when current evidence cannot be established.</summary><param name="code">Bounded stable reason.</param><returns>An indeterminate result.</returns>
    public static PrimitiveResult<EntitlementFact> Indeterminate(string code) => PrimitiveResults.NonSuccess<EntitlementFact>(ResultKind.Indeterminate, code);
    /// <summary>Returns rejection for an authority-local law failure.</summary><param name="code">Bounded stable reason.</param><returns>A rejected result.</returns>
    public static PrimitiveResult<EntitlementFact> Rejected(string code) => PrimitiveResults.NonSuccess<EntitlementFact>(ResultKind.Failure, code);
}
