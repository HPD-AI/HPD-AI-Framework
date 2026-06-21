namespace HPD.OpenApi.Core;

using HPD.OpenApi.Core.Model;

/// <summary>
/// Static convenience methods combining parsing and runner creation.
/// Reduces boilerplate in consumer packages (HPD-Agent.OpenApi, HPD.Integrations.Http).
/// </summary>
public static class OpenApiSpecLoader
{
    private static readonly OpenApiDocumentParser s_parser = new();

    /// <summary>
    /// Loads and parses an OpenAPI spec from either a file path or URI, as configured.
    /// </summary>
    /// <param name="config">The config specifying SpecPath or SpecUri.</param>
    /// <param name="httpClient">Required when loading from SpecUri. If null, falls back to config.HttpClient.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed spec.</returns>
    public static async Task<ParsedOpenApiSpec> LoadAndParseAsync(
        OpenApiCoreConfig config,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        if (config.SpecPath != null)
            return await s_parser.ParseFromFileAsync(config.SpecPath, config, cancellationToken);

        if (config.SpecUri != null)
        {
            var client = httpClient ?? config.HttpClient
                ?? throw new ArgumentException(
                    "HttpClient is required when loading from SpecUri. " +
                    "Either pass one explicitly or set OpenApiCoreConfig.HttpClient.");
            return await s_parser.ParseFromUriAsync(config.SpecUri, client, config, cancellationToken);
        }

        throw new ArgumentException("Either SpecPath or SpecUri must be set on the config.");
    }

    /// <summary>
    /// Creates an <see cref="OpenApiOperationRunner"/> from the given config and HTTP client.
    /// </summary>
    public static OpenApiOperationRunner CreateRunner(
        OpenApiCoreConfig config,
        HttpClient httpClient) =>
        new(httpClient,
            config.AuthCallback,
            config.UserAgent,
            config.EnableDynamicPayload,
            config.EnablePayloadNamespacing,
            config.ErrorDetector);
}
