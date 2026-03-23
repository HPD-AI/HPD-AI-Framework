using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace HPD.Auth.Authentication.PolicyScheme;

/// <summary>
/// Configures the "HPD" policy scheme that acts as the single default authentication
/// scheme and routes each incoming request to either Cookie or JWT Bearer authentication
/// based on the request's characteristics.
///
/// <para>
/// Routing rules (evaluated in order):
/// <list type="number">
///   <item>
///     Requests with an <c>Authorization: Bearer ...</c> header →
///     <see cref="JwtBearerDefaults.AuthenticationScheme"/>.
///   </item>
///   <item>
///     All other requests (browser sessions) →
///     <see cref="CookieAuthenticationDefaults.AuthenticationScheme"/>.
///   </item>
/// </list>
/// </para>
///
/// <para>
/// By routing at the policy scheme level rather than at the controller/middleware
/// level, standard <c>[Authorize]</c> attributes and the ASP.NET Core authorization
/// pipeline work transparently for both client types without any extra annotations.
/// </para>
/// </summary>
internal static class PolicySchemeConfigurator
{
    /// <summary>
    /// Applies the HPD forward-default selector to <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The policy scheme options to configure.</param>
    internal static void Configure(PolicySchemeOptions options)
    {
        options.ForwardDefaultSelector = context =>
        {
            // Rule 1: Bearer token in Authorization header → JWT.
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) &&
                authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return JwtBearerDefaults.AuthenticationScheme;
            }

            // Rule 2: Default → Cookie (browser sessions).
            return CookieAuthenticationDefaults.AuthenticationScheme;
        };
    }
}
