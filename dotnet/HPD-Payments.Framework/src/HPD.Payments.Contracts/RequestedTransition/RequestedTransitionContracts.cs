using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Results;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.RequestedTransition;

/// <summary>Specifies the mutations owned exclusively by Requested Transition for COMM-015.</summary>
public enum RequestedTransitionOperation
{
    /// <summary>Invalid default operation.</summary>
    None = 0,
    /// <summary>Appends a pending future commercial transition.</summary>
    Request,
    /// <summary>Appends a successor that replaces a pending transition.</summary>
    Supersede,
    /// <summary>Appends cancellation without claiming that an already effective change was reversed.</summary>
    Cancel,
}

/// <summary>Specifies the admitted lifecycle of a COMM-015 requested transition.</summary>
public enum RequestedTransitionDisposition
{
    /// <summary>Invalid default disposition.</summary>
    None = 0,
    /// <summary>The transition remains pending until its target is admitted or it is superseded/cancelled.</summary>
    Pending,
    /// <summary>A named successor replaces this transition.</summary>
    Superseded,
    /// <summary>The pending request was cancelled.</summary>
    Cancelled,
}

/// <summary>Requests creation, supersession, or cancellation of one pending commercial commitment.</summary>
public sealed class RequestedTransitionCommand
{
    /// <summary>Gets the immutable requested-transition identity.</summary>
    public SemanticId TransitionId { get; }
    /// <summary>Gets the continuing commercial subject identity without importing another authority's mutator.</summary>
    public SemanticId CommercialSubjectId { get; }
    /// <summary>Gets the requested transition operation.</summary>
    public RequestedTransitionOperation Operation { get; }
    /// <summary>Gets the canonical semantic digest of the requested target.</summary>
    public CanonicalDigest RequestDigest { get; }
    /// <summary>Gets the expected Requested Transition timeline generation.</summary>
    public OwnerGeneration ExpectedTimelineGeneration { get; }
    /// <summary>Gets the revision of the calculation that derived the target.</summary>
    public Revision CalculationRevision { get; }
    /// <summary>Gets when the transition was requested.</summary>
    public NamedTime RequestedAt { get; }
    /// <summary>Gets when the requested target is intended to become effective.</summary>
    public NamedTime TargetEffectiveAt { get; }
    /// <summary>Gets the predecessor transition required for supersession or cancellation.</summary>
    public SemanticId? PredecessorTransitionId { get; }

    /// <summary>Creates a guarded COMM-015 command.</summary>
    /// <param name="transitionId">The immutable transition identity.</param>
    /// <param name="commercialSubjectId">The continuing commercial subject.</param>
    /// <param name="operation">The requested mutation.</param>
    /// <param name="requestDigest">The canonical requested-target digest.</param>
    /// <param name="expectedTimelineGeneration">The observed Requested Transition generation.</param>
    /// <param name="calculationRevision">The target-calculation revision.</param>
    /// <param name="requestedAt">A UTC requested time.</param>
    /// <param name="targetEffectiveAt">A UTC effective time.</param>
    /// <param name="predecessorTransitionId">The required predecessor for supersession/cancellation.</param>
    /// <exception cref="ArgumentException">A coordinate is invalid, scopes differ, or required lineage is absent.</exception>
    public RequestedTransitionCommand(
        SemanticId transitionId,
        SemanticId commercialSubjectId,
        RequestedTransitionOperation operation,
        CanonicalDigest requestDigest,
        OwnerGeneration expectedTimelineGeneration,
        Revision calculationRevision,
        NamedTime requestedAt,
        NamedTime targetEffectiveAt,
        SemanticId? predecessorTransitionId = null)
    {
        ArgumentNullException.ThrowIfNull(requestDigest);
        if (!transitionId.IsValid || !commercialSubjectId.IsValid || transitionId.Scope != commercialSubjectId.Scope ||
            operation == RequestedTransitionOperation.None || !Enum.IsDefined(operation) || !expectedTimelineGeneration.IsValid || !calculationRevision.IsValid ||
            !requestedAt.IsValid || requestedAt.Kind != TimeKind.Requested || !targetEffectiveAt.IsValid || targetEffectiveAt.Kind != TimeKind.Effective ||
            predecessorTransitionId is { IsValid: false } || (predecessorTransitionId is not null && predecessorTransitionId.Value.Scope != transitionId.Scope) ||
            (operation != RequestedTransitionOperation.Request && predecessorTransitionId is null))
            throw new ArgumentException("A requested transition requires valid same-scope identities, guards, times, and terminal lineage.");
        TransitionId = transitionId;
        CommercialSubjectId = commercialSubjectId;
        Operation = operation;
        RequestDigest = requestDigest;
        ExpectedTimelineGeneration = expectedTimelineGeneration;
        CalculationRevision = calculationRevision;
        RequestedAt = requestedAt;
        TargetEffectiveAt = targetEffectiveAt;
        PredecessorTransitionId = predecessorTransitionId;
    }
}

