namespace HPD.Agent.ErrorHandling;

/// <summary>
/// Interface for provider-specific error parsing and retry logic.
/// </summary>
public interface IProviderErrorHandler
{
    /// <summary>
    /// Parse provider-specific error details from an exception.
    /// </summary>
    /// <param name="exception">The exception thrown by the provider.</param>
    /// <returns>Structured error details, or null if exception format is not recognized.</returns>
    ProviderErrorDetails? ParseError(Exception exception);

    /// <summary>
    /// Calculate retry delay for this error based on attempt number.
    /// </summary>
    /// <param name="details">Structured error details.</param>
    /// <param name="attempt">Current attempt number (0-based).</param>
    /// <param name="initialDelay">Initial retry delay from configuration.</param>
    /// <param name="multiplier">Backoff multiplier from configuration.</param>
    /// <param name="maxDelay">Maximum retry delay cap from configuration.</param>
    /// <returns>Retry delay, or null if error should not be retried.</returns>
    TimeSpan? GetRetryDelay(
        ProviderErrorDetails details,
        int attempt,
        TimeSpan initialDelay,
        double multiplier,
        TimeSpan maxDelay);

    /// <summary>
    /// Check if error requires special handling (e.g., token refresh, model fallback).
    /// </summary>
    /// <param name="details">Structured error details.</param>
    /// <returns>True if special handling is needed.</returns>
    bool RequiresSpecialHandling(ProviderErrorDetails details);
}
