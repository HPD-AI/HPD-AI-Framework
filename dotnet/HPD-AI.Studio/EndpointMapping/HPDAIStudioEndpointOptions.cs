using Microsoft.AspNetCore.Routing;

namespace HPD.AI.Studio;

public sealed class HPDAIStudioEndpointOptions
{
    public string RoutePrefix { get; set; } = "/studio";

    public string ApiBasePath { get; set; } = "/api/hpd";

    public string ProductTitle { get; set; } = "HPD AI Studio";

    public string Mode { get; set; } = "development";

    public IList<string> Capabilities { get; } = [];

    public IList<HPDAIStudioModuleOptions> Modules { get; } = [];

    public Action<RouteGroupBuilder>? ConfigureRoutes { get; set; }
}

public sealed record HPDAIStudioModuleOptions(
    string Id,
    string Label,
    string Title,
    string Status = "active");
