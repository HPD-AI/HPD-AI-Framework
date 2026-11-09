using HPD.Agent.AspNetCore.DependencyInjection;
using HPD.Agent.AspNetCore.EndpointMapping.Endpoints;
using HPD.Agent.ClientTools;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Hosting.Serialization;
using HPD.Agent.AspNetCore.Serialization;
using HPD.Agent.AspNetCore.RuntimeExtensions;
using HPD.Agent.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore;

/// <summary>
/// Extension methods for mapping HPD agent host API endpoints.
/// </summary>
public static class HPDAgentHostEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps all HPD agent-host API endpoints for the default host profile.
    /// </summary>
    /// <remarks>
    /// Endpoint behavior is resolved through <see cref="IHPDAgentHostingServicesProvider"/>.
    /// Replace that provider to customize behavior behind built-in routes.
    /// </remarks>
    public static RouteGroupBuilder MapHPDAgentHostApi(
        this IEndpointRouteBuilder endpoints)
        => endpoints.MapHPDAgentHostApi(Options.DefaultName);

    public static RouteGroupBuilder MapHPDAgentHostApi(
        this IEndpointRouteBuilder endpoints,
        Action<HPDAgentEndpointOptions>? configure)
        => endpoints.MapHPDAgentHostApi(Options.DefaultName, configure);

    /// <summary>
    /// Maps all HPD agent-host API endpoints for a named host profile.
    /// The name must match a previous AddHPDAgentHost(name, ...) registration.
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
    /// for both default and named host profiles.
    /// </remarks>
    public static RouteGroupBuilder MapHPDAgentHostApi(
        this IEndpointRouteBuilder endpoints,
        string name)
        => endpoints.MapHPDAgentHostApi(name, null);

    public static RouteGroupBuilder MapHPDAgentHostApi(
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

        MapRuntimeEndpoints(endpoints, routeGroup, name, options);
        if (options.MapClientToolProviders)
            MapClientToolProviderEndpoints(endpoints, routeGroup, ResolveEndpointName(name));
        if (options.MapAgents || options.MapRuntimeExtensionManagement)
            MapAdministrationEndpoints(
                endpoints,
                routeGroup,
                name,
                mapAgentDefinitions: options.MapAgents,
                mapRuntimeExtensionManagement: options.MapRuntimeExtensionManagement);
        options.ConfigureRoutes?.Invoke(routeGroup);

        return routeGroup;
    }

    /// <summary>Maps the application-facing session, thread, streaming, and content API.</summary>
    /// <remarks>The returned group can receive authorization or other endpoint conventions.</remarks>
    public static RouteGroupBuilder MapHPDAgentRuntimeApi(
        this IEndpointRouteBuilder endpoints)
        => endpoints.MapHPDAgentRuntimeApi(Options.DefaultName);

    /// <summary>Maps the application-facing API for a named host profile.</summary>
    public static RouteGroupBuilder MapHPDAgentRuntimeApi(
        this IEndpointRouteBuilder endpoints,
        string name)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(name);

        var routeGroup = endpoints.MapGroup("");
        MapRuntimeEndpoints(endpoints, routeGroup, name, new HPDAgentEndpointOptions());
        return routeGroup;
    }

    /// <summary>Maps connected client-tool provider inspection and attachment routes.</summary>
    public static RouteGroupBuilder MapHPDAgentClientToolProvidersApi(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var routeGroup = endpoints.MapGroup("");
        MapClientToolProviderEndpoints(endpoints, routeGroup, "ClientTools");
        return routeGroup;
    }

    /// <summary>Maps stored-definition and runtime-extension administration routes.</summary>
    /// <remarks>
    /// These routes should normally receive a stricter authorization policy than the runtime API.
    /// </remarks>
    public static RouteGroupBuilder MapHPDAgentAdministrationApi(
        this IEndpointRouteBuilder endpoints)
        => endpoints.MapHPDAgentAdministrationApi(Options.DefaultName);

    /// <summary>Maps administration routes for a named host profile.</summary>
    public static RouteGroupBuilder MapHPDAgentAdministrationApi(
        this IEndpointRouteBuilder endpoints,
        string name)
        => endpoints.MapHPDAgentAdministrationApi(name, null);

    /// <summary>Maps selected administration routes for a named host profile.</summary>
    public static RouteGroupBuilder MapHPDAgentAdministrationApi(
        this IEndpointRouteBuilder endpoints,
        string name,
        Action<HPDAgentAdministrationEndpointOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(name);

        var options = new HPDAgentAdministrationEndpointOptions();
        configure?.Invoke(options);
        var routeGroup = endpoints.MapGroup("");
        MapAdministrationEndpoints(
            endpoints,
            routeGroup,
            name,
            options.MapAgentDefinitions,
            options.MapRuntimeExtensionManagement);
        return routeGroup;
    }

    private static void MapRuntimeEndpoints(
        IEndpointRouteBuilder endpoints,
        RouteGroupBuilder routeGroup,
        string name,
        HPDAgentEndpointOptions options)
    {
        var pair = endpoints.ServiceProvider.GetRequiredService<HPDAgentRegistry>().Get(name);
        var hostingServices = endpoints.ServiceProvider
            .GetRequiredService<IHPDAgentHostingServicesProvider>()
            .Get(name);
        var eventComposition = pair.EventComposition;
        var inputComposition = pair.InputComposition;
        var endpointName = ResolveEndpointName(name);

        routeGroup.MapGet("/event-catalog", () => Results.Json(
            eventComposition.Catalog,
            HPDAgentAspNetCoreJsonSerializerContext.Default.AgentEventCatalog))
            .WithName($"{endpointName}.GetAgentEventCatalog")
            .WithSummary("Get the immutable agent event compatibility catalog");
        routeGroup.MapGet("/input-catalog", () => Results.Json(
            inputComposition.Catalog,
            HPDAgentAspNetCoreJsonSerializerContext.Default.AgentInputCatalog))
            .WithName($"{endpointName}.GetAgentInputCatalog")
            .WithSummary("Get the immutable public and internal agent input compatibility catalog");
        routeGroup.MapGet("/storage", () => Results.Json(
            pair.Storage,
            HPDAgentApiJsonSerializerContext.Default.HPDAgentStorageInfo))
            .WithName($"{endpointName}.GetAgentStorageInfo")
            .WithSummary("Get hosting storage configuration and restart durability capabilities");
        if (options.MapRuntimeExtensions)
        {
            routeGroup.MapGet(
                    "/runtime-extension-catalog",
                    () => Results.Json(
                        endpoints.ServiceProvider.GetService<RuntimeAgentExtensionRegistry>()?.GetCatalog()
                        ?? RuntimeAgentExtensionRegistry.EmptyCatalog,
                        HPDAgentAspNetCoreJsonSerializerContext.Default.RuntimeAgentExtensionCatalog))
                .WithName($"{endpointName}.GetRuntimeAgentExtensionCatalog")
                .WithSummary("Get the current tenant-scoped runtime extension schema catalog");
        }

        // Map all endpoint groups
        if (options.MapSessions)
            SessionEndpoints.Map(routeGroup, hostingServices.Sessions, endpointName);
        if (options.MapThreads)
        {
            ThreadEndpoints.Map(routeGroup, hostingServices.Threads, endpointName);
            ThreadExecutionEndpoints.Map(routeGroup, hostingServices.ThreadExecutions, endpointName);
        }
        if (options.MapContent)
            ContentEndpoints.Map(routeGroup, hostingServices.Content, endpointName);
        if (options.MapStreaming)
            StreamingEndpoints.Map(
                routeGroup,
                hostingServices.Streaming,
                new HPD.Agent.Serialization.AgentInputCodec(
                    endpoints.ServiceProvider.GetRequiredService<HPD.Agent.Providers.ProviderComposition>(),
                    inputComposition),
                endpointName);
        if (options.MapMiddlewareResponses)
            MiddlewareResponseEndpoints.Map(
                routeGroup,
                hostingServices.MiddlewareResponses,
                eventComposition.Codec,
                endpointName);
    }

    private static void MapClientToolProviderEndpoints(
        IEndpointRouteBuilder endpoints,
        RouteGroupBuilder routeGroup,
        string endpointName)
    {
        ClientToolProviderEndpoints.Map(
            routeGroup,
            endpoints.ServiceProvider.GetRequiredService<IClientToolProviderRegistry>(),
            endpointName);
    }

    private static void MapAdministrationEndpoints(
        IEndpointRouteBuilder endpoints,
        RouteGroupBuilder routeGroup,
        string name,
        bool mapAgentDefinitions,
        bool mapRuntimeExtensionManagement)
    {
        if (mapRuntimeExtensionManagement)
            routeGroup.MapRuntimeAgentExtensionManagement();
        if (mapAgentDefinitions)
        {
            var hostingServices = endpoints.ServiceProvider
                .GetRequiredService<IHPDAgentHostingServicesProvider>()
                .Get(name);
            AgentEndpoints.Map(routeGroup, hostingServices.Agents, ResolveEndpointName(name));
        }
    }

    private static string ResolveEndpointName(string name) =>
        string.IsNullOrWhiteSpace(name) ? "DefaultHost" : name;
}
