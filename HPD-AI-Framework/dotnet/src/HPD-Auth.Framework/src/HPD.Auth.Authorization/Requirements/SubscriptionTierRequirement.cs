using Microsoft.AspNetCore.Authorization;

namespace HPD.Auth.Authorization.Requirements;

/// <summary>
/// Requirement: the authenticated user must have at least the specified
/// subscription tier.
/// </summary>
/// <remarks>
/// Tiers are ordered lowest-to-highest: <c>free</c> → <c>pro</c> → <c>enterprise</c>.
/// Specifying <c>pro</c> as the minimum tier will allow both <c>pro</c> and
/// <c>enterprise</c> users through.
/// </remarks>
public class SubscriptionTierRequirement : IAuthorizationRequirement
{
    /// <summary>The lowest tier that satisfies this requirement.</summary>
    public string MinimumTier { get; }

    /// <summary>
    /// All tiers that satisfy this requirement (the minimum tier and every tier
    /// above it in the hierarchy).
    /// </summary>
    public string[] AllowedTiers { get; }

    private static readonly string[] TierOrder = ["free", "pro", "enterprise"];

    /// <param name="minimumTier">
    /// The minimum subscription tier required (case-insensitive).
    /// Recognised values: <c>free</c>, <c>pro</c>, <c>enterprise</c>.
    /// An unrecognised value results in an empty <see cref="AllowedTiers"/> array,
    /// meaning the requirement can never be satisfied.
    /// </param>
    public SubscriptionTierRequirement(string minimumTier)
    {
        MinimumTier = minimumTier;
        var idx = Array.IndexOf(TierOrder, minimumTier.ToLowerInvariant());
        AllowedTiers = idx >= 0 ? TierOrder[idx..] : [];
    }
}
