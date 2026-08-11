using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Gateway.ControlPlane;

internal sealed class GatewayControlPlaneRegistration
{
    internal bool AuthorityConfigured { get; set; }
    internal GatewayAdminApiOptions? AdminOptions { get; set; }
    internal GatewayStudioEndpointOptions? StudioOptions { get; set; }
    internal bool HpdAuthConfigured { get; set; }

    internal GatewayControlPlaneRegistration Freeze() => new()
    {
        AuthorityConfigured = AuthorityConfigured,
        AdminOptions = AdminOptions?.Snapshot(),
        StudioOptions = StudioOptions?.Snapshot(),
        HpdAuthConfigured = HpdAuthConfigured,
    };
}

public static class GatewayControlPlaneEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapHpdGatewayControlPlane(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        GatewayControlPlaneRegistration registration = endpoints.ServiceProvider
            .GetRequiredService<GatewayControlPlaneRegistration>();
        if (registration.AdminOptions is { } admin)
        {
            GatewayAdminEndpointMapper.MapGatewayAdminCore(endpoints, admin);
            endpoints.MapOpenApi()
                .WithHpdGatewayEndpointRole(
                    GatewayListenerRole.Management,
                    admin.EndpointSurfaceId,
                    admin.RequireManagementListener)
                .RequireAuthorization(admin.AuthorizationPolicy);
        }
        if (registration.StudioOptions is { } studio)
            GatewayStudioComposition.MapGatewayStudioCore(endpoints, options =>
            {
                options.RoutePrefix = studio.RoutePrefix;
                options.ApiBasePath = studio.ApiBasePath;
                options.ProductTitle = studio.ProductTitle;
                options.Mode = studio.Mode;
                options.EndpointSurfaceId = studio.EndpointSurfaceId;
                options.RequireManagementListener = studio.RequireManagementListener;
            });
        return endpoints;
    }
}
