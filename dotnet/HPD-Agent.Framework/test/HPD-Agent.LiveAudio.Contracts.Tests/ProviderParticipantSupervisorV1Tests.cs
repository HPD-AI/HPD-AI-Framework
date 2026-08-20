using HPD.Agent.Audio.Runtime.Providers;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class ProviderParticipantSupervisorV1Tests
{
    [Fact]
    public void Participant_prepares_activates_drains_and_stops_without_final_cut()
    {
        var f=new Fixture();var state=ProviderParticipantSupervisorV1.Create();
        state=Applied(state,new ProviderParticipantCommandV1.Prepare(OperationId.Create(),0,f.Plan));
        state=Applied(state,new ProviderParticipantCommandV1.Activate(OperationId.Create(),1));
        state=Applied(state,new ProviderParticipantCommandV1.Drain(OperationId.Create(),2));
        state=Applied(state,new ProviderParticipantCommandV1.Stop(OperationId.Create(),3));
        Assert.Equal(ProviderParticipantPhaseV1.Stopped,state.Snapshot.Phase);Assert.Null(state.Snapshot.Plan);
    }

    [Fact]
    public void Effects_are_generation_fenced_bounded_and_must_settle_before_stop()
    {
        var f=new Fixture(1);var state=Effective(f);var effect=OperationId.Create();
        state=Applied(state,new ProviderParticipantCommandV1.BeginEffect(OperationId.Create(),2,effect));
        Assert.Equal("provider-inflight-capacity-refused",Assert.IsType<ProviderParticipantResultV1.Rejected>(
            ProviderParticipantSupervisorV1.Apply(state,new ProviderParticipantCommandV1.BeginEffect(OperationId.Create(),3,OperationId.Create()),16)).SafeCode.ToString());
        state=Applied(state,new ProviderParticipantCommandV1.Drain(OperationId.Create(),3));
        Assert.Equal("provider-transition-invalid",Assert.IsType<ProviderParticipantResultV1.Rejected>(
            ProviderParticipantSupervisorV1.Apply(state,new ProviderParticipantCommandV1.Stop(OperationId.Create(),4),16)).SafeCode.ToString());
        state=Applied(state,new ProviderParticipantCommandV1.SettleEffect(OperationId.Create(),4,effect));
        state=Applied(state,new ProviderParticipantCommandV1.Stop(OperationId.Create(),5));
        Assert.Equal(ProviderParticipantPhaseV1.Stopped,state.Snapshot.Phase);
    }

    [Fact]
    public void Operation_retry_is_exact_and_contradiction_fails_closed()
    {
        var f=new Fixture();var state=ProviderParticipantSupervisorV1.Create();var operation=OperationId.Create();var command=new ProviderParticipantCommandV1.Prepare(operation,0,f.Plan);
        var applied=Assert.IsType<ProviderParticipantResultV1.Applied>(ProviderParticipantSupervisorV1.Apply(state,command,16));
        Assert.IsType<ProviderParticipantResultV1.Duplicate>(ProviderParticipantSupervisorV1.Apply(applied.State,command,16));
        Assert.Equal("provider-operation-contradiction",Assert.IsType<ProviderParticipantResultV1.Rejected>(ProviderParticipantSupervisorV1.Apply(applied.State,
            new ProviderParticipantCommandV1.Prepare(operation,1,f.Plan),16)).SafeCode.ToString());
    }

    [Fact]
    public void Missing_or_stale_provider_route_authority_never_prepares()
    {
        var f=new Fixture();var missing=ExpectedAuthorityVectorV1.Create(f.Session,[new AuthorityAxisValueV1.Provider(f.Generation)]);
        var plan=new ProviderParticipantPlanV1(f.Plan.ParticipantId,f.Plan.ProviderId,f.Generation,f.Route,missing,f.Plan.CatalogFingerprint,2);
        Assert.Equal("provider-transition-invalid",Assert.IsType<ProviderParticipantResultV1.Rejected>(ProviderParticipantSupervisorV1.Apply(
            ProviderParticipantSupervisorV1.Create(),new ProviderParticipantCommandV1.Prepare(OperationId.Create(),0,plan),16)).SafeCode.ToString());
    }

    [Fact]
    public void Quarantine_is_terminal_for_effect_admission_and_receipts_are_bounded()
    {
        var f=new Fixture();var state=Effective(f);state=Applied(state,new ProviderParticipantCommandV1.Quarantine(OperationId.Create(),2,new BoundedAscii("provider-fenced")));
        Assert.Equal(ProviderParticipantPhaseV1.Quarantined,state.Snapshot.Phase);
        Assert.Equal("provider-transition-invalid",Assert.IsType<ProviderParticipantResultV1.Rejected>(ProviderParticipantSupervisorV1.Apply(state,
            new ProviderParticipantCommandV1.BeginEffect(OperationId.Create(),3,OperationId.Create()),16)).SafeCode.ToString());
        Assert.Equal("provider-receipt-capacity-refused",Assert.IsType<ProviderParticipantResultV1.Rejected>(ProviderParticipantSupervisorV1.Apply(state,
            new ProviderParticipantCommandV1.Stop(OperationId.Create(),3),3)).SafeCode.ToString());
    }

    private static ProviderParticipantStateV1 Effective(Fixture f)
    {var state=ProviderParticipantSupervisorV1.Create();state=Applied(state,new ProviderParticipantCommandV1.Prepare(OperationId.Create(),0,f.Plan));return Applied(state,new ProviderParticipantCommandV1.Activate(OperationId.Create(),1));}
    private static ProviderParticipantStateV1 Applied(ProviderParticipantStateV1 state,ProviderParticipantCommandV1 command)=>
        Assert.IsType<ProviderParticipantResultV1.Applied>(ProviderParticipantSupervisorV1.Apply(state,command,16)).State;

    private sealed class Fixture
    {
        internal Fixture(ushort maximumInflight=2)
        {
            Session=new(RuntimeGenerationId.Create(),LiveSessionId.Create());Generation=ProviderGenerationId.Create();Route=RouteGenerationId.Create();
            var authority=ExpectedAuthorityVectorV1.Create(Session,[new AuthorityAxisValueV1.Provider(Generation),new AuthorityAxisValueV1.Route(Route)]);
            Plan=new(ParticipantId.Create(),ProviderId.Create(),Generation,Route,authority,Hash256.Compute("provider-catalog"u8),maximumInflight);
        }
        internal SessionAuthorityStampV1 Session{get;} internal ProviderGenerationId Generation{get;} internal RouteGenerationId Route{get;} internal ProviderParticipantPlanV1 Plan{get;}
    }
}
