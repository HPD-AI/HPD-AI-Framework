using HPD.Agent.Audio.Runtime.Routing;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class ToolRouteAuthorityIntegrationV1Tests
{
    [Fact]
    public void Admission_replaces_the_old_route_axis_with_the_single_proposed_generation()
    {
        var f=Fixture();var admitted=RouteAuthorityAdmissionV1.Admit(f.Route,new JournalPositionV1(f.Session,9));var routes=admitted.Authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Route>().ToArray();Assert.Single(routes);Assert.Equal(f.Route.ProposedGeneration,routes[0].Value);Assert.DoesNotContain(routes,x=>x.Value==f.OldRoute);
    }
    [Fact]
    public void Compiler_cannot_itself_mint_current_route_authority()
    {var f=Fixture();Assert.Equal(f.OldRoute,f.Route.Authority.Axes.Select(static x=>x.Value).OfType<AuthorityAxisValueV1.Route>().Single().Value);Assert.NotEqual(f.OldRoute,f.Route.ProposedGeneration);}
    [Fact]
    public void Same_compile_request_derives_one_stable_proposed_generation()
    {var f=Fixture();var again=Assert.IsType<RouteCompileResultV1.Compiled>(RouteCompilerV1.Compile(f.Request)).Route;Assert.Equal(f.Route.ProposedGeneration,again.ProposedGeneration);Assert.Equal(f.Route.ProviderId,again.ProviderId);}
    [Fact]
    public void Admission_is_session_bound()
    {var f=Fixture();var other=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());Assert.Throws<ArgumentException>(()=>RouteAuthorityAdmissionV1.Admit(f.Route,new JournalPositionV1(other,1)));}
    private static (RouteCompileRequestV1 Request,CompiledRouteV1 Route,SessionAuthorityStampV1 Session,RouteGenerationId OldRoute) Fixture(){var s=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var old=RouteGenerationId.Create();var a=ExpectedAuthorityVectorV1.Create(s,[new AuthorityAxisValueV1.Route(old)]);var candidate=new RouteCandidateV1(ProviderId.Create(),new BoundedAscii("realtime"),Hash(1),Hash(2),1,true);var request=new RouteCompileRequestV1(OperationId.Create(),a,Hash(3),new BoundedAscii("realtime"),Hash(1),[candidate]);return(request,Assert.IsType<RouteCompileResultV1.Compiled>(RouteCompilerV1.Compile(request)).Route,s,old);}
    private static Hash256 Hash(byte b)=>Hash256.Compute(new[]{b});
}
