namespace HPD.Agent.ErrorHandling;

/// <summary>
/// Immutable, serialization-safe provider error details for public events.
/// </summary>
public sealed record ProviderErrorInfo
{
    /// <summary>Gets the provider or framework error code.</summary>
    public required string Code { get; init; }

    /// <summary>Gets the human-readable error message.</summary>
    public required string Message { get; init; }

    /// <summary>Gets the normalized error category.</summary>
    public ErrorCategory Category { get; init; } = ErrorCategory.Unknown;

    /// <summary>Gets whether a new attempt may succeed without changing configuration.</summary>
    public bool IsRetryable { get; init; }

    /// <summary>Gets the HTTP status code, when supplied by the provider.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Gets the provider-specific error type, when available.</summary>
    public string? ErrorType { get; init; }

    /// <summary>Gets the provider request identifier for support and tracing.</summary>
    public string? RequestId { get; init; }

    /// <summary>Gets the provider-requested retry delay, when available.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Creates public error details from parsed provider details.</summary>
    public static ProviderErrorInfo From(ProviderErrorDetails details, string fallbackCode)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackCode);

        return new ProviderErrorInfo
        {
            Code = string.IsNullOrWhiteSpace(details.ErrorCode) ? fallbackCode : details.ErrorCode!,
            Message = details.Message,
            Category = details.Category,
            IsRetryable = details.Category is
                ErrorCategory.RateLimitRetryable or
                ErrorCategory.ServerError or
                ErrorCategory.Transient,
            StatusCode = details.StatusCode,
            ErrorType = details.ErrorType,
            RequestId = details.RequestId,
            RetryAfter = details.RetryAfter
        };
    }

    /// <summary>Creates public error details from an exception using the supplied or generic classifier.</summary>
    public static ProviderErrorInfo FromException(
        Exception exception,
        string fallbackCode,
        IProviderErrorHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        handler ??= new GenericErrorHandler();
        var details = handler.ParseError(exception) ?? new ProviderErrorDetails
        {
            Category = ErrorCategory.Unknown,
            Message = exception.Message
        };
        return From(details, fallbackCode);
    }
}
