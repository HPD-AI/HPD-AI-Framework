namespace HPD.Auth.Authorization.Services;

internal sealed class DenyAllAppPermissionService : IAppPermissionService
{
    public Task<bool> UserHasAppAccessAsync(Guid userId, string appId, CancellationToken ct = default)
        => Task.FromResult(false);
}

internal sealed class EmptySubscriptionService : ISubscriptionService
{
    public Task<SubscriptionInfo?> GetUserSubscriptionAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<SubscriptionInfo?>(null);
}

internal sealed class DisabledFeatureFlagService : IFeatureFlagService
{
    public Task<bool> IsEnabledAsync(string featureKey, FeatureContext context, CancellationToken ct = default)
        => Task.FromResult(false);
}
