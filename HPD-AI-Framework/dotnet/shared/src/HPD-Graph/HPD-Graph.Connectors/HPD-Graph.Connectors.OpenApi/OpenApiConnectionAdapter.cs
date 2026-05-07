using HPD.OpenApi.Core;
using HPDAgent.Graph.Connectors.Abstractions.Connections;

namespace HPDAgent.Graph.Connectors.OpenApi;

public interface IOpenApiConnectionAdapter
{
    bool CanAdapt(ResolvedConnection connection);

    OpenApiCoreConfig CreateConfig(
        ResolvedConnection connection,
        CancellationToken ct = default);
}
