using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Routing;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Configures which HPD.BASE ASP.NET Core endpoints are mapped.
/// </summary>
public sealed class HPDBaseEndpointOptions
{
    /// <summary>
    /// Gets or sets the route prefix used for all BASE endpoints.
    /// </summary>
    public string RoutePrefix { get; set; } = "/base";

    /// <summary>
    /// Gets or sets whether public metadata endpoints are mapped.
    /// </summary>
    public bool MapMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets how much metadata is exposed on public BASE routes.
    /// </summary>
    public HPDBasePublicMetadataMode PublicMetadataMode { get; set; } = HPDBasePublicMetadataMode.Full;

    /// <summary>
    /// Gets or sets whether collection metadata endpoints are mapped.
    /// </summary>
    public bool MapCollections { get; set; } = true;

    /// <summary>
    /// Gets or sets whether record endpoints are mapped.
    /// </summary>
    public bool MapRecords { get; set; } = true;

    /// <summary>
    /// Gets or sets the ASP.NET Core authorization policy used for record routes
    /// when <see cref="RequireAuthorizationForRecordRoutes"/> is enabled.
    /// </summary>
    public string RecordPolicyName { get; set; } = HPDBasePolicies.Authenticated;

    /// <summary>
    /// Gets or sets whether record routes require ASP.NET Core authorization metadata.
    /// </summary>
    public bool RequireAuthorizationForRecordRoutes { get; set; }

    /// <summary>
    /// Gets or sets whether admin metadata endpoints are mapped.
    /// </summary>
    public bool MapAdminMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the admin policy explain endpoint is mapped.
    /// </summary>
    public bool MapAdminPolicyExplain { get; set; }

    /// <summary>
    /// Gets or sets whether health endpoints are mapped.
    /// </summary>
    public bool MapHealth { get; set; } = true;

    /// <summary>
    /// Gets or sets whether diagnostics endpoints are mapped.
    /// </summary>
    public bool MapDiagnostics { get; set; } = true;

    /// <summary>
    /// Gets or sets the ASP.NET Core authorization policy used for admin routes.
    /// </summary>
    public string AdminPolicyName { get; set; } = HPDBasePolicies.Admin;

    /// <summary>
    /// Gets or sets whether admin routes require ASP.NET Core authorization metadata.
    /// </summary>
    public bool RequireAuthorizationForAdminRoutes { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional route customization callback invoked after built-in routes are mapped.
    /// </summary>
    public Action<RouteGroupBuilder>? ConfigureRoutes { get; set; }
}

/// <summary>
/// Controls the public BASE metadata surface.
/// </summary>
public enum HPDBasePublicMetadataMode
{
    /// <summary>
    /// Maps public manifest, capabilities, schema, and collection metadata routes.
    /// </summary>
Full,

    /// <summary>
    /// Maps only compact public manifest and capabilities routes.
    /// </summary>
Minimal,

    /// <summary>
    /// Maps no public metadata routes.
    /// </summary>
Disabled
}
