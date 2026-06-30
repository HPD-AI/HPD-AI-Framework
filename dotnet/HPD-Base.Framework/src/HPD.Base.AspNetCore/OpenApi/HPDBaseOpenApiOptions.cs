using Microsoft.OpenApi;

namespace HPD.Base.AspNetCore.OpenApi;

/// <summary>
/// Configures HPD.BASE OpenAPI document registration.
/// </summary>
public sealed class HPDBaseOpenApiOptions
{
    /// <summary>Gets or sets the public document name.</summary>
    public string PublicDocumentName { get; set; } = HPDBaseOpenApiDocumentNames.Public;

    /// <summary>Gets or sets the admin document name.</summary>
    public string AdminDocumentName { get; set; } = HPDBaseOpenApiDocumentNames.Admin;

    /// <summary>Gets or sets whether the public document is registered.</summary>
    public bool RegisterPublicDocument { get; set; } = true;

    /// <summary>Gets or sets whether the admin document is registered.</summary>
    public bool RegisterAdminDocument { get; set; } = true;

    /// <summary>Gets or sets whether admin routes are included in the admin document.</summary>
    public bool IncludeAdminRoutesInAdminDocument { get; set; } = true;

    /// <summary>Gets or sets whether record routes are included in the public document.</summary>
    public bool IncludeRecordRoutesInPublicDocument { get; set; } = true;

    /// <summary>Gets or sets whether a bearer security scheme is added for admin operations.</summary>
    public bool AddBearerSecurityScheme { get; set; } = true;

    /// <summary>Gets or sets the bearer security scheme name.</summary>
    public string BearerSecuritySchemeName { get; set; } = "Bearer";

    /// <summary>Gets or sets whether HPD-specific OpenAPI extensions are added.</summary>
    public bool AddHPDExtensions { get; set; } = true;

    /// <summary>Gets or sets the OpenAPI spec version used when serializing HPD documents.</summary>
    public OpenApiSpecVersion OpenApiVersion { get; set; } = OpenApiSpecVersion.OpenApi3_1;
}
