using System.Security.Claims;
using HPD.Auth.Authorization.Requirements;
using HPD.Auth.Authorization.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HPD.Auth.Authorization.Handlers;

/// <summary>
/// Evaluates <see cref="RateLimitRequirement"/> by delegating counter tracking to
/// <see cref="IRateLimitService"/>.
/// </summary>
/// <remarks>
/// Rate-limit key format:
/// <list type="bullet">
///   <item>Authenticated: <c>user:{userId}:{endpoint}</c></item>
///   <item>Anonymous:     <c>ip:{remoteIp}:{endpoint}</c></item>
/// </list>
/// The endpoint display name is included in the key so that limits are tracked
/// independently per endpoint, preventing a busy endpoint from starving others.
/// </remarks>
public class RateLimitHandler : AuthorizationHandler<RateLimitRequirement>
{
    private readonly IRateLimitService _rateLimitService;

    public RateLimitHandler(IRateLimitService rateLimitService)
    {
        _rateLimitService = rateLimitService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RateLimitRequirement requirement)
    {
        var userId   = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var endpoint = (context.Resource as HttpContext)?.GetEndpoint()?.DisplayName ?? "unknown";

        var key = !string.IsNullOrEmpty(userId)
            ? $"user:{userId}:{endpoint}"
            : $"ip:{GetClientIp(context)}:{endpoint}";

        var allowed = await _rateLimitService.CheckRateLimitAsync(
            key, requirement.MaxRequests, requirement.Window);

        if (allowed)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, "Rate limit exceeded"));
        }
    }

    private static string GetClientIp(AuthorizationHandlerContext context)
    {
        if (context.Resource is HttpContext httpContext)
        {
            return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        return "unknown";
    }
}
