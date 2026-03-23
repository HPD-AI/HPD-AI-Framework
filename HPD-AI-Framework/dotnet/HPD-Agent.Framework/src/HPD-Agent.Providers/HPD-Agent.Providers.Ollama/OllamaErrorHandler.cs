using System;
using System.Net.Http;
using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Providers.Ollama;

/// <summary>
/// Error handler for Ollama provider, handling OllamaSharp-specific exceptions.
/// </summary>
internal class OllamaErrorHandler : IProviderErrorHandler
{
    public ProviderErrorDetails? ParseError(Exception exception)
    {
        // Check for OllamaSharp-specific exceptions by type name (AOT-safe)
        var exceptionTypeName = exception.GetType().FullName;

        // Handle OllamaException and its derived types
        if (exceptionTypeName == "OllamaSharp.Models.Exceptions.OllamaException" ||
            exceptionTypeName == "OllamaSharp.Models.Exceptions.ResponseError" ||
            exceptionTypeName == "OllamaSharp.Models.Exceptions.ModelDoesNotSupportToolsException")
        {
            return new ProviderErrorDetails
            {
                StatusCode = null, // Ollama exceptions don't always have HTTP status codes
                Category = ClassifyOllamaException(exceptionTypeName, exception.Message),
                Message = exception.Message,
                ErrorCode = ExtractErrorCode(exception.Message, exceptionTypeName)
            };
        }

        // Handle HTTP exceptions from underlying HTTP client
        if (exception is HttpRequestException httpEx)
        {
            var statusCode = (int?)httpEx.StatusCode;
            return new ProviderErrorDetails
            {
                StatusCode = statusCode,
                Category = ClassifyHttpError(statusCode, httpEx.Message),
                Message = httpEx.Message,
                ErrorCode = statusCode?.ToString()
            };
        }

        // Handle task canceled/timeout exceptions
        if (exception is TaskCanceledException or OperationCanceledException)
        {
            return new ProviderErrorDetails
            {
                StatusCode = 408, // Request Timeout
                Category = ErrorCategory.Transient,
                Message = "Request timed out while communicating with Ollama",
                ErrorCode = "timeout"
            };
        }

        return null;
    }

    public TimeSpan? GetRetryDelay(ProviderErrorDetails details, int attempt, TimeSpan initialDelay, double multiplier, TimeSpan maxDelay)
    {
        // Ollama is typically local, so we only retry on transient/server errors
        // We don't retry on model not found, tool errors, or client errors
        if (details.Category is ErrorCategory.ServerError or ErrorCategory.Transient)
        {
            var baseMs = initialDelay.TotalMilliseconds;
            var expDelayMs = baseMs * Math.Pow(multiplier, attempt);
            return TimeSpan.FromMilliseconds(Math.Min(expDelayMs, maxDelay.TotalMilliseconds));
        }
        return null;
    }

    public bool RequiresSpecialHandling(ProviderErrorDetails details)
    {
        // No special handling like auth refresh needed for local Ollama
        // Tool support errors should be handled by the application
        return false;
    }

    private static string? ExtractErrorCode(string message, string exceptionType)
    {
        // Map exception types to error codes
        if (exceptionType == "OllamaSharp.Models.Exceptions.ModelDoesNotSupportToolsException")
            return "model_does_not_support_tools";

        if (exceptionType == "OllamaSharp.Models.Exceptions.ResponseError")
            return "response_error";

        // Try to extract error code from message
        var lowerMessage = message.ToLowerInvariant();

        if (lowerMessage.Contains("model") && lowerMessage.Contains("not found"))
            return "model_not_found";

        if (lowerMessage.Contains("connection") || lowerMessage.Contains("connect"))
            return "connection_error";

        if (lowerMessage.Contains("timeout"))
            return "timeout";

        if (lowerMessage.Contains("loading"))
            return "model_loading";

        return null;
    }

    private static ErrorCategory ClassifyOllamaException(string exceptionType, string message)
    {
        // ModelDoesNotSupportToolsException is a client error - model limitation
        if (exceptionType == "OllamaSharp.Models.Exceptions.ModelDoesNotSupportToolsException")
            return ErrorCategory.ClientError;

        // Check for model not found errors (Ollama: "try pulling it first")
        if (ModelNotFoundDetector.IsModelNotFoundError(status: null, message, errorCode: null, errorType: null))
            return ErrorCategory.ModelNotFound;

        // Classify based on message content
        var lowerMessage = message.ToLowerInvariant();

        // Connection errors are transient
        if (lowerMessage.Contains("connection") || lowerMessage.Contains("connect"))
            return ErrorCategory.Transient;

        // Model loading errors are transient
        if (lowerMessage.Contains("loading") || lowerMessage.Contains("pulling"))
            return ErrorCategory.Transient;

        // Timeout errors are transient
        if (lowerMessage.Contains("timeout"))
            return ErrorCategory.Transient;

        // Default to unknown for OllamaException and ResponseError
        return ErrorCategory.Unknown;
    }

    private static ErrorCategory ClassifyHttpError(int? status, string message)
    {
        // Check for model not found errors first
        if (ModelNotFoundDetector.IsModelNotFoundError(status, message, errorCode: null, errorType: null))
        {
            return ErrorCategory.ModelNotFound;
        }

        return status switch
        {
            // Client errors
            400 => ErrorCategory.ClientError, // Bad request - invalid parameters
            404 => ErrorCategory.ClientError, // Generic not found (model check done above)

            // Model is loading - temporary condition
            503 => ErrorCategory.Transient, // Service Unavailable - model is loading

            // Server errors - should retry
            >= 500 and < 600 => ErrorCategory.ServerError,

            // Connection errors
            null when IsConnectionError(message) => ErrorCategory.Transient,

            // Unknown
            _ => ErrorCategory.Unknown
        };
    }

    private static bool IsConnectionError(string message)
    {
        var lowerMessage = message.ToLowerInvariant();
        return lowerMessage.Contains("connection") ||
               lowerMessage.Contains("network") ||
               lowerMessage.Contains("unreachable") ||
               lowerMessage.Contains("refused");
    }
}

