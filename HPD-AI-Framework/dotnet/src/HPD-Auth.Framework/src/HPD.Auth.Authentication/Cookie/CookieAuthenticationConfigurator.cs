using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.Authentication.Cookie;

/// <summary>
/// Configures ASP.NET Core cookie authentication for HPD.Auth.
///
/// <para>
/// This class is intentionally <c>internal static</c> — it is an implementation
/// detail of the <see cref="Extensions.HPDAuthAuthenticationBuilderExtensions"/>
/// registration method and should not be consumed directly.
/// </para>
///
/// <para>
/// Two key behaviours are configured here beyond the defaults:
/// <list type="bullet">
///   <item>
///     Security-stamp validation via <see cref="SignInManager{TUser}.ValidateSecurityStampAsync"/>
///     on every authenticated request, enabling instant force-logout when an admin
///     updates the user's security stamp (ADR-003 §9.2).
///   </item>
///   <item>
///     API-aware redirect suppression: instead of a 302 redirect to the login page,
///     API requests (path starts with /api or Accept: application/json) receive a
///     structured JSON 401/403 response.
///   </item>
/// </list>
/// </para>
/// </summary>
internal static class CookieAuthenticationConfigurator
{
    /// <summary>
    /// Applies HPD-specific cookie authentication settings to <paramref name="opts"/>.
    /// </summary>
    /// <param name="opts">The cookie authentication options to configure.</param>
    /// <param name="config">HPD cookie configuration from <see cref="HPDAuthOptions"/>.</param>
    /// <param name="appName">Application name used for cookie scoping.</param>
    internal static void Configure(
        CookieAuthenticationOptions opts,
        HPDCookieOptions config,
        string appName)
    {
        // ── Cookie properties ─────────────────────────────────────────────────
        // Use the configured cookie name if set; fall back to "{AppName}.Auth".
        opts.Cookie.Name         = !string.IsNullOrEmpty(config.CookieName)
                                       ? config.CookieName
                                       : $"{appName}.Auth";
        opts.Cookie.HttpOnly     = true;
        opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        opts.Cookie.SameSite     = config.SameSite;
        opts.Cookie.IsEssential  = true;

        if (!string.IsNullOrEmpty(config.Domain))
            opts.Cookie.Domain = config.Domain;

        opts.Cookie.Path = config.Path;

        // ── Expiration ────────────────────────────────────────────────────────
        opts.ExpireTimeSpan    = config.SlidingExpiration;     // sliding window duration
        opts.SlidingExpiration = config.UseSlidingExpiration;

        // ── Path configuration ────────────────────────────────────────────────
        opts.LoginPath       = "/auth/login";
        opts.LogoutPath      = "/auth/logout";
        opts.AccessDeniedPath = "/auth/access-denied";

        // ── Events ────────────────────────────────────────────────────────────
        opts.Events = new CookieAuthenticationEvents
        {
            // Validate the security stamp on every authenticated request.
            // If the stamp has changed (password reset, admin force-logout) the
            // principal is rejected and the user is signed out immediately.
            OnValidatePrincipal = async context =>
            {
                var signInManager = context.HttpContext.RequestServices
                    .GetRequiredService<SignInManager<ApplicationUser>>();

                var user = await signInManager.ValidateSecurityStampAsync(context.Principal);
                if (user is null)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme);
                }
            },

            // Return a JSON 401 for API requests instead of a redirect to /auth/login.
            // Non-API requests receive the normal browser redirect.
            OnRedirectToLogin = context =>
            {
                if (IsApiRequest(context.Request))
                {
                    context.Response.StatusCode  = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    return context.Response.WriteAsJsonAsync(new
                    {
                        error             = "unauthorized",
                        error_description = "Authentication is required to access this resource.",
                    });
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },

            // Return a JSON 403 for API requests instead of a redirect to the
            // access-denied page.
            OnRedirectToAccessDenied = context =>
            {
                if (IsApiRequest(context.Request))
                {
                    context.Response.StatusCode  = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    return context.Response.WriteAsJsonAsync(new
                    {
                        error             = "forbidden",
                        error_description = "You do not have permission to access this resource.",
                    });
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when the request is an API call and should not receive
    /// browser-style redirects. A request is considered an API request when:
    /// <list type="bullet">
    ///   <item>The path starts with <c>/api</c>, or</item>
    ///   <item>The <c>Accept</c> header contains <c>application/json</c>.</item>
    /// </list>
    /// </summary>
    private static bool IsApiRequest(HttpRequest request)
    {
        if (request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var value in request.Headers.Accept)
        {
            if (value is not null &&
                value.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
