using HPD.Auth.Builder;
using HPD.Auth.TwoFactor.Endpoints;
using HPD.Auth.TwoFactor.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.TwoFactor.Extensions;

/// <summary>
/// Extension methods for integrating the HPD.Auth Two-Factor Authentication package.
///
/// <para>
/// Usage in Program.cs:
/// <code>
/// // 1. Register services:
/// builder.Services
///     .AddHPDAuth(opts => { ... })
///     .AddAuthentication()
///     .AddTwoFactor();
///
/// // 2. Map endpoints (after UseAuthentication + UseAuthorization):
/// app.MapHPDTwoFactorEndpoints();
/// </code>
/// </para>
///
/// <para>
/// Passkey support requires a registered <c>IPasskeyHandler&lt;ApplicationUser&gt;</c>.
/// Enable it via <see cref="Core.Options.FeaturesOptions.EnablePasskeys"/> and
/// configure <c>IdentityPasskeyOptions</c> with your server domain.
/// </para>
/// </summary>
public static class HPDAuthTwoFactorBuilderExtensions
{
    /// <summary>
    /// Registers two-factor authentication services on the <see cref="IHPDAuthBuilder"/>.
    ///
    /// Currently registers:
    /// <list type="bullet">
    ///   <item><see cref="TwoFactorService"/> — TOTP key formatting and URI generation.</item>
    /// </list>
    ///
    /// Future additions (e.g., SMS OTP, email OTP token providers) will be
    /// registered here without requiring changes to the caller's Program.cs.
    /// </summary>
    /// <param name="builder">The fluent builder returned by <c>AddHPDAuth()</c>.</param>
    /// <returns>The same builder for further chaining.</returns>
    public static IHPDAuthBuilder AddTwoFactor(this IHPDAuthBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<TwoFactorService>();

        return builder;
    }

    /// <summary>
    /// Maps all HPD.Auth Two-Factor Minimal API endpoints onto the application.
    ///
    /// Endpoints registered:
    /// <list type="bullet">
    ///   <item>TOTP factor management: <c>/api/auth/factors</c></item>
    ///   <item>Passkey management: <c>/api/auth/passkey/**</c> and <c>/api/auth/passkeys/**</c></item>
    ///   <item>2FA login completion: <c>/api/auth/2fa/verify</c></item>
    /// </list>
    ///
    /// Call this after <c>app.UseAuthentication()</c> and <c>app.UseAuthorization()</c>
    /// to ensure the authentication middleware runs before endpoint handlers.
    /// </summary>
    /// <param name="app">The endpoint route builder (typically <c>WebApplication</c>).</param>
    /// <returns>The same builder for chaining.</returns>
    public static IEndpointRouteBuilder MapHPDTwoFactorEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        TotpEndpoints.Map(app);
        PasskeyEndpoints.Map(app);
        TwoFactorLoginEndpoints.Map(app);

        return app;
    }
}
