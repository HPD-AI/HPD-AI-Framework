using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace HPD.Base.AspNetCore;

internal sealed class HPDBaseOpenApiDocumentTransformer(IOptions<HPDBaseOpenApiOptions> options) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var configured = options.Value;
        var isAdmin = string.Equals(context.DocumentName, configured.AdminDocumentName, StringComparison.OrdinalIgnoreCase);

        document.Info.Title = isAdmin ? "HPD.BASE Admin API" : "HPD.BASE Public API";
        document.Info.Version = "1.0.0";
        document.Info.Description = isAdmin
            ? "Admin and operator HTTP contract for mapped HPD.BASE endpoints."
            : "Public and user-facing HTTP contract for mapped HPD.BASE endpoints.";

        if (configured.AddHPDExtensions)
        {
            document.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            document.Extensions["x-hpd-document-name"] = new JsonNodeExtension(context.DocumentName);
            document.Extensions["x-hpd-contract-version"] = new JsonNodeExtension(document.Info.Version);
        }

        if (configured.AddBearerSecurityScheme && isAdmin)
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[configured.BearerSecuritySchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                In = ParameterLocation.Header,
                BearerFormat = "Json Web Token"
            };
        }

        return Task.CompletedTask;
    }
}
