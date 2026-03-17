using HPD.Agent.ErrorHandling;
using HPD.Agent.OpenApi;
using HPD.OpenApi.Core;

namespace HPD.Agent.OpenApi.Tests.Agent;

public class OpenApiErrorHandlerTests
{
    private readonly OpenApiErrorHandler _handler = new();

    // ────────────────────────────────────────────────────────────
    // ParseError
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseError_NonOpenApiException_ReturnsNull()
    {
        var ex = new InvalidOperationException("not an OpenAPI error");

        var result = _handler.ParseError(ex);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseError_OpenApiRequestException_ReturnsDetails()
    {
        var error = new OpenApiErrorResponse { StatusCode = 429, Body = """{"message":"Rate limited"}""" };
        var ex = new OpenApiRequestException(error);

        var result = _handler.ParseError(ex);

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(429);
        result.Message.Should().Be("Rate limited");
    }

    [Theory]
    [InlineData(429, ErrorCategory.RateLimitRetryable)]
    [InlineData(401, ErrorCategory.AuthError)]
    [InlineData(403, ErrorCategory.AuthError)]
    [InlineData(400, ErrorCategory.ClientError)]
    [InlineData(404, ErrorCategory.ClientError)]
    [InlineData(422, ErrorCategory.ClientError)]
    [InlineData(408, ErrorCategory.Transient)]
    [InlineData(500, ErrorCategory.ServerError)]
    [InlineData(503, ErrorCategory.ServerError)]
    public void ParseError_KnownStatusCode_CorrectCategory(int statusCode, ErrorCategory expectedCategory)
    {
        var error = new OpenApiErrorResponse { StatusCode = statusCode };
        var ex = new OpenApiRequestException(error);

        var result = _handler.ParseError(ex);

        result!.Category.Should().Be(expectedCategory);
    }

    [Fact]
    public void ParseError_UnknownStatusCode_UnknownCategory()
    {
        var error = new OpenApiErrorResponse { StatusCode = 418 }; // I'm a teapot
        var ex = new OpenApiRequestException(error);

        var result = _handler.ParseError(ex);

        result!.Category.Should().Be(ErrorCategory.Unknown);
    }

    [Fact]
    public void ParseError_RetryAfterPropagated()
    {
        var delay = TimeSpan.FromSeconds(30);
        var error = new OpenApiErrorResponse { StatusCode = 429, RetryAfter = delay };
        var ex = new OpenApiRequestException(error);

        var result = _handler.ParseError(ex);

        result!.RetryAfter.Should().Be(delay);
    }

    [Fact]
    public void ParseError_ErrorCodeFromReasonPhrase()
    {
        var error = new OpenApiErrorResponse { StatusCode = 429, ReasonPhrase = "Too Many Requests" };
        var ex = new OpenApiRequestException(error);

        var result = _handler.ParseError(ex);

        result!.ErrorCode.Should().Be("Too Many Requests");
    }

    // ────────────────────────────────────────────────────────────
    // GetRetryDelay
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void GetRetryDelay_ClientError_ReturnsNull()
    {
        var details = new ProviderErrorDetails { Category = ErrorCategory.ClientError };

        var delay = _handler.GetRetryDelay(details, 0, TimeSpan.FromSeconds(1), 2.0, TimeSpan.FromSeconds(60));

        delay.Should().BeNull();
    }

    [Fact]
    public void GetRetryDelay_RateLimitTerminal_ReturnsNull()
    {
        var details = new ProviderErrorDetails { Category = ErrorCategory.RateLimitTerminal };

        var delay = _handler.GetRetryDelay(details, 0, TimeSpan.FromSeconds(1), 2.0, TimeSpan.FromSeconds(60));

        delay.Should().BeNull();
    }

    [Fact]
    public void GetRetryDelay_ModelNotFound_ReturnsNull()
    {
        var details = new ProviderErrorDetails { Category = ErrorCategory.ModelNotFound };

        var delay = _handler.GetRetryDelay(details, 0, TimeSpan.FromSeconds(1), 2.0, TimeSpan.FromSeconds(60));

        delay.Should().BeNull();
    }

    [Fact]
    public void GetRetryDelay_RetryAfterPresent_ReturnsRetryAfterDelay()
    {
        var providerDelay = TimeSpan.FromSeconds(30);
        var details = new ProviderErrorDetails
        {
            Category = ErrorCategory.RateLimitRetryable,
            RetryAfter = providerDelay
        };

        var delay = _handler.GetRetryDelay(details, 0, TimeSpan.FromSeconds(1), 2.0, TimeSpan.FromSeconds(60));

        delay.Should().Be(providerDelay);
    }

    [Fact]
    public void GetRetryDelay_RetryAfterAbsentOn429_ReturnsExponentialBackoffWithJitter()
    {
        var details = new ProviderErrorDetails
        {
            Category = ErrorCategory.RateLimitRetryable,
            RetryAfter = null
        };
        var initialDelay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromSeconds(60);

        var delay = _handler.GetRetryDelay(details, attempt: 0, initialDelay, multiplier: 2.0, maxDelay);

        // With attempt 0: base = 1s, jitter 0.9..1.1 → range 0.9s..1.1s
        delay.Should().BeGreaterThan(TimeSpan.Zero);
        delay.Should().BeLessThanOrEqualTo(maxDelay);
    }

    [Fact]
    public void GetRetryDelay_HighAttempt_CappedAtMaxDelay()
    {
        var details = new ProviderErrorDetails
        {
            Category = ErrorCategory.ServerError,
            RetryAfter = null
        };
        var initialDelay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromSeconds(5);

        // attempt 100 would normally produce an astronomical delay
        var delay = _handler.GetRetryDelay(details, attempt: 100, initialDelay, multiplier: 2.0, maxDelay);

        // Must be capped at maxDelay, plus jitter (jitter is 0.9..1.1 × capped value)
        delay.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(5.5));
    }

    [Fact]
    public void GetRetryDelay_ServerError_ReturnsNonNullDelay()
    {
        var details = new ProviderErrorDetails { Category = ErrorCategory.ServerError };

        var delay = _handler.GetRetryDelay(details, 1, TimeSpan.FromSeconds(1), 2.0, TimeSpan.FromSeconds(60));

        delay.Should().BeGreaterThan(TimeSpan.Zero);
    }

    // ────────────────────────────────────────────────────────────
    // RequiresSpecialHandling
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void RequiresSpecialHandling_AuthError_ReturnsTrue()
    {
        var details = new ProviderErrorDetails { Category = ErrorCategory.AuthError };

        _handler.RequiresSpecialHandling(details).Should().BeTrue();
    }

    [Theory]
    [InlineData(ErrorCategory.ClientError)]
    [InlineData(ErrorCategory.ServerError)]
    [InlineData(ErrorCategory.RateLimitRetryable)]
    [InlineData(ErrorCategory.Transient)]
    [InlineData(ErrorCategory.Unknown)]
    public void RequiresSpecialHandling_NonAuthError_ReturnsFalse(ErrorCategory category)
    {
        var details = new ProviderErrorDetails { Category = category };

        _handler.RequiresSpecialHandling(details).Should().BeFalse();
    }
}
