using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace HPD.Agent.AspNetCore.OpenApi;

/// <summary>
/// Registers and maps the canonical HPD-Agent OpenAPI document.
/// </summary>
public static class HPDAgentOpenApiExtensions
{
    /// <summary>The stable document name used by generated HPD clients.</summary>
    public const string DefaultDocumentName = "hpd-agent-v1";

    /// <summary>The default JSON document route.</summary>
    public const string DefaultRoutePattern = "/openapi/{documentName}.json";

    /// <summary>
    /// Adds the HPD-Agent OpenAPI document and its HPD-specific transformers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="documentName">The stable generated-client document name.</param>
    /// <param name="configure">Optional standard ASP.NET Core OpenAPI configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddHPDAgentOpenApi(
        this IServiceCollection services,
        string documentName = DefaultDocumentName,
        Action<OpenApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);

        static void Configure(OpenApiOptions options)
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
            options.AddDocumentTransformer(new HPDAgentOpenApiDocumentTransformer());
            options.AddOperationTransformer(new HPDAgentOpenApiOperationTransformer());
            options.AddSchemaTransformer(new HPDAgentOpenApiSchemaTransformer());
        }

        // Invoke through a method group so the ASP.NET Core XML-comment source
        // generator cannot intercept the AddOpenApi call in this infrastructure
        // assembly and aggregate XML files from every transitive HPD project.
        // The consuming application's AddOpenApi call still gets XML support.
        Func<IServiceCollection, string, Action<OpenApiOptions>, IServiceCollection> addOpenApi =
            OpenApiServiceCollectionExtensions.AddOpenApi;
        _ = addOpenApi(services, documentName, options =>
        {
            Configure(options);
            configure?.Invoke(options);
        });

        return services;
    }

    /// <summary>
    /// Maps the HPD-Agent OpenAPI document endpoint.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">
    /// The document route. Use a path ending in <c>.yaml</c> or <c>.yml</c> for YAML.
    /// </param>
    /// <returns>The endpoint convention builder.</returns>
    public static IEndpointConventionBuilder MapHPDAgentOpenApi(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultRoutePattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        return endpoints.MapOpenApi(pattern);
    }

    /// <summary>
    /// Maps both the JSON and YAML HPD-Agent OpenAPI documents.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="routePrefix">The shared public route prefix.</param>
    public static void MapHPDAgentOpenApiDocuments(
        this IEndpointRouteBuilder endpoints,
        string routePrefix = "/openapi")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(routePrefix);

        var normalized = routePrefix.TrimEnd('/');
        endpoints.MapHPDAgentOpenApi($"{normalized}/{{documentName}}.json");
        endpoints.MapHPDAgentOpenApi($"{normalized}/{{documentName}}.yaml");
    }
}
