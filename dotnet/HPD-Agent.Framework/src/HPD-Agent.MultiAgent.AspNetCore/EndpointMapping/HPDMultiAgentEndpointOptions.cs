using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.MultiAgent.AspNetCore;

public sealed class HPDMultiAgentEndpointOptions
{
    public string RoutePrefix { get; set; } = "/multi-agent";

    public Action<RouteGroupBuilder>? ConfigureRoutes { get; set; }
}
