using HPD.Agent.Audio.Runtime.Providers;
using HPD.Agent.Audio.Runtime.Routing;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class LiveAudioRouteGenerationTests
{
    [Fact] public void Admitted_route_activates_for_its_exact_generation(){var f=Fixture();var activated=Assert.IsType<LiveAudioRouteActivationResult.Activated>(f.Owner.Activate(f.Operation,f.Evidence));Assert.Equal(f.Route,activated.Receipt.Route.ProposedGeneration);}
    [Fact] public void Exact_retry_returns_the_original_admission(){var f=Fixture();var first=Assert.IsType<LiveAudioRouteActivationResult.Activated>(f.Owner.Activate(f.Operation,f.Evidence));var retry=Assert.IsType<LiveAudioRouteActivationResult.Duplicate>(f.Owner.Activate(f.Operation,f.Evidence));Assert.Equal(first.Receipt,retry.Receipt);}
    [Fact] public void Changed_reuse_cannot_replace_route_authority(){var f=Fixture();Assert.IsType<LiveAudioRouteActivationResult.Activated>(f.Owner.Activate(f.Operation,f.Evidence));var changed=f.Evidence with{Provider=f.Evidence.Provider with{Revision=3}};var rejected=Assert.IsType<LiveAudioRouteActivationResult.Rejected>(f.Owner.Activate(f.Operation,changed));Assert.Equal("route-operation-contradiction",rejected.SafeCode.ToString());}
    [Fact] public void Foreign_generation_has_no_cutover(){var f=Fixture();var foreign=Fixture();var rejected=Assert.IsType<LiveAudioRouteActivationResult.Rejected>(f.Owner.Activate(OperationId.Create(),foreign.Evidence));Assert.Equal("route-generation-stale",rejected.SafeCode.ToString());}
    [Fact] public void Legacy_generation_has_no_S8_owner(){var s=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());Assert.Null(LiveAudioRouteGeneration.TryCreate(ExpectedAuthorityVectorV1.Create(s,[])));}
    private static (LiveAudioRouteGeneration Owner,RouteCutoverEvidence Evidence,OperationId Operation,RouteGenerationId Route) Fixture()
    {var s=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var p=ProviderGenerationId.Create();var old=RouteGenerationId.Create();var route=RouteGenerationId.Create();var prior=ExpectedAuthorityVectorV1.Create(s,[new AuthorityAxisValueV1.Provider(p),new AuthorityAxisValueV1.Route(old)]);var compiled=new CompiledRoute(OperationId.Create(),ProviderId.Create(),route,prior,Hash(1),Hash(2));var admission=RouteAuthorityAdmission.Admit(compiled,new JournalPositionV1(s,4));var prep=new RoutePreparationState(compiled,new(3,RoutePreparationPhase.CutoverAuthorized,OwnerSliceId.S5));var plan=new ProviderParticipantPlan(ParticipantId.Create(),compiled.ProviderId,p,route,admission.Authority,Hash(1),1);var evidence=new RouteCutoverEvidence(prep,new(2,ProviderParticipantPhase.Effective,plan,0,null),admission);return(new(admission.Authority),evidence,OperationId.Create(),route);}
    private static Hash256 Hash(byte value)=>Hash256.Compute(new[]{value});
}
