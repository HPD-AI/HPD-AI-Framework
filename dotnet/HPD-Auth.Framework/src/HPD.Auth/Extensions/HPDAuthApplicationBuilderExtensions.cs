using Microsoft.AspNetCore.Builder;

namespace HPD.Auth.Extensions;

/// <summary>
/// Extension methods on <see cref="IApplicationBuilder"/> for configuring the
/// HPD.Auth middleware pipeline.
///
/// Call <see cref="UseHPDAuth"/> in the correct pipeline position, after routing
/// and before endpoint mapping:
///
/// <code>
/// app.UseRouting();
/// app.UseHPDAuth();   // registers UseAuthentication + UseAuthorization in correct order
/// app.MapControllers();
/// </code>
/// </summary>
public static class HPDAuthApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the HPD.Auth middleware to the application's request pipeline.
    ///
    /// This method calls the two ASP.NET Core middleware components in the
    /// required order:
    ///
    /// 1. <see cref="AuthAppBuilderExtensions.UseAuthentication"/> — populates
    ///    <see cref="Microsoft.AspNetCore.Http.HttpContext.User"/> from the incoming
    ///    request's authentication ticket (cookie or JWT Bearer token).
    ///
    /// 2. <see cref="AuthorizationAppBuilderExtensions.UseAuthorization"/> — evaluates
    ///    authorization policies on the resolved endpoint. Must run after authentication
    ///    so that the ClaimsPrincipal is available.
    ///
    /// Correct pipeline position: after <c>app.UseRouting()</c> (so the endpoint is
    /// resolved) and before <c>app.MapControllers()</c> / <c>app.MapHPDAuthEndpoints()</c>.
    /// </summary>
    /// <param name="app">The <see cref="IApplicationBuilder"/> to configure.</param>
    /// <returns>The same <see cref="IApplicationBuilder"/> for further chaining.</returns>
    public static IApplicationBuilder UseHPDAuth(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}
