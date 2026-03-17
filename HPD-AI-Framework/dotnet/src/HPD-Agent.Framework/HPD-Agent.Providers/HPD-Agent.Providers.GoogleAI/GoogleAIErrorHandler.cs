using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Providers.GoogleAI;

/// <summary>
/// Error handler for Google AI (Gemini) provider.
/// Handles Google-specific exceptions including GenerativeAIException, ApiException, VertexAIException, and FileTooLargeException.
/// </summary>
internal partial class GoogleAIErrorHandler : IProviderErrorHandler
{
    // Source-generated regex patterns for AOT compatibility
    [GeneratedRegex(@"Status:\s*(\d{3})", RegexOptions.IgnoreCase)]
    private static partial Regex StatusPattern();

    [GeneratedRegex(@"\((\d{3})\)")]
    private static partial Regex ParenthesesStatusPattern();

    [GeneratedRegex(@"""code"":\s*(\d+)")]
    private static partial Regex ErrorCodePattern();

    [GeneratedRegex(@"""status"":\s*""([^""]+)""")]
    private static partial Regex ErrorStatusPattern();

    public ProviderErrorDetails? ParseError(Exception exception)
    {
        // Handle Google AI specific exceptions
        var exceptionTypeName = exception.GetType().FullName;

        // ApiException - from GenerativeAI.Exceptions namespace
        if (exceptionTypeName == "GenerativeAI.Exceptions.ApiException")
        {
            return ParseApiException(exception);
        }

        // GenerativeAIException - from GenerativeAI.Exceptions namespace
        if (exceptionTypeName == "GenerativeAI.Exceptions.GenerativeAIException")
        {
            return ParseGenerativeAIException(exception);
        }

        // VertexAIException - from GenerativeAI.Exceptions namespace
        if (exceptionTypeName == "GenerativeAI.Exceptions.VertexAIException")
        {
            return ParseVertexAIException(exception);
        }

        // FileTooLargeException - from GenerativeAI.Exceptions namespace
        if (exceptionTypeName == "GenerativeAI.Exceptions.FileTooLargeException")
        {
            return new ProviderErrorDetails
            {
                StatusCode = 413,
                Category = ErrorCategory.ClientError,
                Message = exception.Message,
                ErrorCode = "FILE_TOO_LARGE"
            };
        }

        // HttpRequestException - fallback for HTTP errors
        if (exception is HttpRequestException httpEx)
        {
            var statusCode = (int?)httpEx.StatusCode;
            return new ProviderErrorDetails
            {
                StatusCode = statusCode,
                Category = ClassifyError(statusCode, httpEx.Message),
                Message = httpEx.Message,
                ErrorCode = ExtractErrorCode(httpEx.Message)
            };
        }

        return null;
    }

    private static ProviderErrorDetails ParseApiException(Exception exception)
    {
        // Use duck typing to access ApiException properties (AOT-safe)
        try
        {
            dynamic apiEx = exception;
            int errorCode = apiEx.ErrorCode;
            string errorMessage = apiEx.ErrorMessage ?? exception.Message;
            string errorStatus = apiEx.ErrorStatus ?? "Unknown";

            return new ProviderErrorDetails
            {
                StatusCode = errorCode,
                Category = ClassifyError(errorCode, errorMessage),
                Message = errorMessage,
                ErrorCode = errorStatus
            };
        }
        catch
        {
            // Fallback to message parsing
            return new ProviderErrorDetails
            {
                StatusCode = ExtractStatusCodeFromMessage(exception.Message),
                Category = ClassifyError(null, exception.Message),
                Message = exception.Message,
                ErrorCode = ExtractErrorCode(exception.Message)
            };
        }
    }

    private static ProviderErrorDetails ParseGenerativeAIException(Exception exception)
    {
        // Use duck typing to access GenerativeAIException.Details property (AOT-safe)
        try
        {
            dynamic genAiEx = exception;
            string details = genAiEx.Details ?? "";

            return new ProviderErrorDetails
            {
                StatusCode = ExtractStatusCodeFromMessage(exception.Message + " " + details),
                Category = ClassifyError(null, exception.Message + " " + details),
                Message = exception.Message,
                ErrorCode = ExtractErrorCode(exception.Message + " " + details)
            };
        }
        catch
        {
            return new ProviderErrorDetails
            {
                StatusCode = ExtractStatusCodeFromMessage(exception.Message),
                Category = ClassifyError(null, exception.Message),
                Message = exception.Message,
                ErrorCode = ExtractErrorCode(exception.Message)
            };
        }
    }

    private static ProviderErrorDetails ParseVertexAIException(Exception exception)
    {
        // VertexAIException contains GoogleRpcStatus with detailed error info
        try
        {
            dynamic vertexEx = exception;
            var status = vertexEx.Status;
            int? code = status?.Code;
            string? message = status?.Message ?? exception.Message;

            return new ProviderErrorDetails
            {
                StatusCode = code,
                Category = ClassifyError(code, message ?? exception.Message),
                Message = message ?? exception.Message,
                ErrorCode = ExtractErrorCode(message ?? exception.Message)
            };
        }
        catch
        {
            return new ProviderErrorDetails
            {
                StatusCode = ExtractStatusCodeFromMessage(exception.Message),
                Category = ClassifyError(null, exception.Message),
                Message = exception.Message,
                ErrorCode = ExtractErrorCode(exception.Message)
            };
        }
    }

    public TimeSpan? GetRetryDelay(ProviderErrorDetails details, int attempt, TimeSpan initialDelay, double multiplier, TimeSpan maxDelay)
    {
        if (details.Category is ErrorCategory.RateLimitRetryable or ErrorCategory.ServerError or ErrorCategory.Transient)
        {
            var baseMs = initialDelay.TotalMilliseconds;
            var expDelayMs = baseMs * Math.Pow(multiplier, attempt);
            return TimeSpan.FromMilliseconds(Math.Min(expDelayMs, maxDelay.TotalMilliseconds));
        }
        return null;
    }

    public bool RequiresSpecialHandling(ProviderErrorDetails details)
    {
        return details.Category == ErrorCategory.AuthError;
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

        // Pattern: "code": 400 in JSON-like structure
        var codeMatch = ErrorCodePattern().Match(message);
        if (codeMatch.Success && int.TryParse(codeMatch.Groups[1].Value, out var jsonCode))
        {
            return jsonCode;
        }

        return null;
    }

    private static string? ExtractErrorCode(string message)
    {
        // Look for specific Google AI error codes
        if (message.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase))
            return "API_KEY_INVALID";
        if (message.Contains("QUOTA_EXCEEDED", StringComparison.OrdinalIgnoreCase))
            return "QUOTA_EXCEEDED";
        if (message.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase))
            return "RESOURCE_EXHAUSTED";
        if (message.Contains("INVALID_ARGUMENT", StringComparison.OrdinalIgnoreCase))
            return "INVALID_ARGUMENT";
        if (message.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase))
            return "PERMISSION_DENIED";
        if (message.Contains("UNAUTHENTICATED", StringComparison.OrdinalIgnoreCase))
            return "UNAUTHENTICATED";
        if (message.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
            return "NOT_FOUND";
        if (message.Contains("SAFETY", StringComparison.OrdinalIgnoreCase))
            return "SAFETY";
        if (message.Contains("RECITATION", StringComparison.OrdinalIgnoreCase))
            return "RECITATION";
        if (message.Contains("BLOCKED", StringComparison.OrdinalIgnoreCase))
            return "BLOCKED";

        // Look for a 'status' field in JSON-like structure
        var statusMatch = ErrorStatusPattern().Match(message);
        if (statusMatch.Success)
        {
            return statusMatch.Groups[1].Value;
        }

        return null;
    }

    private static ErrorCategory ClassifyError(int? status, string message)
    {
        // Check for model not found errors first (Gemini: "is not found for API version")
        var errorCode = ExtractErrorCode(message);
        if (ModelNotFoundDetector.IsModelNotFoundError(status, message, errorCode, errorType: null))
        {
            return ErrorCategory.ModelNotFound;
        }

        // Classify by HTTP status code
        return status switch
        {
            // Client errors
            400 => ErrorCategory.ClientError,  // Bad Request - invalid parameters
            401 => ErrorCategory.AuthError,    // Unauthorized - invalid API key
            403 => ErrorCategory.AuthError,    // Forbidden - permission denied
            404 => ErrorCategory.ClientError,  // Generic Not Found (model check done above)
            413 => ErrorCategory.ClientError,  // Payload Too Large - file too large

            // Rate limiting
            429 => ErrorCategory.RateLimitRetryable,  // Too Many Requests - quota exceeded

            // Server errors
            500 => ErrorCategory.ServerError,  // Internal Server Error
            503 => ErrorCategory.Transient,    // Service Unavailable - temporary issue

            // Server errors range
            >= 500 and < 600 => ErrorCategory.ServerError,

            // Unknown status code - classify by message
            _ => ClassifyByMessage(message)
        };
    }

    private static ErrorCategory ClassifyByMessage(string message)
    {
        var lowerMessage = message.ToLowerInvariant();

        // Authentication/Authorization errors
        if (lowerMessage.Contains("api_key_invalid") ||
            lowerMessage.Contains("unauthenticated") ||
            lowerMessage.Contains("invalid api key") ||
            lowerMessage.Contains("api key not valid") ||
            lowerMessage.Contains("permission_denied") ||
            lowerMessage.Contains("permission denied") ||
            lowerMessage.Contains("unauthorized"))
        {
            return ErrorCategory.AuthError;
        }

        // Rate limiting / Quota
        if (lowerMessage.Contains("quota_exceeded") ||
            lowerMessage.Contains("quota exceeded") ||
            lowerMessage.Contains("resource_exhausted") ||
            lowerMessage.Contains("resource exhausted") ||
            lowerMessage.Contains("rate limit") ||
            lowerMessage.Contains("too many requests") ||
            lowerMessage.Contains("throttl"))
        {
            return ErrorCategory.RateLimitRetryable;
        }

        // Safety/Content filtering (not retryable - content issue)
        if (lowerMessage.Contains("safety") ||
            lowerMessage.Contains("blocked") ||
            lowerMessage.Contains("content filter") ||
            lowerMessage.Contains("recitation"))
        {
            return ErrorCategory.ClientError;
        }

        // Transient errors
        if (lowerMessage.Contains("timeout") ||
            lowerMessage.Contains("temporary") ||
            lowerMessage.Contains("unavailable") ||
            lowerMessage.Contains("try again") ||
            lowerMessage.Contains("connection") ||
            lowerMessage.Contains("network") ||
            lowerMessage.Contains("deadline exceeded"))
        {
            return ErrorCategory.Transient;
        }

        // Client errors
        if (lowerMessage.Contains("invalid") ||
            lowerMessage.Contains("invalid_argument") ||
            lowerMessage.Contains("bad request") ||
            lowerMessage.Contains("malformed") ||
            lowerMessage.Contains("not_found") ||
            lowerMessage.Contains("not found") ||
            lowerMessage.Contains("validation") ||
            lowerMessage.Contains("file too large") ||
            lowerMessage.Contains("payload too large"))
        {
            return ErrorCategory.ClientError;
        }

        // Server errors
        if (lowerMessage.Contains("internal error") ||
            lowerMessage.Contains("server error") ||
            lowerMessage.Contains("internal_error"))
        {
            return ErrorCategory.ServerError;
        }

        return ErrorCategory.Unknown;
    }
}
