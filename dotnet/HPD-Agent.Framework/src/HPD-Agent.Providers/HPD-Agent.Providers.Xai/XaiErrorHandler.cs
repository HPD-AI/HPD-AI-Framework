using System.Net.Http;
using System.Text.RegularExpressions;
using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Providers.Xai;

internal sealed partial class XaiErrorHandler : IProviderErrorHandler
{
    [GeneratedRegex(@"""(?:code|error_code|type)""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorCodePattern();

    [GeneratedRegex(@"Status:\s*(\d{3})", RegexOptions.IgnoreCase)]
    private static partial Regex StatusPattern();

    public ProviderErrorDetails? ParseError(Exception exception)
    {
        if (exception is HttpRequestException httpException)
        {
            var statusCode = (int?)httpException.StatusCode ?? ExtractStatusCode(httpException.Message);
            return new ProviderErrorDetails
            {
                StatusCode = statusCode,
                Category = ClassifyError(statusCode, httpException.Message),
                Message = httpException.Message,
                ErrorCode = ExtractErrorCode(httpException.Message)
            };
        }

        var parsedStatus = ExtractStatusCode(exception.Message);
        var errorCode = ExtractErrorCode(exception.Message);
        if (parsedStatus.HasValue || errorCode is not null)
        {
            return new ProviderErrorDetails
            {
                StatusCode = parsedStatus,
                Category = ClassifyError(parsedStatus, exception.Message),
                Message = exception.Message,
                ErrorCode = errorCode
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
            var expDelayMs = initialDelay.TotalMilliseconds * Math.Pow(multiplier, attempt);
            return TimeSpan.FromMilliseconds(Math.Min(expDelayMs, maxDelay.TotalMilliseconds));
        }

        return null;
    }

    public bool RequiresSpecialHandling(ProviderErrorDetails details)
        => details.Category == ErrorCategory.AuthError;

    private static ErrorCategory ClassifyError(int? statusCode, string message)
    {
        var errorCode = ExtractErrorCode(message);
        if (ModelNotFoundDetector.IsModelNotFoundError(statusCode, message, errorCode, errorType: errorCode))
        {
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
                     message.Contains("credits", StringComparison.OrdinalIgnoreCase) => ErrorCategory.RateLimitTerminal,
            429 => ErrorCategory.RateLimitRetryable,
            >= 500 => ErrorCategory.ServerError,
            >= 400 => ErrorCategory.ClientError,
            _ => ErrorCategory.Unknown
        };
    }

    private static int? ExtractStatusCode(string message)
    {
        var match = StatusPattern().Match(message);
        return match.Success && int.TryParse(match.Groups[1].Value, out var status)
            ? status
            : null;
    }

    private static string? ExtractErrorCode(string message)
    {
        var match = ErrorCodePattern().Match(message);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        if (message.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase))
            return "invalid_api_key";

        if (message.Contains("model_not_found", StringComparison.OrdinalIgnoreCase))
            return "model_not_found";

        return null;
    }
}
