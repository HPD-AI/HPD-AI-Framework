using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Extension methods for mapping HPD.BASE OpenAPI document endpoints.
/// </summary>
public static class HPDBaseOpenApiEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps HPD.BASE OpenAPI document endpoints using default endpoint options.
    /// </summary>
    public static IEndpointConventionBuilder MapHPDBaseOpenApi(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHPDBaseOpenApi(null);

    /// <summary>
    /// Maps HPD.BASE OpenAPI document endpoints using caller-provided endpoint options.
    /// </summary>
    public static IEndpointConventionBuilder MapHPDBaseOpenApi(
        this IEndpointRouteBuilder endpoints,
        Action<HPDBaseOpenApiEndpointOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = new HPDBaseOpenApiEndpointOptions();
        configure?.Invoke(options);
        Validate(options);

        var jsonBuilder = endpoints.MapOpenApi(options.RoutePattern);
        ApplyDescriptionVisibility(jsonBuilder, options);

        var builders = new List<IEndpointConventionBuilder> { jsonBuilder };

        if (options.MapYaml)
        {
            var yamlBuilder = endpoints.MapOpenApi(options.YamlRoutePattern);
            ApplyDescriptionVisibility(yamlBuilder, options);
            builders.Add(yamlBuilder);
        }

        return new CompositeEndpointConventionBuilder(builders);
    }

    private static void ApplyDescriptionVisibility(IEndpointConventionBuilder builder, HPDBaseOpenApiEndpointOptions options)
    {
        if (options.ExcludeOpenApiEndpointFromDescription)
            return;

        builder.Add(endpointBuilder =>
        {
            for (var index = endpointBuilder.Metadata.Count - 1; index >= 0; index--)
            {
                if (endpointBuilder.Metadata[index] is IExcludeFromDescriptionMetadata { ExcludeFromDescription: true })
                    endpointBuilder.Metadata.RemoveAt(index);
            }
        });
    }

    private static void Validate(HPDBaseOpenApiEndpointOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RoutePattern);
        if (!options.RoutePattern.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("RoutePattern must start with '/'.", nameof(options));
        if (!options.RoutePattern.Contains("{documentName}", StringComparison.Ordinal))
            throw new ArgumentException("RoutePattern must include a '{documentName}' route parameter.", nameof(options));

        ArgumentException.ThrowIfNullOrWhiteSpace(options.YamlRoutePattern);
        if (!options.YamlRoutePattern.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("YamlRoutePattern must start with '/'.", nameof(options));
        if (!options.YamlRoutePattern.Contains("{documentName}", StringComparison.Ordinal))
            throw new ArgumentException("YamlRoutePattern must include a '{documentName}' route parameter.", nameof(options));
    }

    private sealed class CompositeEndpointConventionBuilder(IReadOnlyList<IEndpointConventionBuilder> builders) : IEndpointConventionBuilder
    {
        /// <summary>Executes the add operation.</summary>
        public void Add(Action<EndpointBuilder> convention)
        {
            foreach (var builder in builders)
                builder.Add(convention);
        }
    }
}
