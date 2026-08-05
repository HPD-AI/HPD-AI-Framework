using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HPD.Gateway.Status;

public static class GatewayStatusExtensions
{
    public static IServiceCollection AddHpdGatewayStatus(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(static provider => new GatewayStatusCoordinator(
            provider.GetServices<HPD.Gateway.Yarp.IGatewayPublicationObservationReader>(),
            provider.GetRequiredService<global::Yarp.ReverseProxy.IProxyStateLookup>(),
            provider.GetServices<HPD.Gateway.Hosting.GatewayHostRuntimeStatus>(),
            provider.GetRequiredService<IHostApplicationLifetime>()));
        services.AddSingleton<IGatewayStatusReader>(static provider => provider.GetRequiredService<GatewayStatusCoordinator>());
        services.AddSingleton<IHostedService>(static provider => provider.GetRequiredService<GatewayStatusCoordinator>());
        return services;
    }

    public static IEndpointRouteBuilder MapHpdGatewayHealth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/health/live", (RequestDelegate)WriteLivenessAsync);
        endpoints.MapGet("/health/ready", (RequestDelegate)WriteReadinessAsync);
        return endpoints;
    }

    private static Task WriteLivenessAsync(HttpContext context)
    {
        var lifetime = context.RequestServices.GetRequiredService<IHostApplicationLifetime>();
        context.Response.StatusCode = lifetime.ApplicationStopping.IsCancellationRequested
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        return Task.CompletedTask;
    }

    private static Task WriteReadinessAsync(HttpContext context)
    {
        var snapshot = context.RequestServices.GetRequiredService<IGatewayStatusReader>().GetCurrent();
        var response = new GatewayReadinessResponse(
            "hpd.gateway.readiness/v1",
            snapshot.Readiness.Serving == GatewayReadinessState.Ready,
            snapshot.SnapshotSequence,
            snapshot.GeneratedAt,
            snapshot.Readiness.Reasons.Select(static reason => reason.Code).ToImmutableArray());
        context.Response.StatusCode = response.Ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json; charset=utf-8";
        return JsonSerializer.SerializeAsync(context.Response.Body, response, GatewayStatusJsonContext.Default.GatewayReadinessResponse, context.RequestAborted);
    }
}
