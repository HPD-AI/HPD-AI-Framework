using System.Text.Json;
using System.Security.Claims;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Agent.Studio.Tests;

public sealed class AgentStudioCapabilityInspectionTests
{
    [Fact]
    public async Task Capability_routes_project_materialized_functions_and_nested_actions()
    {
        Agent agent = await new AgentBuilder(new AgentConfig { Name = "studio-projection" }).BuildAsync();
        var services = new ServiceCollection();
        services.AddSingleton(agent);
        services.AddSingleton<AgentManager>(provider =>
            new PrebuiltAgentManager(
                new InMemoryAgentStore(),
                provider.GetRequiredService<Agent>()));
        services.AddSingleton<IAgentDefinitionService>(provider =>
            new AgentDefinitionService(provider.GetRequiredService<AgentManager>()));
        await using ServiceProvider provider = services.BuildServiceProvider();
        IAgentDefinitionService definitions = provider.GetRequiredService<IAgentDefinitionService>();
        AgentServiceResult<StoredAgentDto> created = await definitions.CreateAgentAsync(
            new CreateAgentRequest("agent", new AgentConfig { Name = "studio-projection" }));
        Assert.Equal(AgentServiceStatus.Success, created.Status);

        var authority = new DefaultAgentStudioInspectionAuthority(provider);
        const string harness = "HPD-Agent:HPD.Agent:ContentToolHarness";
        JsonElement harnessBody = await ObserveAsync(
            authority, "harness.get", $"harnesses/{Uri.EscapeDataString(harness)}");
        JsonElement function = harnessBody.GetProperty("functions")
            .EnumerateArray().Single();
        string functionId = function.GetProperty("id").GetString()!;

        JsonElement functionBody = await ObserveAsync(
            authority, "function.get",
            $"harnesses/{Uri.EscapeDataString(harness)}/functions/{Uri.EscapeDataString(functionId)}");
        Assert.Equal("Content", functionBody.GetProperty("name").GetString());
        Assert.Equal(3, functionBody.GetProperty("actions").GetArrayLength());

        JsonElement actionBody = await ObserveAsync(
            authority, "function.action.get",
            $"harnesses/{Uri.EscapeDataString(harness)}/functions/{Uri.EscapeDataString(functionId)}/actions/list");
        Assert.Equal("list", actionBody.GetProperty("name").GetString());
        Assert.Equal("object", actionBody.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal("list", actionBody.GetProperty("schema").GetProperty("properties")
            .GetProperty("action").GetProperty("const").GetString());
    }

    private static async Task<JsonElement> ObserveAsync(
        DefaultAgentStudioInspectionAuthority authority,
        string operationId,
        string path)
    {
        var response = await authority.ObserveAsync(
            operationId, path, "", "studio.tests",
            new ClaimsPrincipal(new ClaimsIdentity()), CancellationToken.None);
        Assert.NotNull(response);
        Assert.Equal(200, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(response.GetBody());
        return document.RootElement.Clone();
    }

    private sealed class PrebuiltAgentManager(IAgentStore store, Agent agent) : AgentManager(store)
    {
        protected override Task<Agent> BuildAgentAsync(string agentId, CancellationToken ct) =>
            Task.FromResult(agent);

        protected override TimeSpan GetIdleTimeout() => TimeSpan.FromHours(1);
    }
}
