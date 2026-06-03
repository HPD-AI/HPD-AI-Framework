using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

internal sealed partial class ElevenLabsErrorHandler : IProviderErrorHandler
{
    [GeneratedRegex(@"Status:\s*(\d{3})", RegexOptions.IgnoreCase)]
    private static partial Regex StatusPattern();

    [GeneratedRegex(@"\bHTTP\s+(\d{3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex HttpStatusPattern();

    [GeneratedRegex(@"\((\d{3})\)")]
    private static partial Regex ParenthesesStatusPattern();

    [GeneratedRegex(@"""code"":\s*""([^""]+)""")]
    private static partial Regex ErrorCodePattern();

    [GeneratedRegex(@"""status"":\s*""([^""]+)""")]
    private static partial Regex LegacyStatusPattern();

    [GeneratedRegex(@"""type"":\s*""([^""]+)""")]
    private static partial Regex ErrorTypePattern();

    [GeneratedRegex(@"""message"":\s*""([^""]+)""")]
    private static partial Regex ErrorMessagePattern();

    [GeneratedRegex(@"""request_id"":\s*""([^""]+)""")]
    private static partial Regex RequestIdPattern();

    [GeneratedRegex(@"""param"":\s*""([^""]+)""")]
    private static partial Regex ParamPattern();

    public ProviderErrorDetails? ParseError(Exception exception)
    {
        if (exception is not HttpRequestException and not WebSocketException and not TaskCanceledException)
        {
            return null;
        }

        var message = exception.Message;
        var status = ExtractStatusCode(exception) ?? ExtractStatusCodeFromMessage(message);
        var parsed = TryParseErrorBody(message);
        var errorCode = FirstNonWhiteSpace(parsed.Code, ExtractErrorCode(message), parsed.LegacyStatus);
        var errorType = FirstNonWhiteSpace(parsed.Type, ExtractErrorType(message));
        var providerMessage = FirstNonWhiteSpace(parsed.Message, ExtractErrorMessage(message), message)!;
        var requestId = FirstNonWhiteSpace(parsed.RequestId, ExtractRequestId(message));
        var param = FirstNonWhiteSpace(parsed.Param, ExtractParam(message));

        var rawDetails = new Dictionary<string, object>();
        AddIfNotNull(rawDetails, "legacyStatus", parsed.LegacyStatus);
        AddIfNotNull(rawDetails, "param", param);

        return new ProviderErrorDetails
        {
            StatusCode = status,
            Category = ClassifyError(status, providerMessage, errorCode, errorType),
            Message = providerMessage,
            ErrorCode = errorCode,
            ErrorType = errorType,
            RequestId = requestId,
            RawDetails = rawDetails.Count == 0 ? null : rawDetails
        };
    }

    public TimeSpan? GetRetryDelay(
        ProviderErrorDetails details,
        int attempt,
        TimeSpan initialDelay,
        double multiplier,
        TimeSpan maxDelay)
    {
        if (details.Category is ErrorCategory.ClientError or
            ErrorCategory.ContextWindow or
            ErrorCategory.RateLimitTerminal or
            ErrorCategory.AuthError or
            ErrorCategory.ModelNotFound)
        {
            return null;
        }

        if (details.RetryAfter.HasValue)
        {
            return details.RetryAfter.Value;
        }

        if (details.Category is not (ErrorCategory.RateLimitRetryable or ErrorCategory.ServerError or ErrorCategory.Transient))
        {
            return null;
        }

        var baseMs = initialDelay.TotalMilliseconds;
        var expDelayMs = baseMs * Math.Pow(multiplier, attempt);
        var cappedDelayMs = Math.Min(expDelayMs, maxDelay.TotalMilliseconds);
        var jitter = 0.9 + (Random.Shared.NextDouble() * 0.2);
        return TimeSpan.FromMilliseconds(cappedDelayMs * jitter);
    }

    public bool RequiresSpecialHandling(ProviderErrorDetails details)
        => details.Category == ErrorCategory.AuthError;

    private static int? ExtractStatusCode(Exception exception)
        => exception switch
        {
            HttpRequestException http when http.StatusCode.HasValue => (int)http.StatusCode.Value,
            WebSocketException => null,
            TaskCanceledException => 408,
            _ => null
        };

    private static int? ExtractStatusCodeFromMessage(string message)
    {
        var statusMatch = StatusPattern().Match(message);
        if (statusMatch.Success && int.TryParse(statusMatch.Groups[1].Value, out var statusCode))
        {
            return statusCode;
        }

        var httpStatusMatch = HttpStatusPattern().Match(message);
        if (httpStatusMatch.Success && int.TryParse(httpStatusMatch.Groups[1].Value, out var httpStatusCode))
        {
            return httpStatusCode;
        }

        var parenthesesMatch = ParenthesesStatusPattern().Match(message);
        if (parenthesesMatch.Success && int.TryParse(parenthesesMatch.Groups[1].Value, out var parenStatusCode))
        {
            return parenStatusCode;
        }

        return null;
    }

    private static ElevenLabsErrorBody TryParseErrorBody(string message)
    {
        var start = message.IndexOf('{', StringComparison.Ordinal);
        var end = message.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return default;
        }

        try
        {
            using var document = JsonDocument.Parse(message[start..(end + 1)]);
            var root = document.RootElement;
            var detail = root.TryGetProperty("detail", out var detailElement)
                ? detailElement
                : root;

            return new ElevenLabsErrorBody
            {
                Type = GetString(detail, "type"),
                Code = GetString(detail, "code"),
                LegacyStatus = GetString(detail, "status"),
                Message = GetString(detail, "message"),
                RequestId = GetString(detail, "request_id"),
                Param = GetString(detail, "param")
            };
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? ExtractErrorCode(string message)
    {
        var codeMatch = ErrorCodePattern().Match(message);
        if (codeMatch.Success)
        {
            return codeMatch.Groups[1].Value;
        }

        var statusMatch = LegacyStatusPattern().Match(message);
        if (statusMatch.Success)
        {
            return statusMatch.Groups[1].Value;
        }

        foreach (var known in KnownErrorCodes)
        {
            if (message.Contains(known, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return null;
    }

    private static string? ExtractErrorType(string message)
    {
        var typeMatch = ErrorTypePattern().Match(message);
        return typeMatch.Success ? typeMatch.Groups[1].Value : null;
    }

    private static string? ExtractErrorMessage(string message)
    {
        var match = ErrorMessagePattern().Match(message);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractRequestId(string message)
    {
        var match = RequestIdPattern().Match(message);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractParam(string message)
    {
        var match = ParamPattern().Match(message);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static ErrorCategory ClassifyError(
        int? status,
        string message,
        string? errorCode,
        string? errorType)
    {
        if (IsTerminalQuota(status, message, errorCode, errorType))
        {
            return ErrorCategory.RateLimitTerminal;
        }

        if (IsAuth(status, message, errorCode, errorType))
        {
            return ErrorCategory.AuthError;
        }

        if (IsTextLengthError(message, errorCode))
        {
            return ErrorCategory.ContextWindow;
        }

        if (IsRetryableRateLimit(status, message, errorCode, errorType))
        {
            return ErrorCategory.RateLimitRetryable;
        }

        return status switch
        {
            400 or 409 or 422 => ErrorCategory.ClientError,
            401 or 403 => ErrorCategory.AuthError,
            402 => ErrorCategory.RateLimitTerminal,
            404 => ErrorCategory.ClientError,
            408 => ErrorCategory.Transient,
            429 => ErrorCategory.RateLimitRetryable,
            500 or 502 => ErrorCategory.ServerError,
            503 or 504 => ErrorCategory.Transient,
            >= 500 and < 600 => ErrorCategory.ServerError,
            _ => IsTransient(message, errorCode, errorType)
                ? ErrorCategory.Transient
                : ClassifyByMessage(message, errorCode, errorType)
        };
    }

    private static ErrorCategory ClassifyByMessage(
        string message,
        string? errorCode,
        string? errorType)
    {
        if (IsAuth(status: null, message, errorCode, errorType))
        {
            return ErrorCategory.AuthError;
        }

        if (IsTerminalQuota(status: null, message, errorCode, errorType))
        {
            return ErrorCategory.RateLimitTerminal;
        }

        if (IsRetryableRateLimit(status: null, message, errorCode, errorType))
        {
            return ErrorCategory.RateLimitRetryable;
        }

        if (IsTransient(message, errorCode, errorType))
        {
            return ErrorCategory.Transient;
        }

        var lower = message.ToLowerInvariant();
        return lower.Contains("invalid") ||
            lower.Contains("malformed") ||
            lower.Contains("missing") ||
            lower.Contains("not found") ||
            lower.Contains("unsupported")
            ? ErrorCategory.ClientError
            : ErrorCategory.Unknown;
    }

    private static bool IsAuth(
        int? status,
        string message,
        string? errorCode,
        string? errorType)
    {
        var lower = message.ToLowerInvariant();
        return status is 401 or 403 ||
            Matches(errorType, "authentication_error", "authorization_error") ||
            Matches(errorCode, "invalid_api_key", "missing_api_key", "invalid_authorization_header",
                "unauthorized", "sign_in_required", "forbidden", "insufficient_permissions",
                "workspace_access_denied", "feature_not_available", "subscription_required",
                "voice_access_denied") ||
            lower.Contains("api key") ||
            lower.Contains("unauthorized") ||
            lower.Contains("forbidden");
    }

    private static bool IsTerminalQuota(
        int? status,
        string message,
        string? errorCode,
        string? errorType)
    {
        var lower = message.ToLowerInvariant();
        return status == 402 ||
            Matches(errorType, "payment_required") ||
            Matches(errorCode, "quota_exceeded", "payment_required", "insufficient_credits") ||
            lower.Contains("quota_exceeded") ||
            lower.Contains("insufficient quota") ||
            lower.Contains("insufficient credits") ||
            lower.Contains("payment required");
    }

    private static bool IsRetryableRateLimit(
        int? status,
        string message,
        string? errorCode,
        string? errorType)
    {
        var lower = message.ToLowerInvariant();
        return status == 429 ||
            Matches(errorType, "rate_limit_error") ||
            Matches(errorCode, "rate_limit_exceeded", "concurrent_limit_exceeded",
                "too_many_concurrent_requests") ||
            lower.Contains("rate limit") ||
            lower.Contains("too many concurrent requests") ||
            lower.Contains("concurrent_limit_exceeded");
    }

    private static bool IsTransient(string message, string? errorCode, string? errorType)
    {
        var lower = message.ToLowerInvariant();
        return Matches(errorType, "internal_error", "service_unavailable") ||
            Matches(errorCode, "system_busy", "service_unavailable") ||
            lower.Contains("system_busy") ||
            lower.Contains("service unavailable") ||
            lower.Contains("temporarily unavailable") ||
            lower.Contains("timeout") ||
            lower.Contains("connection") ||
            lower.Contains("websocket") ||
            lower.Contains("network");
    }

    private static bool IsTextLengthError(string message, string? errorCode)
    {
        var lower = message.ToLowerInvariant();
        return Matches(errorCode, "text_too_long", "max_character_limit_exceeded") ||
            lower.Contains("text_too_long") ||
            lower.Contains("max_character_limit_exceeded") ||
            lower.Contains("character limit");
    }

    private static bool Matches(string? value, params string[] expected)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            expected.Any(item => string.Equals(value, item, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static void AddIfNotNull(Dictionary<string, object> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value;
        }
    }

    private static readonly string[] KnownErrorCodes =
    [
        "voice_not_found",
        "text_too_long",
        "text_too_short",
        "invalid_text",
        "empty_text",
        "invalid_parameters",
        "missing_required_field",
        "invalid_voice_settings",
        "invalid_voice_id",
        "unsupported_model",
        "invalid_api_key",
        "missing_api_key",
        "invalid_authorization_header",
        "unauthorized",
        "forbidden",
        "insufficient_permissions",
        "feature_not_available",
        "subscription_required",
        "voice_access_denied",
        "quota_exceeded",
        "rate_limit_exceeded",
        "concurrent_limit_exceeded",
        "too_many_concurrent_requests",
        "system_busy",
        "max_character_limit_exceeded"
    ];

    private readonly record struct ElevenLabsErrorBody(
        string? Type,
        string? Code,
        string? LegacyStatus,
        string? Message,
        string? RequestId,
        string? Param);
}
