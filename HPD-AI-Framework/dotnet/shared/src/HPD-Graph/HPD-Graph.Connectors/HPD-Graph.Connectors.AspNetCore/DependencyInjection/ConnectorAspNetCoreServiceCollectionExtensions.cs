using HPDAgent.Graph.AspNetCore.DependencyInjection;
using HPDAgent.Graph.Connectors.Abstractions.Serialization;
using HPDAgent.Graph.Connectors.AspNetCore.Serialization;
using HPDAgent.Graph.Connectors.Core.DependencyInjection;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HPDAgent.Graph.Connectors.AspNetCore.DependencyInjection;

public static class ConnectorAspNetCoreServiceCollectionExtensions
{
    public static IServiceCollection AddHPDGraphConnectors(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHPDGraphAspNetCore();
        services.AddHPDGraphMaterialization();
        services.AddHPDGraphConnectorsCore();
        services.AddOptions<JsonOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<JsonOptions>, ConnectorJsonOptionsSetup>());

        return services;
    }

    private sealed class ConnectorJsonOptionsSetup : IConfigureOptions<JsonOptions>
    {
        public void Configure(JsonOptions options)
        {
            var chain = options.SerializerOptions.TypeInfoResolverChain;
            chain.Insert(0, ConnectorAspNetCoreJsonSerializerContext.Default);
            chain.Insert(1, ConnectorAbstractionsJsonSerializerContext.Default);
        }
    }
}
