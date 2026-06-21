namespace HPD.Auth.Authorization.Services;

/// <summary>
/// Retrieves subscription information for a user.
/// </summary>
/// <remarks>
/// Consumers of HPD.Auth.Authorization must register an implementation of this
/// interface. The <see cref="Handlers.SubscriptionTierHandler"/> calls this service
/// only as a fallback when the <c>subscription_tier</c> JWT/cookie claim is absent
/// or does not satisfy the requirement (stale-claim scenario).
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>
    /// Returns the current subscription for <paramref name="userId"/>, or
    /// <see langword="null"/> if the user has no active subscription record.
    /// </summary>
    /// <param name="userId">The user whose subscription to retrieve.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task<SubscriptionInfo?> GetUserSubscriptionAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>Represents a user's current subscription state.</summary>
/// <param name="Tier">The subscription tier name (e.g. <c>free</c>, <c>pro</c>, <c>enterprise</c>).</param>
/// <param name="ExpiresAt">
/// When the subscription expires, or <see langword="null"/> for subscriptions that do
/// not expire (e.g. lifetime or perpetual plans).
/// </param>
public record SubscriptionInfo(string Tier, DateTime? ExpiresAt);
