using Microsoft.AspNetCore.Authorization;

namespace HPD.Auth.Authorization.Requirements;

/// <summary>
/// Requirement: a specific feature flag must be enabled for the current user.
/// </summary>
/// <remarks>
/// The feature flag evaluation is delegated to the registered
/// <c>IFeatureFlagService</c> implementation, which receives a
/// <c>FeatureContext</c> built from the current user's claims.
/// </remarks>
public class FeatureFlagRequirement : IAuthorizationRequirement
{
    /// <summary>The feature flag key to evaluate.</summary>
    public string FeatureKey { get; }

    /// <param name="featureKey">The unique key identifying the feature flag.</param>
    public FeatureFlagRequirement(string featureKey) => FeatureKey = featureKey;
}
