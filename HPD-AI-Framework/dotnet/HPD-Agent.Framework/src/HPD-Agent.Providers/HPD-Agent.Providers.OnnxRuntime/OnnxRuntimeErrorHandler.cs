using System;
using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Providers.OnnxRuntime;

/// <summary>
/// Error handler for ONNX Runtime GenAI exceptions.
/// Provides intelligent error classification and retry logic for local model inference scenarios.
/// </summary>
internal class OnnxRuntimeErrorHandler : IProviderErrorHandler
{
    public ProviderErrorDetails? ParseError(Exception exception)
    {
        // Check if this is an ONNX Runtime GenAI exception
        var exceptionTypeName = exception.GetType().FullName;
        if (exceptionTypeName != "Microsoft.ML.OnnxRuntimeGenAI.OnnxRuntimeGenAIException")
        {
            // Not an ONNX-specific exception, handle as generic
            return null;
        }

        var message = exception.Message;
        var category = ClassifyError(message, exception);

        return new ProviderErrorDetails
        {
            StatusCode = null, // Local inference doesn't use HTTP status codes
            Category = category,
            Message = message,
            ErrorCode = exceptionTypeName
        };
    }

    public TimeSpan? GetRetryDelay(ProviderErrorDetails details, int attempt, TimeSpan initialDelay, double multiplier, TimeSpan maxDelay)
    {
        // Only retry for transient errors (e.g., temporary resource issues)
        if (details.Category == ErrorCategory.Transient)
        {
            var baseMs = initialDelay.TotalMilliseconds;
            var expDelayMs = baseMs * Math.Pow(multiplier, attempt);
            return TimeSpan.FromMilliseconds(Math.Min(expDelayMs, maxDelay.TotalMilliseconds));
        }

        // No retry for client errors, server errors, or other categories
        return null;
    }

    public bool RequiresSpecialHandling(ProviderErrorDetails details)
    {
        // Special handling for specific error categories
        return details.Category == ErrorCategory.ClientError;
    }

    private static ErrorCategory ClassifyError(string message, Exception exception)
    {
        var lowerMessage = message.ToLowerInvariant();

        // Model loading errors
        if (lowerMessage.Contains("model") &&
            (lowerMessage.Contains("not found") ||
             lowerMessage.Contains("cannot find") ||
             lowerMessage.Contains("does not exist") ||
             lowerMessage.Contains("failed to load")))
        {
            return ErrorCategory.ClientError;
        }

        // Configuration errors
        if (lowerMessage.Contains("invalid") ||
            lowerMessage.Contains("malformed") ||
            lowerMessage.Contains("unsupported") ||
            lowerMessage.Contains("incompatible") ||
            lowerMessage.Contains("configuration") ||
            lowerMessage.Contains("parameter"))
        {
            return ErrorCategory.ClientError;
        }

        // File I/O errors
        if (exception is System.IO.IOException ||
            exception is System.IO.FileNotFoundException ||
            exception is System.IO.DirectoryNotFoundException ||
            exception is UnauthorizedAccessException)
        {
            return ErrorCategory.ClientError;
        }

        // Memory errors - could be transient if system is under load
        if (lowerMessage.Contains("out of memory") ||
            lowerMessage.Contains("insufficient memory") ||
            lowerMessage.Contains("allocation failed") ||
            lowerMessage.Contains("oom"))
        {
            return ErrorCategory.Transient;
        }

        // CUDA/GPU errors
        if (lowerMessage.Contains("cuda") ||
            lowerMessage.Contains("gpu") ||
            lowerMessage.Contains("device"))
        {
            // Could be transient (device busy) or client error (wrong device ID)
            if (lowerMessage.Contains("busy") ||
                lowerMessage.Contains("unavailable") ||
                lowerMessage.Contains("in use"))
            {
                return ErrorCategory.Transient;
            }
            return ErrorCategory.ClientError;
        }

        // Provider/execution errors
        if (lowerMessage.Contains("provider") ||
            lowerMessage.Contains("execution"))
        {
            return ErrorCategory.ClientError;
        }

        // Tokenizer errors
        if (lowerMessage.Contains("tokenizer") ||
            lowerMessage.Contains("token") ||
            lowerMessage.Contains("vocabulary"))
        {
            return ErrorCategory.ClientError;
        }

        // Generation errors (typically user configuration issues)
        if (lowerMessage.Contains("generation") ||
            lowerMessage.Contains("sampling") ||
            lowerMessage.Contains("beam search") ||
            lowerMessage.Contains("sequence length"))
        {
            return ErrorCategory.ClientError;
        }

        // Timeout errors - transient
        if (lowerMessage.Contains("timeout") ||
            lowerMessage.Contains("timed out"))
        {
            return ErrorCategory.Transient;
        }

        // Thread/concurrency errors - transient
        if (lowerMessage.Contains("thread") ||
            lowerMessage.Contains("concurrency") ||
            lowerMessage.Contains("race condition") ||
            lowerMessage.Contains("deadlock"))
        {
            return ErrorCategory.Transient;
        }

        // Adapter/LoRA errors
        if (lowerMessage.Contains("adapter") ||
            lowerMessage.Contains("lora"))
        {
            return ErrorCategory.ClientError;
        }

        // Default to client error for local inference
        // Most issues with local models are configuration or usage problems
        return ErrorCategory.ClientError;
    }
}
