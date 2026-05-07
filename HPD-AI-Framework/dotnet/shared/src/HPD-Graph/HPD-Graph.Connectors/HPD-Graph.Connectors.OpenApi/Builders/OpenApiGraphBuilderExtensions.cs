using HPDAgent.Graph.Connectors.OpenApi.Handlers;
using HPDAgent.Graph.Core.Builders;

namespace HPDAgent.Graph.Connectors.OpenApi.Builders;

public static class OpenApiGraphBuilderExtensions
{
    public static GraphBuilder AddOpenApiOperationNode(
        this GraphBuilder builder,
        string id,
        string name,
        OpenApiCallOperationConfig config)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(config);

        return builder.AddHandlerNode(
            id,
            name,
            OpenApiCallOperationHandler.Name,
            node => node.WithConfig(System.Text.Json.JsonSerializer.SerializeToElement(
                config,
                OpenApiConnectorJsonSerializerContext.Default.OpenApiCallOperationConfig)));
    }
}
