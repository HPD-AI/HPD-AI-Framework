using HPD.OpenApi.Core;

namespace HPD.Agent.OpenApi.Tests.Core;

public class OpenApiErrorResponseTests
{
    #region Structured JSON extraction

    [Fact]
    public void UserMessage_StripeNestedShape_ExtractsMessageFromError()
    {
        var response = new OpenApiErrorResponse
        {
            StatusCode = 400,
            Body = """{"error":{"message":"No such customer"}}"""
        };

        response.UserMessage.Should().Be("No such customer");
    }

    [Fact]
    public void UserMessage_GitHubFlatShape_ExtractsTopLevelMessage()
    {
        var response = new OpenApiErrorResponse
        {
            StatusCode = 404,
            Body = """{"message":"Not Found"}"""
        };

        response.UserMessage.Should().Be("Not Found");
    }

    [Fact]
    public void UserMessage_SlackShape_ExtractsErrorField()
    {
        var response = new OpenApiErrorResponse
        {
            StatusCode = 200,
            Body = """{"ok":false,"error":"missing_scope"}"""
        };

        response.UserMessage.Should().Be("missing_scope");
    }

    [Fact]
    public void UserMessage_AzureShape_ExtractsMessageFromError()
    {
        var response = new OpenApiErrorResponse
        {
            StatusCode = 400,
            Body = """{"error":{"code":"InvalidRequest","message":"The request is invalid."}}"""
        };

        response.UserMessage.Should().Be("The request is invalid.");
    }

    [Fact]
    public void UserMessage_ArrayUnderMessageKey_ExtractsFirstElement()
    {
        var response = new OpenApiErrorResponse
        {
            StatusCode = 422,
            Body = """{"message":["First error","Second error"]}"""
        };

        response.UserMessage.Should().Be("First error");
    }

    #endregion

    #region Non-JSON body handling

    [Fact]
    public void UserMessage_NonJsonBody_ReturnsTruncatedBody()
    {
        var response = new OpenApiErrorResponse
        {
            StatusCode = 500,
            Body = "Internal Server Error"
        };

        response.UserMessage.Should().Be("Internal Server Error");
    }

    [Fact]
    public void UserMessage_BodyOver200Chars_TruncatesWithEllipsis()
    {
        var longBody = new string('x', 250);
        var response = new OpenApiErrorResponse
        {
            StatusCode = 500,
            Body = longBody
        };

        response.UserMessage.Should().HaveLength(203); // 200 + "..."
        response.UserMessage.Should().EndWith("...");
    }

    [Fact]
    public void UserMessage_NullBody_ReturnsNull()
    {
        var response = new OpenApiErrorResponse
        {
            StatusCode = 500,
            Body = null
        };

        response.UserMessage.Should().BeNull();
    }

    [Fact]
    public void UserMessage_EmptyBody_ReturnsNull()
    {
        var response = new OpenApiErrorResponse
        {
            StatusCode = 500,
            Body = ""
        };

        response.UserMessage.Should().BeNull();
    }

    [Fact]
    public void UserMessage_WhitespaceOnlyBody_ReturnsNull()
    {
        var response = new OpenApiErrorResponse
        {
            StatusCode = 500,
            Body = "   \t\n  "
        };

        response.UserMessage.Should().BeNull();
    }

    #endregion

    #region Recursion depth

    [Fact]
    public void UserMessage_DeeplyNestedObject_StopsAtDepth3ReturnsNull()
    {
        // Depth 4 nesting — beyond the limit of 3
        var response = new OpenApiErrorResponse
        {
            StatusCode = 400,
            Body = """{"a":{"b":{"c":{"d":{"message":"too deep"}}}}}"""
        };

        // The nesting goes past depth 3 before reaching any known message key
        // (a→b→c→d is depth 4), so extraction returns null
        response.UserMessage.Should().BeNull();
    }

    [Fact]
    public void UserMessage_NestingWithinDepthLimit_Extracts()
    {
        // "error" (depth 0) → "message" (depth 1) — within limit
        var response = new OpenApiErrorResponse
        {
            StatusCode = 400,
            Body = """{"error":{"message":"Found it"}}"""
        };

        response.UserMessage.Should().Be("Found it");
    }

    #endregion

    #region Lazy computation / caching

    [Fact]
    public void UserMessage_CalledMultipleTimes_ReturnsSameValue()
    {
        var response = new OpenApiErrorResponse
        {
            StatusCode = 400,
            Body = """{"message":"Cached"}"""
        };

        var first = response.UserMessage;
        var second = response.UserMessage;

        first.Should().Be("Cached");
        second.Should().Be("Cached");
        ReferenceEquals(first, second).Should().BeTrue("result should be cached");
    }

    #endregion

    #region ToString

    [Fact]
    public void ToString_WithUserMessage_UsesUserMessage()
    {
        var response = new OpenApiErrorResponse
        {
            StatusCode = 404,
            ReasonPhrase = "Not Found",
            Body = """{"message":"Pet not found"}"""
        };

        response.ToString().Should().Be("Pet not found");
    }

    [Fact]
    public void ToString_WithNoExtractableMessage_UsesFallback()
    {
        var response = new OpenApiErrorResponse
        {
            StatusCode = 500,
            ReasonPhrase = "Internal Server Error",
            Body = null
        };

        response.ToString().Should().Be("HTTP 500 Internal Server Error: ");
    }

    #endregion

    #region RetryAfter

    [Fact]
    public void RetryAfter_IsNullByDefault()
    {
        var response = new OpenApiErrorResponse { StatusCode = 429 };
        response.RetryAfter.Should().BeNull();
    }

    [Fact]
    public void RetryAfter_CanBeSet()
    {
        var delay = TimeSpan.FromSeconds(30);
        var response = new OpenApiErrorResponse
        {
            StatusCode = 429,
            RetryAfter = delay
        };

        response.RetryAfter.Should().Be(delay);
    }

    #endregion

    #region OpenApiRequestException wrapping

    [Fact]
    public void OpenApiRequestException_ExposesStatusCode()
    {
        var error = new OpenApiErrorResponse { StatusCode = 429 };
        var ex = new OpenApiRequestException(error);

        ex.StatusCode.Should().Be(429);
    }

    [Fact]
    public void OpenApiRequestException_ExposesRetryAfter()
    {
        var delay = TimeSpan.FromSeconds(60);
        var error = new OpenApiErrorResponse { StatusCode = 429, RetryAfter = delay };
        var ex = new OpenApiRequestException(error);

        ex.RetryAfter.Should().Be(delay);
    }

    [Fact]
    public void OpenApiRequestException_MessageUsesUserMessage()
    {
        var error = new OpenApiErrorResponse
        {
            StatusCode = 429,
            Body = """{"message":"Rate limit exceeded"}"""
        };
        var ex = new OpenApiRequestException(error);

        ex.Message.Should().Be("Rate limit exceeded");
    }

    [Fact]
    public void OpenApiRequestException_WithInnerException_PreservesInner()
    {
        var error = new OpenApiErrorResponse { StatusCode = 500 };
        var inner = new InvalidOperationException("inner");
        var ex = new OpenApiRequestException(error, inner);

        ex.InnerException.Should().BeSameAs(inner);
    }

    #endregion
}
