namespace HPD.Auth.Authorization.Services;

/// <summary>
/// Tracks request counts and enforces rate limits.
/// </summary>
/// <remarks>
/// The default development-time implementation is
/// <see cref="InMemoryRateLimitService"/>. For production workloads register a
/// distributed implementation (e.g. Redis-backed) by calling
/// <c>services.AddSingleton&lt;IRateLimitService, YourRedisRateLimitService&gt;()</c>
/// <b>after</b> <c>AddHPDAuth().AddAuthorization()</c>. The later registration will
/// override the in-memory default due to the way .NET DI resolves singletons when
/// multiple registrations exist (last one wins for <c>GetService&lt;T&gt;</c>).
/// </remarks>
public interface IRateLimitService
{
    /// <summary>
    /// Checks whether the caller identified by <paramref name="key"/> is within the
    /// allowed rate limit and, if so, increments the request counter.
    /// </summary>
    /// <param name="key">
    /// A unique string that identifies the rate-limit bucket
    /// (e.g. <c>user:{userId}:{endpoint}</c> or <c>ip:{ip}:{endpoint}</c>).
    /// </param>
    /// <param name="maxRequests">Maximum requests allowed within <paramref name="window"/>.</param>
    /// <param name="window">The rolling time window.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the request is within the limit;
    /// <see langword="false"/> when the limit has been exceeded.
    /// </returns>
    Task<bool> CheckRateLimitAsync(string key, int maxRequests, TimeSpan window, CancellationToken ct = default);
}
