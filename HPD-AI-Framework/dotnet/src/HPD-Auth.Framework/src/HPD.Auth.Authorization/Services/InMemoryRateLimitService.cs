using System.Collections.Concurrent;

namespace HPD.Auth.Authorization.Services;

/// <summary>
/// An in-process, in-memory implementation of <see cref="IRateLimitService"/>
/// intended for <b>development and testing only</b>.
/// </summary>
/// <remarks>
/// <para>
/// State is stored in a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by
/// the rate-limit bucket key. Each entry records the number of requests seen in the
/// current window and the timestamp at which the window started. The window resets
/// (and the counter restarts at 1) whenever the elapsed time since
/// <c>windowStart</c> exceeds <c>window</c>.
/// </para>
/// <para>
/// <b>Limitations:</b> state is not shared across process instances (no horizontal
/// scale-out), not persisted across restarts, and not cleaned up — buckets accumulate
/// indefinitely. Production callers should register a distributed backend
/// (e.g. Redis) by replacing this registration.
/// </para>
/// <para>
/// Thread safety: the implementation uses <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// with <see cref="ConcurrentDictionary{TKey,TValue}.AddOrUpdate"/> to ensure atomic
/// read-modify-write under concurrent load.
/// </para>
/// </remarks>
public sealed class InMemoryRateLimitService : IRateLimitService
{
    // Value: (requestCount, windowStartUtc)
    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _buckets = new();

    /// <inheritdoc />
    public Task<bool> CheckRateLimitAsync(
        string key,
        int maxRequests,
        TimeSpan window,
        CancellationToken ct = default)
    {
        var now    = DateTime.UtcNow;
        var result = false;

        _buckets.AddOrUpdate(
            key,
            // Key does not exist yet — first request in a new bucket.
            addValueFactory: _ =>
            {
                result = true;
                return (1, now);
            },
            // Key exists — check and update atomically.
            updateValueFactory: (_, existing) =>
            {
                var (count, windowStart) = existing;

                if (now - windowStart > window)
                {
                    // Window has expired — reset the counter.
                    result = true;
                    return (1, now);
                }

                if (count < maxRequests)
                {
                    // Still within the window and under the limit.
                    result = true;
                    return (count + 1, windowStart);
                }

                // Limit exceeded.
                result = false;
                return (count, windowStart);
            });

        return Task.FromResult(result);
    }
}
