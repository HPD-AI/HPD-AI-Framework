using HPD.Graph.Abstractions.Serialization;
using HPD.Graph.AspNetCore.Serialization;
using HPD.Graph.Hosting.DependencyInjection;
using HPD.Graph.Hosting.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Graph.AspNetCore.DependencyInjection;

public static class GraphAspNetCoreServiceCollectionExtensions
{
    public static IServiceCollection AddHPDGraphAspNetCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHPDGraphHosting();
        services.AddOptions<JsonOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<JsonOptions>, GraphJsonOptionsSetup>());

        return services;
    }

    private sealed class GraphJsonOptionsSetup(
        IEnumerable<IGraphJsonTypeInfoResolverContributor> contributors) : IConfigureOptions<JsonOptions>
    {
        public void Configure(JsonOptions options)
        {
            var chain = options.SerializerOptions.TypeInfoResolverChain;
            chain.Insert(0, GraphAspNetCoreJsonSerializerContext.Default);
            chain.Insert(1, GraphHostingJsonSerializerContext.Default);
            chain.Insert(2, GraphConfigJsonSerializerContext.Default);

            var insertIndex = 3;
            foreach (var contributor in contributors)
            {
                chain.Insert(insertIndex++, contributor.Resolver);
            }
        }
    }

}
