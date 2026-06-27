using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Providers.Moonshot;

internal sealed class MoonshotErrorHandler : IProviderErrorHandler
{
    public ProviderErrorDetails? ParseError(Exception exception)
    {
        if (exception is HttpRequestException httpException)
        {
            var statusCode = (int?)httpException.StatusCode;
            var errorCode = ExtractErrorCode(httpException.Message);
            return new ProviderErrorDetails
            {
                StatusCode = statusCode,
                Category = ClassifyError(statusCode, httpException.Message, errorCode),
                Message = ExtractMessage(httpException.Message) ?? httpException.Message,
                ErrorCode = errorCode
            };
        }

        var parsedStatus = ExtractStatusCode(exception.Message);
        var parsedCode = ExtractErrorCode(exception.Message);
        if (parsedStatus.HasValue || parsedCode is not null)
        {
            return new ProviderErrorDetails
            {
                StatusCode = parsedStatus,
                Category = ClassifyError(parsedStatus, exception.Message, parsedCode),
                Message = ExtractMessage(exception.Message) ?? exception.Message,
                ErrorCode = parsedCode
            };
        }

        return null;
    }

    public TimeSpan? GetRetryDelay(
        ProviderErrorDetails details,
        int attempt,
        TimeSpan initialDelay,
        double multiplier,
        TimeSpan maxDelay)
    {
        if (details.RetryAfter.HasValue)
        {
            return details.RetryAfter.Value;
        }

        if (details.Category is ErrorCategory.RateLimitRetryable or ErrorCategory.ServerError or ErrorCategory.Transient)
        {
            var delayMs = initialDelay.TotalMilliseconds * Math.Pow(multiplier, attempt);
            return TimeSpan.FromMilliseconds(Math.Min(delayMs, maxDelay.TotalMilliseconds));
        }

        return null;
    }

    public bool RequiresSpecialHandling(ProviderErrorDetails details)
        => details.Category == ErrorCategory.AuthError;

    private static ErrorCategory ClassifyError(int? statusCode, string message, string? errorCode)
    {
        if (ModelNotFoundDetector.IsModelNotFoundError(statusCode, message, errorCode, errorType: null))
        {
            return ErrorCategory.ModelNotFound;
        }

        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            if (errorCode.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Contains("permission", StringComparison.OrdinalIgnoreCase))
                return ErrorCategory.AuthError;

            if (errorCode.Contains("rate", StringComparison.OrdinalIgnoreCase))
                return ErrorCategory.RateLimitRetryable;

            if (errorCode.Contains("model_not_found", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Contains("invalid_model", StringComparison.OrdinalIgnoreCase))
                return ErrorCategory.ModelNotFound;
        }

        return statusCode switch
        {
            400 or 422 when message.Contains("context", StringComparison.OrdinalIgnoreCase) &&
                            (message.Contains("length", StringComparison.OrdinalIgnoreCase) ||
                             message.Contains("too long", StringComparison.OrdinalIgnoreCase)) => ErrorCategory.ContextWindow,
            401 or 403 => ErrorCategory.AuthError,
            404 => ErrorCategory.ModelNotFound,
            408 or 504 => ErrorCategory.Transient,
            429 when message.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
                     message.Contains("billing", StringComparison.OrdinalIgnoreCase) ||
                     message.Contains("balance", StringComparison.OrdinalIgnoreCase) => ErrorCategory.RateLimitTerminal,
            429 => ErrorCategory.RateLimitRetryable,
            >= 500 => ErrorCategory.ServerError,
            >= 400 => ErrorCategory.ClientError,
            _ => ErrorCategory.Unknown
        };
    }

    private static int? ExtractStatusCode(string message)
    {
        var match = Regex.Match(message, @"Status:\s*(\d{3})", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var status)
            ? status
            : null;
    }

    private static string? ExtractErrorCode(string message)
    {
        var match = Regex.Match(message, @"""(?:code|type)""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        if (message.Contains("invalid api key", StringComparison.OrdinalIgnoreCase))
        {
            return "invalid_api_key";
        }

        if (message.Contains("model not found", StringComparison.OrdinalIgnoreCase))
        {
            return "model_not_found";
        }

        return null;
    }

    private static string? ExtractMessage(string message)
    {
        var match = Regex.Match(message, @"""message""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }
}
