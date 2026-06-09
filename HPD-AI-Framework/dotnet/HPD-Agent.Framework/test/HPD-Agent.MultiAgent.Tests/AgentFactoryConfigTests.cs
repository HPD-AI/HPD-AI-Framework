using System.Text.Json;
using HPD.Agent;
using HPD.MultiAgent;
using HPD.MultiAgent.Config;
using HPDAgent.Graph.Core.Builders;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.MultiAgent.Tests;

/// <summary>
/// Tests that AgentFactory.GetConfig() returns the correct AgentConfig for each factory type.
/// Because ConfigAgentFactory and PrebuiltAgentFactory are internal, these tests exercise
/// GetConfig() indirectly via ExportConfigJson() — the only public surface that calls it.
/// </summary>
public class AgentFactoryConfigTests
{
    private static AgentConfig Cfg(string name, string instructions)
        => new() { Name = name, SystemInstructions = instructions };

    private static JsonElement ParseJson(string json)
        => JsonDocument.Parse(json).RootElement;

    // ── ConfigAgentFactory ────────────────────────────────────────────────────

    [Fact]
    public async Task ConfigAgentFactory_GetConfig_Returns_Original_SystemInstructions()
    {
        // ConfigAgentFactory is created when AddAgent(id, AgentConfig) is used.
        // GetConfig() must return the original AgentConfig so ExportConfigJson
        // can embed SystemInstructions in the output.
        const string instructions = "You are a precise fact-checker.";

        var workflow = await AgentWorkflow.Create()
            .WithName("W")
            .AddAgent("checker", Cfg("Checker", instructions))
            .BuildAsync();

        var root = ParseJson(workflow.ExportConfigJson());

        // The exported JSON embeds the agent config from GetConfig()
        var agentSection = root.GetProperty("agents").GetProperty("checker").GetProperty("agent");
        agentSection.GetProperty("systemInstructions").GetString().Should().Be(instructions);
    }

    [Fact]
    public async Task ConfigAgentFactory_GetConfig_Returns_Agent_Name()
    {
        const string agentName = "FactChecker";

        var workflow = await AgentWorkflow.Create()
            .WithName("W")
            .AddAgent("checker", Cfg(agentName, "Check facts"))
            .BuildAsync();

        var root = ParseJson(workflow.ExportConfigJson());

        var agentSection = root.GetProperty("agents").GetProperty("checker").GetProperty("agent");
        agentSection.GetProperty("name").GetString().Should().Be(agentName);
    }

    // ── Non-exportable factories ──────────────────────────────────────────────

    [Fact]
    public void ExportConfigJson_FactoryWithoutConfig_Throws()
    {
        var graph = new GraphBuilder()
            .WithName("W")
            .AddStartNode()
            .AddHandlerNode("only", "only", "onlyHandler")
            .AddEndNode()
            .AddEdge("START", "only")
            .AddEdge("only", "END")
            .Build();

        var workflow = new AgentWorkflowInstance(
            graph,
            new Dictionary<string, AgentFactory> { ["only"] = new NonExportableAgentFactory() },
            new Dictionary<string, AgentNodeOptions> { ["only"] = new() },
            new ServiceCollection().BuildServiceProvider(),
            "W");

        var act = () => workflow.ExportConfigJson();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*only*does not expose an AgentConfig*");
    }

    [Fact]
    public async Task ExportConfigJson_InlineBuilderAgent_Throws()
    {
        var workflow = await AgentWorkflow.Create()
            .WithName("W")
            .AddAgent("inline", builder => builder.WithName("Inline"))
            .BuildAsync();

        var act = () => workflow.ExportConfigJson();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*inline*does not expose an AgentConfig*");
    }

    private sealed class NonExportableAgentFactory : AgentFactory
    {
        public override Task<Agent.Agent> BuildAsync(
            IChatClient? fallbackChatClient,
            ISessionStore? workflowSessionStore,
            bool requireWorkflowSessionStore,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        internal override AgentConfig? GetConfig() => null;
    }
}
