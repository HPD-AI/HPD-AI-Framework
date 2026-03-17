namespace HPD.Agent;

/// <summary>
/// State for error tracking. Immutable record.
/// Tracks consecutive failures to detect and handle persistent errors.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b></para>
/// <para>
/// This state is immutable and flows through the context.
/// It is NOT stored in middleware instance fields, preserving thread safety
/// for concurrent RunAsync() calls.
/// </para>
///
/// <para><b>Usage:</b></para>
/// <code>
/// // Read state
/// var errState = context.State.MiddlewareState.ErrorTracking ?? new();
/// if (errState.ConsecutiveFailures >= maxErrors) { ... }
///
/// // Increment on error
/// context.UpdateState(s => s with
/// {
///     MiddlewareState = s.MiddlewareState.WithErrorTracking(errState.IncrementFailures())
/// });
///
/// // Reset on success
/// context.UpdateState(s => s with
/// {
///     MiddlewareState = s.MiddlewareState.WithErrorTracking(errState.ResetFailures())
/// });
/// </code>
/// </remarks>
[MiddlewareState]
public sealed record ErrorTrackingStateData
{

    /// <summary>
    /// Number of consecutive iterations with errors.
    /// Reset to 0 on any successful iteration.
    /// Triggers termination if it reaches MaxConsecutiveErrors.
    /// </summary>
    public int ConsecutiveFailures { get; init; } = 0;

    /// <summary>
    /// Increments the failure count by one.
    /// </summary>
    /// <returns>New state with incremented failure count</returns>
    public ErrorTrackingStateData IncrementFailures() =>
        this with { ConsecutiveFailures = ConsecutiveFailures + 1 };

    /// <summary>
    /// Resets the failure count to zero (on successful iteration).
    /// </summary>
    /// <returns>New state with zero failures</returns>
    public ErrorTrackingStateData ResetFailures() =>
        this with { ConsecutiveFailures = 0 };
}
