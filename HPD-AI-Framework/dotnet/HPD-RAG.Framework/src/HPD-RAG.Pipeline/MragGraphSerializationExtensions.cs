using System.Text.Json.Serialization.Metadata;
using HPD.RAG.Core.Serialization;
using HPDAgent.Graph.Abstractions.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.RAG.Pipeline;

public static class MragGraphSerializationExtensions
{
    public static IServiceCollection AddMragGraphSerialization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IGraphJsonTypeInfoResolverContributor,
            MragGraphJsonTypeInfoResolverContributor>());

        return services;
    }
}

public sealed class MragGraphJsonTypeInfoResolverContributor : IGraphJsonTypeInfoResolverContributor
{
    public IJsonTypeInfoResolver Resolver => MragJsonSerializerContext.Shared;
}
