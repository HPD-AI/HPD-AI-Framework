using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Results;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.EffectiveCommercialFact;

/// <summary>Names canonical routes whose effective commercial truth is owned by Effective Commercial Fact.</summary>
public enum EffectiveCommercialRoute
{
    /// <summary>Invalid default route.</summary>
    None = 0,
    /// <summary>COMM-008 effective-dated subscription lifecycle.</summary>
    Comm008,
    /// <summary>COMM-009 coordinated bundle/add-on lifecycle.</summary>
    Comm009,
    /// <summary>COMM-010 billing-cycle date and quantity change.</summary>
    Comm010,
    /// <summary>COMM-012 independently scoped commercial blocking state.</summary>
    Comm012,
}

/// <summary>Specifies Effective Commercial Fact-owned mutations.</summary>
public enum EffectiveCommercialOperation
{
    /// <summary>Invalid default operation.</summary>
    None = 0,
    /// <summary>Activates lifecycle applicability from current agreement and transition evidence.</summary>
    Activate,
    /// <summary>Appends a correction while retaining original record-time truth.</summary>
    Correct,
}

/// <summary>Specifies typed precedence between facts with an equal effective time.</summary>
public enum EffectiveCommercialPrecedence
{
    /// <summary>Invalid or unresolved precedence.</summary>
    None = 0,
    /// <summary>The fact is the initial admitted value at its effective coordinate.</summary>
    Initial,
    /// <summary>The fact explicitly supersedes a named predecessor.</summary>
    ExplicitSupersession,
    /// <summary>The fact is a correction of a named predecessor and preserves both histories.</summary>
    Correction,
}

/// <summary>Requests activation or correction of one effective commercial fact.</summary>
public sealed class EffectiveCommercialCommand
{
    /// <summary>Gets the canonical route addressed by the command.</summary>
    public EffectiveCommercialRoute Route { get; }
    /// <summary>Gets the requested owner mutation.</summary>
    public EffectiveCommercialOperation Operation { get; }
    /// <summary>Gets the continuing commercial subject.</summary>
    public SemanticId CommercialSubjectId { get; }
    /// <summary>Gets the source agreement fact reference; it is immutable evidence and not an Agreement mutator.</summary>
    public SemanticId AgreementFactId { get; }
    /// <summary>Gets the optional requested-transition reference used to authorize activation.</summary>
    public SemanticId? RequestedTransitionId { get; }
    /// <summary>Gets the canonical digest of lifecycle applicability to admit.</summary>
    public CanonicalDigest ApplicabilityDigest { get; }
    /// <summary>Gets the expected Effective Commercial Fact owner generation.</summary>
    public OwnerGeneration ExpectedGeneration { get; }
    /// <summary>Gets the agreement generation observed by the command.</summary>
    public OwnerGeneration AgreementGeneration { get; }
    /// <summary>Gets the requested-transition generation when a transition reference is supplied.</summary>
    public OwnerGeneration? TransitionGeneration { get; }
    /// <summary>Gets when this command was requested.</summary>
    public NamedTime RequestedAt { get; }
    /// <summary>Gets the semantic effective time.</summary>
    public NamedTime EffectiveAt { get; }
    /// <summary>Gets the predecessor fact required for correction.</summary>
    public SemanticId? PredecessorFactId { get; }

