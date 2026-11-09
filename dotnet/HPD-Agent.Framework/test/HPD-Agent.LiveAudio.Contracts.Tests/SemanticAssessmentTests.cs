using HPD.Agent.Audio.Endpointing;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class SemanticAssessmentTests
{
    [Fact]
    public void Independent_axes_coexist_without_collapsing_backchannel_into_completion()
    {
        var assessment = new SemanticAssessment(
            SemanticCompletion.IncompleteLong,
            InteractionFunction.BackchannelOpportunity,
            ProviderTurnTransition.EagerEndCandidate);

        var measured = new SemanticAssessmentOutcome.Measured(assessment);
        Assert.Equal(SemanticCompletion.IncompleteLong, measured.Assessment.Completion);
        Assert.Equal(InteractionFunction.BackchannelOpportunity, measured.Assessment.InteractionFunction);
        Assert.Equal(ProviderTurnTransition.EagerEndCandidate, measured.Assessment.ProviderTransition);
    }

    [Fact]
    public void No_measurement_retains_reason_work_retry_authority_deadline_and_detail()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var authority = ExpectedAuthorityVectorV1.Create(session, []);
        var evidence = new NoMeasurement(
            NoMeasurementReason.TimedOut,
            MeasurementWorkDisposition.Quarantined,
            MeasurementRetryability.SameIdentityOnly,
            authority,
            42,
            new BoundedAscii("semantic-eot-timeout"));

        var outcome = new SemanticAssessmentOutcome.NotMeasured(evidence);
        Assert.Equal(NoMeasurementReason.TimedOut, outcome.Evidence.Reason);
        Assert.Equal(MeasurementWorkDisposition.Quarantined, outcome.Evidence.WorkDisposition);
        Assert.Equal(MeasurementRetryability.SameIdentityOnly, outcome.Evidence.Retryability);
        Assert.Same(authority, outcome.Evidence.Authority);
        Assert.Equal(42ul, outcome.Evidence.DeadlineMonotonicNanoseconds);
        Assert.Equal("semantic-eot-timeout", outcome.Evidence.Detail.ToString());
    }

    [Fact]
    public void Unknown_work_cannot_authorize_a_new_identity_retry()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var authority = ExpectedAuthorityVectorV1.Create(session, []);
        Assert.Throws<ArgumentException>(() => new NoMeasurement(
            NoMeasurementReason.Unavailable,
            MeasurementWorkDisposition.OutcomeUnknown,
            MeasurementRetryability.NewIdentityRequired,
            authority,
            0,
            new BoundedAscii("unknown-effect")));
    }

    [Fact]
    public void All_closed_axes_reject_zero_and_have_unique_values()
    {
        AssertClosed<SemanticCompletion>();
        AssertClosed<InteractionFunction>();
        AssertClosed<ProviderTurnTransition>();
        AssertClosed<NoMeasurementReason>();
        AssertClosed<MeasurementWorkDisposition>();
        AssertClosed<MeasurementRetryability>();
        Assert.Throws<ArgumentException>(() => new SemanticAssessment(
            0, InteractionFunction.Unknown, ProviderTurnTransition.NotObservable));
    }

    private static void AssertClosed<T>() where T : struct, Enum
    {
        var values = Enum.GetValues<T>().Select(static value => Convert.ToUInt16(value)).ToArray();
        Assert.DoesNotContain((ushort)0, values);
        Assert.Equal(values.Length, values.Distinct().Count());
    }
}
