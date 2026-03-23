using System.Security.Claims;
using HPD.Auth.Authorization.Requirements;
using HPD.Auth.Authorization.Services;
using Microsoft.AspNetCore.Authorization;

namespace HPD.Auth.Authorization.Handlers;

/// <summary>
/// Evaluates <see cref="SubscriptionTierRequirement"/> using a two-phase strategy.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fast path (claims):</b> If the <c>subscription_tier</c> claim is present and
/// its value is in <see cref="SubscriptionTierRequirement.AllowedTiers"/> the
/// requirement succeeds immediately without a database round-trip.
/// </para>
/// <para>
/// <b>Fallback (database):</b> When the claim is absent or stale,
/// <see cref="ISubscriptionService.GetUserSubscriptionAsync"/> is called.
/// The requirement succeeds only when the returned <see cref="SubscriptionInfo"/>
/// is non-null, has an allowed tier, and is not expired
/// (<see cref="SubscriptionInfo.ExpiresAt"/> is <see langword="null"/> or in the future).
/// </para>
/// </remarks>
public class SubscriptionTierHandler : AuthorizationHandler<SubscriptionTierRequirement>
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionTierHandler(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SubscriptionTierRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            // Not authenticated — let the challenge pipeline handle it.
            return;
        }

        // --- Fast path: read from JWT/cookie claim ---
        var tierClaim = context.User.FindFirstValue("subscription_tier");
        if (!string.IsNullOrEmpty(tierClaim) &&
            requirement.AllowedTiers.Contains(tierClaim, StringComparer.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
            return;
        }

        // --- Fallback: query the subscription service (handles stale claims) ---
        var subscription = await _subscriptionService.GetUserSubscriptionAsync(Guid.Parse(userId));

        if (subscription is not null &&
            requirement.AllowedTiers.Contains(subscription.Tier, StringComparer.OrdinalIgnoreCase) &&
            (subscription.ExpiresAt == null || subscription.ExpiresAt > DateTime.UtcNow))
        {
            context.Succeed(requirement);
        }
    }
}
