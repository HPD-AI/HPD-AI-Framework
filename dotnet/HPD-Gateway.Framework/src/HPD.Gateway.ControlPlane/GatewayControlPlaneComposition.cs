using HPD.AI.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Gateway.ControlPlane;

internal sealed class GatewayControlPlaneRegistration
{
    internal bool AuthorityConfigured { get; set; }
    internal GatewayAdminApiOptions? AdminOptions { get; set; }
    internal bool StudioConfigured { get; set; }
    internal bool HpdAuthConfigured { get; set; }

    internal GatewayControlPlaneRegistration Freeze() => new()
    {
        AuthorityConfigured = AuthorityConfigured,
        AdminOptions = AdminOptions?.Snapshot(),
        StudioConfigured = StudioConfigured,
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
        if (registration.StudioConfigured && registration.AdminOptions is { } studioAdmin)
            endpoints.MapHPDAIPlatform().WithHpdGatewayEndpointRole(
                GatewayListenerRole.Management, studioAdmin.EndpointSurfaceId,
                studioAdmin.RequireManagementListener);
        return endpoints;
    }
}
