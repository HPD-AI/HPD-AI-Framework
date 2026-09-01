using HPD.Agent.StructuredOutput;

namespace HPD.Agent.Tests.Core;

public sealed class AgentRunConfigSnapshotTests
{
    [Fact]
    public void CaptureOwnershipMap_CoversEveryDeclaredRunConfigProperty()
    {
        var declaredProperties = typeof(AgentRunConfig)
            .GetProperties(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly)
            .Select(static property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            declaredProperties.SetEquals(AgentRunConfigSnapshot.CapturedPropertyNames),
            $"Snapshot ownership map mismatch. Declared: {string.Join(", ", declaredProperties.Order())}; " +
            $"mapped: {string.Join(", ", AgentRunConfigSnapshot.CapturedPropertyNames.Order())}.");
    }

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
                    Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" },
                    ModelName = "model",
                    StopSequences = ["stop"]
                }
            },
            Context = new AgentContextRunConfig
            {
                Properties = new Dictionary<string, object> { ["opaque"] = opaque }
            },
            StructuredOutput = new StructuredOutputOptions { UnionTypes = [typeof(string)] },
            Collapsing = new CollapsingRunPolicy
            {
                EnableErrorRecovery = true,
                RecoveryHistoryMode = ContainerRecoveryHistoryMode.Preserve
            },
            SubAgents = new SubAgentRunOverrides
            {
                Capabilities = [new SubAgentRunPolicyOverride
                {
                    CapabilityId = CapabilityId.Create("test:worker"),
                    Clients = new AgentClientInheritancePatch
                    {
                        Chat = ClientFamilyInheritanceMode.UseOwn
                    }
                }]
            },
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
        Assert.NotSame(source.Collapsing, snapshot.Collapsing);
        Assert.NotSame(source.SubAgents, snapshot.SubAgents);
        Assert.NotSame(source.SubAgents.Capabilities, snapshot.SubAgents.Capabilities);
        Assert.NotSame(source.SubAgents.Capabilities[0].Clients, snapshot.SubAgents.Capabilities[0].Clients);
        Assert.Equal(ClientFamilyInheritanceMode.UseOwn,
            snapshot.SubAgents.Capabilities[0].Clients!.Chat);
        Assert.Equal(ContainerRecoveryHistoryMode.Preserve, snapshot.Collapsing!.RecoveryHistoryMode);
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
