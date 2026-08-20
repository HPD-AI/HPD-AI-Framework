using HPD.Agent.Audio.Runtime.Routing;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class RoutePreparationV1Tests
{
    [Fact]
    public void Compiler_is_deterministic_across_candidate_order()
    {var f=new Fixture();var a=Assert.IsType<RouteCompileResultV1.Compiled>(RouteCompilerV1.Compile(f.Request([f.Second,f.First]))).Route;var b=Assert.IsType<RouteCompileResultV1.Compiled>(RouteCompilerV1.Compile(f.Request([f.First,f.Second]))).Route;Assert.Equal(f.First.ProviderId,a.ProviderId);Assert.Equal(a,b);}
    [Fact]
    public void Compiler_filters_unavailable_role_and_capability_and_rejects_duplicates()
    {var f=new Fixture();var unavailable=new RouteCandidateV1(f.Second.ProviderId,f.Second.Role,f.Second.CapabilityFingerprint,f.Second.ConfigurationFingerprint,f.Second.Priority,false);Assert.IsType<RouteCompileResultV1.Unavailable>(RouteCompilerV1.Compile(f.Request([unavailable])));Assert.Equal("route-candidate-duplicate",Assert.IsType<RouteCompileResultV1.Invalid>(RouteCompilerV1.Compile(f.Request([f.First,f.First]))).SafeCode.ToString());}
    [Fact]
    public void Preparation_closes_LP0_through_LP2_without_committing_route_authority()
    {var state=State();state=Applied(state,new RoutePreparationCommandV1.Admit(OperationId.Create(),0));state=Applied(state,new RoutePreparationCommandV1.ClaimOwner(OperationId.Create(),1,OwnerSliceId.S5));state=Applied(state,new RoutePreparationCommandV1.AuthorizeCutover(OperationId.Create(),2,true));Assert.Equal(RoutePreparationPhaseV1.CutoverAuthorized,state.Snapshot.Phase);var unavailable=Assert.IsType<RoutePreparationResultV1.CutoverUnavailable>(RoutePreparationSupervisorV1.Apply(state,new RoutePreparationCommandV1.CommitCutover(OperationId.Create(),3),16));Assert.Equal("route-cutover-unavailable",unavailable.SafeCode.ToString());Assert.Equal(3UL,unavailable.State.Snapshot.Revision);}
    [Fact]
    public void Only_S5_can_claim_preparation_and_unprepared_provider_cannot_authorize()
    {var state=State();state=Applied(state,new RoutePreparationCommandV1.Admit(OperationId.Create(),0));Assert.IsType<RoutePreparationResultV1.Rejected>(RoutePreparationSupervisorV1.Apply(state,new RoutePreparationCommandV1.ClaimOwner(OperationId.Create(),1,OwnerSliceId.S8),16));state=Applied(state,new RoutePreparationCommandV1.ClaimOwner(OperationId.Create(),1,OwnerSliceId.S5));Assert.IsType<RoutePreparationResultV1.Rejected>(RoutePreparationSupervisorV1.Apply(state,new RoutePreparationCommandV1.AuthorizeCutover(OperationId.Create(),2,false),16));}
    [Fact]
    public void Preparation_retry_is_exact_and_contradiction_fails_closed()
    {var state=State();var operation=OperationId.Create();var command=new RoutePreparationCommandV1.Admit(operation,0);var applied=Assert.IsType<RoutePreparationResultV1.Applied>(RoutePreparationSupervisorV1.Apply(state,command,16));Assert.IsType<RoutePreparationResultV1.Duplicate>(RoutePreparationSupervisorV1.Apply(applied.State,command,16));Assert.Equal("route-operation-contradiction",Assert.IsType<RoutePreparationResultV1.Rejected>(RoutePreparationSupervisorV1.Apply(applied.State,new RoutePreparationCommandV1.Admit(operation,1),16)).SafeCode.ToString());}
    private static RoutePreparationStateV1 State(){var f=new Fixture();return RoutePreparationSupervisorV1.Create(Assert.IsType<RouteCompileResultV1.Compiled>(RouteCompilerV1.Compile(f.Request([f.First]))).Route);}
    private static RoutePreparationStateV1 Applied(RoutePreparationStateV1 state,RoutePreparationCommandV1 command)=>Assert.IsType<RoutePreparationResultV1.Applied>(RoutePreparationSupervisorV1.Apply(state,command,16)).State;
    private sealed class Fixture
    {internal Fixture(){Operation=OperationId.Create();Session=new(RuntimeGenerationId.Create(),LiveSessionId.Create());var route=RouteGenerationId.Create();Authority=ExpectedAuthorityVectorV1.Create(Session,[new AuthorityAxisValueV1.Route(route)]);Capability=Hash256.Compute("cap"u8);First=new(ProviderId.Create(),new("chat"),Capability,Hash256.Compute("first"u8),1,true);Second=new(ProviderId.Create(),new("chat"),Capability,Hash256.Compute("second"u8),2,true);}internal OperationId Operation{get;}internal SessionAuthorityStampV1 Session{get;}internal ExpectedAuthorityVectorV1 Authority{get;}internal Hash256 Capability{get;}internal RouteCandidateV1 First{get;}internal RouteCandidateV1 Second{get;}internal RouteCompileRequestV1 Request(IReadOnlyList<RouteCandidateV1> candidates)=>new(Operation,Authority,Hash256.Compute("catalog"u8),new("chat"),Capability,candidates);}
}
