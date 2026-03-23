using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HPD.Auth.Authorization.Middleware;

/// <summary>
/// Custom <see cref="IAuthorizationMiddlewareResultHandler"/> that produces
/// machine-readable JSON responses for authorization failures instead of the
/// default redirect/challenge behaviour.
/// </summary>
/// <remarks>
/// <para>
/// Response rules:
/// <list type="table">
///   <listheader><term>Outcome</term><description>Behaviour</description></listheader>
///   <item>
///     <term>Succeeded</term>
///     <description>The pipeline continues normally via <c>next(context)</c>.</description>
///   </item>
///   <item>
///     <term>Challenged (not authenticated), API request</term>
///     <description>
///       HTTP 401 with JSON body <c>{ error, message }</c>.
///       A request is considered an API request when the path starts with <c>/api</c>
///       or the <c>Accept</c> header contains <c>application/json</c>.
///     </description>
///   </item>
///   <item>
///     <term>Challenged (not authenticated), non-API request</term>
///     <description>
///       Delegates to <see cref="HttpContext.ChallengeAsync()"/> so that the
///       registered authentication scheme can redirect to a login page.
///     </description>
///   </item>
///   <item>
///     <term>Forbidden (authenticated but not authorized)</term>
///     <description>
///       HTTP 403 with JSON body <c>{ error, message, reasons }</c> where
///       <c>reasons</c> is the list of <see cref="AuthorizationFailureReason"/>
///       messages collected during handler evaluation.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// All failures are logged at <see cref="LogLevel.Warning"/> with the user ID and
/// endpoint name for audit and alerting purposes.
/// </para>
/// </remarks>
public sealed class HPDAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly ILogger<HPDAuthorizationMiddlewareResultHandler> _logger;

    public HPDAuthorizationMiddlewareResultHandler(
        ILogger<HPDAuthorizationMiddlewareResultHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await next(context);
            return;
        }

        // Log failure details for audit / alerting.
        var userId   = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var endpoint = context.GetEndpoint()?.DisplayName;

        _logger.LogWarning(
            "Authorization failed for user {UserId} on endpoint {Endpoint}. " +
            "Challenged: {Challenged}, Forbidden: {Forbidden}",
            userId,
            endpoint,
            authorizeResult.Challenged,
            authorizeResult.Forbidden);

        if (authorizeResult.Challenged)
        {
            // Not authenticated.
            if (IsApiRequest(context))
            {
                context.Response.StatusCode  = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    error   = "unauthorized",
                    message = "Authentication required"
                });
            }
            else
            {
                // Let the authentication scheme handle the challenge (e.g. redirect to login).
                await context.ChallengeAsync();
            }
        }
        else if (authorizeResult.Forbidden)
        {
            // Authenticated but lacking the required permissions.
            context.Response.StatusCode  = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            var failureReasons = authorizeResult.AuthorizationFailure?.FailureReasons
                .Select(r => r.Message)
                .ToList() ?? new List<string>();

            await context.Response.WriteAsJsonAsync(new
            {
                error   = "forbidden",
                message = "You do not have permission to access this resource",
                reasons = failureReasons
            });
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when the request is considered an API call
    /// (path under <c>/api</c> or the <c>Accept</c> header requests JSON).
    /// </summary>
    private static bool IsApiRequest(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments("/api") ||
               context.Request.Headers.Accept.Any(h =>
                   h?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);
    }
}
