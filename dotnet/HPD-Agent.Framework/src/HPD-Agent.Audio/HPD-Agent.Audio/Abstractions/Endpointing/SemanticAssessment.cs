using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Endpointing;

internal enum SemanticCompletion : ushort
{
    CompleteCandidate = 1,
    IncompleteShort = 2,
    IncompleteLong = 3,
    Ambiguous = 4,
    NotApplicable = 5,
    Unknown = 6,
}

internal enum InteractionFunction : ushort
{
    BackchannelOpportunity = 1,
    DiscourseContinuationExpected = 2,
    RepairOrClarificationLikely = 3,
    OrdinaryContent = 4,
    Unknown = 5,
}

internal enum ProviderTurnTransition : ushort
{
    EagerEndCandidate = 1,
    CandidateResumed = 2,
    ProviderTurnEnded = 3,
    ProviderTurnStarted = 4,
    NotObservable = 5,
}

internal sealed record SemanticAssessment
{
    internal SemanticAssessment(
        SemanticCompletion completion,
        InteractionFunction interactionFunction,
        ProviderTurnTransition providerTransition)
    {
        if (!Enum.IsDefined(completion) || !Enum.IsDefined(interactionFunction) ||
            !Enum.IsDefined(providerTransition))
            throw new ArgumentException("Semantic assessment contains an unknown closed value.");
        Completion = completion;
        InteractionFunction = interactionFunction;
        ProviderTransition = providerTransition;
    }

    internal SemanticCompletion Completion { get; }
    internal InteractionFunction InteractionFunction { get; }
    internal ProviderTurnTransition ProviderTransition { get; }
}

internal enum NoMeasurementReason : ushort
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

internal enum MeasurementWorkDisposition : ushort
{
    NotStarted = 1,
    Released = 2,
    Quarantined = 3,
    OutcomeUnknown = 4,
}

internal enum MeasurementRetryability : ushort
{
    Never = 1,
    SameIdentityOnly = 2,
    NewIdentityRequired = 3,
    Unknown = 4,
}

internal sealed record NoMeasurement
{
    internal NoMeasurement(
        NoMeasurementReason reason,
        MeasurementWorkDisposition workDisposition,
        MeasurementRetryability retryability,
        ExpectedAuthorityVectorV1 authority,
        ulong deadlineMonotonicNanoseconds,
        BoundedAscii detail)
    {
        if (!Enum.IsDefined(reason) || !Enum.IsDefined(workDisposition) || !Enum.IsDefined(retryability))
            throw new ArgumentException("No-measurement contains an unknown closed value.");
        ArgumentNullException.ThrowIfNull(authority);
        if (!authority.Session.IsValid) throw new ArgumentException("Authority must name a valid session.", nameof(authority));
        if (!detail.IsValid) throw new ArgumentException("A bounded detail is required.", nameof(detail));
        if (workDisposition == MeasurementWorkDisposition.OutcomeUnknown && retryability == MeasurementRetryability.NewIdentityRequired)
            throw new ArgumentException("Unknown work cannot be retried under a new identity.", nameof(retryability));
        Reason = reason;
        WorkDisposition = workDisposition;
        Retryability = retryability;
        Authority = authority;
        DeadlineMonotonicNanoseconds = deadlineMonotonicNanoseconds;
        Detail = detail;
    }

    internal NoMeasurementReason Reason { get; }
    internal MeasurementWorkDisposition WorkDisposition { get; }
    internal MeasurementRetryability Retryability { get; }
    internal ExpectedAuthorityVectorV1 Authority { get; }
    internal ulong DeadlineMonotonicNanoseconds { get; }
    internal BoundedAscii Detail { get; }
}

internal abstract record SemanticAssessmentOutcome
{
    private SemanticAssessmentOutcome() { }
    internal sealed record Measured : SemanticAssessmentOutcome
    {
        internal Measured(SemanticAssessment assessment) =>
            Assessment = assessment ?? throw new ArgumentNullException(nameof(assessment));
        internal SemanticAssessment Assessment { get; }
    }
    internal sealed record NotMeasured : SemanticAssessmentOutcome
    {
        internal NotMeasured(NoMeasurement evidence) =>
            Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        internal NoMeasurement Evidence { get; }
    }
}
