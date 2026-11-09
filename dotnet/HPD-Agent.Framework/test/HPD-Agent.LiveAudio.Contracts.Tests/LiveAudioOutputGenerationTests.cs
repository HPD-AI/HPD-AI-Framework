using HPD.Agent.Audio.Authority;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Runtime.Providers;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class LiveAudioOutputGenerationTests
{
    [Fact]
    public void New_generation_activates_one_Output_controller()
    {
        var fixture = Fixture();
        var generation = Assert.IsType<LiveAudioOutputGeneration>(LiveAudioOutputGeneration.TryCreate(fixture.Authority));
        var activated = Assert.IsType<LiveAudioOutputActivationResult.Activated>(generation.Activate(fixture.Offer));
        Assert.Equal(fixture.Output, generation.OutputGeneration);
        Assert.IsType<OutputCommandResult.Applied>(activated.Controller.Apply(new OutputCommand.Generate(OperationId.Create(), 0, 4)));
    }

    [Fact]
    public void Legacy_generation_without_Output_axis_remains_read_only()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var legacy = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);
        Assert.Null(LiveAudioOutputGeneration.TryCreate(legacy));
    }

    [Fact]
    public void Exact_retry_returns_the_same_effect_owner()
    {
        var fixture = Fixture();
        var generation = new LiveAudioOutputGeneration(fixture.Authority, 4, 16);
        var first = Assert.IsType<LiveAudioOutputActivationResult.Activated>(generation.Activate(fixture.Offer));
        var retry = Assert.IsType<LiveAudioOutputActivationResult.Duplicate>(generation.Activate(fixture.Offer));
        Assert.Same(first.Controller, retry.Controller);
        Assert.Equal(first.Receipt, retry.Receipt);
    }

    [Fact]
    public void Changed_retry_cannot_create_a_second_effect_owner()
    {
        var fixture = Fixture();
        var generation = new LiveAudioOutputGeneration(fixture.Authority, 4, 16);
        var first = Assert.IsType<LiveAudioOutputActivationResult.Activated>(generation.Activate(fixture.Offer));
        var changed = new OutputOffer(fixture.Offer.OperationId, fixture.Output, fixture.Offer.MaximumUnits + 1,
            fixture.Offer.ContentFingerprint, fixture.Offer.Origin);
        var rejected = Assert.IsType<LiveAudioOutputActivationResult.Rejected>(generation.Activate(changed));
        Assert.Equal("output-offer-contradiction", rejected.SafeCode.ToString());
        Assert.Equal(0UL, first.Controller.Read().Revision);
    }

    [Fact]
    public void Stale_generation_has_no_legacy_fallback()
    {
        var fixture = Fixture();
        var generation = new LiveAudioOutputGeneration(fixture.Authority, 4, 16);
        var stale = new OutputOffer(OperationId.Create(), OutputGenerationId.Create(), 10, Hash(7), fixture.Offer.Origin);
        var rejected = Assert.IsType<LiveAudioOutputActivationResult.Rejected>(generation.Activate(stale));
        Assert.Equal("output-generation-stale", rejected.SafeCode.ToString());
        Assert.DoesNotContain(typeof(LiveAudioOutputGeneration).GetMethods(), method =>
            method.ReturnType.Name.Contains("OutputFlow", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Activated_controller_persists_distinct_generated_sent_played_and_heard_axes()
    {
        var fixture = Fixture();
        var generation = new LiveAudioOutputGeneration(fixture.Authority, 4, 16);
        var controller = Assert.IsType<LiveAudioOutputActivationResult.Activated>(
            generation.Activate(fixture.Offer)).Controller;
        var provider = new DeterministicPcmSynthesisProvider(OutputSynthesisFamily.SegmentedPcm);
        var sink = new ManualOutputSinkEffectPort();

        Assert.IsType<OutputPipelineResult.Applied>(controller.Generate(
            new OutputSynthesisRequest(OperationId.Create(), OutputSynthesisFamily.SegmentedPcm, "hello", 10),
            provider));
        Assert.IsType<OutputPipelineResult.Applied>(await controller.SendAsync(
            new OutputSinkEffect.Send(OperationId.Create(), 5), sink));
        Assert.IsType<OutputPipelineResult.Applied>(await controller.PlayAsync(
            new OutputSinkEffect.Play(OperationId.Create(), 4), sink));
        Assert.IsType<OutputPipelineResult.Applied>(await controller.HearAsync(
            new OutputSinkEffect.Hear(OperationId.Create(), 3), sink));

        var status = controller.Read();
        Assert.Equal((5L, 5L, 4L, 3L),
            (status.GeneratedUntil, status.SentUntil, status.PlayedUntil, status.HeardUntil));
    }

    private static (OutputOffer Offer, ExpectedAuthorityVectorV1 Authority, OutputGenerationId Output) Fixture()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var turn = TurnGenerationId.Create(); var provider = ProviderGenerationId.Create();
        var route = RouteGenerationId.Create(); var output = OutputGenerationId.Create();
        var authority = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Turn(turn),
            new AuthorityAxisValueV1.Provider(provider), new AuthorityAxisValueV1.Route(route),
            new AuthorityAxisValueV1.Output(output)]);
        var decision = new TurnDecisionFinalized(OperationId.Create(), new JournalPositionV1(session, 4), authority, 1);
        var plan = new ProviderParticipantPlan(ParticipantId.Create(), ProviderId.Create(), provider, route, authority, Hash(3), 2);
        var origin = new OutputOriginEvidence(decision, new ProviderParticipantSnapshot(2,
            ProviderParticipantPhase.Effective, plan, 0, null));
        return (new OutputOffer(OperationId.Create(), output, 10, Hash(4), origin), authority, output);
    }

    private static Hash256 Hash(byte value) => Hash256.Compute(new[] { value });
}
