using HPD.Agent.Audio.Authority;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Runtime.Providers;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class LiveAudioOutputGenerationV2Tests
{
    [Fact]
    public void New_generation_activates_one_OutputV2_controller()
    {
        var fixture = Fixture();
        var generation = Assert.IsType<LiveAudioOutputGenerationV2>(LiveAudioOutputGenerationV2.TryCreate(fixture.Authority));
        var activated = Assert.IsType<LiveAudioOutputActivationResultV2.Activated>(generation.Activate(fixture.Offer));
        Assert.Equal(fixture.Output, generation.OutputGeneration);
        Assert.IsType<OutputCommandResultV2.Applied>(activated.Controller.Apply(new OutputCommandV2.Generate(OperationId.Create(), 0, 4)));
    }

    [Fact]
    public void Legacy_generation_without_Output_axis_remains_read_only()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var legacy = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);
        Assert.Null(LiveAudioOutputGenerationV2.TryCreate(legacy));
    }

    [Fact]
    public void Exact_retry_returns_the_same_effect_owner()
    {
        var fixture = Fixture();
        var generation = new LiveAudioOutputGenerationV2(fixture.Authority, 4, 16);
        var first = Assert.IsType<LiveAudioOutputActivationResultV2.Activated>(generation.Activate(fixture.Offer));
        var retry = Assert.IsType<LiveAudioOutputActivationResultV2.Duplicate>(generation.Activate(fixture.Offer));
        Assert.Same(first.Controller, retry.Controller);
        Assert.Equal(first.Receipt, retry.Receipt);
    }

    [Fact]
    public void Changed_retry_cannot_create_a_second_effect_owner()
    {
        var fixture = Fixture();
        var generation = new LiveAudioOutputGenerationV2(fixture.Authority, 4, 16);
        var first = Assert.IsType<LiveAudioOutputActivationResultV2.Activated>(generation.Activate(fixture.Offer));
        var changed = new OutputOfferV2(fixture.Offer.OperationId, fixture.Output, fixture.Offer.MaximumUnits + 1,
            fixture.Offer.ContentFingerprint, fixture.Offer.Origin);
        var rejected = Assert.IsType<LiveAudioOutputActivationResultV2.Rejected>(generation.Activate(changed));
        Assert.Equal("output-offer-contradiction", rejected.SafeCode.ToString());
        Assert.Equal(0UL, first.Controller.Read().Revision);
    }

    [Fact]
    public void Stale_generation_has_no_legacy_fallback()
    {
        var fixture = Fixture();
        var generation = new LiveAudioOutputGenerationV2(fixture.Authority, 4, 16);
        var stale = new OutputOfferV2(OperationId.Create(), OutputGenerationId.Create(), 10, Hash(7), fixture.Offer.Origin);
        var rejected = Assert.IsType<LiveAudioOutputActivationResultV2.Rejected>(generation.Activate(stale));
        Assert.Equal("output-generation-stale", rejected.SafeCode.ToString());
        Assert.DoesNotContain(typeof(LiveAudioOutputGenerationV2).GetMethods(), method =>
            method.ReturnType.Name.Contains("OutputFlow", StringComparison.Ordinal));
    }

    [Fact]
    public void Activated_controller_persists_distinct_generated_sent_played_and_heard_axes()
    {
        var fixture = Fixture();
        var generation = new LiveAudioOutputGenerationV2(fixture.Authority, 4, 16);
        var controller = Assert.IsType<LiveAudioOutputActivationResultV2.Activated>(
            generation.Activate(fixture.Offer)).Controller;
        var provider = new DeterministicPcmSynthesisProviderV2(OutputSynthesisFamilyV2.SegmentedPcm);
        var sink = new ManualOutputSinkEffectPortV2();

        Assert.IsType<OutputPipelineResultV2.Applied>(controller.Generate(
            new OutputSynthesisRequestV2(OperationId.Create(), OutputSynthesisFamilyV2.SegmentedPcm, "hello", 10),
            provider));
        Assert.IsType<OutputPipelineResultV2.Applied>(controller.Send(
            new OutputSinkEffectV2.Send(OperationId.Create(), 5), sink));
        Assert.IsType<OutputPipelineResultV2.Applied>(controller.Play(
            new OutputSinkEffectV2.Play(OperationId.Create(), 4), sink));
        Assert.IsType<OutputPipelineResultV2.Applied>(controller.Hear(
            new OutputSinkEffectV2.Hear(OperationId.Create(), 3), sink));

        var status = controller.Read();
        Assert.Equal((5L, 5L, 4L, 3L),
            (status.GeneratedUntil, status.SentUntil, status.PlayedUntil, status.HeardUntil));
    }

    private static (OutputOfferV2 Offer, ExpectedAuthorityVectorV1 Authority, OutputGenerationId Output) Fixture()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var turn = TurnGenerationId.Create(); var provider = ProviderGenerationId.Create();
        var route = RouteGenerationId.Create(); var output = OutputGenerationId.Create();
        var authority = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Turn(turn),
            new AuthorityAxisValueV1.Provider(provider), new AuthorityAxisValueV1.Route(route),
            new AuthorityAxisValueV1.Output(output)]);
        var decision = new TurnDecisionFinalizedV1(OperationId.Create(), new JournalPositionV1(session, 4), authority, 1);
        var plan = new ProviderParticipantPlanV1(ParticipantId.Create(), ProviderId.Create(), provider, route, authority, Hash(3), 2);
        var origin = new OutputOriginEvidenceV2(decision, new ProviderParticipantSnapshotV1(2,
            ProviderParticipantPhaseV1.Effective, plan, 0, null));
        return (new OutputOfferV2(OperationId.Create(), output, 10, Hash(4), origin), authority, output);
    }

    private static Hash256 Hash(byte value) => Hash256.Compute(new[] { value });
}
