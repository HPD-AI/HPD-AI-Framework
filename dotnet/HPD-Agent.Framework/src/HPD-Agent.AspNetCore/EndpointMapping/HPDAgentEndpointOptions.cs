using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore;

public sealed class HPDAgentEndpointOptions
{
    public string RoutePrefix { get; set; } = "";

    public bool MapSessions { get; set; } = true;
    public bool MapThreads { get; set; } = true;
    public bool MapContent { get; set; } = true;
    public bool MapStreaming { get; set; } = true;
    public bool MapMiddlewareResponses { get; set; } = true;
    public bool MapAgents { get; set; } = true;
    public bool MapEvals { get; set; } = true;

    public Action<RouteGroupBuilder>? ConfigureRoutes { get; set; }
}
