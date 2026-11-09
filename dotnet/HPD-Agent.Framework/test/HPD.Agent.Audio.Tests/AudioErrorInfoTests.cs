using System.Net;
using HPD.Agent.Audio;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.Audio.Meai;

namespace HPD.Agent.Audio.Tests;

public sealed class AudioErrorInfoTests
{
    [Fact]
    public void FromException_PopulatesSharedProviderClassification()
    {
        var error = AudioErrorInfo.FromException(
            new HttpRequestException(
                "invalid API key",
                inner: null,
                HttpStatusCode.Unauthorized),
            "AudioAuthenticationFailed",
            "TextToSpeech");

        Assert.Equal("AudioAuthenticationFailed", error.Code);
        Assert.Equal("TextToSpeech", error.Category);
        Assert.Equal(ErrorCategory.AuthError, error.ErrorCategory);
        Assert.Equal(401, error.StatusCode);
        Assert.False(error.IsRetryable);

        var shared = error.ToProviderErrorInfo();
        Assert.Equal(error.Code, shared.Code);
        Assert.Equal(error.ErrorCategory, shared.Category);
        Assert.Equal(error.StatusCode, shared.StatusCode);
    }

    [Fact]
    public void ProviderServerError_ExposesNormalizedFieldsWithoutMetadataDigging()
    {
        var error = MeaiAudioErrorMapper.FromProviderServerError(
            "rate_limit_exceeded",
            "Rate limit exceeded.",
            "retry later",
            "AudioFailed",
            "Audio request failed.",
            "SpeechToText");

        Assert.Equal("rate_limit_exceeded", error.Code);
        Assert.Equal(ErrorCategory.RateLimitRetryable, error.ErrorCategory);
        Assert.True(error.IsRetryable);
    }

    [Fact]
    public void FromException_UsesProviderHandlerForOutputErrors()
    {
        var error = AudioErrorInfo.FromException(
            new InvalidOperationException("provider wrapped failure"),
            "TtsFailed",
            "TextToSpeech",
            errorHandler: new FixedProviderErrorHandler());

        Assert.Equal("rate_limit_exceeded", error.Code);
        Assert.Equal(ErrorCategory.RateLimitRetryable, error.ErrorCategory);
        Assert.Equal(429, error.StatusCode);
        Assert.Equal("request-1", error.RequestId);
        Assert.Equal(TimeSpan.FromSeconds(3), error.RetryAfter);
        Assert.True(error.IsRetryable);
    }

    private sealed class FixedProviderErrorHandler : IProviderErrorHandler
    {
        public ProviderErrorDetails? ParseError(Exception exception) => new()
        {
            Category = ErrorCategory.RateLimitRetryable,
            ErrorCode = "rate_limit_exceeded",
            Message = "Rate limit exceeded.",
            StatusCode = 429,
            RequestId = "request-1",
            RetryAfter = TimeSpan.FromSeconds(3)
        };

        public TimeSpan? GetRetryDelay(
            ProviderErrorDetails details,
            int attempt,
            TimeSpan initialDelay,
            double multiplier,
            TimeSpan maxDelay) => details.RetryAfter;

        public bool RequiresSpecialHandling(ProviderErrorDetails details) => false;
    }
}
