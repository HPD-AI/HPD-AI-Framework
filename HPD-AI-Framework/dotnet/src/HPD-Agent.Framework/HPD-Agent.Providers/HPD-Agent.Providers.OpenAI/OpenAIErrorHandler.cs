using System;
using System.Text.RegularExpressions;
using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Providers.OpenAI;

/// <summary>
/// Error handler for OpenAI and Azure OpenAI providers.
/// Handles ClientResultException from the OpenAI .NET SDK and Azure.RequestFailedException.
/// </summary>
internal partial class OpenAIErrorHandler : IProviderErrorHandler
{
    // Source-generated regex patterns for AOT compatibility
    [GeneratedRegex(@"Status:\s*(\d{3})", RegexOptions.IgnoreCase)]
    private static partial Regex StatusPattern();

    [GeneratedRegex(@"\((\d{3})\)")]
    private static partial Regex ParenthesesStatusPattern();

    [GeneratedRegex(@"""code"":\s*""([^""]+)""")]
    private static partial Regex ErrorCodePattern();

    [GeneratedRegex(@"""type"":\s*""([^""]+)""")]
    private static partial Regex ErrorTypePattern();

    [GeneratedRegex(@"try again in ([\d.]+)(s|ms)", RegexOptions.IgnoreCase)]
    private static partial Regex RetryDelayPattern();

    [GeneratedRegex(@"Request[-\s]?Id:\s*([a-zA-Z0-9\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RequestIdPattern();

    public ProviderErrorDetails? ParseError(Exception exception)
    {
        // DEBUG: Log at the very start
        Console.WriteLine($"[DEBUG ParseError ENTRY] Type: {exception.GetType().FullName}");
        Console.WriteLine($"[DEBUG ParseError ENTRY] Msg: {exception.Message}");

        // OpenAI SDK uses System.ClientModel.ClientResultException
        // Azure SDK uses Azure.RequestFailedException
        var exceptionTypeName = exception.GetType().FullName;

        if (exceptionTypeName == "System.ClientModel.ClientResultException" ||
            exceptionTypeName == "Azure.RequestFailedException")
        {
            var message = exception.Message;

            // DEBUG: Log full exception details
            Console.WriteLine($"[DEBUG] Exception Type: {exceptionTypeName}");
            Console.WriteLine($"[DEBUG] Message: {message}");
            Console.WriteLine($"[DEBUG] Full Exception: {exception}");
            if (exception.InnerException != null)
            {
                Console.WriteLine($"[DEBUG] Inner Exception: {exception.InnerException}");
            }

            // Try to get status code from the exception using duck typing (AOT-safe)
            int? status = ExtractStatusCodeFromException(exception);

            // Fallback to message parsing if we couldn't get it from the exception
            status ??= ExtractStatusCodeFromMessage(message);

            // Extract additional error details
            string? errorCode = ExtractErrorCode(message);
            string? errorType = ExtractErrorType(message);
            string? requestId = ExtractRequestId(message);
            TimeSpan? retryAfter = ExtractRetryDelay(message);

            return new ProviderErrorDetails
            {
                StatusCode = status,
                Category = ClassifyError(status, message, errorCode, errorType),
                Message = message,
                ErrorCode = errorCode,
                RequestId = requestId,
                RetryAfter = retryAfter
            };
        }

        return null; // Unknown exception type
    }

    public TimeSpan? GetRetryDelay(ProviderErrorDetails details, int attempt,
        TimeSpan initialDelay, double multiplier, TimeSpan maxDelay)
    {
        // Don't retry terminal errors
        if (details.Category is ErrorCategory.ClientError or
            ErrorCategory.ContextWindow or ErrorCategory.RateLimitTerminal or
            ErrorCategory.AuthError or ErrorCategory.ModelNotFound)
        {
            return null;
        }

        // Priority 1: Use API-specified retry delay
        if (details.RetryAfter.HasValue)
        {
            return details.RetryAfter.Value;
        }

        // Priority 2: Exponential backoff for retryable errors
        if (details.Category is ErrorCategory.RateLimitRetryable or
            ErrorCategory.ServerError or ErrorCategory.Transient)
        {
            var baseMs = initialDelay.TotalMilliseconds;
            var expDelayMs = baseMs * Math.Pow(multiplier, attempt);
            var cappedDelayMs = Math.Min(expDelayMs, maxDelay.TotalMilliseconds);
            var jitter = 0.9 + (Random.Shared.NextDouble() * 0.2); // ±10% jitter

            return TimeSpan.FromMilliseconds(cappedDelayMs * jitter);
        }

        return null;
    }

    public bool RequiresSpecialHandling(ProviderErrorDetails details)
    {
        return details.Category == ErrorCategory.AuthError;
    }

    private static int? ExtractStatusCodeFromException(Exception exception)
    {
        // Try to get Status property using dynamic (AOT-compatible when the type is known at compile time)
        // Both ClientResultException and RequestFailedException have a Status property of type int
        try
        {
            dynamic ex = exception;
            return (int)ex.Status;
        }
        catch
        {
            // If the property doesn't exist or can't be accessed, fall back to message parsing
            return null;
        }
    }

    private static int? ExtractStatusCodeFromMessage(string message)
    {
        // Pattern: "Status: 400" or "Status:400"
        var statusMatch = StatusPattern().Match(message);
        if (statusMatch.Success && int.TryParse(statusMatch.Groups[1].Value, out var statusCode))
        {
            return statusCode;
        }

        // Pattern: "(400)" - sometimes status is in parentheses
        var parenthesesMatch = ParenthesesStatusPattern().Match(message);
        if (parenthesesMatch.Success && int.TryParse(parenthesesMatch.Groups[1].Value, out var parenStatusCode))
        {
            return parenStatusCode;
        }

        return null;
    }

    private static string? ExtractErrorCode(string message)
    {
        // Look for 'code' field in JSON-like structure in the message
        // OpenAI error format: {"error": {"code": "rate_limit_exceeded", "message": "..."}}
        var codeMatch = ErrorCodePattern().Match(message);
        if (codeMatch.Success)
        {
            return codeMatch.Groups[1].Value;
        }

        // Fallback: Look for common error codes in the message text
        if (message.Contains("rate_limit_exceeded", StringComparison.OrdinalIgnoreCase))
            return "rate_limit_exceeded";
        if (message.Contains("context_length_exceeded", StringComparison.OrdinalIgnoreCase))
            return "context_length_exceeded";
        if (message.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase))
            return "insufficient_quota";
        if (message.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase))
            return "invalid_api_key";
        if (message.Contains("model_not_found", StringComparison.OrdinalIgnoreCase))
            return "model_not_found";
        if (message.Contains("content_filter", StringComparison.OrdinalIgnoreCase))
            return "content_filter";

        return null;
    }

