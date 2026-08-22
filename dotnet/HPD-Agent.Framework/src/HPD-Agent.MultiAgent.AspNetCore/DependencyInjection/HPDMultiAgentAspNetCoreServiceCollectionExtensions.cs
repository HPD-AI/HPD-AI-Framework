using HPD.Agent.MultiAgent.AspNetCore.Serialization;
using HPD.Graph.Abstractions.Serialization;
using HPD.Graph.Base;
using HPD.MultiAgent;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Agent.MultiAgent.AspNetCore;

public static class HPDMultiAgentAspNetCoreServiceCollectionExtensions
{
    public static IServiceCollection AddHPDMultiAgentAspNetCore(
        this IServiceCollection services,
        params BaseGraphActivationDefinition[] graphs)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(graphs);
        if (graphs.Length == 0 || graphs.Select(static graph => graph.GraphId).Distinct(StringComparer.Ordinal).Count() != graphs.Length)
            throw new ArgumentException("At least one uniquely identified installed graph is required.", nameof(graphs));

        services.AddMultiAgentGraphSerialization();
        foreach (BaseGraphActivationDefinition graph in graphs) services.AddSingleton(graph);
        services.AddOptions<JsonOptions>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<JsonOptions>, HPDMultiAgentJsonOptionsSetup>());

        return services;
    }

    private sealed class HPDMultiAgentJsonOptionsSetup(
        IEnumerable<IGraphJsonTypeInfoResolverContributor> graphResolverContributors) : IConfigureOptions<JsonOptions>
    {
        public void Configure(JsonOptions options)
        {
            var chain = options.SerializerOptions.TypeInfoResolverChain;
            chain.Insert(0, HPDMultiAgentAspNetCoreJsonSerializerContext.Default);
            chain.Insert(1, GraphConfigJsonSerializerContext.Default);

            var insertIndex = 2;
            foreach (var contributor in graphResolverContributors)
            {
                chain.Insert(insertIndex++, contributor.Resolver);
            }
        }
    }
}
