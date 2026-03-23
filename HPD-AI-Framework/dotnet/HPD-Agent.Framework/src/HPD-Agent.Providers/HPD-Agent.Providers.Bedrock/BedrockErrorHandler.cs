using System.Net;
using System.Text.RegularExpressions;
using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Providers.Bedrock;

/// <summary>
/// Error handler for AWS Bedrock-specific exceptions.
/// Handles all AWS SDK and Bedrock-specific error types with intelligent classification.
/// </summary>
internal partial class BedrockErrorHandler : IProviderErrorHandler
{
    // Source-generated regex patterns for AOT compatibility
    [GeneratedRegex(@"Status[:\s]+(\d{3})", RegexOptions.IgnoreCase)]
    private static partial Regex StatusPattern();

    [GeneratedRegex(@"\((\d{3})\)")]
    private static partial Regex ParenthesesStatusPattern();

    [GeneratedRegex(@"""errorCode"":\s*""([^""]+)""")]
    private static partial Regex ErrorCodePattern();

    [GeneratedRegex(@"error[_\s]type['""]?\s*:\s*['""]?([a-z_]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorTypePattern();

    public ProviderErrorDetails? ParseError(Exception exception)
    {
        // AWS SDK exceptions inherit from AmazonServiceException
        var exceptionTypeName = exception.GetType().FullName ?? string.Empty;

        // Check if this is an AWS exception
        if (!exceptionTypeName.Contains("Amazon") && !exceptionTypeName.Contains("AWS"))
        {
            return null;
        }

        var message = exception.Message;

        // Try to get status code and error code from the exception using duck typing (AOT-safe)
        int? statusCode = ExtractStatusCodeFromException(exception);
        string? errorCode = ExtractErrorCodeFromException(exception);

        // Fallback to message parsing if we couldn't get it from the exception
        statusCode ??= ExtractStatusCodeFromMessage(message);
        errorCode ??= ExtractErrorCodeFromMessage(message);

        return new ProviderErrorDetails
        {
            StatusCode = statusCode,
            Category = ClassifyError(exceptionTypeName, statusCode, errorCode, message),
            Message = message,
            ErrorCode = errorCode
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
        // Try to get StatusCode property using dynamic (AOT-compatible when the type is known at compile time)
        // We intentionally use dynamic here for duck typing to avoid reflection
#pragma warning disable IL2026, IL3050 // Dynamic code is intentional for AWS SDK exception handling
        try
        {
            // AmazonServiceException has a StatusCode property of type HttpStatusCode
            dynamic ex = exception;
            HttpStatusCode statusCode = ex.StatusCode;
            return (int)statusCode;
        }
        catch
        {
            // If the property doesn't exist or can't be accessed, fall back to message parsing
            return null;
        }
#pragma warning restore IL2026, IL3050
    }

    private static string? ExtractErrorCodeFromException(Exception exception)
    {
        // Try to get ErrorCode property using dynamic
#pragma warning disable IL2026, IL3050 // Dynamic code is intentional for AWS SDK exception handling
        try
        {
            dynamic ex = exception;
            return ex.ErrorCode as string;
        }
        catch
        {
            return null;
        }
#pragma warning restore IL2026, IL3050
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

    private static string? ExtractErrorCodeFromMessage(string message)
    {
        // Look for an 'errorCode' field in a JSON-like structure in the message
        var codeMatch = ErrorCodePattern().Match(message);
        if (codeMatch.Success)
        {
            return codeMatch.Groups[1].Value;
        }

        // Look for error type patterns
        var errorTypeMatch = ErrorTypePattern().Match(message);
        if (errorTypeMatch.Success)
        {
            return errorTypeMatch.Groups[1].Value;
        }

        return null;
    }

    private static ErrorCategory ClassifyError(string exceptionTypeName, int? statusCode, string? errorCode, string message)
    {
        // Check for model not found errors first (Bedrock: "model identifier is invalid")
        if (ModelNotFoundDetector.IsModelNotFoundError(statusCode, message, errorCode, errorType: null))
        {
            return ErrorCategory.ModelNotFound;
        }

        // First, classify by specific exception types (most reliable)
        var category = ClassifyByExceptionType(exceptionTypeName);
        if (category != ErrorCategory.Unknown)
        {
            return category;
        }

        // Then by error code (AWS-specific codes)
        category = ClassifyByErrorCode(errorCode);
        if (category != ErrorCategory.Unknown)
        {
            return category;
        }

        // Then by HTTP status code
        category = ClassifyByStatusCode(statusCode);
        if (category != ErrorCategory.Unknown)
        {
            return category;
        }

        // Finally, by message content
        return ClassifyByMessage(message);
    }

    private static ErrorCategory ClassifyByExceptionType(string exceptionTypeName)
    {
        // AWS Bedrock-specific exceptions
        if (exceptionTypeName.Contains("ThrottlingException"))
            return ErrorCategory.RateLimitRetryable;

        if (exceptionTypeName.Contains("ValidationException"))
            return ErrorCategory.ClientError;

        if (exceptionTypeName.Contains("AccessDeniedException"))
            return ErrorCategory.AuthError;

        if (exceptionTypeName.Contains("ResourceNotFoundException"))
            return ErrorCategory.ClientError;

        if (exceptionTypeName.Contains("ServiceQuotaExceededException"))
            return ErrorCategory.RateLimitRetryable;

        if (exceptionTypeName.Contains("ModelTimeoutException"))
            return ErrorCategory.Transient;

        if (exceptionTypeName.Contains("ModelNotReadyException"))
            return ErrorCategory.Transient;

        if (exceptionTypeName.Contains("InternalServerException"))
            return ErrorCategory.ServerError;

        if (exceptionTypeName.Contains("ServiceUnavailableException"))
            return ErrorCategory.Transient;

        if (exceptionTypeName.Contains("ModelErrorException"))
            return ErrorCategory.ServerError;

        if (exceptionTypeName.Contains("ModelStreamErrorException"))
            return ErrorCategory.ServerError;

        if (exceptionTypeName.Contains("ConflictException"))
            return ErrorCategory.ClientError;

        return ErrorCategory.Unknown;
    }

    private static ErrorCategory ClassifyByErrorCode(string? errorCode)
    {
        if (string.IsNullOrEmpty(errorCode))
            return ErrorCategory.Unknown;

        return errorCode.ToLowerInvariant() switch
        {
            // Rate limiting
            "throttlingexception" or "throttled" or "throttling" or "toomanyrequests" => ErrorCategory.RateLimitRetryable,
            "servicequotaexceeded" or "limitexceeded" or "quotaexceeded" => ErrorCategory.RateLimitRetryable,

            // Authentication/Authorization
            "accessdenied" or "accessdeniedException" or "unauthorized" or "forbidden" => ErrorCategory.AuthError,
            "invalidaccesskeyid" or "invalidsecuritytoken" or "expiredtoken" => ErrorCategory.AuthError,

            // Client errors
            "validationexception" or "validation" or "invalidrequest" or "invalidparameter" => ErrorCategory.ClientError,
            "resourcenotfound" or "notfound" or "modelnotfound" => ErrorCategory.ClientError,
            "conflict" or "conflictexception" => ErrorCategory.ClientError,
            "badrequest" => ErrorCategory.ClientError,

            // Transient errors
            "modeltimeout" or "modeltimeoutexception" or "timeout" => ErrorCategory.Transient,
            "modelnotready" or "modelnotreadyexception" => ErrorCategory.Transient,
            "serviceunavailable" or "serviceunavailableexception" => ErrorCategory.Transient,

            // Server errors
            "internalserver" or "internalservererror" or "internalserverexception" => ErrorCategory.ServerError,
            "modelerror" or "modelerrorexception" or "modelstreamerror" => ErrorCategory.ServerError,

            _ => ErrorCategory.Unknown
        };
    }

    private static ErrorCategory ClassifyByStatusCode(int? statusCode)
    {
        return statusCode switch
        {
            // Client errors - invalid request
            400 => ErrorCategory.ClientError,
            404 => ErrorCategory.ClientError, // Model or resource not found

            // Authentication/Authorization errors
            401 => ErrorCategory.AuthError, // Unauthorized - invalid credentials
            403 => ErrorCategory.AuthError, // Forbidden - insufficient permissions

            // Rate limiting - retryable with backoff
            429 => ErrorCategory.RateLimitRetryable,

            // Service errors - temporary, should retry
            503 => ErrorCategory.Transient, // Service Unavailable - temporary issue

            // Server errors - retryable
            >= 500 and < 600 => ErrorCategory.ServerError,

            // Unknown status code
            _ => ErrorCategory.Unknown
        };
    }

    private static ErrorCategory ClassifyByMessage(string message)
    {
        // Additional classification based on message content
        var lowerMessage = message.ToLowerInvariant();

        // Authentication-related errors
        if (lowerMessage.Contains("access denied") ||
            lowerMessage.Contains("unauthorized") ||
            lowerMessage.Contains("authentication") ||
            lowerMessage.Contains("access key") ||
            lowerMessage.Contains("secret key") ||
            lowerMessage.Contains("invalid token") ||
            lowerMessage.Contains("expired token") ||
            lowerMessage.Contains("credential") ||
            lowerMessage.Contains("iam") ||
            lowerMessage.Contains("permission"))
        {
            return ErrorCategory.AuthError;
        }

        // Rate limiting
        if (lowerMessage.Contains("throttl") ||
            lowerMessage.Contains("rate limit") ||
            lowerMessage.Contains("too many requests") ||
            lowerMessage.Contains("quota exceeded") ||
            lowerMessage.Contains("limit exceeded"))
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
            lowerMessage.Contains("model not ready") ||
            lowerMessage.Contains("still loading"))
        {
            return ErrorCategory.Transient;
        }

        // Client errors
        if (lowerMessage.Contains("invalid") ||
            lowerMessage.Contains("bad request") ||
            lowerMessage.Contains("malformed") ||
            lowerMessage.Contains("validation") ||
            lowerMessage.Contains("not found") ||
            lowerMessage.Contains("model id") ||
            lowerMessage.Contains("guardrail") ||
            lowerMessage.Contains("content filter"))
        {
            return ErrorCategory.ClientError;
        }

        // Server errors
        if (lowerMessage.Contains("internal server") ||
            lowerMessage.Contains("internal error") ||
            lowerMessage.Contains("model error"))
        {
            return ErrorCategory.ServerError;
        }

        return ErrorCategory.Unknown;
    }
}