    private static string? ExtractErrorType(string message)
    {
        // Look for 'type' field in JSON-like structure
        var typeMatch = ErrorTypePattern().Match(message);
        if (typeMatch.Success)
        {
            return typeMatch.Groups[1].Value;
        }

        return null;
    }

    private static string? ExtractRequestId(string message)
    {
        // Look for pattern "RequestId: xxx" or "Request-Id: xxx"
        var requestIdMatch = RequestIdPattern().Match(message);
        if (requestIdMatch.Success)
        {
            return requestIdMatch.Groups[1].Value;
        }

        return null;
    }

    private static TimeSpan? ExtractRetryDelay(string message)
    {
        // Parse: "Please try again in 1.898s" or "Please try again in 28ms"
        var match = RetryDelayPattern().Match(message);
        if (match.Success && double.TryParse(match.Groups[1].Value, out var value))
        {
            var unit = match.Groups[2].Value;
            return unit == "s"
                ? TimeSpan.FromSeconds(value)
                : TimeSpan.FromMilliseconds(value);
        }

        return null;
    }

    private static ErrorCategory ClassifyError(int? status, string message, string? errorCode, string? errorType)
    {
        // Check for model not found errors first
        if (ModelNotFoundDetector.IsModelNotFoundError(status, message, errorCode, errorType))
        {
            return ErrorCategory.ModelNotFound;
        }

        return status switch
        {
            // Client errors - invalid request
            400 => ClassifyBadRequest(message, errorCode),
            404 => ErrorCategory.ClientError, // Generic not found (model check done above)

            // Authentication/Authorization errors
            401 => ErrorCategory.AuthError, // Unauthorized - invalid API key
            403 => ErrorCategory.AuthError, // Forbidden - insufficient permissions

            // Rate limiting - retryable with backoff
            429 => ClassifyRateLimit(message, errorCode),

            // Server errors - retryable
            500 => ErrorCategory.ServerError, // Internal Server Error
            502 => ErrorCategory.ServerError, // Bad Gateway
            503 => ErrorCategory.Transient,   // Service Unavailable
            504 => ErrorCategory.Transient,   // Gateway Timeout

            // Request timeout
            408 => ErrorCategory.Transient,

            // Other server errors
            >= 500 and < 600 => ErrorCategory.ServerError,

            // Unknown status code
            _ => ClassifyByMessage(message, errorCode, errorType)
        };
    }

    private static ErrorCategory ClassifyBadRequest(string message, string? errorCode)
    {
        // Check for context window exceeded
        if (errorCode == "context_length_exceeded" ||
            message.Contains("context_length_exceeded", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("maximum context length", StringComparison.OrdinalIgnoreCase))
        {
            return ErrorCategory.ContextWindow;
        }

        return ErrorCategory.ClientError;
    }

    private static ErrorCategory ClassifyRateLimit(string message, string? errorCode)
    {
        // Check if it's terminal quota exhaustion
        if (errorCode == "insufficient_quota" ||
            message.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("exceeded your current quota", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("quota has been exceeded", StringComparison.OrdinalIgnoreCase))
        {
            return ErrorCategory.RateLimitTerminal;
        }

        return ErrorCategory.RateLimitRetryable;
    }

    private static ErrorCategory ClassifyByMessage(string message, string? errorCode, string? errorType)
    {
        var lowerMessage = message.ToLowerInvariant();

        // Authentication-related errors
        if (errorCode == "invalid_api_key" ||
            lowerMessage.Contains("unauthorized") ||
            lowerMessage.Contains("authentication") ||
            lowerMessage.Contains("api key") ||
            lowerMessage.Contains("invalid token"))
        {
            return ErrorCategory.AuthError;
        }

        // Rate limiting
        if (errorCode == "rate_limit_exceeded" ||
            lowerMessage.Contains("rate limit") ||
            lowerMessage.Contains("too many requests") ||
            lowerMessage.Contains("throttl"))
        {
            return ClassifyRateLimit(message, errorCode);
        }

        // Transient errors
        if (lowerMessage.Contains("timeout") ||
            lowerMessage.Contains("temporary") ||
            lowerMessage.Contains("unavailable") ||
            lowerMessage.Contains("try again") ||
            lowerMessage.Contains("connection") ||
            lowerMessage.Contains("network"))
        {
            return ErrorCategory.Transient;
        }

        // Client errors
        if (lowerMessage.Contains("invalid") ||
            lowerMessage.Contains("bad request") ||
            lowerMessage.Contains("malformed") ||
            lowerMessage.Contains("validation"))
        {
            return ErrorCategory.ClientError;
        }

        return ErrorCategory.Unknown;
    }
}