    /// <summary>Creates an immutable activation/correction command with explicit source generations.</summary>
    /// <param name="route">A known effective-commercial route.</param>
    /// <param name="operation">The requested mutation.</param>
    /// <param name="commercialSubjectId">The continuing commercial subject.</param>
    /// <param name="agreementFactId">The immutable agreement fact reference.</param>
    /// <param name="applicabilityDigest">The canonical applicability digest.</param>
    /// <param name="expectedGeneration">The observed owner generation.</param>
    /// <param name="agreementGeneration">The observed agreement generation.</param>
    /// <param name="requestedAt">A UTC requested time.</param>
    /// <param name="effectiveAt">A UTC effective time.</param>
    /// <param name="requestedTransitionId">Optional transition reference.</param>
    /// <param name="transitionGeneration">Required exactly when a transition reference is supplied.</param>
    /// <param name="predecessorFactId">Required for correction.</param>
    /// <exception cref="ArgumentException">Coordinates are invalid, scopes differ, or paired/lineage values are inconsistent.</exception>
    public EffectiveCommercialCommand(
        EffectiveCommercialRoute route,
        EffectiveCommercialOperation operation,
        SemanticId commercialSubjectId,
        SemanticId agreementFactId,
        CanonicalDigest applicabilityDigest,
        OwnerGeneration expectedGeneration,
        OwnerGeneration agreementGeneration,
        NamedTime requestedAt,
        NamedTime effectiveAt,
        SemanticId? requestedTransitionId = null,
        OwnerGeneration? transitionGeneration = null,
        SemanticId? predecessorFactId = null)
    {
        ArgumentNullException.ThrowIfNull(applicabilityDigest);
        var scope = commercialSubjectId.Scope;
        if (route == EffectiveCommercialRoute.None || !Enum.IsDefined(route) || operation == EffectiveCommercialOperation.None || !Enum.IsDefined(operation) ||
            !commercialSubjectId.IsValid || !agreementFactId.IsValid || agreementFactId.Scope != scope || !expectedGeneration.IsValid || !agreementGeneration.IsValid ||
            !requestedAt.IsValid || requestedAt.Kind != TimeKind.Requested || !effectiveAt.IsValid || effectiveAt.Kind != TimeKind.Effective ||
            requestedTransitionId is { IsValid: false } || (requestedTransitionId is not null && requestedTransitionId.Value.Scope != scope) ||
            (requestedTransitionId is null) != (transitionGeneration is null) || transitionGeneration is { IsValid: false } ||
            predecessorFactId is { IsValid: false } || (predecessorFactId is not null && predecessorFactId.Value.Scope != scope) ||
            (operation == EffectiveCommercialOperation.Correct && predecessorFactId is null))
            throw new ArgumentException("An effective-commercial command requires valid same-scope sources, guards, named times, and correction lineage.");
        Route = route;
        Operation = operation;
        CommercialSubjectId = commercialSubjectId;
        AgreementFactId = agreementFactId;
        ApplicabilityDigest = applicabilityDigest;
        ExpectedGeneration = expectedGeneration;
        AgreementGeneration = agreementGeneration;
        RequestedAt = requestedAt;
        EffectiveAt = effectiveAt;
        RequestedTransitionId = requestedTransitionId;
        TransitionGeneration = transitionGeneration;
        PredecessorFactId = predecessorFactId;
    }
}

/// <summary>Records immutable effective commercial truth without overwriting backdated or corrected history.</summary>
public sealed class EffectiveCommercialFactRecord
{
    /// <summary>Gets the immutable fact identity.</summary>
    public SemanticId FactId { get; }
    /// <summary>Gets the continuing commercial subject.</summary>
    public SemanticId CommercialSubjectId { get; }
    /// <summary>Gets the canonical lifecycle-applicability digest.</summary>
    public CanonicalDigest ApplicabilityDigest { get; }
    /// <summary>Gets the admitted owner generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets the immutable source agreement fact reference.</summary>
    public SemanticId AgreementFactId { get; }
    /// <summary>Gets the optional source requested-transition reference.</summary>
    public SemanticId? RequestedTransitionId { get; }
    /// <summary>Gets the optional predecessor retained for correction/supersession lineage.</summary>
    public SemanticId? PredecessorFactId { get; }
    /// <summary>Gets typed same-time precedence.</summary>
    public EffectiveCommercialPrecedence Precedence { get; }
    /// <summary>Gets the semantic effective time.</summary>
    public NamedTime EffectiveAt { get; }
    /// <summary>Gets the durable record time.</summary>
    public NamedTime RecordedAt { get; }

