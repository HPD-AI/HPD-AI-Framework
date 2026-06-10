using HPD.OpenApi.Core;
using HPD.OpenApi.Core.Model;

namespace HPD.Graph.Connectors.OpenApi.Catalog;

public static class OpenApiOperationCatalogLoader
{
    public static IReadOnlyList<OpenApiOperationRegistration> FromParsedSpec(
        string connectorId,
        ParsedOpenApiSpec spec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentNullException.ThrowIfNull(spec);

        return spec.Operations
            .Where(operation => !string.IsNullOrWhiteSpace(operation.Id))
            .Select(operation => new OpenApiOperationRegistration(connectorId, operation))
            .ToArray();
    }

    public static async Task<IReadOnlyList<OpenApiOperationRegistration>> LoadAsync(
        string connectorId,
        OpenApiCoreConfig config,
        HttpClient? httpClient = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentNullException.ThrowIfNull(config);

        var spec = await OpenApiSpecLoader
            .LoadAndParseAsync(config, httpClient, ct)
            .ConfigureAwait(false);

        return FromParsedSpec(connectorId, spec);
    }
}
