using HPD.Agent.Audio.Endpointing;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class SemanticAssessmentV1Tests
{
    [Fact]
    public void Independent_axes_coexist_without_collapsing_backchannel_into_completion()
    {
        var assessment = new SemanticAssessmentV1(
            SemanticCompletionV1.IncompleteLong,
            InteractionFunctionV1.BackchannelOpportunity,
            ProviderTurnTransitionV1.EagerEndCandidate);

        var measured = new SemanticAssessmentOutcomeV1.Measured(assessment);
        Assert.Equal(SemanticCompletionV1.IncompleteLong, measured.Assessment.Completion);
        Assert.Equal(InteractionFunctionV1.BackchannelOpportunity, measured.Assessment.InteractionFunction);
        Assert.Equal(ProviderTurnTransitionV1.EagerEndCandidate, measured.Assessment.ProviderTransition);
    }

    [Fact]
    public void No_measurement_retains_reason_work_retry_authority_deadline_and_detail()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var authority = ExpectedAuthorityVectorV1.Create(session, []);
        var evidence = new NoMeasurementV1(
            NoMeasurementReasonV1.TimedOut,
            MeasurementWorkDispositionV1.Quarantined,
            MeasurementRetryabilityV1.SameIdentityOnly,
            authority,
            42,
            new BoundedAscii("semantic-eot-timeout"));

        var outcome = new SemanticAssessmentOutcomeV1.NotMeasured(evidence);
        Assert.Equal(NoMeasurementReasonV1.TimedOut, outcome.Evidence.Reason);
        Assert.Equal(MeasurementWorkDispositionV1.Quarantined, outcome.Evidence.WorkDisposition);
        Assert.Equal(MeasurementRetryabilityV1.SameIdentityOnly, outcome.Evidence.Retryability);
        Assert.Same(authority, outcome.Evidence.Authority);
        Assert.Equal(42ul, outcome.Evidence.DeadlineMonotonicNanoseconds);
        Assert.Equal("semantic-eot-timeout", outcome.Evidence.Detail.ToString());
    }

    [Fact]
    public void Unknown_work_cannot_authorize_a_new_identity_retry()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var authority = ExpectedAuthorityVectorV1.Create(session, []);
        Assert.Throws<ArgumentException>(() => new NoMeasurementV1(
            NoMeasurementReasonV1.Unavailable,
            MeasurementWorkDispositionV1.OutcomeUnknown,
            MeasurementRetryabilityV1.NewIdentityRequired,
            authority,
            0,
            new BoundedAscii("unknown-effect")));
    }

    [Fact]
    public void All_closed_axes_reject_zero_and_have_unique_values()
    {
        AssertClosed<SemanticCompletionV1>();
        AssertClosed<InteractionFunctionV1>();
        AssertClosed<ProviderTurnTransitionV1>();
        AssertClosed<NoMeasurementReasonV1>();
        AssertClosed<MeasurementWorkDispositionV1>();
        AssertClosed<MeasurementRetryabilityV1>();
        Assert.Throws<ArgumentException>(() => new SemanticAssessmentV1(
            0, InteractionFunctionV1.Unknown, ProviderTurnTransitionV1.NotObservable));
    }

    private static void AssertClosed<T>() where T : struct, Enum
    {
        var values = Enum.GetValues<T>().Select(static value => Convert.ToUInt16(value)).ToArray();
        Assert.DoesNotContain((ushort)0, values);
        Assert.Equal(values.Length, values.Distinct().Count());
    }
}
