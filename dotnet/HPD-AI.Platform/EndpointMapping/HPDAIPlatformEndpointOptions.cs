using Microsoft.AspNetCore.Routing;

namespace HPD.AI.Platform;

/// <summary>Configures only host-owned Studio endpoint placement.</summary>
public sealed class HPDAIPlatformEndpointOptions
{
    /// <summary>Gets or sets the non-root route prefix under which Studio is hosted.</summary>
    public string RoutePrefix { get; set; } = "/studio";

    /// <summary>Gets or sets an optional hook for additional host-owned endpoints.</summary>
    public Action<RouteGroupBuilder>? ConfigureRoutes { get; set; }
}
