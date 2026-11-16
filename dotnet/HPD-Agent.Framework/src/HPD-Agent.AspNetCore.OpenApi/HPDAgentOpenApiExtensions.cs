using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using System.Text;

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
        AddDocumentProviderDecorator(services, documentName);

        return services;
    }

    private static void AddDocumentProviderDecorator(
        IServiceCollection services,
        string documentName)
    {
        // The framework registers one AnyKey provider for all document names.
        // Replace that descriptor with a wrapper that finalizes the generated
        // document while preserving the requested keyed document identity.
        var providerIndex = services
            .Select(static (descriptor, index) => (descriptor, index))
            .Where(item =>
                item.descriptor.ServiceType == typeof(IOpenApiDocumentProvider) &&
                item.descriptor.IsKeyedService &&
                (Equals(item.descriptor.ServiceKey, documentName) ||
                 Equals(item.descriptor.ServiceKey, KeyedService.AnyKey) ||
                 Equals(item.descriptor.ServiceKey, "*")))
            .Select(static item => item.index)
            .LastOrDefault(-1);
        var provider = providerIndex >= 0
            ? services[providerIndex]
            : null;
        if (provider is null)
            return;

        services.RemoveAt(providerIndex);
        services.AddKeyedSingleton<IOpenApiDocumentProvider>(
            provider.ServiceKey,
            (serviceProvider, serviceKey) =>
            {
                var resolvedDocumentName = serviceKey is string requested &&
                                           requested != "*"
                    ? requested
                    : documentName;
                var inner = provider.KeyedImplementationFactory is { } factory
                    ? (IOpenApiDocumentProvider)factory(
                        serviceProvider,
                        Equals(provider.ServiceKey, KeyedService.AnyKey)
                            ? resolvedDocumentName
                            : provider.ServiceKey)
                    : provider.KeyedImplementationInstance is IOpenApiDocumentProvider instance
                        ? instance
                        : (IOpenApiDocumentProvider)ActivatorUtilities.CreateInstance(
                            serviceProvider,
                            provider.KeyedImplementationType!,
                            resolvedDocumentName);
                return new HPDAgentOpenApiDocumentProvider(inner);
            });
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
        var yaml = pattern.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                   pattern.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);
        RequestDelegate handler = async context =>
        {
            var documentName =
                context.Request.RouteValues["documentName"] as string ??
                DefaultDocumentName;
            var provider = context.RequestServices
                .GetRequiredKeyedService<IOpenApiDocumentProvider>(documentName);
            var document = await provider.GetOpenApiDocumentAsync(
                    context.RequestAborted)
                .ConfigureAwait(false);
            var content = yaml
                ? await document.SerializeAsYamlAsync(
                        OpenApiSpecVersion.OpenApi3_1,
                        context.RequestAborted)
                    .ConfigureAwait(false)
                : await document.SerializeAsJsonAsync(
                        OpenApiSpecVersion.OpenApi3_1,
                        context.RequestAborted)
                    .ConfigureAwait(false);
            var result = Results.Text(
                content,
                yaml ? "application/yaml" : "application/json",
                Encoding.UTF8);
            await result.ExecuteAsync(context).ConfigureAwait(false);
        };
        return endpoints.MapGet(pattern, handler);
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
