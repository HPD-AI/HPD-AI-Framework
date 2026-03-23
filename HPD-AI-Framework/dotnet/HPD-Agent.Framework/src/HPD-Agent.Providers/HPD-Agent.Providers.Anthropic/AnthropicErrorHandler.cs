using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic.Exceptions;
using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Providers.Anthropic;

internal class AnthropicErrorHandler : IProviderErrorHandler
{
    public ProviderErrorDetails? ParseError(Exception exception)
    {
        // Handle official Anthropic SDK exceptions (v12+)

        // API exceptions with status code + response body (most common)
        if (exception is AnthropicApiException anthropicEx)
        {
            return ParseAnthropicException(anthropicEx);
        }

        // Network/IO exceptions (connectivity issues, timeouts)
        if (exception is AnthropicIOException ioEx)
        {
            return new ProviderErrorDetails
            {
                StatusCode = null,
                Category = ErrorCategory.Transient, // Network issues are transient
                Message = ioEx.Message,
                ErrorCode = "network_error"
            };
        }

        // Streaming exceptions (SSE parsing errors during streaming)
        if (exception is AnthropicSseException sseEx)
        {
            return new ProviderErrorDetails
            {
                StatusCode = null,
                Category = ErrorCategory.Transient, // Streaming errors are often transient
                Message = sseEx.Message,
                ErrorCode = "streaming_error"
            };
        }

        // Data validation exceptions (SDK couldn't deserialize response)
        if (exception is AnthropicInvalidDataException invalidDataEx)
        {
            return new ProviderErrorDetails
            {
                StatusCode = null,
                Category = ErrorCategory.ClientError, // Data validation is a client-side issue
                Message = invalidDataEx.Message,
                ErrorCode = "invalid_data"
            };
        }

        // Fallback: Handle raw HttpRequestException (if SDK doesn't wrap it)
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

    public TimeSpan? GetRetryDelay(ProviderErrorDetails details, int attempt, TimeSpan initialDelay, double multiplier, TimeSpan maxDelay)
    {
        // Respect Retry-After if provided by Anthropic
        if (details.RetryAfter.HasValue)
        {
            return details.RetryAfter.Value;
        }

        // Only retry for specific categories
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

    private static ProviderErrorDetails ParseAnthropicException(AnthropicApiException exception)
    {
        var statusCode = (int)exception.StatusCode;
        var responseBody = exception.ResponseBody;
        var message = exception.Message;

        // Parse JSON response body for detailed error information
        string? errorType = null;
        string? errorCode = null;
        string? errorMessage = null;
        TimeSpan? retryAfter = null;

        if (!string.IsNullOrEmpty(responseBody))
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("error", out var errorObj))
                {
                    if (errorObj.TryGetProperty("type", out var typeElem))
                        errorType = typeElem.GetString();

                    if (errorObj.TryGetProperty("message", out var msgElem))
                        errorMessage = msgElem.GetString();
                }
            }
            catch
            {
                // If JSON parsing fails, use raw message
            }
        }

        // Determine error category
        var category = ClassifyAnthropicError(statusCode, errorType, errorMessage ?? message);

        // Extract Retry-After from rate limit errors
        if (category == ErrorCategory.RateLimitRetryable)
        {
            retryAfter = ExtractRetryAfter(errorMessage ?? message);
        }

        // Determine error code for context window errors
        if (errorMessage?.Contains("maximum context length") == true ||
            errorMessage?.Contains("prompt is too long") == true)
        {
            errorCode = "context_length_exceeded";
            category = ErrorCategory.ContextWindow;
        }

        // Check for JSON Schema validation errors
        if (errorMessage?.Contains("JSON schema is invalid") == true ||
            errorMessage?.Contains("input_schema") == true)
        {
            errorCode = "invalid_json_schema";
            category = ErrorCategory.ClientError;
            
            // Enhance error message with debugging hint
            var enhancedMessage = errorMessage + 
                " [DEBUG: Enable detailed errors in AgentBuilder or check JSON Schema draft 2020-12 compliance. " +
                "Common issues: using 'additionalProperties: false' with 'anyOf', missing 'type: \"object\"' for root schema, " +
                "or incompatible schema keywords. Check tool schemas with: " +
                "https://www.jsonschemavalidator.net/ using draft 2020-12]";
            
            return new ProviderErrorDetails
            {
                StatusCode = statusCode,
                Category = category,
                Message = enhancedMessage,
                ErrorType = errorType,
                ErrorCode = errorCode,
                RetryAfter = retryAfter
            };
        }

        return new ProviderErrorDetails
        {
            StatusCode = statusCode,
            Category = category,
            Message = errorMessage ?? message,
            ErrorType = errorType,
            ErrorCode = errorCode,
            RetryAfter = retryAfter
        };
    }

    private static ErrorCategory ClassifyAnthropicError(int statusCode, string? errorType, string message)
    {
        // Check for model not found errors first (Anthropic uses "not_found_error" type)
        if (ModelNotFoundDetector.IsModelNotFoundError(statusCode, message, errorCode: null, errorType))
        {
            return ErrorCategory.ModelNotFound;
        }

        // Check for insufficient credits (common 400 error)
        if (errorType == "invalid_request_error" &&
            (message.Contains("credit balance is too low") ||
             message.Contains("insufficient_quota")))
        {
            return ErrorCategory.RateLimitTerminal; // Don't retry - user needs to add credits
        }

        // Check for authentication errors
        if (statusCode == 401 || errorType == "authentication_error")
        {
            return ErrorCategory.AuthError;
        }

        // Check for rate limiting
        if (statusCode == 429 || errorType == "rate_limit_error")
        {
            // Check if it's a terminal quota error
            if (message.Contains("insufficient_quota") ||
                message.Contains("exceeded your API quota"))
            {
                return ErrorCategory.RateLimitTerminal;
            }
            return ErrorCategory.RateLimitRetryable;
        }

        // Check for context window errors
        if (message.Contains("maximum context length") ||
            message.Contains("prompt is too long"))
        {
            return ErrorCategory.ContextWindow;
        }

        // Classify by status code
        return ClassifyError(statusCode, message);
    }

    private static ErrorCategory ClassifyError(int? status, string message)
    {
        if (status == 400) return ErrorCategory.ClientError;
        if (status == 401) return ErrorCategory.AuthError;
        if (status == 403) return ErrorCategory.AuthError;
        if (status == 429) return ErrorCategory.RateLimitRetryable;
        if (status >= 500 && status < 600) return ErrorCategory.ServerError;
        return ErrorCategory.Unknown;
    }

    private static string? ExtractErrorCode(string message)
    {
        if (message.Contains("invalid_api_key")) return "invalid_api_key";
        if (message.Contains("rate_limit_error")) return "rate_limit_error";
        if (message.Contains("context_length_exceeded")) return "context_length_exceeded";
        if (message.Contains("insufficient_quota")) return "insufficient_quota";
        return null;
    }

    private static TimeSpan? ExtractRetryAfter(string message)
    {
        // Pattern: "Please retry after X seconds" or "retry after X ms"
        var match = Regex.Match(message, @"retry\s+after\s+(\d+(?:\.\d+)?)\s*(seconds?|ms|milliseconds?)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            if (double.TryParse(match.Groups[1].Value, out var value))
            {
                var unit = match.Groups[2].Value.ToLowerInvariant();
                if (unit.StartsWith("ms"))
                    return TimeSpan.FromMilliseconds(value);
                else
                    return TimeSpan.FromSeconds(value);
            }
        }

        return null;
    }
}
