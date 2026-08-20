using HPD.Agent.Audio.Authority;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Runtime.Providers;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class OutputOriginOfferV2Tests
{
    [Fact]
    public void Exact_S4_and_S5_origin_is_accepted_only_by_S6()
    {
        var f=Fixture();var accepted=Assert.IsType<OutputOfferResultV2.Accepted>(OutputOfferCoordinatorV2.Accept(new(),f.Offer,8,16));
        Assert.Equal(f.Offer.OperationId,accepted.Receipt.Plan.OutputOperation);Assert.Equal(0UL,accepted.Controller.Read().Revision);
        Assert.IsType<OutputCommandResultV2.Applied>(accepted.Controller.Apply(new OutputCommandV2.Generate(OperationId.Create(),0,4)));
    }

    [Fact]
    public void Offer_retry_is_exact_and_changed_offer_is_contradictory()
    {
        var f=Fixture();var accepted=Assert.IsType<OutputOfferResultV2.Accepted>(OutputOfferCoordinatorV2.Accept(new(),f.Offer,8,16));
        Assert.IsType<OutputOfferResultV2.Duplicate>(OutputOfferCoordinatorV2.Accept(accepted.State,f.Offer,8,16));
        var changed=new OutputOfferV2(f.Offer.OperationId,f.Offer.OutputGeneration,f.Offer.MaximumUnits+1,f.Offer.ContentFingerprint,f.Offer.Origin);
        Assert.Equal("output-offer-contradiction",Assert.IsType<OutputOfferResultV2.Rejected>(OutputOfferCoordinatorV2.Accept(accepted.State,changed,8,16)).SafeCode.ToString());
    }

    [Fact]
    public void Missing_effective_provider_origin_cannot_create_output_state()
    {
        var f=Fixture();Assert.Throws<ArgumentException>(()=>new OutputOriginEvidenceV2(f.Offer.Origin.Decision,f.Offer.Origin.Provider with{Phase=ProviderParticipantPhaseV1.Prepared}));
    }

    [Fact]
    public void Stale_output_or_provider_axes_fail_closed()
    {
        var f=Fixture();var staleOutput=new OutputOfferV2(OperationId.Create(),OutputGenerationId.Create(),10,Hash(8),f.Offer.Origin);
        Assert.Equal("output-origin-output-stale",Assert.IsType<OutputOfferResultV2.Rejected>(OutputOfferCoordinatorV2.Accept(new(),staleOutput,8,16)).SafeCode.ToString());
        var stalePlan=new ProviderParticipantPlanV1(f.Plan.ParticipantId,f.Plan.ProviderId,ProviderGenerationId.Create(),f.Plan.RouteGeneration,f.Plan.Authority,f.Plan.CatalogFingerprint,1);
        var staleOrigin=new OutputOriginEvidenceV2(f.Offer.Origin.Decision,f.Offer.Origin.Provider with{Plan=stalePlan});var staleProvider=new OutputOfferV2(OperationId.Create(),f.Offer.OutputGeneration,10,Hash(9),staleOrigin);
        Assert.Equal("output-origin-provider-stale",Assert.IsType<OutputOfferResultV2.Rejected>(OutputOfferCoordinatorV2.Accept(new(),staleProvider,8,16)).SafeCode.ToString());
    }

    [Fact]
    public void Accepted_plan_retains_exact_turn_provider_route_and_output_authority()
    {
        var f=Fixture();var accepted=Assert.IsType<OutputOfferResultV2.Accepted>(OutputOfferCoordinatorV2.Accept(new(),f.Offer,8,16));
        Assert.Equal(f.Authority.Session,accepted.Receipt.Plan.Authority.Session);Assert.Equal(4,accepted.Receipt.Plan.Authority.Axes.Length);
        Assert.Equal(f.Plan.ParticipantId,accepted.Receipt.Offer.Origin.Provider.Plan!.ParticipantId);
    }

    private static (OutputOfferV2 Offer,ProviderParticipantPlanV1 Plan,ExpectedAuthorityVectorV1 Authority) Fixture()
    {
        var session=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var turn=TurnGenerationId.Create();var provider=ProviderGenerationId.Create();var route=RouteGenerationId.Create();var output=OutputGenerationId.Create();
        var authority=ExpectedAuthorityVectorV1.Create(session,[new AuthorityAxisValueV1.Turn(turn),new AuthorityAxisValueV1.Provider(provider),new AuthorityAxisValueV1.Route(route),new AuthorityAxisValueV1.Output(output)]);
        var decision=new TurnDecisionFinalizedV1(OperationId.Create(),new JournalPositionV1(session,4),authority,1);var plan=new ProviderParticipantPlanV1(ParticipantId.Create(),ProviderId.Create(),provider,route,authority,Hash(3),2);
        var origin=new OutputOriginEvidenceV2(decision,new ProviderParticipantSnapshotV1(2,ProviderParticipantPhaseV1.Effective,plan,0,null));return(new OutputOfferV2(OperationId.Create(),output,10,Hash(4),origin),plan,authority);
    }
    private static Hash256 Hash(byte value)=>Hash256.Compute(new byte[]{value});
}
