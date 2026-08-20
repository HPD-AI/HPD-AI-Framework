using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Endpointing;

internal enum SemanticCompletionV1 : ushort
{
    CompleteCandidate = 1,
    IncompleteShort = 2,
    IncompleteLong = 3,
    Ambiguous = 4,
    NotApplicable = 5,
    Unknown = 6,
}

internal enum InteractionFunctionV1 : ushort
{
    BackchannelOpportunity = 1,
    DiscourseContinuationExpected = 2,
    RepairOrClarificationLikely = 3,
    OrdinaryContent = 4,
    Unknown = 5,
}

internal enum ProviderTurnTransitionV1 : ushort
{
    EagerEndCandidate = 1,
    CandidateResumed = 2,
    ProviderTurnEnded = 3,
    ProviderTurnStarted = 4,
    NotObservable = 5,
}

internal sealed record SemanticAssessmentV1
{
    internal SemanticAssessmentV1(
        SemanticCompletionV1 completion,
        InteractionFunctionV1 interactionFunction,
        ProviderTurnTransitionV1 providerTransition)
    {
        if (!Enum.IsDefined(completion) || !Enum.IsDefined(interactionFunction) ||
            !Enum.IsDefined(providerTransition))
            throw new ArgumentException("Semantic assessment contains an unknown closed value.");
        Completion = completion;
        InteractionFunction = interactionFunction;
        ProviderTransition = providerTransition;
    }

    internal SemanticCompletionV1 Completion { get; }
    internal InteractionFunctionV1 InteractionFunction { get; }
    internal ProviderTurnTransitionV1 ProviderTransition { get; }
}

internal enum NoMeasurementReasonV1 : ushort
{
    Unsupported = 1,
    InsufficientInput = 2,
    NotObservable = 3,
    Lost = 4,
    TimedOut = 5,
    Unavailable = 6,
    Rejected = 7,
    Superseded = 8,
    Opaque = 9,
}

internal enum MeasurementWorkDispositionV1 : ushort
{
    NotStarted = 1,
    Released = 2,
    Quarantined = 3,
    OutcomeUnknown = 4,
}

internal enum MeasurementRetryabilityV1 : ushort
{
    Never = 1,
    SameIdentityOnly = 2,
    NewIdentityRequired = 3,
    Unknown = 4,
}

internal sealed record NoMeasurementV1
{
    internal NoMeasurementV1(
        NoMeasurementReasonV1 reason,
        MeasurementWorkDispositionV1 workDisposition,
        MeasurementRetryabilityV1 retryability,
        ExpectedAuthorityVectorV1 authority,
        ulong deadlineMonotonicNanoseconds,
        BoundedAscii detail)
    {
        if (!Enum.IsDefined(reason) || !Enum.IsDefined(workDisposition) || !Enum.IsDefined(retryability))
            throw new ArgumentException("No-measurement contains an unknown closed value.");
        ArgumentNullException.ThrowIfNull(authority);
        if (!authority.Session.IsValid) throw new ArgumentException("Authority must name a valid session.", nameof(authority));
        if (!detail.IsValid) throw new ArgumentException("A bounded detail is required.", nameof(detail));
        if (workDisposition == MeasurementWorkDispositionV1.OutcomeUnknown && retryability == MeasurementRetryabilityV1.NewIdentityRequired)
            throw new ArgumentException("Unknown work cannot be retried under a new identity.", nameof(retryability));
        Reason = reason;
        WorkDisposition = workDisposition;
        Retryability = retryability;
        Authority = authority;
        DeadlineMonotonicNanoseconds = deadlineMonotonicNanoseconds;
        Detail = detail;
    }

    internal NoMeasurementReasonV1 Reason { get; }
    internal MeasurementWorkDispositionV1 WorkDisposition { get; }
    internal MeasurementRetryabilityV1 Retryability { get; }
    internal ExpectedAuthorityVectorV1 Authority { get; }
    internal ulong DeadlineMonotonicNanoseconds { get; }
    internal BoundedAscii Detail { get; }
}

internal abstract record SemanticAssessmentOutcomeV1
{
    private SemanticAssessmentOutcomeV1() { }
    internal sealed record Measured : SemanticAssessmentOutcomeV1
    {
        internal Measured(SemanticAssessmentV1 assessment) =>
            Assessment = assessment ?? throw new ArgumentNullException(nameof(assessment));
        internal SemanticAssessmentV1 Assessment { get; }
    }
    internal sealed record NotMeasured : SemanticAssessmentOutcomeV1
    {
        internal NotMeasured(NoMeasurementV1 evidence) =>
            Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        internal NoMeasurementV1 Evidence { get; }
    }
}
