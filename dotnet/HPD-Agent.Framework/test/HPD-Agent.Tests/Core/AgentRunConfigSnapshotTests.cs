using HPD.Agent.StructuredOutput;

namespace HPD.Agent.Tests.Core;

public sealed class AgentRunConfigSnapshotTests
{
    [Fact]
    public void Capture_OwnsMutableConfigurationAndRetainsOpaqueRuntimeIdentity()
    {
        var opaque = new object();
        var evaluations = new SnapshottingEvaluationConfig();
        var source = new AgentRunConfig
        {
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig
                {
                    ProviderKey = "test",
                    ModelName = "model",
                    StopSequences = ["stop"]
                }
            },
            Context = new AgentContextRunConfig
            {
                Properties = new Dictionary<string, object> { ["opaque"] = opaque }
            },
            StructuredOutput = new StructuredOutputOptions { UnionTypes = [typeof(string)] },
            Evaluations = evaluations
        };

        var snapshot = AgentRunConfigSnapshot.Capture(source, composition: null)!;

        Assert.NotSame(source, snapshot);
        Assert.NotSame(source.Clients, snapshot.Clients);
        Assert.NotSame(source.Clients.Chat, snapshot.Clients.Chat);
        Assert.NotSame(source.Clients.Chat!.StopSequences, snapshot.Clients.Chat!.StopSequences);
        Assert.NotSame(source.Context!.Properties, snapshot.Context!.Properties);
        Assert.Same(opaque, snapshot.Context.Properties!["opaque"]);
        Assert.NotSame(source.StructuredOutput!.UnionTypes, snapshot.StructuredOutput!.UnionTypes);
        Assert.NotSame(evaluations, snapshot.Evaluations);
    }

    [Fact]
    public void Capture_WrapsEvaluationSnapshotFailures()
    {
        var source = new AgentRunConfig { Evaluations = new ThrowingEvaluationConfig() };

        var exception = Assert.Throws<AgentRunConfigurationException>(
            () => AgentRunConfigSnapshot.Capture(source, composition: null));

        Assert.Equal("EvaluationSnapshotFailed", exception.Code);
        Assert.Equal(nameof(AgentRunConfig.Evaluations), exception.Path);
    }

    private sealed class SnapshottingEvaluationConfig : IAgentRunEvaluationConfig
    {
        public IAgentRunEvaluationConfig Snapshot() => new SnapshottingEvaluationConfig();
    }

    private sealed class ThrowingEvaluationConfig : IAgentRunEvaluationConfig
    {
        public IAgentRunEvaluationConfig Snapshot() => throw new InvalidOperationException("boom");
    }
}
