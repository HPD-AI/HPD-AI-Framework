using HPD.Auth.Admin.Endpoints;
using HPD.Auth.ControlPlane;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.Admin.Extensions;

/// <summary>
/// Extension methods for explicitly secured HPD.Auth Admin endpoints.
/// </summary>
public static class HPDAuthAdminBuilderExtensions
{
    /// <summary>
    /// Maps all HPD.Auth Admin API Minimal API endpoints onto the application.
    /// Call this after <c>app.UseAuthentication()</c> and <c>app.UseAuthorization()</c>.
    /// </summary>
    /// <param name="app">The endpoint route builder (typically <c>WebApplication</c>).</param>
    /// <returns>The same builder for chaining.</returns>
    public static IEndpointRouteBuilder MapHPDAdminEndpoints(
        this IEndpointRouteBuilder app,
        HPDAuthAdminEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var group = app.MapHPDControlPlaneGroup(options.RoutePrefix, options.ControlPlaneProfile);

        AdminUsersEndpoints.Map(group, app);
        AdminUserActionsEndpoints.Map(group, app);
        AdminUserPasswordEndpoints.Map(group, app);
        AdminUserRolesEndpoints.Map(group, app);
        AdminUserClaimsEndpoints.Map(group, app);
        AdminUserLoginsEndpoints.Map(group, app);
        AdminUser2faEndpoints.Map(group, app);
        AdminUserSessionsEndpoints.Map(group, app);
        AdminLinksEndpoints.Map(group, app);
        AdminAuditEndpoints.Map(group, app);

        return app;
    }
}
