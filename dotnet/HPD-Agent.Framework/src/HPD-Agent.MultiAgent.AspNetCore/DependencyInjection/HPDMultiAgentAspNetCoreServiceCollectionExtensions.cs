using HPD.Agent.MultiAgent.AspNetCore.Serialization;
using HPD.Graph.Abstractions.Serialization;
using HPD.MultiAgent;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPD.Agent.MultiAgent.AspNetCore;

public static class HPDMultiAgentAspNetCoreServiceCollectionExtensions
{
    public static IServiceCollection AddHPDMultiAgentAspNetCore(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMultiAgentGraphSerialization();
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
