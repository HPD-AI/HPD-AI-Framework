using System;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Providers.Mistral;

internal partial class MistralErrorHandler : IProviderErrorHandler
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

    public ProviderErrorDetails? ParseError(Exception exception)
    {
        // Handle HttpRequestException from Mistral SDK
        if (exception is HttpRequestException httpEx)
        {
            var message = httpEx.Message;
            var statusCode = (int?)httpEx.StatusCode;

            // Try to extract status code from message if not in exception
            statusCode ??= ExtractStatusCodeFromMessage(message);

            return new ProviderErrorDetails
            {
                StatusCode = statusCode,
                Category = ClassifyError(statusCode, message),
                Message = message,
                ErrorCode = ExtractErrorCode(message)
            };
        }

        // Handle AuthenticationException from Mistral SDK (401 errors)
        if (exception is AuthenticationException authEx)
        {
            return new ProviderErrorDetails
            {
                StatusCode = 401,
                Category = ErrorCategory.AuthError,
                Message = authEx.Message,
                ErrorCode = "unauthorized"
            };
        }

        // Handle general exceptions that might contain error information
        if (exception?.Message != null)
        {
            var message = exception.Message;
            var statusCode = ExtractStatusCodeFromMessage(message);

            if (statusCode.HasValue)
            {
                return new ProviderErrorDetails
                {
                    StatusCode = statusCode,
                    Category = ClassifyError(statusCode, message),
                    Message = message,
                    ErrorCode = ExtractErrorCode(message)
                };
            }
        }

        return null;
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

        return null;
    }

    private static string? ExtractErrorCode(string message)
    {
        // Look for a 'code' field in a JSON-like structure in the message
        var codeMatch = ErrorCodePattern().Match(message);
        if (codeMatch.Success)
        {
            return codeMatch.Groups[1].Value;
        }

        // Look for a 'type' field in error responses
        var typeMatch = ErrorTypePattern().Match(message);
        if (typeMatch.Success)
        {
            return typeMatch.Groups[1].Value;
        }

        return null;
    }

    private static string? ExtractErrorType(string message)
    {
        // Look for a 'type' field in error responses (e.g., "type": "invalid_model")
        var typeMatch = ErrorTypePattern().Match(message);
        if (typeMatch.Success)
        {
            return typeMatch.Groups[1].Value;
        }

        // Check for "invalid_model" pattern in message
        if (message.Contains("invalid_model", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Invalid model:", StringComparison.OrdinalIgnoreCase))
        {
            return "invalid_model";
        }

        return null;
    }

    private static ErrorCategory ClassifyError(int? status, string message)
    {
        // Check for model not found errors first (Mistral: "Invalid model: X" with type "invalid_model")
        var errorType = ExtractErrorType(message);
        if (ModelNotFoundDetector.IsModelNotFoundError(status, message, errorCode: null, errorType))
        {
            return ErrorCategory.ModelNotFound;
        }

        return status switch
        {
            // Client errors - invalid request
            400 => ErrorCategory.ClientError,
            404 => ErrorCategory.ClientError, // Generic not found (model check done above)

            // Authentication/Authorization errors
            401 => ErrorCategory.AuthError, // Unauthorized - invalid API key
            403 => ErrorCategory.AuthError, // Forbidden - insufficient permissions

            // Rate limiting - retryable with backoff
            429 => ErrorCategory.RateLimitRetryable,

            // Service errors - temporary, should retry
            503 => ErrorCategory.Transient, // Service Unavailable - temporary issue

            // Server errors - retryable
            >= 500 and < 600 => ErrorCategory.ServerError,

            // Unknown status code
            _ => ClassifyByMessage(message)
        };
    }

    private static ErrorCategory ClassifyByMessage(string message)
    {
        // Additional classification based on message content
        var lowerMessage = message.ToLowerInvariant();

        // Authentication-related errors
        if (lowerMessage.Contains("unauthorized") ||
            lowerMessage.Contains("authentication") ||
            lowerMessage.Contains("api key") ||
            lowerMessage.Contains("invalid token") ||
            lowerMessage.Contains("rejected your authorization"))
        {
            return ErrorCategory.AuthError;
        }

        // Rate limiting
        if (lowerMessage.Contains("rate limit") ||
            lowerMessage.Contains("too many requests") ||
            lowerMessage.Contains("quota exceeded") ||
            lowerMessage.Contains("throttl"))
        {
            return ErrorCategory.RateLimitRetryable;
        }

        // Transient errors
        if (lowerMessage.Contains("timeout") ||
            lowerMessage.Contains("temporary") ||
            lowerMessage.Contains("unavailable") ||
            lowerMessage.Contains("try again") ||
            lowerMessage.Contains("connection") ||
            lowerMessage.Contains("network") ||
            lowerMessage.Contains("internal server error"))
        {
            return ErrorCategory.Transient;
        }

        // Client errors
        if (lowerMessage.Contains("invalid") ||
            lowerMessage.Contains("bad request") ||
            lowerMessage.Contains("malformed") ||
            lowerMessage.Contains("validation") ||
            lowerMessage.Contains("model not found") ||
            lowerMessage.Contains("not found"))
        {
            return ErrorCategory.ClientError;
        }

        return ErrorCategory.Unknown;
    }
}

