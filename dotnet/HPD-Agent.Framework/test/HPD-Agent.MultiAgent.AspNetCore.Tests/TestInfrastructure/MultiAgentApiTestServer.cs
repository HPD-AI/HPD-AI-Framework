using HPD.Agent.MultiAgent.AspNetCore;
using HPD.Graph.Abstractions.Config;
using HPD.Graph.Hosting.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HPD.Agent.MultiAgent.AspNetCore.Tests.TestInfrastructure;

internal sealed class MultiAgentApiTestServer : IDisposable
{
    private readonly IHost _host;

    public MultiAgentApiTestServer(Action<HPDMultiAgentEndpointOptions>? configure = null)
    {
        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddHPDMultiAgentAspNetCore();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapHPDMultiAgentApi(options =>
                            {
                                options.RoutePrefix = "/multi-agent";
                                configure?.Invoke(options);
                            });
                        });
                    });
            })
            .Start();
    }

    public IServiceProvider Services => _host.Services;

    public HttpClient CreateClient()
    {
        var client = _host.GetTestClient();
        client.BaseAddress = new Uri("http://localhost");
        return client;
    }

    public async Task SeedWorkflowAsync(
        string workflowId,
        bool multiAgent = true,
        CancellationToken cancellationToken = default)
    {
        var manager = Services.GetRequiredService<GraphManager>();
        await manager.CreateDefinitionAsync(
            CreateWorkflowConfig(workflowId, multiAgent),
            cancellationToken).ConfigureAwait(false);
    }

    private static GraphConfig CreateWorkflowConfig(string workflowId, bool multiAgent)
    {
        var metadata = multiAgent
            ? new Dictionary<string, string>
            {
                ["kind"] = "multi-agent",
                ["workspaceKind"] = "multi-agent-workflow"
            }
            : new Dictionary<string, string>();

        return new GraphConfig
        {
            GraphId = workflowId,
            Name = workflowId,
            Description = $"Test workflow {workflowId}",
            Metadata = metadata,
            Nodes = new Dictionary<string, NodeConfig>
            {
                ["handler"] = new()
                {
                    Id = "handler",
                    Name = "Handler",
                    Type = NodeKindConfig.Handler,
                    HandlerName = "handler"
                }
            },
            Edges =
            [
                new EdgeConfig { From = "START", To = "handler" },
                new EdgeConfig { From = "handler", To = "END" }
            ]
        };
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}
