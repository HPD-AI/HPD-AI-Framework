using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.MultiAgent.AspNetCore;

public sealed class HPDMultiAgentEndpointOptions
{
    public string RoutePrefix { get; set; } = "/multi-agent";

    public bool IncludeGenericWorkflows { get; set; }

    public bool MapWorkflows { get; set; } = true;

    public bool MapRuns { get; set; } = true;

    public bool MapEvents { get; set; } = true;

    public bool MapApprovals { get; set; } = true;

    public Action<RouteGroupBuilder>? ConfigureRoutes { get; set; }
}
