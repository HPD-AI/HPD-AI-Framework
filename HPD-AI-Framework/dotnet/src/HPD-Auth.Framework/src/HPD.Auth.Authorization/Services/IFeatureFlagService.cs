namespace HPD.Auth.Authorization.Services;

/// <summary>
/// Evaluates whether a feature flag is enabled for a given user context.
/// </summary>
/// <remarks>
/// Consumers of HPD.Auth.Authorization may register their own implementation (e.g.
/// backed by LaunchDarkly, Flagsmith, or a custom database table). When no
/// implementation is registered the <see cref="Handlers.FeatureFlagHandler"/> will
/// throw at resolution time — ensure an implementation is always registered.
/// </remarks>
public interface IFeatureFlagService
{
    /// <summary>
    /// Returns <see langword="true"/> when the feature identified by
    /// <paramref name="featureKey"/> is enabled for the supplied
    /// <paramref name="context"/>; otherwise <see langword="false"/>.
    /// </summary>
    /// <param name="featureKey">The unique feature flag key to evaluate.</param>
    /// <param name="context">Contextual information about the current user.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task<bool> IsEnabledAsync(string featureKey, FeatureContext context, CancellationToken ct = default);
}

/// <summary>
/// Contextual information about the current user, used by
/// <see cref="IFeatureFlagService"/> to make targeting decisions.
/// </summary>
/// <param name="UserId">The subject claim value, or <see langword="null"/> for anonymous users.</param>
/// <param name="SubscriptionTier">The user's current subscription tier (e.g. <c>free</c>, <c>pro</c>, <c>enterprise</c>).</param>
/// <param name="Roles">The list of roles the user belongs to.</param>
public record FeatureContext(string? UserId, string SubscriptionTier, IList<string> Roles);