    /// <summary>Creates an immutable effective-commercial fact.</summary>
    /// <param name="factId">The fact identity.</param>
    /// <param name="commercialSubjectId">The continuing subject.</param>
    /// <param name="applicabilityDigest">The canonical applicability digest.</param>
    /// <param name="generation">The admitted owner generation.</param>
    /// <param name="agreementFactId">The immutable source agreement fact.</param>
    /// <param name="precedence">Typed precedence for historical reduction.</param>
    /// <param name="effectiveAt">The effective time.</param>
    /// <param name="recordedAt">The durable record time; it may be after a backdated effective coordinate.</param>
    /// <param name="requestedTransitionId">Optional source transition.</param>
    /// <param name="predecessorFactId">Required for correction or supersession precedence.</param>
    /// <exception cref="ArgumentException">A coordinate is invalid, scopes differ, or precedence lineage is inconsistent.</exception>
    public EffectiveCommercialFactRecord(
        SemanticId factId,
        SemanticId commercialSubjectId,
        CanonicalDigest applicabilityDigest,
        OwnerGeneration generation,
        SemanticId agreementFactId,
        EffectiveCommercialPrecedence precedence,
        NamedTime effectiveAt,
        NamedTime recordedAt,
        SemanticId? requestedTransitionId = null,
        SemanticId? predecessorFactId = null)
    {
        ArgumentNullException.ThrowIfNull(applicabilityDigest);
        var scope = commercialSubjectId.Scope;
        if (!factId.IsValid || !commercialSubjectId.IsValid || factId.Scope != scope || !agreementFactId.IsValid || agreementFactId.Scope != scope ||
            !generation.IsValid || precedence == EffectiveCommercialPrecedence.None || !Enum.IsDefined(precedence) ||
            !effectiveAt.IsValid || effectiveAt.Kind != TimeKind.Effective || !recordedAt.IsValid || recordedAt.Kind != TimeKind.Record ||
            requestedTransitionId is { IsValid: false } || (requestedTransitionId is not null && requestedTransitionId.Value.Scope != scope) ||
            predecessorFactId is { IsValid: false } || (predecessorFactId is not null && predecessorFactId.Value.Scope != scope) ||
            (precedence != EffectiveCommercialPrecedence.Initial && predecessorFactId is null) ||
            (precedence == EffectiveCommercialPrecedence.Initial && predecessorFactId is not null))
            throw new ArgumentException("An effective-commercial fact requires valid same-scope sources, times, and precedence lineage.");
        FactId = factId;
        CommercialSubjectId = commercialSubjectId;
        ApplicabilityDigest = applicabilityDigest;
        Generation = generation;
        AgreementFactId = agreementFactId;
        RequestedTransitionId = requestedTransitionId;
        PredecessorFactId = predecessorFactId;
        Precedence = precedence;
        EffectiveAt = effectiveAt;
        RecordedAt = recordedAt;
    }
}

/// <summary>Creates Effective Commercial Fact-specific closed results.</summary>
public static class EffectiveCommercialResults
{
    /// <summary>Returns the admitted or semantically replayed fact.</summary>
    /// <param name="fact">The admitted fact.</param>
    /// <returns>A typed successful result.</returns>
    public static PrimitiveResult<EffectiveCommercialFactRecord> Admitted(EffectiveCommercialFactRecord fact) => PrimitiveResults.Success(fact);
    /// <summary>Returns a conflict for generation, digest, source, or equal-time precedence disagreement.</summary>
    /// <param name="code">A bounded stable reason code.</param>
    /// <returns>A typed conflict result.</returns>
    public static PrimitiveResult<EffectiveCommercialFactRecord> Conflict(string code) => PrimitiveResults.NonSuccess<EffectiveCommercialFactRecord>(ResultKind.Conflict, code);
    /// <summary>Returns indeterminate when source, authorization, or current revision evidence cannot be established.</summary>
    /// <param name="code">A bounded stable reason code.</param>
    /// <returns>A typed indeterminate result.</returns>
    public static PrimitiveResult<EffectiveCommercialFactRecord> Indeterminate(string code) => PrimitiveResults.NonSuccess<EffectiveCommercialFactRecord>(ResultKind.Indeterminate, code);
    /// <summary>Returns superseded while preserving the original fact and its historical truth.</summary>
    /// <param name="code">A bounded stable reason code.</param>
    /// <returns>A typed superseded result.</returns>
    public static PrimitiveResult<EffectiveCommercialFactRecord> Superseded(string code) => PrimitiveResults.NonSuccess<EffectiveCommercialFactRecord>(ResultKind.Superseded, code);
}
