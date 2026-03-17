namespace HPD.Agent.ErrorHandling;

/// <summary>
/// Categorizes errors for determining retry behavior.
/// </summary>
public enum ErrorCategory
{
    /// <summary>
    /// Unknown error type - conservative retry approach.
    /// </summary>
    Unknown,

    /// <summary>
    /// Transient network or connectivity error - retry with exponential backoff.
    /// </summary>
    Transient,

    /// <summary>
    /// Rate limit with Retry-After header - retry after specified delay.
    /// </summary>
    RateLimitRetryable,

    /// <summary>
    /// Hard quota exceeded - don't retry, user needs to upgrade or wait.
    /// </summary>
    RateLimitTerminal,

    /// <summary>
    /// Client error (400) - bad request, don't retry.
    /// </summary>
    ClientError,

    /// <summary>
    /// Authentication error (401) - refresh token and retry.
    /// </summary>
    AuthError,

    /// <summary>
    /// Context window exceeded - don't retry, need history reduction.
    /// </summary>
    ContextWindow,

    /// <summary>
    /// Server error (5xx) - retry with exponential backoff.
    /// </summary>
    ServerError,

    /// <summary>
    /// Model not found or invalid model identifier - don't retry, user needs to fix model name.
    /// This includes: model doesn't exist, deployment not found (Azure), model not pulled (Ollama).
    /// </summary>
    ModelNotFound
}
