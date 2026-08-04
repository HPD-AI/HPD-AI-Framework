using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace HPD.Auth.ControlPlane;

internal sealed class ControlPlaneOpenApiDocumentTransformer(ControlPlaneRegistry registry)
    : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        foreach (var profile in registry.Profiles.Where(static value => value.OpenApiSecurityScheme is not null))
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[profile.OpenApiSecurityScheme!] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header
            };
        }
        return Task.CompletedTask;
    }
}

internal sealed class ControlPlaneOpenApiOperationTransformer(ControlPlaneRegistry registry)
    : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<ControlPlaneEndpointMetadata>().FirstOrDefault();
        if (metadata is null)
            return Task.CompletedTask;

        var profile = registry.GetProfile(metadata.Profile);
        if (profile.OpenApiSecurityScheme is { } scheme)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(scheme, context.Document)] = []
            });
        }
        AddResponse(operation, "401", "Authentication is required.");
        AddResponse(operation, "403", "The authenticated actor is not authorized.");
        if (profile.RateLimitPolicy is not null)
            AddResponse(operation, "429", "The configured traffic-admission policy rejected the request.");
        if (profile.RequestTimeoutPolicy is not null)
            AddResponse(operation, "504", "The configured request-timeout policy expired.");
        return Task.CompletedTask;
    }

    private static void AddResponse(OpenApiOperation operation, string status, string description)
    {
        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd(status, new OpenApiResponse { Description = description });
    }
}

public static class HPDControlPlaneOpenApiServiceCollectionExtensions
{
    public static IServiceCollection AddHPDControlPlaneOpenApi(
        this IServiceCollection services, string documentName = "v1")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
        services.AddOpenApi(documentName, options =>
        {
            options.AddDocumentTransformer<ControlPlaneOpenApiDocumentTransformer>();
            options.AddOperationTransformer<ControlPlaneOpenApiOperationTransformer>();
        });
        return services;
    }
}
