using HPD.Agent.ErrorHandling;

namespace HPD.Agent.Audio;

public sealed record AudioErrorInfo
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    /// <summary>Gets the audio subsystem label, such as TextToSpeech or Playback.</summary>
    public string? Category { get; init; }

    public bool IsRetryable { get; init; }

    /// <summary>Gets the normalized provider failure category.</summary>
    public ErrorCategory ErrorCategory { get; init; } = HPD.Agent.ErrorHandling.ErrorCategory.Unknown;

    /// <summary>Gets the HTTP status code, when supplied by the provider.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Gets the provider error type, when available.</summary>
    public string? ErrorType { get; init; }

    /// <summary>Gets the provider request identifier for support and tracing.</summary>
    public string? RequestId { get; init; }

    /// <summary>Gets the provider-requested retry delay, when available.</summary>
    public TimeSpan? RetryAfter { get; init; }

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;

    /// <summary>Projects this audio-specific error into the shared provider error contract.</summary>
    public ProviderErrorInfo ToProviderErrorInfo() => new()
    {
        Code = Code,
        Message = Message,
        Category = ErrorCategory,
        IsRetryable = IsRetryable,
        StatusCode = StatusCode,
        ErrorType = ErrorType,
        RequestId = RequestId,
        RetryAfter = RetryAfter
    };

    /// <summary>Creates normalized audio error details from an exception.</summary>
    public static AudioErrorInfo FromException(
        Exception exception,
        string fallbackCode,
        string category,
        bool fallbackRetryable = false,
        IProviderErrorHandler? errorHandler = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackCode);

        errorHandler ??= new GenericErrorHandler();
        var details = errorHandler.ParseError(exception);
        return new AudioErrorInfo
        {
            Code = string.IsNullOrWhiteSpace(details?.ErrorCode) ? fallbackCode : details!.ErrorCode!,
            Message = details?.Message ?? exception.Message,
            Category = category,
            IsRetryable = details is null ||
                details.Category == HPD.Agent.ErrorHandling.ErrorCategory.Unknown
                ? fallbackRetryable
                : details.Category is
                    HPD.Agent.ErrorHandling.ErrorCategory.RateLimitRetryable or
                    HPD.Agent.ErrorHandling.ErrorCategory.ServerError or
                    HPD.Agent.ErrorHandling.ErrorCategory.Transient,
            ErrorCategory = details?.Category ?? HPD.Agent.ErrorHandling.ErrorCategory.Unknown,
            StatusCode = details?.StatusCode,
            ErrorType = details?.ErrorType,
            RequestId = details?.RequestId,
            RetryAfter = details?.RetryAfter
        };
    }
}
