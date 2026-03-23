using System.Text;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace HPD.Auth.Authentication.Jwt;

/// <summary>
/// Configures ASP.NET Core JWT Bearer authentication for HPD.Auth.
///
/// <para>
/// Three extra behaviours beyond the defaults:
/// <list type="bullet">
///   <item>
///     <b>Security stamp validation</b> — on every successfully validated token the
///     user's security stamp is re-checked via <see cref="SignInManager{TUser}"/>.
///     This enables instant revocation when an admin updates the stamp (ADR-003 §9.2).
///     Inactive or deleted users are also rejected here.
///   </item>
///   <item>
///     <b>X-Token-Expired header</b> — when a <see cref="SecurityTokenExpiredException"/>
///     is raised during validation, the response includes an <c>X-Token-Expired: true</c>
///     header so clients can distinguish an expired token from an invalid one and
///     proactively invoke the refresh flow.
///   </item>
///   <item>
///     <b>Structured 401 challenge</b> — the default JWT challenge issues a bare
///     <c>WWW-Authenticate</c> header with no body. This override returns a
///     structured JSON error body consistent with the OAuth 2.0 error format used
///     across all HPD.Auth responses (ADR-003 §2).
///   </item>
/// </list>
/// </para>
/// </summary>
internal static class JwtBearerConfigurator
{
    /// <summary>
    /// Applies HPD-specific JWT Bearer settings to <paramref name="opts"/>.
    /// </summary>
    /// <param name="opts">The JWT bearer options to configure.</param>
    /// <param name="config">JWT configuration from <see cref="HPDAuthOptions"/>.</param>
    internal static void Configure(JwtBearerOptions opts, JwtOptions config)
    {
        // ── Token validation parameters ───────────────────────────────────────
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer            = true,
            ValidateAudience          = true,
            ValidateLifetime          = config.ValidateLifetime,
            ValidateIssuerSigningKey  = true,

            ValidIssuer       = config.Issuer,
            ValidAudience     = config.Audience,
            IssuerSigningKey  = new SymmetricSecurityKey(
                                    Encoding.UTF8.GetBytes(config.Secret!)),

            ClockSkew             = config.ClockSkew,
            RequireExpirationTime = true,
            RequireSignedTokens   = true,
        };

        // ── Events ────────────────────────────────────────────────────────────
        opts.Events = new JwtBearerEvents
        {
            // Re-validate security stamp after the token's signature and claims
            // have been confirmed. This is the primary mechanism for instant
            // revocation (ADR-003 §9.2) without a database lookup on every request.
            OnTokenValidated = async context =>
            {
                var signInManager = context.HttpContext.RequestServices
                    .GetRequiredService<SignInManager<ApplicationUser>>();

                var user = await signInManager.ValidateSecurityStampAsync(context.Principal);

                if (user is null)
                {
                    context.Fail("Security stamp validation failed. The token has been revoked.");
                    return;
                }

                if (!user.IsActive || user.IsDeleted)
                {
                    context.Fail("User account is disabled or has been deleted.");
                }
            },

            // Add X-Token-Expired when the failure is specifically an expired token.
            // Clients use this header to trigger a refresh rather than showing an
            // "unauthenticated" error to the user.
            OnAuthenticationFailed = context =>
            {
                if (context.Exception is SecurityTokenExpiredException)
                {
                    context.Response.Headers.Append("X-Token-Expired", "true");
                }

                return Task.CompletedTask;
            },

            // Return a structured JSON 401 instead of the default bare
            // WWW-Authenticate challenge. HandleResponse() prevents the middleware
            // from appending any additional response after this handler runs.
            OnChallenge = context =>
            {
                context.HandleResponse();

                context.Response.StatusCode  = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";

                return context.Response.WriteAsJsonAsync(new
                {
                    error             = "unauthorized",
                    error_description = string.IsNullOrEmpty(context.ErrorDescription)
                        ? "Authentication is required to access this resource."
                        : context.ErrorDescription,
                });
            },
        };
    }
}
