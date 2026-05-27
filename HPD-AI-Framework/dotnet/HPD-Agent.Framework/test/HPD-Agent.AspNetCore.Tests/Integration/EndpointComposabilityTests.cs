using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AspNetCore;
using HPD.Agent.ClientTools;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.AspNetCore.Tests.Integration;

public class EndpointComposabilityTests
{
    [Fact]
    public async Task MapSessionsFalse_DoesNotMapBuiltInSessionRoutes_ButKeepsOtherGroups()
    {
        using var server = CreateServer(
            services => services.AddHPDAgent(),
            endpoints => endpoints.MapHPDAgentApi(options => options.MapSessions = false));
        using var client = CreateClient(server);

        var sessions = await client.GetAsync("/sessions");
        var agents = await client.GetAsync("/agents");

        sessions.StatusCode.Should().Be(HttpStatusCode.NotFound);
        agents.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConfigureRoutes_AddsCustomRouteInsideAgentGroup()
    {
        using var server = CreateServer(
            services => services.AddHPDAgent(),
            endpoints => endpoints.MapHPDAgentApi(options =>
            {
                options.RoutePrefix = "/agent";
                options.MapSessions = false;
                options.ConfigureRoutes = group =>
                {
                    group.MapGet("/sessions/custom", () => Results.Ok("custom"));
                };
            }));
        using var client = CreateClient(server);

        var response = await client.GetAsync("/agent/sessions/custom");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("custom");
    }

    [Fact]
    public async Task CustomHostingServicesProvider_ChangesBuiltInDefaultRouteBehavior()
    {
        using var server = CreateServer(
            services =>
            {
                services.AddSingleton<IHPDAgentHostingServicesProvider, CustomHostingServicesProvider>();
                services.AddHPDAgent();
            },
            endpoints => endpoints.MapHPDAgentApi(options =>
            {
                options.MapBranches = false;
                options.MapAssets = false;
                options.MapStreaming = false;
                options.MapMiddlewareResponses = false;
                options.MapAgents = false;
                options.MapEvals = false;
            }));
        using var client = CreateClient(server);

        var response = await client.PostAsync("/sessions", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        session!.Id.Should().Be("custom-session");
    }

    [Fact]
    public async Task DirectSessionServiceReplacement_DoesNotChangeBuiltInRouteBehavior()
    {
        using var server = CreateServer(
            services =>
            {
                services.AddSingleton<IAgentSessionService, CustomSessionService>();
                services.AddHPDAgent();
            },
            endpoints => endpoints.MapHPDAgentApi(options =>
            {
                options.MapBranches = false;
                options.MapAssets = false;
                options.MapStreaming = false;
                options.MapMiddlewareResponses = false;
                options.MapAgents = false;
                options.MapEvals = false;
            }));
        using var client = CreateClient(server);

        var response = await client.PostAsync("/sessions", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        session!.Id.Should().NotBe("custom-session");
    }

    [Fact]
    public async Task CustomHostingServicesProvider_ChangesBuiltInNamedRouteBehavior()
    {
        using var server = CreateServer(
            services =>
            {
                services.AddSingleton<IHPDAgentHostingServicesProvider, CustomHostingServicesProvider>();
                services.AddHPDAgent("named-agent");
            },
            endpoints => endpoints.MapHPDAgentApi("named-agent", options =>
            {
                options.MapBranches = false;
                options.MapAssets = false;
                options.MapStreaming = false;
                options.MapMiddlewareResponses = false;
                options.MapAgents = false;
                options.MapEvals = false;
            }));
        using var client = CreateClient(server);

        var response = await client.PostAsync("/sessions", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        session!.Id.Should().Be("custom-session");
    }

    private static TestServer CreateServer(
        Action<IServiceCollection> configureServices,
        Action<IEndpointRouteBuilder> configureEndpoints)
    {
        var builder = new WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                configureServices(services);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(configureEndpoints);
            });

        return new TestServer(builder);
    }

    private static HttpClient CreateClient(TestServer server)
    {
        var client = new HttpClient(server.CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };
        return client;
    }

    private sealed class CustomSessionService : IAgentSessionService
    {
        private static readonly SessionDto CustomSession = new(
            "custom-session",
            DateTime.UnixEpoch,
            DateTime.UnixEpoch,
            new Dictionary<string, object> { ["source"] = "custom" });

        public Task<SessionDto> CreateSessionAsync(
            CreateSessionRequest? request = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CustomSession);

        public Task<IReadOnlyList<SessionDto>> SearchSessionsAsync(
            SearchSessionsRequest? request = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionDto>>([CustomSession]);

        public Task<SessionDto?> GetSessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SessionDto?>(sessionId == CustomSession.Id ? CustomSession : null);

        public Task<SessionDto?> UpdateSessionAsync(
            string sessionId,
            UpdateSessionRequest request,
            CancellationToken cancellationToken = default) =>
            GetSessionAsync(sessionId, cancellationToken);

        public Task<bool> DeleteSessionAsync(
            string sessionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(sessionId == CustomSession.Id);
    }

    private sealed class CustomHostingServicesProvider : IHPDAgentHostingServicesProvider
    {
        private readonly HPDAgentHostingServices _services = new(
            new CustomSessionService(),
            new UnsupportedBranchService(),
            new UnsupportedAssetService(),
            new UnsupportedAgentDefinitionService(),
            new UnsupportedMiddlewareResponseService(),
            new UnsupportedStreamingService());

        public HPDAgentHostingServices Get(string name) => _services;
    }

    private sealed class UnsupportedBranchService : IAgentBranchService
    {
        public Task<AgentServiceResult<IReadOnlyList<BranchDto>>> ListBranchesAsync(string sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentServiceResult<BranchDto>> GetBranchAsync(string sessionId, string branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentServiceResult<BranchDto>> CreateBranchAsync(string agentId, string sessionId, CreateBranchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentServiceResult<BranchDto>> ForkBranchAsync(string agentId, string sessionId, string branchId, ForkBranchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentServiceResult<BranchDto>> UpdateBranchAsync(string sessionId, string branchId, UpdateBranchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentServiceResult> DeleteBranchAsync(string sessionId, string branchId, bool recursive = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentServiceResult<IReadOnlyList<AgentEvent>>> GetEventsAsync(string sessionId, string branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentServiceResult<IReadOnlyList<BranchDto>>> GetSiblingsAsync(string sessionId, string branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnsupportedAssetService : IAgentAssetService
    {
        public Task<AgentServiceResult<AssetDto>> UploadAssetAsync(string sessionId, Stream content, string fileName, string? contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentServiceResult<IReadOnlyList<AssetDto>>> ListAssetsAsync(string sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentServiceResult<AgentAssetDownload>> DownloadAssetAsync(string sessionId, string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentServiceResult> DeleteAssetAsync(string sessionId, string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnsupportedAgentDefinitionService : IAgentDefinitionService
    {
        public Task<AgentServiceResult<StoredAgentDto>> CreateAgentAsync(CreateAgentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentSummaryDto>> ListAgentsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredAgentDto?> GetAgentAsync(string agentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentServiceResult<StoredAgentDto>> UpdateAgentAsync(string agentId, UpdateAgentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentServiceResult> DeleteAgentAsync(string agentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnsupportedMiddlewareResponseService : IAgentMiddlewareResponseService
    {
        public Task<AgentServiceResult> RespondToPermissionAsync(string agentId, string sessionId, string branchId, PermissionResponseEvent response, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentServiceResult> RespondToContinuationAsync(string agentId, string sessionId, string branchId, ContinuationResponseEvent response, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentServiceResult> RespondToClarificationAsync(string agentId, string sessionId, string branchId, ClarificationResponseEvent response, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentServiceResult> RespondToClientToolAsync(string agentId, string sessionId, string branchId, ClientToolInvokeResponseEvent response, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnsupportedStreamingService : IAgentStreamingService
    {
        public Task<AgentServiceResult<AgentStreamLease>> BeginStreamAsync(string agentId, string sessionId, string branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void ReleaseStream(string sessionId, string branchId) => throw new NotSupportedException();
        public AgentInputEvent ApplyRouteScope(AgentInputEvent input, string agentId, string sessionId, string branchId) => throw new NotSupportedException();
    }
}
