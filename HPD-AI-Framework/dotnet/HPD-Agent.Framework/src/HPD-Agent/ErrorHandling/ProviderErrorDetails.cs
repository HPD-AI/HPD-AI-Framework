namespace HPD.Agent.ErrorHandling;

/// <summary>
/// Structured error information extracted from provider exceptions.
/// </summary>
public class ProviderErrorDetails
{
    /// <summary>
    /// Error category for determining retry behavior.
    /// </summary>
    public ErrorCategory Category { get; set; } = ErrorCategory.Unknown;

    /// <summary>
    /// HTTP status code if available.
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// Provider-specific error code (e.g., "rate_limit_exceeded", "context_length_exceeded").
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Provider-specific error type (e.g., "insufficient_quota").
    /// </summary>
    public string? ErrorType { get; set; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Retry delay specified by the provider (from Retry-After header or error message).
    /// </summary>
    public TimeSpan? RetryAfter { get; set; }

    /// <summary>
    /// Request ID for debugging (e.g., cf-ray, x-request-id).
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// Raw provider-specific details for advanced scenarios.
    /// </summary>
    public Dictionary<string, object>? RawDetails { get; set; }
}
