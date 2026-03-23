using System.Security.Claims;
using HPD.Auth.Authorization.Requirements;
using Microsoft.AspNetCore.Authorization;

namespace HPD.Auth.Authorization.Handlers;

/// <summary>
/// Evaluates <see cref="ResourceOwnerRequirement"/> against an <see cref="IOwnable"/>
/// resource.
/// </summary>
/// <remarks>
/// Evaluation rules:
/// <list type="bullet">
///   <item>Users in the <c>Admin</c> role always succeed regardless of ownership.</item>
///   <item>All other authenticated users succeed only when their subject claim matches
///   <see cref="IOwnable.OwnerId"/>.</item>
///   <item>Unauthenticated callers do not succeed (no explicit failure; the pipeline
///   will issue a 401 challenge).</item>
/// </list>
/// </remarks>
public class ResourceOwnerHandler : AuthorizationHandler<ResourceOwnerRequirement, IOwnable>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceOwnerRequirement requirement,
        IOwnable resource)
    {
        // Admins can access any resource.
        if (context.User.IsInRole("Admin"))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Succeed only when the authenticated user is the owner.
        if (!string.IsNullOrEmpty(userId) &&
            resource.OwnerId.ToString() == userId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
