using System.ClientModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Providers.Audio.OpenAI;

internal sealed partial class OpenAIAudioErrorHandler : IProviderErrorHandler
{
    [GeneratedRegex(@"Status:\s*(\d{3})", RegexOptions.IgnoreCase)]
    private static partial Regex StatusPattern();

    [GeneratedRegex(@"\bHTTP\s+(\d{3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex HttpStatusPattern();

    [GeneratedRegex(@"\((\d{3})\)")]
    private static partial Regex ParenthesesStatusPattern();

    [GeneratedRegex(@"""code"":\s*""([^""]+)""")]
    private static partial Regex ErrorCodePattern();

    [GeneratedRegex(@"""type"":\s*""([^""]+)""")]
    private static partial Regex ErrorTypePattern();

    [GeneratedRegex(@"""param"":\s*(?:""([^""]+)""|null)")]
    private static partial Regex ErrorParamPattern();

    [GeneratedRegex(@"""event_id"":\s*""([^""]+)""")]
    private static partial Regex EventIdPattern();

    [GeneratedRegex(@"try again in ([\d.]+)(s|ms)", RegexOptions.IgnoreCase)]
    private static partial Regex RetryDelayPattern();

    [GeneratedRegex(@"Request[-\s]?Id:\s*([a-zA-Z0-9_\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RequestIdPattern();

    public ProviderErrorDetails? ParseError(Exception exception)
    {
        var exceptionTypeName = exception.GetType().FullName;
        if (exception is not ClientResultException &&
            exceptionTypeName != "Azure.RequestFailedException")
        {
            return null;
        }

        var message = exception.Message;
        var status = ExtractStatusCodeFromException(exception)
            ?? ExtractStatusCodeFromMessage(message);
        var parsed = TryParseErrorBody(message);
        var errorCode = FirstNonWhiteSpace(parsed.Code, ExtractErrorCode(message));
        var errorType = FirstNonWhiteSpace(parsed.Type, ExtractErrorType(message));
        var rawDetails = BuildRawDetails(message, parsed);

        return new ProviderErrorDetails
        {
            StatusCode = status,
            Category = ClassifyError(status, message, errorCode, errorType),
            Message = message,
            ErrorCode = errorCode,
            ErrorType = errorType,
            RequestId = ExtractRequestId(message),
            RetryAfter = ExtractRetryDelay(message),
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

    private static int? ExtractStatusCodeFromException(Exception exception)
    {
        if (exception is ClientResultException clientResultException)
        {
            return clientResultException.Status;
        }

        return null;
    }

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

    private static string? ExtractErrorCode(string message)
    {
        var codeMatch = ErrorCodePattern().Match(message);
        if (codeMatch.Success)
        {
            return codeMatch.Groups[1].Value;
        }

        if (message.Contains("rate_limit_exceeded", StringComparison.OrdinalIgnoreCase))
        {
            return "rate_limit_exceeded";
        }

        if (message.Contains("context_length_exceeded", StringComparison.OrdinalIgnoreCase))
        {
            return "context_length_exceeded";
        }

        if (message.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase))
        {
            return "insufficient_quota";
        }

        if (message.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase))
        {
            return "invalid_api_key";
        }

        if (message.Contains("model_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return "model_not_found";
        }

        if (message.Contains("content_filter", StringComparison.OrdinalIgnoreCase))
        {
            return "content_filter";
        }

        return null;
    }

    private static string? ExtractErrorType(string message)
    {
        var typeMatch = ErrorTypePattern().Match(message);
        return typeMatch.Success ? typeMatch.Groups[1].Value : null;
    }

    private static string? ExtractErrorParam(string message)
    {
        var paramMatch = ErrorParamPattern().Match(message);
        return paramMatch.Success && paramMatch.Groups[1].Success
            ? paramMatch.Groups[1].Value
            : null;
    }

    private static string[] ExtractEventIds(string message)
    {
        var eventIdMatches = EventIdPattern().Matches(message);
        if (eventIdMatches.Count == 0)
        {
            return [];
        }

        return eventIdMatches
            .Select(match => match.Groups[1].Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string? ExtractRequestId(string message)
    {
        var requestIdMatch = RequestIdPattern().Match(message);
        return requestIdMatch.Success ? requestIdMatch.Groups[1].Value : null;
    }

    private static TimeSpan? ExtractRetryDelay(string message)
    {
        var match = RetryDelayPattern().Match(message);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, out var value))
        {
            return null;
        }

        return match.Groups[2].Value == "s"
            ? TimeSpan.FromSeconds(value)
            : TimeSpan.FromMilliseconds(value);
    }

    private static OpenAIErrorBody TryParseErrorBody(string message)
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
            var error = root.TryGetProperty("error", out var errorElement)
                ? errorElement
                : root;

            return new OpenAIErrorBody(
                Type: GetString(error, "type"),
                Code: GetString(error, "code"),
                Param: GetString(error, "param"));
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

    private static Dictionary<string, object> BuildRawDetails(string message, OpenAIErrorBody parsed)
    {
        var rawDetails = new Dictionary<string, object>();
        AddIfNotNull(rawDetails, "param", FirstNonWhiteSpace(parsed.Param, ExtractErrorParam(message)));

        var eventIds = ExtractEventIds(message);
        if (eventIds.Length == 1)
        {
            rawDetails["eventId"] = eventIds[0];
        }
        else if (eventIds.Length > 1)
        {
            rawDetails["eventIds"] = eventIds;
        }

        return rawDetails;
    }

    private static void AddIfNotNull(Dictionary<string, object> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value;
        }
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

    private static ErrorCategory ClassifyError(
        int? status,
        string message,
        string? errorCode,
        string? errorType)
    {
        if (ModelNotFoundDetector.IsModelNotFoundError(status, message, errorCode, errorType))
        {
            return ErrorCategory.ModelNotFound;
        }

        return status switch
        {
            400 => ClassifyBadRequest(message, errorCode),
            404 => ErrorCategory.ClientError,
            401 or 403 => ErrorCategory.AuthError,
            408 => ErrorCategory.Transient,
            429 => ClassifyRateLimit(message, errorCode),
            500 or 502 => ErrorCategory.ServerError,
            503 or 504 => ErrorCategory.Transient,
            >= 500 and < 600 => ErrorCategory.ServerError,
            _ => ClassifyByMessage(message, errorCode)
        };
    }

    private static ErrorCategory ClassifyBadRequest(string message, string? errorCode)
    {
        if (errorCode == "context_length_exceeded" ||
            message.Contains("context_length_exceeded", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("maximum context length", StringComparison.OrdinalIgnoreCase))
        {
            return ErrorCategory.ContextWindow;
        }

        return ErrorCategory.ClientError;
    }

    private static ErrorCategory ClassifyRateLimit(string message, string? errorCode)
    {
        if (errorCode == "insufficient_quota" ||
            message.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("exceeded your current quota", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("quota has been exceeded", StringComparison.OrdinalIgnoreCase))
        {
            return ErrorCategory.RateLimitTerminal;
        }

        return ErrorCategory.RateLimitRetryable;
    }

    private static ErrorCategory ClassifyByMessage(string message, string? errorCode)
    {
        var lowerMessage = message.ToLowerInvariant();

        if (errorCode == "invalid_api_key" ||
            lowerMessage.Contains("unauthorized") ||
            lowerMessage.Contains("authentication") ||
            lowerMessage.Contains("api key") ||
            lowerMessage.Contains("invalid token"))
        {
            return ErrorCategory.AuthError;
        }

        if (errorCode == "rate_limit_exceeded" ||
            errorCode == "insufficient_quota" ||
            lowerMessage.Contains("rate limit") ||
            lowerMessage.Contains("insufficient_quota") ||
            lowerMessage.Contains("exceeded your current quota") ||
            lowerMessage.Contains("quota has been exceeded") ||
            lowerMessage.Contains("too many requests") ||
            lowerMessage.Contains("throttl"))
        {
            return ClassifyRateLimit(message, errorCode);
        }

        if (errorCode == "context_length_exceeded" ||
            lowerMessage.Contains("context_length_exceeded") ||
            lowerMessage.Contains("maximum context length"))
        {
            return ErrorCategory.ContextWindow;
        }

        if (lowerMessage.Contains("timeout") ||
            lowerMessage.Contains("temporary") ||
            lowerMessage.Contains("unavailable") ||
            lowerMessage.Contains("try again") ||
            lowerMessage.Contains("connection") ||
            lowerMessage.Contains("network"))
        {
            return ErrorCategory.Transient;
        }

        if (lowerMessage.Contains("invalid") ||
            lowerMessage.Contains("bad request") ||
            lowerMessage.Contains("malformed") ||
            lowerMessage.Contains("validation"))
        {
            return ErrorCategory.ClientError;
        }

        return ErrorCategory.Unknown;
    }

    private readonly record struct OpenAIErrorBody(
        string? Type,
        string? Code,
        string? Param);
}
