using HPD.Agent.AspNetCore.DependencyInjection;
using HPD.Agent.AspNetCore.EndpointMapping.Endpoints;
using HPD.Agent.ClientTools;
using HPD.Agent.Hosting.Lifecycle;
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
    /// <remarks>
    /// Endpoint behavior is resolved through <see cref="IHPDAgentHostingServicesProvider"/>.
    /// Replace that provider to customize behavior behind built-in routes.
    /// </remarks>
    public static RouteGroupBuilder MapHPDAgentApi(
        this IEndpointRouteBuilder endpoints)
        => endpoints.MapHPDAgentApi(Options.DefaultName);

    public static RouteGroupBuilder MapHPDAgentApi(
        this IEndpointRouteBuilder endpoints,
        Action<HPDAgentEndpointOptions>? configure)
        => endpoints.MapHPDAgentApi(Options.DefaultName, configure);

    /// <summary>
    /// Maps all HPD-Agent API endpoints for a named agent.
    /// The name must match a previous AddHPDAgent(name, ...) registration.
    /// </summary>
    /// <returns>RouteGroupBuilder for further customization</returns>
    /// <remarks>
    /// Maps 20+ endpoints:
    /// - Session CRUD (Create, Search/List, Get, Update, Delete)
    /// - Thread CRUD (List, Get, Create, Fork, Delete, Messages, Thread Graph)
    /// - Content management (Upload, Download, List, Delete)
    /// - Committed thread-state snapshots and resumable SSE observation
    /// - Middleware responses (Permissions, Client Tools)
    /// - Agent definition CRUD (Create, List, Get, Update, Delete)
    /// </remarks>
    /// <remarks>
    /// Endpoint behavior is resolved through <see cref="IHPDAgentHostingServicesProvider"/>
    /// for both default and named agents.
    /// </remarks>
    public static RouteGroupBuilder MapHPDAgentApi(
        this IEndpointRouteBuilder endpoints,
        string name)
        => endpoints.MapHPDAgentApi(name, null);

    public static RouteGroupBuilder MapHPDAgentApi(
        this IEndpointRouteBuilder endpoints,
        string name,
        Action<HPDAgentEndpointOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(name);
        // Allow empty string for Options.DefaultName

        var options = new HPDAgentEndpointOptions();
        configure?.Invoke(options);

        var routeGroup = endpoints.MapGroup(options.RoutePrefix);

        var servicesProvider = endpoints.ServiceProvider.GetRequiredService<IHPDAgentHostingServicesProvider>();
        var hostingServices = servicesProvider.Get(name);

        // Map all endpoint groups
        if (options.MapSessions)
            SessionEndpoints.Map(routeGroup, hostingServices.Sessions);
        if (options.MapThreads)
        {
            ThreadEndpoints.Map(routeGroup, hostingServices.Threads);
            ThreadRunEndpoints.Map(routeGroup, hostingServices.ThreadRuns);
        }
        if (options.MapContent)
            ContentEndpoints.Map(routeGroup, hostingServices.Content);
        if (options.MapStreaming)
            StreamingEndpoints.Map(routeGroup, hostingServices.Streaming);
        if (options.MapMiddlewareResponses)
            MiddlewareResponseEndpoints.Map(routeGroup, hostingServices.MiddlewareResponses);
        if (options.MapClientToolProviders)
            ClientToolProviderEndpoints.Map(
                routeGroup,
                endpoints.ServiceProvider.GetRequiredService<IClientToolProviderRegistry>());
        if (options.MapAgents)
            AgentEndpoints.Map(routeGroup, hostingServices.Agents);
        if (options.MapEvals)
            EvalEndpoints.Map(routeGroup);

        options.ConfigureRoutes?.Invoke(routeGroup);

        return routeGroup;
    }
}
