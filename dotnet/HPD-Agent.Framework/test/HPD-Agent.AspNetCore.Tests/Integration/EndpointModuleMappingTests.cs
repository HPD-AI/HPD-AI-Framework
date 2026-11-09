using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.AspNetCore.Tests.Integration;

public sealed class EndpointModuleMappingTests
{
    [Fact]
    public void ModuleMappings_ExposeOnlyTheirOwnSurface()
    {
        using var server = CreateServer(applyAuthorization: false);
        var paths = EndpointPaths(server);

        paths.Should().Contain("/hpd/api/sessions");
        paths.Should().Contain("/hpd/api/storage");
        paths.Should().Contain("/hpd/api/agents/{agentId}/sessions/{sessionId}/threads/{threadId}/inputs");
        paths.Should().NotContain("/hpd/api/agents");

        paths.Should().Contain("/hpd/client-tools/client-tool-providers");
        paths.Should().NotContain("/hpd/client-tools/sessions");

        paths.Should().Contain("/hpd/admin/agents");
        paths.Should().Contain("/hpd/admin/runtime-extension-schemas");
        paths.Should().NotContain("/hpd/admin/sessions");
    }

    [Fact]
    public void ModuleGroups_ApplyIndependentAuthorizationMetadata()
    {
        using var server = CreateServer(applyAuthorization: true);
        var endpoints = Endpoints(server).ToArray();

        AuthorizeAttribute PolicyFor(string suffix) =>
            endpoints.Single(endpoint =>
                    endpoint.RoutePattern.RawText == suffix &&
                    endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
                .Metadata.GetMetadata<AuthorizeAttribute>()!;

        PolicyFor("/hpd/api/sessions").Policy.Should().Be("AgentUser");
        PolicyFor("/hpd/client-tools/client-tool-providers").Policy.Should().Be("AgentUser");
        PolicyFor("/hpd/admin/agents").Policy.Should().Be("AgentAdmin");
    }

    [Fact]
    public async Task StorageEndpoint_ReportsDefaultFallbackCapabilities()
    {
        using var server = CreateServer(applyAuthorization: false);
        using var client = server.CreateClient();

        var storage = await client.GetFromJsonAsync<HPDAgentStorageInfo>("/hpd/api/storage");

        storage.Should().NotBeNull();
        storage!.Sessions.UsingDefaultFallback.Should().BeTrue();
        storage.Sessions.Persistence.Should().Be(StorePersistenceCapability.Ephemeral);
        storage.AgentDefinitions.UsingDefaultFallback.Should().BeTrue();
        storage.AgentDefinitions.Persistence.Should().Be(StorePersistenceCapability.Ephemeral);
        storage.Content.UsingDefaultFallback.Should().BeTrue();
        storage.Content.Persistence.Should().Be(StorePersistenceCapability.Ephemeral);
    }

    [Fact]
    public async Task CreatedLocations_IncludeComposedRoutePrefix()
    {
        using var server = CreateServer(applyAuthorization: false);
        using var client = server.CreateClient();

        using var createSession = await client.PostAsync("/hpd/api/sessions", null);
        createSession.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await createSession.Content.ReadFromJsonAsync<SessionDto>();
        session.Should().NotBeNull();
        createSession.Headers.Location!.ToString()
            .Should().Be($"/hpd/api/sessions/{session!.Id}");

        using var createThread = await client.PostAsJsonAsync(
            $"/hpd/api/agents/test-agent/sessions/{session.Id}/threads",
            new CreateThreadRequest("custom-thread", "Custom", null, null));
        createThread.StatusCode.Should().Be(HttpStatusCode.Created);
        createThread.Headers.Location!.ToString()
            .Should().Be($"/hpd/api/sessions/{session.Id}/threads/custom-thread");
    }

    [Fact]
    public async Task QueuePromotionWithoutPausedQueue_ReturnsConflict()
    {
        using var server = CreateServer(applyAuthorization: false);
        using var client = server.CreateClient();
        var session = await client.PostAsJsonAsync("/hpd/api/sessions", new CreateSessionRequest());
        var created = await session.Content.ReadFromJsonAsync<SessionDto>();

        using var response = await client.PostAsync(
            $"/hpd/api/agents/test-agent/sessions/{created!.Id}/threads/main/queue/start",
            null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errors = await response.Content.ReadFromJsonAsync<Dictionary<string, string[]>>();
        errors.Should().ContainKey("QueuePromotionNotPaused");
    }

    private static TestServer CreateServer(bool applyAuthorization)
    {
        var builder = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddTestApplicationCompositions().AddHPDAgentHost("test-agent");
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    var hpd = endpoints.MapGroup("/hpd");

                    var runtime = hpd.MapGroup("/api").MapHPDAgentRuntimeApi("test-agent");
                    var clientTools = hpd.MapGroup("/client-tools")
                        .MapHPDAgentClientToolProvidersApi();
                    var administration = hpd.MapGroup("/admin")
                        .MapHPDAgentAdministrationApi("test-agent");

                    if (applyAuthorization)
                    {
                        runtime.RequireAuthorization("AgentUser");
                        clientTools.RequireAuthorization("AgentUser");
                        administration.RequireAuthorization("AgentAdmin");
                    }
                });
            });

        return new TestServer(builder);
    }

    private static IEnumerable<RouteEndpoint> Endpoints(TestServer server) =>
        server.Services.GetServices<EndpointDataSource>()
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>();

    private static HashSet<string?> EndpointPaths(TestServer server) =>
        Endpoints(server).Select(static endpoint => endpoint.RoutePattern.RawText).ToHashSet();
}
