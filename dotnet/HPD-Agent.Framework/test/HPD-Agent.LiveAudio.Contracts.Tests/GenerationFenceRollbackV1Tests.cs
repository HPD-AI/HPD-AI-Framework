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
    public void Rollback_cannot_recreate_or_replace_an_OutputV2_effect_owner()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var output = OutputGenerationId.Create();
        var turn = TurnGenerationId.Create();
        var provider = ProviderGenerationId.Create();
        var route = RouteGenerationId.Create();
        var authority = ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Turn(turn), new AuthorityAxisValueV1.Provider(provider),
                new AuthorityAxisValueV1.Route(route), new AuthorityAxisValueV1.Output(output)]);
        var generation = new LiveAudioOutputGenerationV2(authority, 4, 16);
        var offer = Offer(authority, output, provider, route);

        var activated = Assert.IsType<LiveAudioOutputActivationResultV2.Activated>(
            generation.Activate(offer));
        Assert.IsType<OutputCommandResultV2.Applied>(activated.Controller.Apply(
            new OutputCommandV2.Close(OperationId.Create(), 0)));

        var changed = new OutputOfferV2(offer.OperationId, output,
            offer.MaximumUnits + 1, offer.ContentFingerprint, offer.Origin);
        var rejected = Assert.IsType<LiveAudioOutputActivationResultV2.Rejected>(
            generation.Activate(changed));
        var retry = Assert.IsType<LiveAudioOutputActivationResultV2.Duplicate>(
            generation.Activate(offer));

        Assert.Equal("output-offer-contradiction", rejected.SafeCode.ToString());
        Assert.Same(activated.Controller, retry.Controller);
        Assert.True(retry.Controller.Read().Closed);

        var legacy = ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);
        Assert.Null(LiveAudioOutputGenerationV2.TryCreate(legacy));
    }

    [Fact]
    public void Rollback_cannot_reopen_S7_or_replace_S8_after_the_generation_cut()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var outputId = OutputGenerationId.Create();
        var toolId = ToolGenerationId.Create();
        var toolAuthority = ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Output(outputId), new AuthorityAxisValueV1.Tool(toolId)]);
        var output = new InMemoryOutputControllerV2(
            new OutputPlanV2(OperationId.Create(), outputId, toolAuthority, 8), 16);
        var tool = ToolTransactionSupervisorV1.Create(new ToolTransactionPlanV1(
            OperationId.Create(), toolId, outputId, toolAuthority,
            new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 10), true));
        var toolOwner = new LiveAudioToolGenerationV1(toolAuthority);
        var interruptOperation = OperationId.Create();

        Assert.IsType<OutputInterruptionResultV2.Applied>(
            toolOwner.Interrupt(output, tool, interruptOperation, 16));
        var changedTool = ToolTransactionSupervisorV1.Create(new ToolTransactionPlanV1(
            OperationId.Create(), toolId, outputId, toolAuthority,
            new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 11), true));
        Assert.IsType<OutputInterruptionResultV2.Rejected>(
            toolOwner.Interrupt(output, changedTool, interruptOperation, 16));
        Assert.True(output.Read().Closed);

        var provider = ProviderGenerationId.Create();
        var priorRoute = RouteGenerationId.Create();
        var nextRoute = RouteGenerationId.Create();
        var priorAuthority = ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Provider(provider), new AuthorityAxisValueV1.Route(priorRoute)]);
        var compiled = new CompiledRouteV1(OperationId.Create(), ProviderId.Create(), nextRoute,
            priorAuthority, Hash(1), Hash(2));
        var admission = RouteAuthorityAdmissionV1.Admit(compiled, new JournalPositionV1(session, 4));
        var preparation = new RoutePreparationStateV1(compiled,
            new RoutePreparationSnapshotV1(3, RoutePreparationPhaseV1.CutoverAuthorized, OwnerSliceId.S5));
        var plan = new ProviderParticipantPlanV1(ParticipantId.Create(), compiled.ProviderId,
            provider, nextRoute, admission.Authority, Hash(1), 1);
        var evidence = new RouteCutoverEvidenceV1(preparation,
            new ProviderParticipantSnapshotV1(2, ProviderParticipantPhaseV1.Effective, plan, 0, null),
            admission);
        var routeOwner = new LiveAudioRouteGenerationV1(admission.Authority);
        var routeOperation = OperationId.Create();
        var first = Assert.IsType<LiveAudioRouteActivationResultV1.Activated>(
            routeOwner.Activate(routeOperation, evidence));
        var changedEvidence = evidence with
        {
            Provider = evidence.Provider with { Revision = evidence.Provider.Revision + 1 }
        };

        Assert.IsType<LiveAudioRouteActivationResultV1.Rejected>(
            routeOwner.Activate(routeOperation, changedEvidence));
        var retry = Assert.IsType<LiveAudioRouteActivationResultV1.Duplicate>(
            routeOwner.Activate(routeOperation, evidence));
        Assert.Equal(first.Receipt, retry.Receipt);

        Assert.Null(LiveAudioToolGenerationV1.TryCreate(ExpectedAuthorityVectorV1.Create(session, [])));
        Assert.Null(LiveAudioRouteGenerationV1.TryCreate(ExpectedAuthorityVectorV1.Create(session, [])));
    }

    private static OutputOfferV2 Offer(ExpectedAuthorityVectorV1 authority, OutputGenerationId output,
        ProviderGenerationId provider, RouteGenerationId route)
    {
        var decision = new TurnDecisionFinalizedV1(OperationId.Create(),
            new JournalPositionV1(authority.Session, 3), authority, 1);
        var plan = new ProviderParticipantPlanV1(ParticipantId.Create(), ProviderId.Create(),
            provider, route, authority, Hash(3), 1);
        var origin = new OutputOriginEvidenceV2(decision,
            new ProviderParticipantSnapshotV1(2, ProviderParticipantPhaseV1.Effective, plan, 0, null));
        return new OutputOfferV2(OperationId.Create(), output, 8, Hash(4), origin);
    }

    private static Hash256 Hash(byte value) => Hash256.Compute([value]);
}
