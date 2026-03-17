using HPD.Agent.AspNetCore.DependencyInjection;
using HPD.Agent.AspNetCore.EndpointMapping.Endpoints;
using HPD.Agent.AspNetCore.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore;

/// <summary>
/// Extension methods for mapping HPD Agent API endpoints.
/// </summary>
public static class HPDAgentEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps all HPD-Agent API endpoints for the default (unnamed) agent.
    /// </summary>
    public static RouteGroupBuilder MapHPDAgentApi(
        this IEndpointRouteBuilder endpoints)
        => endpoints.MapHPDAgentApi(Options.DefaultName);

    /// <summary>
    /// Maps all HPD-Agent API endpoints for a named agent.
    /// The name must match a previous AddHPDAgent(name, ...) registration.
    /// </summary>
    /// <returns>RouteGroupBuilder for further customization</returns>
    /// <remarks>
    /// Maps 20+ endpoints:
    /// - Session CRUD (Create, Search/List, Get, Update, Delete)
    /// - Branch CRUD (List, Get, Create, Fork, Delete, Messages, Siblings)
    /// - Asset management (Upload, Download, List, Delete)
    /// - Streaming (SSE + WebSocket)
    /// - Middleware responses (Permissions, Client Tools)
    /// - Agent definition CRUD (Create, List, Get, Update, Delete)
    /// </remarks>
    public static RouteGroupBuilder MapHPDAgentApi(
        this IEndpointRouteBuilder endpoints,
        string name)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(name);
        // Allow empty string for Options.DefaultName

        var routeGroup = endpoints.MapGroup("");

        // Resolve the named pair from the registry
        var registry = endpoints.ServiceProvider.GetRequiredService<HPDAgentRegistry>();
        var pair = registry.Get(name);

        var sessionManager = pair.SessionManager;
        var agentManager = pair.AgentManager;

        // Map all endpoint groups
        SessionEndpoints.Map(routeGroup, sessionManager);
        BranchEndpoints.Map(routeGroup, sessionManager, agentManager);
        AssetEndpoints.Map(routeGroup, sessionManager);
        StreamingEndpoints.Map(routeGroup, sessionManager, agentManager);
        MiddlewareResponseEndpoints.Map(routeGroup, sessionManager, agentManager);
        AgentEndpoints.Map(routeGroup, agentManager);
        EvalEndpoints.Map(routeGroup);

        return routeGroup;
    }
}
