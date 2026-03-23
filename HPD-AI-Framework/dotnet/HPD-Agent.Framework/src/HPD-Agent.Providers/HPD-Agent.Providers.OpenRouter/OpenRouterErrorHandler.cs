using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text.Json;
using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Providers.OpenRouter;

internal class OpenRouterErrorHandler : IProviderErrorHandler
{
    public ProviderErrorDetails? ParseError(Exception exception)
    {
        if (exception is HttpRequestException httpEx)
        {
            var statusCode = (int?)httpEx.StatusCode;
            var message = httpEx.Message;
            string? errorCode = null;
            string? requestId = null;
            TimeSpan? retryAfter = null;
            var rawDetails = new Dictionary<string, object>();

            // Try to parse JSON error body from message
            // OpenRouter format: {"error": {"code": 402, "message": "...", "metadata": {...}}}
            // Mid-stream format: {"id":"cmpl-123","error":{"code":"server_error","message":"..."},"choices":[...]}
            var jsonMatch = Regex.Match(message, @"\{.*""error"".*\}", RegexOptions.Singleline);
            if (jsonMatch.Success)
            {
                try
                {
                    using var doc = JsonDocument.Parse(jsonMatch.Value);
                    
                    // Handle mid-stream error format (has "choices" array)
                    if (doc.RootElement.TryGetProperty("choices", out var choicesElement) &&
                        doc.RootElement.TryGetProperty("error", out var errorElement))
                    {
                        // Mid-stream error format
                        ParseErrorElement(errorElement, ref message, ref errorCode, rawDetails);
                        
                        // Extract response ID for mid-stream errors
                        if (doc.RootElement.TryGetProperty("id", out var idElement))
                        {
                            requestId = idElement.GetString();
                        }
                    }
                    // Handle standard error format
                    else if (doc.RootElement.TryGetProperty("error", out errorElement))
                    {
                        ParseErrorElement(errorElement, ref message, ref errorCode, rawDetails);
                    }
                }
                catch
                {
                    // JSON parsing failed, continue with original message
                }
            }

            // Extract request ID from message (common patterns)
            var requestIdMatch = Regex.Match(message, @"request[_-]?id[:\s]+([a-zA-Z0-9\-_]+)", RegexOptions.IgnoreCase);
            if (requestIdMatch.Success)
            {
                requestId = requestIdMatch.Groups[1].Value;
            }

            // Extract Retry-After from message (e.g., "retry after 5s" or "try again in 2.5s")
            var retryMatch = Regex.Match(message, @"(?:retry after|try again in)\s+(\d+(?:\.\d+)?)\s*s", RegexOptions.IgnoreCase);
            if (retryMatch.Success && double.TryParse(retryMatch.Groups[1].Value, out var seconds))
            {
                retryAfter = TimeSpan.FromSeconds(seconds);
            }

            // Extract X-RateLimit-Reset from message (timestamp in seconds)
            var rateLimitResetMatch = Regex.Match(message, @"X-RateLimit-Reset[:\s]+(\d+)", RegexOptions.IgnoreCase);
            if (rateLimitResetMatch.Success && long.TryParse(rateLimitResetMatch.Groups[1].Value, out var resetTimestamp))
            {
                var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetTimestamp);
                var delayUntilReset = resetTime - DateTimeOffset.UtcNow;
                if (delayUntilReset > TimeSpan.Zero)
                {
                    retryAfter = delayUntilReset;
                }
            }

            var category = ClassifyError(statusCode, message, errorCode);

            return new ProviderErrorDetails
            {
                StatusCode = statusCode,
                Category = category,
                Message = message,
                ErrorCode = errorCode,
                RequestId = requestId,
                RetryAfter = retryAfter,
                RawDetails = rawDetails.Count > 0 ? rawDetails : null
            };
        }
        return null;
    }

    public TimeSpan? GetRetryDelay(ProviderErrorDetails details, int attempt, TimeSpan initialDelay, double multiplier, TimeSpan maxDelay)
    {
        // Priority 1: Use RetryAfter from provider if available
        if (details.RetryAfter.HasValue && details.Category == ErrorCategory.RateLimitRetryable)
        {
            return details.RetryAfter.Value;
        }

        // Priority 2: Exponential backoff for retryable errors
        if (details.Category is ErrorCategory.RateLimitRetryable or ErrorCategory.ServerError or ErrorCategory.Transient)
        {
            var baseMs = initialDelay.TotalMilliseconds;
            var expDelayMs = baseMs * Math.Pow(multiplier, attempt);
            return TimeSpan.FromMilliseconds(Math.Min(expDelayMs, maxDelay.TotalMilliseconds));
        }

        // Don't retry terminal errors
        return null;
    }

    public bool RequiresSpecialHandling(ProviderErrorDetails details)
    {
        // Auth errors may need token refresh
        return details.Category == ErrorCategory.AuthError;
    }

    private static ErrorCategory ClassifyError(int? status, string message, string? errorCode)
    {
        // Check for model not found errors first
        if (ModelNotFoundDetector.IsModelNotFoundError(status, message, errorCode, errorType: null))
        {
            return ErrorCategory.ModelNotFound;
        }

        // Check error code first (more specific than status)
        if (!string.IsNullOrEmpty(errorCode))
        {
            // Credit-related errors (terminal - don't retry)
            if (errorCode.Contains("insufficient_credit", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Contains("no_credit", StringComparison.OrdinalIgnoreCase))
            {
                return ErrorCategory.RateLimitTerminal;
            }

            // Rate limiting (retryable)
            if (errorCode.Contains("rate_limit", StringComparison.OrdinalIgnoreCase))
            {
                return ErrorCategory.RateLimitRetryable;
            }

            // Timeout-related (retryable)
            if (errorCode.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Contains("request_timeout", StringComparison.OrdinalIgnoreCase))
            {
                return ErrorCategory.Transient;
            }

            // Content moderation (don't retry)
            if (errorCode.Contains("moderation", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Contains("content_filter", StringComparison.OrdinalIgnoreCase))
            {
                return ErrorCategory.ClientError;
            }

            // Server/provider errors (retryable)
            if (errorCode.Contains("server_error", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Contains("provider_error", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Contains("model_unavailable", StringComparison.OrdinalIgnoreCase))
            {
                return ErrorCategory.ServerError;
            }

            // Authentication errors
            if (errorCode.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            {
                return ErrorCategory.AuthError;
            }

            // Client errors (don't retry)
            if (errorCode.Contains("invalid_", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Contains("bad_request", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Contains("invalid_prompt", StringComparison.OrdinalIgnoreCase))
            {
                return ErrorCategory.ClientError;
            }
        }

        // Classify by HTTP status code
        return status switch
        {
            400 => ErrorCategory.ClientError,           // Bad request
            401 => ErrorCategory.AuthError,             // Invalid credentials
            402 => ErrorCategory.RateLimitTerminal,     // Insufficient credits (don't retry)
            403 => message.Contains("moderation", StringComparison.OrdinalIgnoreCase)
                ? ErrorCategory.ClientError              // Content flagged (don't retry)
                : ErrorCategory.AuthError,               // Other auth issue
            408 => ErrorCategory.Transient,             // Request timeout (retryable)
            429 => ErrorCategory.RateLimitRetryable,    // Rate limit (retryable)
            502 => ErrorCategory.ServerError,           // Model unavailable (retryable)
            503 => ErrorCategory.ServerError,           // No provider available (retryable)
            >= 500 and < 600 => ErrorCategory.ServerError, // Other server errors (retryable)
            _ => ErrorCategory.Unknown
        };
    }

    /// <summary>
    /// Checks if an error is due to insufficient credits (402 error).
    /// </summary>
    /// <param name="details">The error details to check.</param>
    /// <returns>True if this is a credit exhaustion error.</returns>
    public static bool IsInsufficientCreditsError(ProviderErrorDetails details)
    {
        return details.StatusCode == 402 || 
               details.ErrorCode?.Contains("insufficient_credit", StringComparison.OrdinalIgnoreCase) == true ||
               details.ErrorCode?.Contains("no_credit", StringComparison.OrdinalIgnoreCase) == true ||
               details.Message.Contains("insufficient credits", StringComparison.OrdinalIgnoreCase) ||
               details.Message.Contains("negative credit balance", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if an error is due to free tier limits.
    /// </summary>
    /// <param name="details">The error details to check.</param>
    /// <returns>True if this is a free tier rate limit error.</returns>
    public static bool IsFreeTierLimitError(ProviderErrorDetails details)
    {
        return details.StatusCode == 429 &&
               (details.Message.Contains("free model", StringComparison.OrdinalIgnoreCase) ||
                details.Message.Contains(":free", StringComparison.OrdinalIgnoreCase) ||
                details.ErrorCode?.Contains("free_tier_limit", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static void ParseErrorElement(JsonElement errorElement, ref string message, ref string? errorCode, Dictionary<string, object> rawDetails)
    {
        // Extract error message
        if (errorElement.TryGetProperty("message", out var msgElement))
        {
            message = msgElement.GetString() ?? message;
        }

        // Extract error code (string like "insufficient_credits" or number)
        if (errorElement.TryGetProperty("code", out var codeElement))
        {
            errorCode = codeElement.ValueKind == JsonValueKind.String
                ? codeElement.GetString()
                : codeElement.GetInt32().ToString();
        }

        // Extract metadata (provider info, moderation flags, etc.)
        if (errorElement.TryGetProperty("metadata", out var metadataElement))
        {
            if (metadataElement.TryGetProperty("provider_name", out var providerElement))
            {
                rawDetails["provider_name"] = providerElement.GetString() ?? "";
            }
            if (metadataElement.TryGetProperty("flagged_input", out var flaggedElement))
            {
                rawDetails["flagged_input"] = flaggedElement.GetString() ?? "";
            }
            if (metadataElement.TryGetProperty("reasons", out var reasonsElement))
            {
                rawDetails["moderation_reasons"] = reasonsElement.ToString();
            }
            // Handle provider error metadata
            if (metadataElement.TryGetProperty("raw", out var rawElement))
            {
                rawDetails["provider_raw_error"] = rawElement.ToString();
            }
        }
    }

    /// <summary>
    /// Determines if an SSE line is an OpenRouter processing comment that should be ignored.
    /// OpenRouter sends comments like ": OPENROUTER PROCESSING" to prevent connection timeouts.
    /// </summary>
    /// <param name="line">The SSE line to check</param>
    /// <returns>True if this is a processing comment that should be ignored</returns>
    public static bool IsProcessingComment(string line)
    {
        return !string.IsNullOrEmpty(line) && 
               line.StartsWith(": OPENROUTER", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Enhanced error classification that handles OpenRouter-specific error patterns
    /// including Responses API transformations where certain errors become successful responses.
    /// </summary>
    /// <param name="errorCode">The error code from OpenRouter</param>
    /// <returns>True if this error should be transformed to a successful response with appropriate finish_reason</returns>
    public static bool ShouldTransformToSuccess(string? errorCode)
    {
        if (string.IsNullOrEmpty(errorCode)) return false;

        // Based on OpenRouter docs: certain errors in Responses API become successful responses
        return errorCode.Contains("context_length_exceeded", StringComparison.OrdinalIgnoreCase) ||
               errorCode.Contains("max_tokens_exceeded", StringComparison.OrdinalIgnoreCase) ||
               errorCode.Contains("token_limit_exceeded", StringComparison.OrdinalIgnoreCase) ||
               errorCode.Contains("string_too_long", StringComparison.OrdinalIgnoreCase);
    }
}
