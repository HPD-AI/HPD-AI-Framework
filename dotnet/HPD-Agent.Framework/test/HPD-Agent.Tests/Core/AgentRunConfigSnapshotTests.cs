using HPD.Agent.StructuredOutput;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Serialization;

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
    public void JsonProjection_ExactlyCoversNonIgnoredPublicProperties()
    {
        var publicProperties = typeof(AgentRunConfig)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .ToArray();
        var expectedJsonNames = publicProperties
            .Select(static property =>
                property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ??
                JsonNamingPolicy.CamelCase.ConvertName(property.Name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        var projectedJsonNames = options.GetTypeInfo(typeof(AgentRunConfig)).Properties
            .Where(static property => property.Get is not null)
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedJsonNames, projectedJsonNames);
    }

    [Fact]
    public void SourceGeneratedProjection_ExcludesRuntimeOnlyPublicValues()
    {
        var source = new AgentRunConfig
        {
            RuntimeMiddleware = [new NoOpMiddleware()],
            Evaluations = new SnapshottingEvaluationConfig()
        };

        var json = JsonSerializer.Serialize(source, HPDJsonContext.Default.AgentRunConfig);

        Assert.DoesNotContain("\"runtimeMiddleware\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"evaluations\"", json, StringComparison.Ordinal);
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
                Properties = new Dictionary<string, object> { ["opaque"] = opaque },
                SharedProperties = new HashSet<string> { "opaque" }
            },
            StructuredOutput = new StructuredOutputOptions { UnionTypes = [typeof(string)] },
            Collapsing = new CollapsingRunPolicy
            {
                EnableErrorRecovery = true,
                RecoveryHistoryMode = ContainerRecoveryHistoryMode.Preserve
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

    private sealed class NoOpMiddleware : HPD.Agent.Middleware.IAgentMiddleware;
}
