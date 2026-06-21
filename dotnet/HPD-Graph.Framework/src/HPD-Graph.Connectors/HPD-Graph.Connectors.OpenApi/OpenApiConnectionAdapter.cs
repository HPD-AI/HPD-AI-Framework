using HPD.OpenApi.Core;
using HPD.Graph.Connectors.Abstractions.Connections;

namespace HPD.Graph.Connectors.OpenApi;

public interface IOpenApiConnectionAdapter
{
    bool CanAdapt(ResolvedConnection connection);

    OpenApiCoreConfig CreateConfig(
        ResolvedConnection connection,
        CancellationToken ct = default);
}
