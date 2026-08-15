using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Model;

namespace HPD.Gateway;

internal static class GatewayInspectionMetadata
{
    internal const string Inspector = "hpd.gateway.inspection.inspector";
    internal const string Mode = "hpd.gateway.inspection.mode";
    internal const string MaximumAccepted = "hpd.gateway.inspection.maximum-accepted";
    internal const string MaximumInspected = "hpd.gateway.inspection.maximum-inspected";
    internal const string MemoryThreshold = "hpd.gateway.inspection.memory-threshold";
    internal const string Spill = "hpd.gateway.inspection.spill";
}

internal sealed class GatewayInspectionMiddleware(RequestDelegate next, GatewayInspectionExecutor executor)
{
    private readonly RequestDelegate _next = next;
    private readonly GatewayInspectionExecutor _executor = executor;

    public Task InvokeAsync(HttpContext context)
    {
        var metadata = context.GetReverseProxyFeature().Route.Config.Metadata;
        if (metadata is null || !metadata.TryGetValue(GatewayInspectionMetadata.Inspector, out var inspector)) return _next(context);
        try
        {
            var selection = new GatewayInspectionSelection(
                inspector,
                Enum.Parse<RequestInspectionMode>(metadata[GatewayInspectionMetadata.Mode], ignoreCase: false),
                long.Parse(metadata[GatewayInspectionMetadata.MaximumAccepted], CultureInfo.InvariantCulture),
                ParseOptionalInt(metadata, GatewayInspectionMetadata.MaximumInspected),
                ParseOptionalInt(metadata, GatewayInspectionMetadata.MemoryThreshold),
                Enum.Parse<RequestInspectionSpillPolicy>(metadata[GatewayInspectionMetadata.Spill], ignoreCase: false));
            return _executor.ExecuteAsync(context, selection, _next);
        }
        catch (Exception)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        }
    }

    private static int? ParseOptionalInt(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : null;
}

internal sealed class HpdInspectionPipelineMarker : IGatewayEndpointMappingParticipant
{
    internal bool IsMapped { get; set; }
    bool IGatewayEndpointMappingParticipant.IsMapped => IsMapped;
    void IGatewayEndpointMappingParticipant.MarkMapped() => IsMapped = true;
}

internal sealed class HpdInspectionPipelineGuard(HpdInspectionPipelineMarker marker) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!marker.IsMapped) throw new InvalidOperationException("HPD request inspection requires MapHpdGatewayReverseProxy so inspection runs before YARP forwarding.");
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal static class GatewayInspectionYarpExtensions
{
    public static IServiceCollection AddHpdGatewayYarpInspection(this IServiceCollection services, Action<GatewayInspectionRegistryBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(GatewayInspectionRegistry)))
            throw new InvalidOperationException("HPD request inspection can be registered only once.");
        services.AddHpdGatewayInspection(configure);
        AddPipelineServices(services);
        return services;
    }

    internal static IServiceCollection AddHpdGatewayYarpInspection(
        this IServiceCollection services,
        GatewayInspectionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registry);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(GatewayInspectionRegistry)))
            throw new InvalidOperationException("HPD request inspection can be registered only once.");
        services.AddSingleton(registry);
        services.AddSingleton<GatewayInspectionExecutor>();
        AddPipelineServices(services);
        return services;
    }

    private static void AddPipelineServices(IServiceCollection services)
    {
        services.AddSingleton<HpdInspectionPipelineMarker>();
        services.AddSingleton<IGatewayEndpointMappingParticipant>(static provider => provider.GetRequiredService<HpdInspectionPipelineMarker>());
        services.AddSingleton<IHostedService, HpdInspectionPipelineGuard>();
    }

    public static ReverseProxyConventionBuilder MapHpdGatewayReverseProxy(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var participants = endpoints.ServiceProvider.GetServices<IGatewayEndpointMappingParticipant>().ToArray();
        if (participants.Length == 0) throw new InvalidOperationException("The HPD reverse-proxy mapping requires at least one installed pipeline participant.");
        if (participants.Any(static participant => participant.IsMapped)) throw new InvalidOperationException("The HPD reverse-proxy pipeline can be mapped only once.");
        foreach (var participant in participants) participant.MarkMapped();
        var inspectionInstalled = endpoints.ServiceProvider.GetService<GatewayInspectionRegistry>() is not null;
        var builder = endpoints.MapReverseProxy(proxy =>
        {
            if (inspectionInstalled) proxy.UseMiddleware<GatewayInspectionMiddleware>();
            proxy.UseSessionAffinity();
            proxy.UseLoadBalancing();
            proxy.UsePassiveHealthChecks();
        });
        if (endpoints.ServiceProvider.GetService<GatewayTrafficAdmissionRegistry>() is { } admission)
            builder.ConfigureEndpoints((endpoint, route) => endpoint.Add(builder =>
            {
                if (route.Metadata is null || !route.Metadata.TryGetValue(GatewayTrafficAdmissionMetadataCodec.Plan, out var encoded)) return;
                if (!route.Metadata.TryGetValue(GatewayRuntimePlanner.ApplicationIdMetadata, out var applicationId) ||
                    !route.Metadata.TryGetValue(GatewayRuntimePlanner.SymbolicPlanIdentityMetadata, out var symbolic) ||
                    !route.Metadata.TryGetValue(GatewayTrafficAdmissionMetadataCodec.PlanIdentity, out var planIdentity))
                    throw new InvalidOperationException("Traffic-admission endpoint metadata is incomplete.");
                var plan = GatewayTrafficAdmissionMetadataCodec.Decode(encoded);
                if (GatewayRuntimePlanner.HashTrafficAdmission(plan).Value != planIdentity)
                    throw new InvalidOperationException("Traffic-admission endpoint plan identity is invalid.");
                foreach (var entry in plan.Entries)
                    if (!admission.TryGet(entry.ProfileName, out _)) throw new InvalidOperationException("Traffic-admission endpoint references an unavailable runtime profile.");
                builder.Metadata.Add(GatewayTrafficAdmissionMetadata.Create(applicationId,
                    new ContentHash("sha-256", symbolic), new RouteId(route.RouteId),
                    new ContentHash("sha-256", planIdentity), plan));
            }));
        return builder;
    }
}
