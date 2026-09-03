using HPD.Agent.Providers;

namespace HPD.Agent.Tests.SubAgents;

public sealed class SubAgentRunConfigTests
{
    [Fact]
    public void IsAnAgentRunConfigForDirectChildren()
    {
        var config = new SubAgentRunConfig
        {
            SystemInstructions = new SystemInstructionsRunConfig { Append = "child-only" },
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig { ModelName = "child-model" }
            }
        };

        Assert.IsAssignableFrom<AgentRunConfig>(config);
        Assert.Equal("child-only", config.SystemInstructions!.Append);
        Assert.Equal("child-model", config.Clients.Chat!.ModelName);
    }

    [Fact]
    public void ThroughDepthRejectsNonPositiveDepth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SubAgentClientPropagation.ThroughDepth(0));
    }

    [Fact]
    public void ThroughDepthOneUsesDirectSingleton()
    {
        Assert.Same(
            SubAgentClientPropagation.DirectChildren,
            SubAgentClientPropagation.ThroughDepth(1));
    }

    [Fact]
    public void ExplicitPropagationShapesAreDistinct()
    {
        Assert.IsType<DirectSubAgentClientPropagation>(SubAgentClientPropagation.DirectChildren);
        Assert.Equal(3, Assert.IsType<BoundedSubAgentClientPropagation>(
            SubAgentClientPropagation.ThroughDepth(3)).Depth);
        Assert.IsType<UnboundedSubAgentClientPropagation>(SubAgentClientPropagation.EntireTree);
    }

    [Fact]
    public void DurablePolicyFingerprintIncludesInitialRunConfiguration()
    {
        var chat = new ChatClientConfig
        {
            Provider = new ProviderReference { Key = "test" },
            ModelName = "child-model"
        };
        var first = SubAgentExecutionPolicy.Create(
            new AgentRunConfig { SystemInstructions = new() { Append = "first" } },
            chat,
            SubAgentClientSelectionSource.InputSubAgentRun,
            new NoSubAgentClientPropagation());
        var second = SubAgentExecutionPolicy.Create(
            new AgentRunConfig { SystemInstructions = new() { Append = "second" } },
            chat,
            SubAgentClientSelectionSource.InputSubAgentRun,
            new NoSubAgentClientPropagation());

        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void DurablePolicyRejectsRuntimeOnlyInitialConfiguration()
    {
        var run = new AgentRunConfig
        {
            Streaming = new StreamingRunConfig { Callback = _ => Task.CompletedTask }
        };
        var chat = new ChatClientConfig
        {
            Provider = new ProviderReference { Key = "test" },
            ModelName = "child-model"
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            SubAgentExecutionPolicy.Create(
                run,
                chat,
                SubAgentClientSelectionSource.InputSubAgentRun,
                new NoSubAgentClientPropagation()));

        Assert.Equal("subagent_run_config_not_portable", error.Message);
    }
}
