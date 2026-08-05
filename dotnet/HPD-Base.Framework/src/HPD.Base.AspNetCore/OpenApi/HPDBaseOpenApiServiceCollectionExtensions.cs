using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Extension methods for registering HPD.BASE OpenAPI documents.
/// </summary>
public static class HPDBaseOpenApiServiceCollectionExtensions
{
    /// <summary>
    /// Registers opt-in HPD.BASE OpenAPI document generation.
    /// </summary>
    public static IServiceCollection AddHPDBaseOpenApi(
        this IServiceCollection services,
        Action<HPDBaseOpenApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new HPDBaseOpenApiOptions();
        configure?.Invoke(options);
        Validate(options);

        services.AddSingleton<IConfigureOptions<HPDBaseOpenApiOptions>>(
            new ConfigureNamedOptions<HPDBaseOpenApiOptions>(Options.DefaultName, configured =>
            {
                configured.PublicDocumentName = options.PublicDocumentName;
                configured.AdminDocumentName = options.AdminDocumentName;
                configured.RegisterPublicDocument = options.RegisterPublicDocument;
                configured.RegisterAdminDocument = options.RegisterAdminDocument;
                configured.IncludeAdminRoutesInAdminDocument = options.IncludeAdminRoutesInAdminDocument;
                configured.IncludeRecordRoutesInPublicDocument = options.IncludeRecordRoutesInPublicDocument;
                configured.AddBearerSecurityScheme = options.AddBearerSecurityScheme;
                configured.BearerSecuritySchemeName = options.BearerSecuritySchemeName;
                configured.AddHPDExtensions = options.AddHPDExtensions;
                configured.OpenApiVersion = options.OpenApiVersion;
            }));

        if (options.RegisterPublicDocument)
            services.AddOpenApi(options.PublicDocumentName, openApi => ConfigurePublicDocument(openApi, options));

        if (options.RegisterAdminDocument)
            services.AddOpenApi(options.AdminDocumentName, openApi => ConfigureAdminDocument(openApi, options));

        return services;
    }

    private static void ConfigurePublicDocument(OpenApiOptions openApi, HPDBaseOpenApiOptions options)
    {
        openApi.OpenApiVersion = options.OpenApiVersion;
        openApi.ShouldInclude = description =>
            HPDBaseOpenApiFilters.Public(description)
            && (options.IncludeRecordRoutesInPublicDocument
                || description.ActionDescriptor.EndpointMetadata.OfType<HPDBaseOpenApiRouteMetadata>().FirstOrDefault()?.IsRecord != true);
        ConfigureShared(openApi);
    }

    private static void ConfigureAdminDocument(OpenApiOptions openApi, HPDBaseOpenApiOptions options)
    {
        openApi.OpenApiVersion = options.OpenApiVersion;
        openApi.ShouldInclude = description => options.IncludeAdminRoutesInAdminDocument && HPDBaseOpenApiFilters.Admin(description);
        ConfigureShared(openApi);
    }

    private static void ConfigureShared(OpenApiOptions openApi)
    {
        openApi.CreateSchemaReferenceId = HPDBaseOpenApiSchemaReferenceIds.Create;
        openApi.AddDocumentTransformer<HPDBaseOpenApiDocumentTransformer>();
        openApi.AddOperationTransformer<HPDBaseOpenApiOperationTransformer>();
        openApi.AddSchemaTransformer<HPDBaseOpenApiSchemaTransformer>();
    }

    private static void Validate(HPDBaseOpenApiOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PublicDocumentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AdminDocumentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.BearerSecuritySchemeName);
    }
}
