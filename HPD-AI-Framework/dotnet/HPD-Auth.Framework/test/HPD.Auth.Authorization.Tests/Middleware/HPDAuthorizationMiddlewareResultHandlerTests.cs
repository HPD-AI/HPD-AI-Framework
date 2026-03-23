using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using HPD.Auth.Authorization.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HPD.Auth.Authorization.Tests.Middleware;

[Trait("Category", "Middleware")]
public class HPDAuthorizationMiddlewareResultHandlerTests
{
    private static HPDAuthorizationMiddlewareResultHandler CreateHandler(
        ILogger<HPDAuthorizationMiddlewareResultHandler>? logger = null)
    {
        logger ??= new Mock<ILogger<HPDAuthorizationMiddlewareResultHandler>>().Object;
        return new HPDAuthorizationMiddlewareResultHandler(logger);
    }

    private static AuthorizationPolicy EmptyPolicy() =>
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    private static DefaultHttpContext BuildHttpContext(
        string path = "/",
        string? acceptHeader = null,
        string? endpointName = null,
        ClaimsPrincipal? user = null)
    {
        var services = new ServiceCollection();
        services.AddAuthentication();
        services.AddLogging();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };

        httpContext.Request.Path = path;

        if (acceptHeader is not null)
            httpContext.Request.Headers.Accept = acceptHeader;

        if (endpointName is not null)
        {
            var endpoint = new Endpoint(null, null, endpointName);
            httpContext.Features.Set<IEndpointFeature>(new EndpointFeature { Endpoint = endpoint });
        }

        if (user is not null)
            httpContext.User = user;

