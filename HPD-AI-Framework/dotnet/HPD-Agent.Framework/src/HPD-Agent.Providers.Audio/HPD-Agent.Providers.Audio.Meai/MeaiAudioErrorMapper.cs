using HPD.Agent.Audio;
using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Providers.Audio.Meai;

internal static class MeaiAudioErrorMapper
{
    public static AudioErrorInfo FromException(
        Exception exception,
        IProviderErrorHandler? errorHandler,
        string fallbackCode,
        string fallbackMessagePrefix,
        string category,
        bool fallbackRetryable)
    {
        var details = errorHandler?.ParseError(exception);
        if (details is not null)
        {
            return FromProviderDetails(details, fallbackCode, category);
        }

        return new AudioErrorInfo
        {
            Code = fallbackCode,
            Message = $"{fallbackMessagePrefix}: {exception.Message}",
            Category = category,
            IsRetryable = fallbackRetryable
        };
    }

    public static AudioErrorInfo FromProviderServerError(
        string? code,
        string? message,
        string? details,
        string fallbackCode,
        string fallbackMessage,
        string category)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(code) ? fallbackCode : code!;
        var normalizedMessage = string.IsNullOrWhiteSpace(message) ? fallbackMessage : message!;
        var errorCategory = ClassifyByMessage(normalizedMessage, normalizedCode);
        var metadata = new Dictionary<string, object?>
        {
            ["providerErrorCategory"] = errorCategory.ToString()
        };

        if (!string.IsNullOrWhiteSpace(details))
        {
            metadata["details"] = details;
        }

        return new AudioErrorInfo
        {
            Code = normalizedCode,
            Message = normalizedMessage,
            Category = category,
            IsRetryable = IsRetryable(errorCategory),
            Metadata = new AudioExtensionData(metadata)
        };
    }

    private static AudioErrorInfo FromProviderDetails(
        ProviderErrorDetails details,
        string fallbackCode,
        string category)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["providerErrorCategory"] = details.Category.ToString()
        };

        AddIfNotNull(metadata, "statusCode", details.StatusCode);
        AddIfNotNull(metadata, "errorCode", details.ErrorCode);
        AddIfNotNull(metadata, "errorType", details.ErrorType);
        AddIfNotNull(metadata, "requestId", details.RequestId);
        AddIfNotNull(metadata, "retryAfter", details.RetryAfter?.ToString());

        if (details.RawDetails is not null)
        {
            foreach (var item in details.RawDetails)
            {
                metadata[$"raw.{item.Key}"] = item.Value;
            }
        }

        return new AudioErrorInfo
        {
            Code = string.IsNullOrWhiteSpace(details.ErrorCode) ? fallbackCode : details.ErrorCode!,
            Message = details.Message,
            Category = category,
            IsRetryable = IsRetryable(details.Category),
            Metadata = new AudioExtensionData(metadata)
        };
    }

    private static ErrorCategory ClassifyByMessage(string message, string? errorCode)
    {
        if (ModelNotFoundDetector.IsModelNotFoundError(status: null, message, errorCode, errorType: null))
        {
            return ErrorCategory.ModelNotFound;
        }

        var lowerMessage = message.ToLowerInvariant();

        if (errorCode == "invalid_api_key" ||
            lowerMessage.Contains("unauthorized") ||
            lowerMessage.Contains("authentication") ||
            lowerMessage.Contains("api key") ||
            lowerMessage.Contains("invalid token"))
        {
            return ErrorCategory.AuthError;
        }

        if (errorCode == "insufficient_quota" ||
            lowerMessage.Contains("insufficient_quota") ||
            lowerMessage.Contains("exceeded your current quota") ||
            lowerMessage.Contains("quota has been exceeded"))
        {
            return ErrorCategory.RateLimitTerminal;
        }

        if (errorCode == "rate_limit_exceeded" ||
            lowerMessage.Contains("rate limit") ||
            lowerMessage.Contains("too many requests") ||
            lowerMessage.Contains("throttl"))
        {
            return ErrorCategory.RateLimitRetryable;
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

    private static bool IsRetryable(ErrorCategory category)
        => category is ErrorCategory.RateLimitRetryable or
            ErrorCategory.ServerError or
            ErrorCategory.Transient or
            ErrorCategory.Unknown;

    private static void AddIfNotNull(
        Dictionary<string, object?> metadata,
        string key,
        object? value)
    {
        if (value is not null)
        {
            metadata[key] = value;
        }
    }
}
