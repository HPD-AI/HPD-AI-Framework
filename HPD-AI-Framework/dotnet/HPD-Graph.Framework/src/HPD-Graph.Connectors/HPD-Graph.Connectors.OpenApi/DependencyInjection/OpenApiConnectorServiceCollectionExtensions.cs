using HPD.Graph.Abstractions.Handlers;
using HPD.Graph.Connectors.OpenApi.Catalog;
using HPD.Graph.Connectors.OpenApi.Descriptors;
using HPD.Graph.Connectors.OpenApi.Handlers;
using HPD.Graph.Core.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Graph.Connectors.OpenApi.DependencyInjection;

public static class OpenApiConnectorServiceCollectionExtensions
{
    public static IServiceCollection AddHPDGraphConnectorsOpenApi(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IOpenApiOperationCatalog, OpenApiOperationCatalog>();
        services.TryAddSingleton<IOpenApiDescriptorCatalog, OpenApiDescriptorCatalog>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IGraphNodeHandler<GraphContext>, OpenApiCallOperationHandler>());

        return services;
    }

    public static IServiceCollection AddOpenApiOperations(
        this IServiceCollection services,
        string connectorId,
        IEnumerable<HPD.OpenApi.Core.Model.RestApiOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentNullException.ThrowIfNull(operations);

        services.AddHPDGraphConnectorsOpenApi();
        foreach (var operation in operations)
            services.AddSingleton(new OpenApiOperationRegistration(connectorId, operation));

        return services;
    }
}
