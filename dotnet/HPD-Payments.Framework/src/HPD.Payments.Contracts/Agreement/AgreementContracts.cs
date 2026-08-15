using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Results;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.Agreement;

/// <summary>Names canonical commercial routes owned by Agreement.</summary>
public enum AgreementRoute
{
    /// <summary>Invalid default route.</summary>
    None = 0,
    /// <summary>COMM-001 payer and bill-to context.</summary>
    Comm001,
    /// <summary>COMM-002 account hierarchy and delegated responsibility.</summary>
    Comm002,
    /// <summary>COMM-003 reusable external/business keys.</summary>
    Comm003,
    /// <summary>COMM-004 versioned catalog and effective pricing.</summary>
    Comm004,
    /// <summary>COMM-005 products, plans, phases, add-ons, and compatibility.</summary>
    Comm005,
    /// <summary>COMM-006 negotiated overrides.</summary>
    Comm006,
    /// <summary>COMM-007 quote, order, order-form, and signature terms.</summary>
    Comm007,
    /// <summary>COMM-011 future commercial responsibility transfer.</summary>
    Comm011,
    /// <summary>COMM-013 agreement-sourced feature privileges.</summary>
    Comm013,
    /// <summary>COMM-014 commercial-definition resolution provenance.</summary>
    Comm014,
    /// <summary>COMM-016 commercial-correction impact manifest.</summary>
    Comm016,
}

/// <summary>Specifies mutations owned exclusively by Agreement.</summary>
public enum AgreementOperation
{
    /// <summary>Invalid default operation.</summary>
    None = 0,
    /// <summary>Proposes terms without admitting them as accepted commercial truth.</summary>
    Propose,
    /// <summary>Accepts the proposed terms under current guards.</summary>
    Accept,
    /// <summary>Appends successor terms without rewriting the predecessor.</summary>
    Amend,
}

/// <summary>Requests proposal, acceptance, or amendment of one agreement.</summary>
public sealed class AgreementCommand
{
    /// <summary>Gets the canonical route addressed by this command.</summary>
    public AgreementRoute Route { get; }
    /// <summary>Gets the requested Agreement-owned operation.</summary>
    public AgreementOperation Operation { get; }
    /// <summary>Gets the agreement's complete semantic identity.</summary>
    public SemanticId AgreementId { get; }
    /// <summary>Gets the owner-defined digest of bounded terms, parties, and applicable manifests.</summary>
    public CanonicalDigest TermsDigest { get; }
    /// <summary>Gets the expected agreement generation; mismatches must return conflict instead of overwriting history.</summary>
    public OwnerGeneration ExpectedGeneration { get; }
    /// <summary>Gets the action-time party authorization revision.</summary>
    public Revision PartyAuthorizationRevision { get; }
    /// <summary>Gets the exact bounded terms-manifest revision.</summary>
    public Revision TermsManifestRevision { get; }
    /// <summary>Gets when the command was explicitly requested.</summary>
    public NamedTime RequestedAt { get; }
    /// <summary>Gets when the proposed terms begin to apply if admitted.</summary>
    public NamedTime EffectiveAt { get; }
    /// <summary>Gets the predecessor agreement fact for an amendment; null is permitted only for an initial proposal or acceptance.</summary>
    public SemanticId? PredecessorFactId { get; }

    /// <summary>Creates an immutable, fully guarded Agreement command.</summary>
    /// <param name="route">A known Agreement route.</param>
    /// <param name="operation">The requested mutation.</param>
    /// <param name="agreementId">The agreement identity.</param>
    /// <param name="termsDigest">The owner-defined terms digest.</param>
    /// <param name="expectedGeneration">The observed generation.</param>
    /// <param name="partyAuthorizationRevision">Current action-specific party authorization.</param>
    /// <param name="termsManifestRevision">The terms-manifest revision.</param>
    /// <param name="requestedAt">A UTC requested time.</param>
    /// <param name="effectiveAt">A UTC effective time; backdating is represented explicitly and is not silently normalized.</param>
    /// <param name="predecessorFactId">The predecessor fact required for amendment.</param>
    /// <exception cref="ArgumentException">A coordinate is invalid, unknown, wrongly timed, or amendment lineage is absent.</exception>
    public AgreementCommand(
        AgreementRoute route,
        AgreementOperation operation,
        SemanticId agreementId,
        CanonicalDigest termsDigest,
        OwnerGeneration expectedGeneration,
        Revision partyAuthorizationRevision,
        Revision termsManifestRevision,
        NamedTime requestedAt,
        NamedTime effectiveAt,
        SemanticId? predecessorFactId = null)
    {
        ArgumentNullException.ThrowIfNull(termsDigest);
        if (route == AgreementRoute.None || !Enum.IsDefined(route) || operation == AgreementOperation.None || !Enum.IsDefined(operation) ||
            !agreementId.IsValid || !expectedGeneration.IsValid || !partyAuthorizationRevision.IsValid || !termsManifestRevision.IsValid ||
            !requestedAt.IsValid || requestedAt.Kind != TimeKind.Requested || !effectiveAt.IsValid || effectiveAt.Kind != TimeKind.Effective ||
            (predecessorFactId is { IsValid: false }) || (operation == AgreementOperation.Amend && predecessorFactId is null))
            throw new ArgumentException("An agreement command requires valid owner guards, named times, and amendment lineage.");
        Route = route;
        Operation = operation;
        AgreementId = agreementId;
        TermsDigest = termsDigest;
        ExpectedGeneration = expectedGeneration;
        PartyAuthorizationRevision = partyAuthorizationRevision;
        TermsManifestRevision = termsManifestRevision;
        RequestedAt = requestedAt;
        EffectiveAt = effectiveAt;
        PredecessorFactId = predecessorFactId;
    }
}

