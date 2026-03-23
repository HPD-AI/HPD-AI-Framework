using HPD.Auth.Admin.Endpoints;
using HPD.Auth.Builder;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.Admin.Extensions;

/// <summary>
/// Extension methods for integrating the HPD.Auth Admin API package.
///
/// Usage in Program.cs:
/// <code>
/// // 1. Register services:
/// builder.Services
///     .AddHPDAuth(opts => { ... })
///     .AddAdmin();                   // no-op today; reserved for future admin-specific services
///
/// // 2. Map endpoints:
/// app.MapHPDAdminEndpoints();
/// </code>
///
/// Authorization policy "RequireAdmin" must be registered separately, e.g.:
/// <code>
/// builder.Services.AddAuthorization(opts =>
/// {
///     opts.AddPolicy("RequireAdmin", p => p.RequireRole("Admin"));
/// });
/// </code>
/// </summary>
public static class HPDAuthAdminBuilderExtensions
{
    /// <summary>
    /// Registers admin-specific services on the <see cref="IHPDAuthBuilder"/>.
    /// Currently a no-op reservation point — admin endpoints themselves are
    /// registered via <see cref="MapHPDAdminEndpoints"/>.
    /// </summary>
    /// <param name="builder">The HPD Auth fluent builder returned by <c>AddHPDAuth()</c>.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IHPDAuthBuilder AddAdmin(this IHPDAuthBuilder builder)
    {
        // Admin endpoints use services already registered by AddHPDAuth()
        // (UserManager, IAuditLogger, ISessionManager).
        // If future versions add admin-specific services (e.g., an impersonation
        // service), register them here via builder.Services.AddScoped<...>().
        return builder;
    }

    /// <summary>
    /// Maps all HPD.Auth Admin API Minimal API endpoints onto the application.
    /// Call this after <c>app.UseAuthentication()</c> and <c>app.UseAuthorization()</c>.
    /// </summary>
    /// <param name="app">The endpoint route builder (typically <c>WebApplication</c>).</param>
    /// <returns>The same builder for chaining.</returns>
    public static IEndpointRouteBuilder MapHPDAdminEndpoints(this IEndpointRouteBuilder app)
    {
        AdminUsersEndpoints.Map(app);
        AdminUserActionsEndpoints.Map(app);
        AdminUserPasswordEndpoints.Map(app);
        AdminUserRolesEndpoints.Map(app);
        AdminUserClaimsEndpoints.Map(app);
        AdminUserLoginsEndpoints.Map(app);
        AdminUser2faEndpoints.Map(app);
        AdminUserSessionsEndpoints.Map(app);
        AdminLinksEndpoints.Map(app);
        AdminAuditEndpoints.Map(app);

        return app;
    }
}
