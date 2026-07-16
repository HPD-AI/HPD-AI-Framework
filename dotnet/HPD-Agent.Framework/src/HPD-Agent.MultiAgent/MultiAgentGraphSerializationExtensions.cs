using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HPD.Agent;
using HPD.MultiAgent.Config;
using HPD.Graph.Abstractions.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.MultiAgent;

public static class MultiAgentGraphSerializationExtensions
{
    public static IServiceCollection AddMultiAgentGraphSerialization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IGraphJsonTypeInfoResolverContributor,
            MultiAgentGraphJsonTypeInfoResolverContributor>());

        return services;
    }
}

public sealed class MultiAgentGraphJsonTypeInfoResolverContributor : IGraphJsonTypeInfoResolverContributor
{
    public IJsonTypeInfoResolver Resolver => MultiAgentGraphConfigJsonContext.Default;
}

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MultiAgentWorkflowConfig))]
[JsonSerializable(typeof(AgentNodeConfig))]
[JsonSerializable(typeof(RetryConfig))]
[JsonSerializable(typeof(ErrorConfig))]
[JsonSerializable(typeof(EdgeConfig))]
[JsonSerializable(typeof(ConditionConfig))]
[JsonSerializable(typeof(WorkflowSettingsConfig))]
[JsonSerializable(typeof(IterationOptionsConfig))]
[JsonSerializable(typeof(AgentConfig))]
[JsonSerializable(typeof(CompactionSpecification))]
[JsonSerializable(typeof(Dictionary<string, AgentNodeConfig>))]
[JsonSerializable(typeof(List<EdgeConfig>))]
[JsonSerializable(typeof(List<ConditionConfig>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(JsonElement))]
public partial class MultiAgentGraphConfigJsonContext : JsonSerializerContext
{
}
