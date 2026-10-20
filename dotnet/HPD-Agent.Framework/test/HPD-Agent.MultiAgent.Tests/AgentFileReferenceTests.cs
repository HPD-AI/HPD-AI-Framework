using HPD.Agent;
using HPD.Agent.Serialization;
using HPD.MultiAgent;
using HPD.MultiAgent.Config;
using HPD.MultiAgent.Serialization;

namespace HPD.Agent.MultiAgent.Tests;

public sealed class AgentFileReferenceTests
{
    [Theory]
    [InlineData("workflow.yaml", "agent.json")]
    [InlineData("workflow.json", "agent.yaml")]
    public async Task ReferencesResolveRelativeToWorkflow_AndExportResolvedAgents(string workflowName, string agentName)
    {
        var root = Path.Combine(Path.GetTempPath(), "hpd-agent-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "agents"));
        try
        {
            HpdAgentConfigSerializer.WriteFile(Path.Combine(root, "agents", agentName),
                new AgentConfig { Name = "Referenced reviewer", SystemInstructions = "Inspect evidence." });
            var path = Path.Combine(root, workflowName);
            MultiAgentConfigSerializer.WriteFile(path, new MultiAgentWorkflowConfig
            {
                Name = "References",
                Agents = new() { ["reviewer"] = new() { AgentFile = "agents/" + agentName } }
            });
            var config = MultiAgentConfigSerializer.ReadFile(path)!;
            Assert.Equal("Referenced reviewer", config.Agents["reviewer"].Agent!.Name);
            Assert.Null(config.Agents["reviewer"].AgentFile);
            var asynchronous = await MultiAgentConfigSerializer.ReadFileAsync(path);
            Assert.Equal("Inspect evidence.", asynchronous!.Agents["reviewer"].Agent!.SystemInstructions);
            var workflow = await global::HPD.MultiAgent.MultiAgent.FromFile(path).BuildAsync();
            Assert.Contains("Referenced reviewer", workflow.ExportConfigJson());
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("{\"agent\":{},\"agentFile\":\"agent.yaml\"}")]
    [InlineData("{}")]
    [InlineData("{\"agentFile\":\" \"}")]
    public void InvalidSourcesAreRejected(string node)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{\"name\":\"Invalid\",\"agents\":{\"reviewer\":" + node + "}}");
            Assert.Throws<System.Text.Json.JsonException>(() => MultiAgentConfigSerializer.ReadFile(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingReferencedFileIdentifiesNodeAndResolvedPath()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{\"name\":\"Missing\",\"agents\":{\"metrics\":{\"agentFile\":\"absent/reviewer.yaml\"}}}");
            var error = Assert.Throws<InvalidOperationException>(() => MultiAgentConfigSerializer.ReadFile(path));
            Assert.Contains("metrics", error.Message);
            Assert.Contains(Path.Combine(Path.GetDirectoryName(path)!, "absent", "reviewer.yaml"), error.Message);
        }
        finally { File.Delete(path); }
    }
}
