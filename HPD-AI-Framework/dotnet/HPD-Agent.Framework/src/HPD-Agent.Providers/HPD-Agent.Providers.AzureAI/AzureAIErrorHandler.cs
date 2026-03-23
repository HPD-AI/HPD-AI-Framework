using System;
using System.Text.RegularExpressions;
using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Providers.AzureAI;

internal partial class AzureAIErrorHandler : IProviderErrorHandler
{
    // Source-generated regex patterns for AOT compatibility
    [GeneratedRegex(@"Status:\s*(\d{3})", RegexOptions.IgnoreCase)]
    private static partial Regex StatusPattern();

    [GeneratedRegex(@"\((\d{3})\)")]
    private static partial Regex ParenthesesStatusPattern();

    [GeneratedRegex(@"""code"":\s*""([^""]+)""")]
    private static partial Regex ErrorCodePattern();

    [GeneratedRegex(@"error[_\s]type['""]?\s*:\s*['""]?([a-z_]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorTypePattern();

    public ProviderErrorDetails? ParseError(Exception exception)
    {
        // Azure SDK uses Azure.RequestFailedException and ClientResultException
        var exceptionTypeName = exception.GetType().FullName;
        if (exceptionTypeName != "Azure.RequestFailedException" &&
            exceptionTypeName != "System.ClientModel.ClientResultException")
        {
            return null;
        }

        var message = exception.Message;

        // Try to get status code from the exception using duck typing (AOT-safe)
        int? status = ExtractStatusCodeFromException(exception);

        // Fallback to message parsing if we couldn't get it from the exception
        status ??= ExtractStatusCodeFromMessage(message);

        return new ProviderErrorDetails
        {
            StatusCode = status,
            Category = ClassifyError(status, message),
            Message = message,
            ErrorCode = ExtractErrorCode(message)
        };
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

    private static int? ExtractStatusCodeFromException(Exception exception)
    {
        // Try to get Status property using dynamic (AOT-compatible when the type is known at compile time)
        // This avoids reflection while still accessing the property
        try
        {
            // Both RequestFailedException and ClientResultException have a Status property of type int
            // Using dynamic allows us to access it without reflection
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
        // Look for a 'code' field in a JSON-like structure in the message
        var codeMatch = ErrorCodePattern().Match(message);
        if (codeMatch.Success)
        {
            return codeMatch.Groups[1].Value;
        }

        // Look for error type patterns like "content_filter" or "rate_limit"
        var errorTypeMatch = ErrorTypePattern().Match(message);
        if (errorTypeMatch.Success)
        {
            return errorTypeMatch.Groups[1].Value;
        }

        return null;
    }

    private static ErrorCategory ClassifyError(int? status, string message)
    {
        // Check for model/deployment not found errors first (Azure: "DeploymentNotFound", "deployment does not exist")
        var errorCode = ExtractErrorCode(message);
        if (ModelNotFoundDetector.IsModelNotFoundError(status, message, errorCode, errorType: null))
        {
            return ErrorCategory.ModelNotFound;
        }

        return status switch
        {
            // Client errors - invalid request
            400 => ErrorCategory.ClientError,
            404 => ErrorCategory.ClientError, // Generic not found (model/deployment check done above)

            // Authentication/Authorization errors
            401 => ErrorCategory.AuthError, // Unauthorized - invalid API key or token
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
            lowerMessage.Contains("credential") ||
            lowerMessage.Contains("entra") ||
            lowerMessage.Contains("azure active directory"))
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
            lowerMessage.Contains("network"))
        {
            return ErrorCategory.Transient;
        }

        // Client errors
        if (lowerMessage.Contains("invalid") ||
            lowerMessage.Contains("bad request") ||
            lowerMessage.Contains("malformed") ||
            lowerMessage.Contains("schema") ||
            lowerMessage.Contains("validation") ||
            lowerMessage.Contains("deployment not found") ||
            lowerMessage.Contains("model not found"))
        {
            return ErrorCategory.ClientError;
        }

        return ErrorCategory.Unknown;
    }
}
