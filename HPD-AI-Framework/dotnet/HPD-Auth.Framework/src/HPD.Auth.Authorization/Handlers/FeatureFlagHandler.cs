using System.Security.Claims;
using HPD.Auth.Authorization.Requirements;
using HPD.Auth.Authorization.Services;
using Microsoft.AspNetCore.Authorization;

namespace HPD.Auth.Authorization.Handlers;

/// <summary>
/// Evaluates <see cref="FeatureFlagRequirement"/> by delegating to
/// <see cref="IFeatureFlagService"/>.
/// </summary>
/// <remarks>
/// A <see cref="FeatureContext"/> is constructed from the current user's claims
/// before being passed to the service, allowing the feature flag system to make
/// targeting decisions based on user ID, subscription tier, and role membership.
/// If the flag is not enabled the handler simply returns without calling
/// <see cref="AuthorizationHandlerContext.Succeed"/> (implicit failure).
/// </remarks>
public class FeatureFlagHandler : AuthorizationHandler<FeatureFlagRequirement>
{
    private readonly IFeatureFlagService _featureFlags;

    public FeatureFlagHandler(IFeatureFlagService featureFlags)
    {
        _featureFlags = featureFlags;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        FeatureFlagRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tier   = context.User.FindFirstValue("subscription_tier") ?? "free";
        var roles  = context.User
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        var featureContext = new FeatureContext(userId, tier, roles);

        var enabled = await _featureFlags.IsEnabledAsync(requirement.FeatureKey, featureContext);

        if (enabled)
        {
            context.Succeed(requirement);
        }
    }
}