/// <summary>Records immutable Agreement-owned commercial terms admitted for an effective interval.</summary>
public sealed class AcceptedAgreementFact
{
    /// <summary>Gets the immutable fact identity, distinct from the continuing agreement identity.</summary>
    public SemanticId FactId { get; }
    /// <summary>Gets the continuing agreement identity.</summary>
    public SemanticId AgreementId { get; }
    /// <summary>Gets the canonical digest of terms admitted at this generation.</summary>
    public CanonicalDigest TermsDigest { get; }
    /// <summary>Gets the admitted agreement generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets the optional predecessor fact; history is append-only.</summary>
    public SemanticId? PredecessorFactId { get; }
    /// <summary>Gets the effective interval start.</summary>
    public NamedTime EffectiveFrom { get; }
    /// <summary>Gets the optional exclusive effective interval end.</summary>
    public NamedTime? EffectiveUntil { get; }
    /// <summary>Gets the local durable record time.</summary>
    public NamedTime RecordedAt { get; }

    /// <summary>Creates an accepted agreement fact with explicit lineage and effective-time coordinates.</summary>
    /// <param name="factId">The immutable fact identity.</param>
    /// <param name="agreementId">The continuing agreement identity.</param>
    /// <param name="termsDigest">The admitted terms digest.</param>
    /// <param name="generation">The admitted owner generation.</param>
    /// <param name="effectiveFrom">The effective interval start.</param>
    /// <param name="recordedAt">The record time; backdated effective facts may precede it.</param>
    /// <param name="predecessorFactId">The predecessor fact for a successor.</param>
    /// <param name="effectiveUntil">The optional exclusive effective interval end.</param>
    /// <exception cref="ArgumentException">A coordinate is invalid, scopes differ, or the interval is empty/inverted.</exception>
    public AcceptedAgreementFact(
        SemanticId factId,
        SemanticId agreementId,
        CanonicalDigest termsDigest,
        OwnerGeneration generation,
        NamedTime effectiveFrom,
        NamedTime recordedAt,
        SemanticId? predecessorFactId = null,
        NamedTime? effectiveUntil = null)
    {
        ArgumentNullException.ThrowIfNull(termsDigest);
        if (!factId.IsValid || !agreementId.IsValid || factId.Scope != agreementId.Scope || !generation.IsValid ||
            !effectiveFrom.IsValid || effectiveFrom.Kind != TimeKind.Effective || !recordedAt.IsValid || recordedAt.Kind != TimeKind.Record ||
            predecessorFactId is { IsValid: false } || (predecessorFactId is not null && predecessorFactId.Value.Scope != agreementId.Scope) ||
            effectiveUntil is { IsValid: false } || (effectiveUntil is not null && (effectiveUntil.Value.Kind != TimeKind.Effective || effectiveUntil.Value.Value <= effectiveFrom.Value)))
            throw new ArgumentException("An accepted agreement requires valid same-scope identities, generation, lineage, and effective interval.");
        FactId = factId;
        AgreementId = agreementId;
        TermsDigest = termsDigest;
        Generation = generation;
        EffectiveFrom = effectiveFrom;
        RecordedAt = recordedAt;
        PredecessorFactId = predecessorFactId;
        EffectiveUntil = effectiveUntil;
    }
}

/// <summary>Creates Agreement-specific closed results without a universal mutable status.</summary>
public static class AgreementResults
{
    /// <summary>Returns the admitted fact.</summary>
    /// <param name="fact">The non-null admitted or replayed fact.</param>
    /// <returns>A typed successful result.</returns>
    public static PrimitiveResult<AcceptedAgreementFact> Admitted(AcceptedAgreementFact fact) => PrimitiveResults.Success(fact);

    /// <summary>Returns a generation or digest conflict while preserving the existing fact.</summary>
    /// <param name="code">A bounded stable reason code.</param>
    /// <returns>A typed conflict result.</returns>
    public static PrimitiveResult<AcceptedAgreementFact> Conflict(string code) => PrimitiveResults.NonSuccess<AcceptedAgreementFact>(ResultKind.Conflict, code);

    /// <summary>Returns an indeterminate decision when current authorization, manifest, or predecessor evidence cannot be established.</summary>
    /// <param name="code">A bounded stable reason code.</param>
    /// <returns>A typed indeterminate result.</returns>
    public static PrimitiveResult<AcceptedAgreementFact> Indeterminate(string code) => PrimitiveResults.NonSuccess<AcceptedAgreementFact>(ResultKind.Indeterminate, code);

    /// <summary>Returns unsupported when a valid semantic profile or agreement variant is outside the admitted reader range.</summary>
    /// <param name="code">A bounded stable reason code.</param>
    /// <returns>A typed unsupported result.</returns>
    public static PrimitiveResult<AcceptedAgreementFact> Unsupported(string code) => PrimitiveResults.NonSuccess<AcceptedAgreementFact>(ResultKind.Unsupported, code);
}
