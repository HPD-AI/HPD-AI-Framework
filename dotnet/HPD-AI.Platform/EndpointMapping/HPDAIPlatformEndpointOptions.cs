using Microsoft.AspNetCore.Routing;

namespace HPD.AI.Platform;

public sealed class HPDAIPlatformEndpointOptions
{
    public string RoutePrefix { get; set; } = "/studio";

    public string ApiBasePath { get; set; } = "/api/hpd";

    public string ProductTitle { get; set; } = "HPD AI Platform";

    public string Mode { get; set; } = "development";

    public IList<string> Capabilities { get; } = [];

    public IList<HPDAIPlatformModuleOptions> Modules { get; } = [];

    public Action<RouteGroupBuilder>? ConfigureRoutes { get; set; }
}

public sealed record HPDAIPlatformModuleOptions(
    string Id,
    string Label,
    string Title,
    string Status = "active");
