using System.Security.Claims;
using HPD.Auth.Authorization.Requirements;
using HPD.Auth.Authorization.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.Authorization.Handlers;

/// <summary>
/// Evaluates <see cref="AppAccessRequirement"/> by querying
/// <see cref="IAppPermissionService"/> for the current user.
/// </summary>
/// <remarks>
/// The app ID is resolved with the following priority:
/// <list type="number">
///   <item>The <see cref="AppAccessRequirement.AppId"/> property on the requirement (static/explicit).</item>
///   <item>The <c>appId</c> route value on the current <see cref="HttpContext"/> (dynamic, per-request).</item>
/// </list>
/// If no user ID is present the handler returns without calling
/// <see cref="AuthorizationHandlerContext.Succeed"/> (the request is unauthenticated and
/// will be challenged rather than forbidden).
/// </remarks>
public class AppAccessHandler : AuthorizationHandler<AppAccessRequirement>
{
    private readonly IAppPermissionService _permissionService;

    public AppAccessHandler(IAppPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AppAccessRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            // Not authenticated — let the challenge pipeline handle it.
            return;
        }

        // Prefer the app ID baked into the requirement; fall back to route.
        var appId = requirement.AppId;
        if (string.IsNullOrEmpty(appId) && context.Resource is HttpContext httpContext)
        {
            appId = httpContext.GetRouteValue("appId")?.ToString();
        }

        if (string.IsNullOrEmpty(appId))
        {
            context.Fail(new AuthorizationFailureReason(this, "App ID not specified"));
            return;
        }

        var hasAccess = await _permissionService.UserHasAppAccessAsync(
            Guid.Parse(userId), appId);

        if (hasAccess)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, "User does not have access to this app"));
        }
    }
}
