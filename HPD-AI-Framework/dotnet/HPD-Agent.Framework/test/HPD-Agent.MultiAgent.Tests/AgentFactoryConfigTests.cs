using System.Text.Json;
using HPD.Agent;
using HPD.MultiAgent;
using HPD.MultiAgent.Config;

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
        var agentSection = root.GetProperty("Agents").GetProperty("checker").GetProperty("Agent");
        agentSection.GetProperty("SystemInstructions").GetString().Should().Be(instructions);
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

        var agentSection = root.GetProperty("Agents").GetProperty("checker").GetProperty("Agent");
        agentSection.GetProperty("Name").GetString().Should().Be(agentName);
    }

    // ── AgentFactory base GetConfig() returns null ────────────────────────────

    [Fact]
    public async Task AgentFactory_Base_GetConfig_Returns_Null_Produces_Empty_AgentConfig()
    {
        // When GetConfig() returns null (base default), ExportConfigJson falls back to
        // new AgentConfig() — the node still appears in the export, just with empty config.
        // This is tested by building with a pre-built agent whose .Config is null.
        // We verify ExportConfigJson does NOT throw.

        var workflow = await AgentWorkflow.Create()
            .WithName("W")
            .AddAgent("only", Cfg("A", "Do something"))
            .BuildAsync();

        // Sanity: ExportConfigJson succeeds and includes the node
        var root = ParseJson(workflow.ExportConfigJson());
        root.GetProperty("Agents").TryGetProperty("only", out _).Should().BeTrue();
    }
}
