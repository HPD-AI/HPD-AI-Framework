using Microsoft.AspNetCore.Authorization;

namespace HPD.Auth.Authorization.Requirements;

/// <summary>
/// Requirement: the caller must not exceed the configured request rate limit.
/// </summary>
/// <remarks>
/// The rate-limit key is derived from the authenticated user ID when the user is
/// authenticated, or from the client IP address when the request is anonymous.
/// The endpoint display name is always included in the key so that limits are
/// tracked independently per endpoint.
/// </remarks>
public class RateLimitRequirement : IAuthorizationRequirement
{
    /// <summary>Maximum number of requests allowed within <see cref="Window"/>.</summary>
    public int MaxRequests { get; }

    /// <summary>The rolling time window over which <see cref="MaxRequests"/> is measured.</summary>
    public TimeSpan Window { get; }

    /// <param name="maxRequests">Maximum number of allowed requests.</param>
    /// <param name="window">The time window for the rate limit.</param>
    public RateLimitRequirement(int maxRequests, TimeSpan window)
    {
        MaxRequests = maxRequests;
        Window = window;
    }
}