        return httpContext;
    }

    private static ClaimsPrincipal AuthenticatedUser(string userId = "user-1") =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            authenticationType: "Test"));

    private static async Task<(int StatusCode, string Body, string? ContentType)> InvokeAsync(
        HPDAuthorizationMiddlewareResultHandler handler,
        DefaultHttpContext httpContext,
        PolicyAuthorizationResult result)
    {
        var body = new MemoryStream();
        httpContext.Response.Body = body;

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        };

        await handler.HandleAsync(next, httpContext, EmptyPolicy(), result);

        body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(body).ReadToEndAsync();

        return (httpContext.Response.StatusCode, responseBody, httpContext.Response.ContentType);
    }

    [Fact]
    public async Task Succeeded_calls_next()
    {
        var handler = CreateHandler();
        var httpContext = BuildHttpContext();

        var (statusCode, _, _) = await InvokeAsync(handler, httpContext, PolicyAuthorizationResult.Success());

        statusCode.Should().Be(200);
    }

    [Fact]
    public async Task Unauthenticated_API_request_returns_JSON_401()
    {
        var handler = CreateHandler();
        var httpContext = BuildHttpContext(path: "/api/test");

        var (statusCode, body, contentType) = await InvokeAsync(
            handler, httpContext, PolicyAuthorizationResult.Challenge());

        statusCode.Should().Be(401);
        contentType.Should().Contain("application/json");
        body.Should().Contain("unauthorized");
    }

    [Fact]
    public async Task Unauthenticated_API_request_via_Accept_header_returns_JSON_401()
    {
        var handler = CreateHandler();
        var httpContext = BuildHttpContext(path: "/dashboard", acceptHeader: "application/json");

        var (statusCode, body, _) = await InvokeAsync(
            handler, httpContext, PolicyAuthorizationResult.Challenge());

        statusCode.Should().Be(401);
        body.Should().Contain("Authentication required");
    }

    [Fact]
    public async Task Unauthenticated_non_API_request_calls_ChallengeAsync()
    {
        var mockAuthService = new Mock<IAuthenticationService>();
        mockAuthService
            .Setup(a => a.ChallengeAsync(It.IsAny<HttpContext>(), null, It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(mockAuthService.Object);
        services.AddLogging();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Request = { Path = "/dashboard" }
        };
        var body = new MemoryStream();
        httpContext.Response.Body = body;

        var handler = CreateHandler();

        RequestDelegate next = _ => Task.CompletedTask;
        await handler.HandleAsync(next, httpContext, EmptyPolicy(), PolicyAuthorizationResult.Challenge());

        mockAuthService.Verify(
            a => a.ChallengeAsync(httpContext, null, It.IsAny<AuthenticationProperties>()),
            Times.Once);

        body.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(body).ReadToEndAsync();
        responseBody.Should().BeEmpty();
    }

    [Fact]
    public async Task Forbidden_returns_JSON_403_with_reasons()
    {
        var handler = CreateHandler();
        var user = AuthenticatedUser();
        var httpContext = BuildHttpContext(path: "/api/test", user: user);

        var fakeHandler = new Mock<IAuthorizationHandler>().Object;
        var failure = AuthorizationFailure.Failed(
            [new AuthorizationFailureReason(fakeHandler, "reason-1")]);
        var result = PolicyAuthorizationResult.Forbid(failure);

        var (statusCode, body, _) = await InvokeAsync(handler, httpContext, result);

        statusCode.Should().Be(403);
        body.Should().Contain("forbidden");
        body.Should().Contain("reason-1");
    }

    [Fact]
    public async Task Forbidden_with_no_reasons_returns_empty_reasons_array()
    {
        var handler = CreateHandler();
        var user = AuthenticatedUser();
        var httpContext = BuildHttpContext(path: "/api/test", user: user);

        var failure = AuthorizationFailure.Failed(Array.Empty<AuthorizationFailureReason>());
        var result = PolicyAuthorizationResult.Forbid(failure);

        var (statusCode, body, _) = await InvokeAsync(handler, httpContext, result);

        statusCode.Should().Be(403);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("reasons").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Warning_log_written_on_challenge()
    {
        var loggerMock = new Mock<ILogger<HPDAuthorizationMiddlewareResultHandler>>();
        var handler = CreateHandler(loggerMock.Object);
        var httpContext = BuildHttpContext(path: "/api/test", endpointName: "GET /api/test");
        var body = new MemoryStream();
        httpContext.Response.Body = body;

        RequestDelegate next = _ => Task.CompletedTask;
        await handler.HandleAsync(next, httpContext, EmptyPolicy(), PolicyAuthorizationResult.Challenge());

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("GET /api/test")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Warning_log_written_on_forbidden()
    {
        var loggerMock = new Mock<ILogger<HPDAuthorizationMiddlewareResultHandler>>();
        var handler = CreateHandler(loggerMock.Object);

        var userId = "user-42";
        var user = AuthenticatedUser(userId);
        var httpContext = BuildHttpContext(path: "/api/test", user: user);
        var body = new MemoryStream();
        httpContext.Response.Body = body;

        var failure = AuthorizationFailure.Failed(Array.Empty<AuthorizationFailureReason>());
        RequestDelegate next = _ => Task.CompletedTask;
        await handler.HandleAsync(next, httpContext, EmptyPolicy(), PolicyAuthorizationResult.Forbid(failure));

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(userId)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Forbidden_with_null_AuthorizationFailure_returns_empty_reasons_array()
    {
        var handler = CreateHandler();
        var user = AuthenticatedUser();
        var httpContext = BuildHttpContext(path: "/api/test", user: user);

        // PolicyAuthorizationResult.Forbid() with no argument → AuthorizationFailure is null
        var result = PolicyAuthorizationResult.Forbid();

        var (statusCode, body, _) = await InvokeAsync(handler, httpContext, result);

        statusCode.Should().Be(403);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("reasons").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Challenge_does_not_call_next()
    {
        var handler = CreateHandler();
        var httpContext = BuildHttpContext(path: "/api/test");
        var nextCalled = false;
        var body = new MemoryStream();
        httpContext.Response.Body = body;

        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        await handler.HandleAsync(next, httpContext, EmptyPolicy(), PolicyAuthorizationResult.Challenge());

        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Forbidden_does_not_call_next()
    {
        var handler = CreateHandler();
        var user = AuthenticatedUser();
        var httpContext = BuildHttpContext(path: "/api/test", user: user);
        var nextCalled = false;
        var body = new MemoryStream();
        httpContext.Response.Body = body;

        var failure = AuthorizationFailure.Failed(Array.Empty<AuthorizationFailureReason>());
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        await handler.HandleAsync(next, httpContext, EmptyPolicy(), PolicyAuthorizationResult.Forbid(failure));

        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Forbidden_returns_all_multiple_reasons_in_body()
    {
        var handler = CreateHandler();
        var user = AuthenticatedUser();
        var httpContext = BuildHttpContext(path: "/api/test", user: user);

        var fakeHandler = new Mock<IAuthorizationHandler>().Object;
        var failure = AuthorizationFailure.Failed([
            new AuthorizationFailureReason(fakeHandler, "reason-A"),
            new AuthorizationFailureReason(fakeHandler, "reason-B"),
        ]);

        var (statusCode, body, _) = await InvokeAsync(handler, httpContext, PolicyAuthorizationResult.Forbid(failure));

        statusCode.Should().Be(403);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("reasons").GetArrayLength().Should().Be(2);
        var reasons = doc.RootElement.GetProperty("reasons").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        reasons.Should().Contain("reason-A").And.Contain("reason-B");
    }

    [Fact]
    public async Task Succeeded_does_not_log_warning()
    {
        var loggerMock = new Mock<ILogger<HPDAuthorizationMiddlewareResultHandler>>();
        var handler = CreateHandler(loggerMock.Object);
        var httpContext = BuildHttpContext();
        var body = new MemoryStream();
        httpContext.Response.Body = body;

        RequestDelegate next = _ => Task.CompletedTask;
        await handler.HandleAsync(next, httpContext, EmptyPolicy(), PolicyAuthorizationResult.Success());

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    private sealed class EndpointFeature : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; }
    }
}
