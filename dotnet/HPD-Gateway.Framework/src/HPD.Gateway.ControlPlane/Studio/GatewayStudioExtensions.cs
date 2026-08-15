using HPD.AI.Platform;
using HPD.Gateway;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Gateway.ControlPlane;

public sealed class GatewayStudioEndpointOptions
{
    public string RoutePrefix { get; set; } = "/studio";
    public string ApiBasePath { get; set; } = "/management/gateway/v1";
    public string ProductTitle { get; set; } = "HPD Gateway Studio";
    public string Mode { get; set; } = "development";
    public string EndpointSurfaceId { get; set; } = "gateway-admin-v1";
    public bool RequireManagementListener { get; set; } = true;

    internal GatewayStudioEndpointOptions Snapshot() => new()
    {
        RoutePrefix = RoutePrefix,
        ApiBasePath = ApiBasePath,
        ProductTitle = ProductTitle,
        Mode = Mode,
        EndpointSurfaceId = EndpointSurfaceId,
        RequireManagementListener = RequireManagementListener,
    };
}

internal static class GatewayStudioComposition
{
    internal static HPDAIPlatformBuilder AddGatewayStudioCore(this HPDAIPlatformBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddModule(
            "gateway",
            "Gateway",
            "HPD Gateway Studio",
            "active",
            "gateway");
    }

    internal static RouteGroupBuilder MapGatewayStudioCore(this IEndpointRouteBuilder endpoints)
        => endpoints.MapGatewayStudioCore(static _ => { });

    internal static RouteGroupBuilder MapGatewayStudioCore(
        this IEndpointRouteBuilder endpoints,
        Action<GatewayStudioEndpointOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        HPDAIPlatformOptions registration = endpoints.ServiceProvider
            .GetRequiredService<IOptions<HPDAIPlatformOptions>>().Value;
        if (!registration.Modules.Any(static module => StringComparer.Ordinal.Equals(module.Id, "gateway")))
            throw new InvalidOperationException("Gateway Studio must be registered before it is mapped.");

        var options = new GatewayStudioEndpointOptions();
        configure?.Invoke(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.EndpointSurfaceId);

        RouteGroupBuilder group = endpoints.MapHPDAIPlatform(platform =>
        {
            platform.RoutePrefix = options.RoutePrefix;
            platform.ApiBasePath = options.ApiBasePath;
            platform.ProductTitle = options.ProductTitle;
            platform.Mode = options.Mode;
            platform.SpaRoutes.Add("/gateway");
            platform.SpaRoutes.Add("/gateway/configure");
            platform.SpaRoutes.Add("/gateway/operate");
            platform.SpaRoutes.Add("/gateway/diagnose");
        });
        return group.WithHpdGatewayEndpointRole(
            GatewayListenerRole.Management,
            options.EndpointSurfaceId,
            options.RequireManagementListener);
    }
}
