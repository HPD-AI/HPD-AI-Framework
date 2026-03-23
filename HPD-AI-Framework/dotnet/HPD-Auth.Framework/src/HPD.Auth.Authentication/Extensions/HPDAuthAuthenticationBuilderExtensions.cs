using HPD.Auth.Authentication.Cookie;
using HPD.Auth.Authentication.Jwt;
using HPD.Auth.Authentication.PolicyScheme;
using HPD.Auth.Builder;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.Authentication.Extensions;

/// <summary>
/// Extension methods on <see cref="IHPDAuthBuilder"/> for registering the
/// HPD authentication stack (Cookie + JWT Bearer + PolicyScheme router + TokenService).
///
/// <para>
/// Usage — chain after <c>AddHPDAuth()</c> in <c>Program.cs</c>:
/// <code>
/// services.AddHPDAuth(options => { ... })
///         .AddAuthentication();
/// </code>
/// </para>
///
/// <para>
/// When <see cref="Core.Options.JwtOptions.Secret"/> is null or empty the extension
/// operates in <b>cookie-only mode</b>: only cookie authentication is registered and
/// the policy scheme / JWT Bearer stacks are omitted. This is appropriate for
/// server-rendered web applications that have no native-app or API clients.
/// </para>
/// </summary>
public static class HPDAuthAuthenticationBuilderExtensions
{
    /// <summary>
    /// Registers cookie authentication, optionally JWT Bearer authentication and the
    /// "HPD" policy scheme router, and the <see cref="ITokenService"/> implementation.
    ///
    /// <para>
    /// JWT support is enabled only when <c>options.Jwt.Secret</c> is non-null and
    /// non-empty. When JWT is enabled the following schemes are registered:
    /// <list type="bullet">
    ///   <item><c>"HPD"</c> — PolicyScheme router (default authenticate/challenge scheme)</item>
    ///   <item><c>CookieAuthenticationDefaults.AuthenticationScheme</c> ("Cookies")</item>
    ///   <item><c>JwtBearerDefaults.AuthenticationScheme</c> ("Bearer")</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="builder">The fluent builder returned by <c>AddHPDAuth()</c>.</param>
    /// <returns>The same <paramref name="builder"/> for further chaining.</returns>
    public static IHPDAuthBuilder AddAuthentication(this IHPDAuthBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options  = builder.Options;
        var services = builder.Services;

        var jwtKeyConfigured = !string.IsNullOrEmpty(options.Jwt.Secret);

        if (jwtKeyConfigured)
        {
            // Full dual-auth stack: PolicyScheme → Cookie | JwtBearer
            services
                .AddAuthentication(authOptions =>
                {
                    // All defaults point at the "HPD" policy scheme, which then
                    // forwards to Cookie or JWT based on the request.
                    authOptions.DefaultScheme            = "HPD";
                    authOptions.DefaultChallengeScheme   = "HPD";
                    authOptions.DefaultAuthenticateScheme = "HPD";

                    // Sign-in always goes to Cookie so that SignInManager works
                    // transparently without extra overloads.
                    authOptions.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                })
                .AddPolicyScheme("HPD", "HPD Authentication", policyOptions =>
                    PolicySchemeConfigurator.Configure(policyOptions))
                .AddCookie(cookieOptions =>
                    CookieAuthenticationConfigurator.Configure(cookieOptions, options.Cookie, options.AppName))
                .AddJwtBearer(jwtOptions =>
                    JwtBearerConfigurator.Configure(jwtOptions, options.Jwt));
        }
        else
        {
            // Cookie-only mode — no JWT key is configured.
            // Suitable for server-rendered web apps without native/API clients.
            services
                .AddAuthentication(authOptions =>
                {
                    authOptions.DefaultScheme            = CookieAuthenticationDefaults.AuthenticationScheme;
                    authOptions.DefaultChallengeScheme   = CookieAuthenticationDefaults.AuthenticationScheme;
                    authOptions.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                })
                .AddCookie(cookieOptions =>
                    CookieAuthenticationConfigurator.Configure(cookieOptions, options.Cookie, options.AppName));
        }

        // ITokenService is always registered. In cookie-only mode the service
        // generates no access token (empty string) but still handles refresh token
        // persistence, which can be useful for future JWT enablement without
        // code changes in consuming services.
        services.AddScoped<ITokenService, TokenService>();

        return builder;
    }
}
