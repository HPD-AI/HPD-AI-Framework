namespace HPD.Agent;

/// <summary>Represents one absolute invocation deadline that can cross nested or remote hops.</summary>
/// <remarks>Unlike a duration, an absolute deadline cannot be restarted by a downstream hop.</remarks>
public sealed record AgentInvocationDeadline
{
    /// <summary>Gets the UTC instant after which the invocation must stop.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Creates a deadline from a duration measured once at the current boundary.</summary>
    /// <param name="timeout">The positive time available to the complete invocation.</param>
    /// <returns>The resulting absolute deadline.</returns>
    public static AgentInvocationDeadline FromTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The invocation timeout must be positive.");
        return new AgentInvocationDeadline { ExpiresAt = DateTimeOffset.UtcNow.Add(timeout) };
    }

    /// <summary>Gets whether the deadline has expired at the supplied clock value.</summary>
    /// <param name="now">The current UTC clock value.</param>
    /// <returns><see langword="true"/> when no time remains.</returns>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Gets the non-negative remaining duration at the supplied clock value.</summary>
    /// <param name="now">The current UTC clock value.</param>
    /// <returns>The remaining duration, or zero after expiry.</returns>
    public TimeSpan GetRemaining(DateTimeOffset now)
    {
        var remaining = ExpiresAt - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