/// <summary>Records one immutable COMM-015 requested-transition fact.</summary>
public sealed class RequestedTransitionFact
{
    /// <summary>Gets the immutable transition identity.</summary>
    public SemanticId TransitionId { get; }
    /// <summary>Gets the commercial subject identity.</summary>
    public SemanticId CommercialSubjectId { get; }
    /// <summary>Gets the admitted canonical target digest.</summary>
    public CanonicalDigest RequestDigest { get; }
    /// <summary>Gets the Requested Transition owner generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets the explicit lifecycle disposition.</summary>
    public RequestedTransitionDisposition Disposition { get; }
    /// <summary>Gets the optional predecessor transition.</summary>
    public SemanticId? PredecessorTransitionId { get; }
    /// <summary>Gets the optional successor required by <see cref="RequestedTransitionDisposition.Superseded"/>.</summary>
    public SemanticId? SuccessorTransitionId { get; }
    /// <summary>Gets the target effective time retained even after cancellation/supersession.</summary>
    public NamedTime TargetEffectiveAt { get; }
    /// <summary>Gets the durable record time.</summary>
    public NamedTime RecordedAt { get; }

    /// <summary>Creates an append-only requested-transition fact.</summary>
    /// <param name="transitionId">The transition identity.</param>
    /// <param name="commercialSubjectId">The commercial subject.</param>
    /// <param name="requestDigest">The canonical target digest.</param>
    /// <param name="generation">The admitted generation.</param>
    /// <param name="disposition">The explicit lifecycle disposition.</param>
    /// <param name="targetEffectiveAt">The target effective time.</param>
    /// <param name="recordedAt">The durable record time.</param>
    /// <param name="predecessorTransitionId">Optional predecessor lineage.</param>
    /// <param name="successorTransitionId">Required successor when superseded.</param>
    /// <exception cref="ArgumentException">A coordinate is invalid, scopes differ, or disposition lineage is inconsistent.</exception>
    public RequestedTransitionFact(
        SemanticId transitionId,
        SemanticId commercialSubjectId,
        CanonicalDigest requestDigest,
        OwnerGeneration generation,
        RequestedTransitionDisposition disposition,
        NamedTime targetEffectiveAt,
        NamedTime recordedAt,
        SemanticId? predecessorTransitionId = null,
        SemanticId? successorTransitionId = null)
    {
        ArgumentNullException.ThrowIfNull(requestDigest);
        if (!transitionId.IsValid || !commercialSubjectId.IsValid || transitionId.Scope != commercialSubjectId.Scope || !generation.IsValid ||
            disposition == RequestedTransitionDisposition.None || !Enum.IsDefined(disposition) ||
            !targetEffectiveAt.IsValid || targetEffectiveAt.Kind != TimeKind.Effective || !recordedAt.IsValid || recordedAt.Kind != TimeKind.Record ||
            predecessorTransitionId is { IsValid: false } || successorTransitionId is { IsValid: false } ||
            (predecessorTransitionId is not null && predecessorTransitionId.Value.Scope != transitionId.Scope) ||
            (successorTransitionId is not null && successorTransitionId.Value.Scope != transitionId.Scope) ||
            (disposition == RequestedTransitionDisposition.Superseded) != (successorTransitionId is not null))
            throw new ArgumentException("A requested-transition fact requires valid same-scope coordinates and consistent disposition lineage.");
        TransitionId = transitionId;
        CommercialSubjectId = commercialSubjectId;
        RequestDigest = requestDigest;
        Generation = generation;
        Disposition = disposition;
        TargetEffectiveAt = targetEffectiveAt;
        RecordedAt = recordedAt;
        PredecessorTransitionId = predecessorTransitionId;
        SuccessorTransitionId = successorTransitionId;
    }
}

/// <summary>Creates Requested Transition-specific results.</summary>
public static class RequestedTransitionResults
{
    /// <summary>Returns an admitted or semantically replayed transition fact.</summary>
    /// <param name="fact">The admitted fact.</param>
    /// <returns>A typed successful result.</returns>
    public static PrimitiveResult<RequestedTransitionFact> Admitted(RequestedTransitionFact fact) => PrimitiveResults.Success(fact);
    /// <summary>Returns a conflict for a mismatched digest, generation, or terminal lineage.</summary>
    /// <param name="code">A bounded stable reason code.</param>
    /// <returns>A typed conflict result.</returns>
    public static PrimitiveResult<RequestedTransitionFact> Conflict(string code) => PrimitiveResults.NonSuccess<RequestedTransitionFact>(ResultKind.Conflict, code);
    /// <summary>Returns an explicit cancelled terminal.</summary>
    /// <param name="code">A bounded stable reason code.</param>
    /// <returns>A typed cancelled result.</returns>
    public static PrimitiveResult<RequestedTransitionFact> Cancelled(string code) => PrimitiveResults.NonSuccess<RequestedTransitionFact>(ResultKind.Cancelled, code);
    /// <summary>Returns an explicit superseded terminal.</summary>
    /// <param name="code">A bounded stable reason code.</param>
    /// <returns>A typed superseded result.</returns>
    public static PrimitiveResult<RequestedTransitionFact> Superseded(string code) => PrimitiveResults.NonSuccess<RequestedTransitionFact>(ResultKind.Superseded, code);
}
