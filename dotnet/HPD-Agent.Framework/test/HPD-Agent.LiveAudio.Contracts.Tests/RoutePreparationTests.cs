using HPD.Agent.Audio.Runtime.Routing;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class RoutePreparationTests
{
    [Fact]
    public void Compiler_is_deterministic_across_candidate_order()
    {var f=new Fixture();var a=Assert.IsType<RouteCompileResult.Compiled>(RouteCompiler.Compile(f.Request([f.Second,f.First]))).Route;var b=Assert.IsType<RouteCompileResult.Compiled>(RouteCompiler.Compile(f.Request([f.First,f.Second]))).Route;Assert.Equal(f.First.ProviderId,a.ProviderId);Assert.Equal(a,b);}
    [Fact]
    public void Compiler_filters_unavailable_role_and_capability_and_rejects_duplicates()
    {var f=new Fixture();var unavailable=new RouteCandidate(f.Second.ProviderId,f.Second.Role,f.Second.CapabilityFingerprint,f.Second.ConfigurationFingerprint,f.Second.Priority,false);Assert.IsType<RouteCompileResult.Unavailable>(RouteCompiler.Compile(f.Request([unavailable])));Assert.Equal("route-candidate-duplicate",Assert.IsType<RouteCompileResult.Invalid>(RouteCompiler.Compile(f.Request([f.First,f.First]))).SafeCode.ToString());}
    [Fact]
    public void Preparation_closes_LP0_through_LP2_without_committing_route_authority()
    {var state=State();state=Applied(state,new RoutePreparationCommand.Admit(OperationId.Create(),0));state=Applied(state,new RoutePreparationCommand.ClaimOwner(OperationId.Create(),1,OwnerSliceId.S5));state=Applied(state,new RoutePreparationCommand.AuthorizeCutover(OperationId.Create(),2,true));Assert.Equal(RoutePreparationPhase.CutoverAuthorized,state.Snapshot.Phase);var unavailable=Assert.IsType<RoutePreparationResult.CutoverUnavailable>(RoutePreparationSupervisor.Apply(state,new RoutePreparationCommand.CommitCutover(OperationId.Create(),3),16));Assert.Equal("route-cutover-unavailable",unavailable.SafeCode.ToString());Assert.Equal(3UL,unavailable.State.Snapshot.Revision);}
    [Fact]
    public void Only_S5_can_claim_preparation_and_unprepared_provider_cannot_authorize()
    {var state=State();state=Applied(state,new RoutePreparationCommand.Admit(OperationId.Create(),0));Assert.IsType<RoutePreparationResult.Rejected>(RoutePreparationSupervisor.Apply(state,new RoutePreparationCommand.ClaimOwner(OperationId.Create(),1,OwnerSliceId.S8),16));state=Applied(state,new RoutePreparationCommand.ClaimOwner(OperationId.Create(),1,OwnerSliceId.S5));Assert.IsType<RoutePreparationResult.Rejected>(RoutePreparationSupervisor.Apply(state,new RoutePreparationCommand.AuthorizeCutover(OperationId.Create(),2,false),16));}
    [Fact]
    public void Preparation_retry_is_exact_and_contradiction_fails_closed()
    {var state=State();var operation=OperationId.Create();var command=new RoutePreparationCommand.Admit(operation,0);var applied=Assert.IsType<RoutePreparationResult.Applied>(RoutePreparationSupervisor.Apply(state,command,16));Assert.IsType<RoutePreparationResult.Duplicate>(RoutePreparationSupervisor.Apply(applied.State,command,16));Assert.Equal("route-operation-contradiction",Assert.IsType<RoutePreparationResult.Rejected>(RoutePreparationSupervisor.Apply(applied.State,new RoutePreparationCommand.Admit(operation,1),16)).SafeCode.ToString());}
    private static RoutePreparationState State(){var f=new Fixture();return RoutePreparationSupervisor.Create(Assert.IsType<RouteCompileResult.Compiled>(RouteCompiler.Compile(f.Request([f.First]))).Route);}
    private static RoutePreparationState Applied(RoutePreparationState state,RoutePreparationCommand command)=>Assert.IsType<RoutePreparationResult.Applied>(RoutePreparationSupervisor.Apply(state,command,16)).State;
    private sealed class Fixture
    {internal Fixture(){Operation=OperationId.Create();Session=new(RuntimeGenerationId.Create(),LiveSessionId.Create());var route=RouteGenerationId.Create();Authority=ExpectedAuthorityVectorV1.Create(Session,[new AuthorityAxisValueV1.Route(route)]);Capability=Hash256.Compute("cap"u8);First=new(ProviderId.Create(),new("chat"),Capability,Hash256.Compute("first"u8),1,true);Second=new(ProviderId.Create(),new("chat"),Capability,Hash256.Compute("second"u8),2,true);}internal OperationId Operation{get;}internal SessionAuthorityStampV1 Session{get;}internal ExpectedAuthorityVectorV1 Authority{get;}internal Hash256 Capability{get;}internal RouteCandidate First{get;}internal RouteCandidate Second{get;}internal RouteCompileRequest Request(IReadOnlyList<RouteCandidate> candidates)=>new(Operation,Authority,Hash256.Compute("catalog"u8),new("chat"),Capability,candidates);}
}
