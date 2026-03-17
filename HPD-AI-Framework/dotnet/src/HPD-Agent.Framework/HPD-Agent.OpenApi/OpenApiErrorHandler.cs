using HPD.Agent.ErrorHandling;
using HPD.OpenApi.Core;

namespace HPD.Agent.OpenApi;

/// <summary>
/// Classifies <see cref="OpenApiRequestException"/> into the agent's <see cref="ErrorCategory"/> system.
/// Registered automatically when OpenAPI functions are added to the agent.
///
/// Plugs into FunctionRetryMiddleware's 3-tier retry pipeline:
///   1. Custom strategy (if user provided)
///   2. This handler (provider-aware: respects Retry-After, classifies by status code)
///   3. Exponential backoff (fallback)
///
/// Same pattern as AnthropicErrorHandler, OpenAIErrorHandler, etc.
/// </summary>
public sealed class OpenApiErrorHandler : IProviderErrorHandler
{
    public ProviderErrorDetails? ParseError(Exception exception)
    {
        if (exception is not OpenApiRequestException openApiEx) return null;

        var error = openApiEx.ErrorResponse;
        return new ProviderErrorDetails
        {
            StatusCode = error.StatusCode,
            Category = ClassifyStatusCode(error.StatusCode),
            Message = error.UserMessage ?? error.ToString(),
            RetryAfter = error.RetryAfter,
            ErrorCode = error.ReasonPhrase
        };
    }

    public TimeSpan? GetRetryDelay(
        ProviderErrorDetails details,
        int attempt,
        TimeSpan initialDelay,
        double multiplier,
        TimeSpan maxDelay)
    {
        if (details.Category is ErrorCategory.ClientError
            or ErrorCategory.RateLimitTerminal
            or ErrorCategory.ModelNotFound)
            return null;

        // Respect provider-specified Retry-After delay (e.g., from rate limit headers)
        if (details.RetryAfter.HasValue) return details.RetryAfter.Value;

        // Exponential backoff with jitter
        var baseMs = initialDelay.TotalMilliseconds;
        var expDelayMs = baseMs * Math.Pow(multiplier, attempt);
        var cappedDelayMs = Math.Min(expDelayMs, maxDelay.TotalMilliseconds);
        var jitter = 0.9 + (Random.Shared.NextDouble() * 0.2);
        return TimeSpan.FromMilliseconds(cappedDelayMs * jitter);
    }

    public bool RequiresSpecialHandling(ProviderErrorDetails details) =>
        details.Category == ErrorCategory.AuthError;

    private static ErrorCategory ClassifyStatusCode(int statusCode) => statusCode switch
    {
        400 => ErrorCategory.ClientError,
        401 => ErrorCategory.AuthError,
        403 => ErrorCategory.AuthError,
        404 => ErrorCategory.ClientError,
        408 => ErrorCategory.Transient,
        422 => ErrorCategory.ClientError,
        429 => ErrorCategory.RateLimitRetryable,
        >= 500 and < 600 => ErrorCategory.ServerError,
        _ => ErrorCategory.Unknown
    };
}
