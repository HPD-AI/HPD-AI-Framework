using HPD.Auth.Endpoints;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.Extensions;

/// <summary>
/// Extension method on <see cref="IEndpointRouteBuilder"/> that maps all core
/// HPD.Auth endpoints in one call.
///
/// <para>
/// Usage — call after <c>app.UseHPDAuth()</c>:
/// <code>
/// app.UseHPDAuth();
/// app.MapHPDAuthEndpoints();
///
/// // Optionally map additional feature endpoints from the sub-packages:
/// app.MapHPDAdminEndpoints();       // from HPD.Auth.Admin
/// app.MapHPDTwoFactorEndpoints();   // from HPD.Auth.TwoFactor
/// app.MapHPDOAuthEndpoints();       // from HPD.Auth.OAuth
/// </code>
/// </para>
///
/// <para>
/// Core endpoints registered by this method:
/// <list type="bullet">
///   <item>POST /api/auth/signup</item>
///   <item>POST /api/auth/token  (OAuth 2.0 — grant_type=password|refresh_token)</item>
///   <item>POST /api/auth/logout</item>
///   <item>GET  /api/auth/user</item>
///   <item>PUT  /api/auth/user</item>
///   <item>POST /api/auth/recover</item>
///   <item>POST /api/auth/verify</item>
///   <item>POST /api/auth/resend</item>
///   <item>GET  /api/auth/sessions</item>
///   <item>DELETE /api/auth/sessions/{id}</item>
///   <item>DELETE /api/auth/sessions</item>
/// </list>
/// </para>
///
/// <para>
/// 2FA, OAuth, and Admin endpoints are owned by their respective sub-packages
/// and are registered via <c>MapHPDTwoFactorEndpoints()</c>,
/// <c>MapHPDOAuthEndpoints()</c>, and <c>MapHPDAdminEndpoints()</c>.
/// </para>
/// </summary>
public static class HPDAuthEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps all core HPD.Auth Minimal API endpoints.
    /// </summary>
    /// <param name="app">The endpoint route builder (typically <c>WebApplication</c>).</param>
    /// <returns>The same <paramref name="app"/> for further chaining.</returns>
    public static IEndpointRouteBuilder MapHPDAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Core authentication endpoints (signup, logout, get/update user).
        AuthEndpoints.Map(app);

        // OAuth 2.0 token endpoint (password grant + refresh_token grant).
        TokenEndpoints.Map(app);

        // Password recovery, OTP verification, and resend.
        PasswordEndpoints.Map(app);

        // Session listing and revocation.
        SessionEndpoints.Map(app);

        return app;
    }
}
