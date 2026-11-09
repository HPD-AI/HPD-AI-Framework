using HPD.Agent.Audio.Authority;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Runtime.Providers;
using HPD.Agent.Audio.Runtime.Routing;
using HPD.Agent.Audio.Runtime.Tools;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GenerationFenceRollbackV1Tests
{
    [Fact]
    public void Rollback_cannot_recreate_or_replace_an_Output_effect_owner()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var output = OutputGenerationId.Create();
        var turn = TurnGenerationId.Create();
        var provider = ProviderGenerationId.Create();
        var route = RouteGenerationId.Create();
        var authority = ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Turn(turn), new AuthorityAxisValueV1.Provider(provider),
                new AuthorityAxisValueV1.Route(route), new AuthorityAxisValueV1.Output(output)]);
        var generation = new LiveAudioOutputGeneration(authority, 4, 16);
        var offer = Offer(authority, output, provider, route);

        var activated = Assert.IsType<LiveAudioOutputActivationResult.Activated>(
            generation.Activate(offer));
        Assert.IsType<OutputCommandResult.Applied>(activated.Controller.Apply(
            new OutputCommand.Close(OperationId.Create(), 0)));

        var changed = new OutputOffer(offer.OperationId, output,
            offer.MaximumUnits + 1, offer.ContentFingerprint, offer.Origin);
        var rejected = Assert.IsType<LiveAudioOutputActivationResult.Rejected>(
            generation.Activate(changed));
        var retry = Assert.IsType<LiveAudioOutputActivationResult.Duplicate>(
            generation.Activate(offer));

        Assert.Equal("output-offer-contradiction", rejected.SafeCode.ToString());
        Assert.Same(activated.Controller, retry.Controller);
        Assert.True(retry.Controller.Read().Closed);

        var legacy = ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);
        Assert.Null(LiveAudioOutputGeneration.TryCreate(legacy));
    }

    [Fact]
    public void Rollback_cannot_reopen_S7_or_replace_S8_after_the_generation_cut()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var outputId = OutputGenerationId.Create();
        var toolId = ToolGenerationId.Create();
        var toolAuthority = ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Output(outputId), new AuthorityAxisValueV1.Tool(toolId)]);
        var output = new InMemoryOutputController(
            new OutputPlan(OperationId.Create(), outputId, toolAuthority, 8), 16);
        var tool = ToolTransactionSupervisor.Create(new ToolTransactionPlan(
            OperationId.Create(), toolId, outputId, toolAuthority,
            new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 10), true));
        var toolOwner = new LiveAudioToolGeneration(toolAuthority);
        var interruptOperation = OperationId.Create();

        Assert.IsType<OutputInterruptionResult.Applied>(
            toolOwner.Interrupt(output, tool, interruptOperation, 16));
        var changedTool = ToolTransactionSupervisor.Create(new ToolTransactionPlan(
            OperationId.Create(), toolId, outputId, toolAuthority,
            new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 11), true));
        Assert.IsType<OutputInterruptionResult.Rejected>(
            toolOwner.Interrupt(output, changedTool, interruptOperation, 16));
        Assert.True(output.Read().Closed);

        var provider = ProviderGenerationId.Create();
        var priorRoute = RouteGenerationId.Create();
        var nextRoute = RouteGenerationId.Create();
        var priorAuthority = ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Provider(provider), new AuthorityAxisValueV1.Route(priorRoute)]);
        var compiled = new CompiledRoute(OperationId.Create(), ProviderId.Create(), nextRoute,
            priorAuthority, Hash(1), Hash(2));
        var admission = RouteAuthorityAdmission.Admit(compiled, new JournalPositionV1(session, 4));
        var preparation = new RoutePreparationState(compiled,
            new RoutePreparationSnapshot(3, RoutePreparationPhase.CutoverAuthorized, OwnerSliceId.S5));
        var plan = new ProviderParticipantPlan(ParticipantId.Create(), compiled.ProviderId,
            provider, nextRoute, admission.Authority, Hash(1), 1);
        var evidence = new RouteCutoverEvidence(preparation,
            new ProviderParticipantSnapshot(2, ProviderParticipantPhase.Effective, plan, 0, null),
            admission);
        var routeOwner = new LiveAudioRouteGeneration(admission.Authority);
        var routeOperation = OperationId.Create();
        var first = Assert.IsType<LiveAudioRouteActivationResult.Activated>(
            routeOwner.Activate(routeOperation, evidence));
        var changedEvidence = evidence with
        {
            Provider = evidence.Provider with { Revision = evidence.Provider.Revision + 1 }
        };

        Assert.IsType<LiveAudioRouteActivationResult.Rejected>(
            routeOwner.Activate(routeOperation, changedEvidence));
        var retry = Assert.IsType<LiveAudioRouteActivationResult.Duplicate>(
            routeOwner.Activate(routeOperation, evidence));
        Assert.Equal(first.Receipt, retry.Receipt);

        Assert.Null(LiveAudioToolGeneration.TryCreate(ExpectedAuthorityVectorV1.Create(session, [])));
        Assert.Null(LiveAudioRouteGeneration.TryCreate(ExpectedAuthorityVectorV1.Create(session, [])));
    }

    private static OutputOffer Offer(ExpectedAuthorityVectorV1 authority, OutputGenerationId output,
        ProviderGenerationId provider, RouteGenerationId route)
    {
        var decision = new TurnDecisionFinalized(OperationId.Create(),
            new JournalPositionV1(authority.Session, 3), authority, 1);
        var plan = new ProviderParticipantPlan(ParticipantId.Create(), ProviderId.Create(),
            provider, route, authority, Hash(3), 1);
        var origin = new OutputOriginEvidence(decision,
            new ProviderParticipantSnapshot(2, ProviderParticipantPhase.Effective, plan, 0, null));
        return new OutputOffer(OperationId.Create(), output, 8, Hash(4), origin);
    }

    private static Hash256 Hash(byte value) => Hash256.Compute([value]);
}
