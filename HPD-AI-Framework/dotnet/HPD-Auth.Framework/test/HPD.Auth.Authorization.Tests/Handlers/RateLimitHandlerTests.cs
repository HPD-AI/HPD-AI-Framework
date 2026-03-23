using System.Net;
using System.Security.Claims;
using FluentAssertions;
using HPD.Auth.Authorization.Handlers;
using HPD.Auth.Authorization.Requirements;
using HPD.Auth.Authorization.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Moq;
using Xunit;

namespace HPD.Auth.Authorization.Tests.Handlers;

[Trait("Category", "Handlers")]
public class RateLimitHandlerTests
{
    private readonly Mock<IRateLimitService> _rateLimitService = new();
    private readonly RateLimitHandler _handler;
    private static readonly RateLimitRequirement DefaultRequirement = new(100, TimeSpan.FromHours(1));

    public RateLimitHandlerTests()
    {
        _handler = new RateLimitHandler(_rateLimitService.Object);
    }

    private static ClaimsPrincipal AuthenticatedUser(string userId) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            authenticationType: "Test"));

    private static ClaimsPrincipal AnonymousUser() => new(new ClaimsIdentity());

    private static HttpContext BuildHttpContext(
        string? userId = null,
        string? remoteIp = null,
        string? endpointDisplayName = null)
    {
        var httpContext = new DefaultHttpContext();

        if (remoteIp is not null)
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);

        if (endpointDisplayName is not null)
        {
            var endpoint = new Endpoint(null, null, endpointDisplayName);
            httpContext.Features.Set<IEndpointFeature>(new EndpointFeature { Endpoint = endpoint });
        }

        return httpContext;
    }

    private AuthorizationHandlerContext BuildContext(
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        return new AuthorizationHandlerContext([DefaultRequirement], user, httpContext);
    }

    [Fact]
    public async Task Within_limit_succeeds()
    {
        var userId = Guid.NewGuid().ToString();
        var user = AuthenticatedUser(userId);
        var httpContext = BuildHttpContext(userId, endpointDisplayName: "GET /api/test");

        _rateLimitService
            .Setup(s => s.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), default))
            .ReturnsAsync(true);

        var context = BuildContext(user, httpContext);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task At_limit_fails_with_reason()
    {
        var userId = Guid.NewGuid().ToString();
        var user = AuthenticatedUser(userId);
        var httpContext = BuildHttpContext(userId, endpointDisplayName: "GET /api/test");

        _rateLimitService
            .Setup(s => s.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), default))
            .ReturnsAsync(false);

        var context = BuildContext(user, httpContext);
        await _handler.HandleAsync(context);

        context.HasFailed.Should().BeTrue();
        context.FailureReasons.Should().Contain(r => r.Message.Contains("Rate limit exceeded"));
    }

    [Fact]
    public async Task Authenticated_key_uses_user_prefix()
    {
        var userId = Guid.NewGuid().ToString();
        var user = AuthenticatedUser(userId);
        var httpContext = BuildHttpContext(endpointDisplayName: "GET /api/apps");

        string? capturedKey = null;
        _rateLimitService
            .Setup(s => s.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), default))
            .Callback<string, int, TimeSpan, CancellationToken>((k, _, _, _) => capturedKey = k)
            .ReturnsAsync(true);

        var context = BuildContext(user, httpContext);
        await _handler.HandleAsync(context);

        capturedKey.Should().StartWith($"user:{userId}:");
    }

    [Fact]
    public async Task Anonymous_key_uses_ip_prefix()
    {
        var user = AnonymousUser();
        var httpContext = BuildHttpContext(remoteIp: "127.0.0.1", endpointDisplayName: "GET /api/apps");

        string? capturedKey = null;
        _rateLimitService
            .Setup(s => s.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), default))
            .Callback<string, int, TimeSpan, CancellationToken>((k, _, _, _) => capturedKey = k)
            .ReturnsAsync(true);

        var context = BuildContext(user, httpContext);
        await _handler.HandleAsync(context);

        capturedKey.Should().StartWith("ip:127.0.0.1:");
    }

    [Fact]
    public async Task Endpoint_name_is_included_in_key()
    {
        var userId = Guid.NewGuid().ToString();
        var user = AuthenticatedUser(userId);
        var httpContext = BuildHttpContext(endpointDisplayName: "GET /api/apps");

        string? capturedKey = null;
        _rateLimitService
            .Setup(s => s.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), default))
            .Callback<string, int, TimeSpan, CancellationToken>((k, _, _, _) => capturedKey = k)
            .ReturnsAsync(true);

        var context = BuildContext(user, httpContext);
        await _handler.HandleAsync(context);

        capturedKey.Should().EndWith(":GET /api/apps");
    }

    [Fact]
    public async Task Different_endpoints_produce_different_keys()
    {
        var userId = Guid.NewGuid().ToString();
        var user = AuthenticatedUser(userId);

        var keys = new List<string>();
        _rateLimitService
            .Setup(s => s.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), default))
            .Callback<string, int, TimeSpan, CancellationToken>((k, _, _, _) => keys.Add(k))
            .ReturnsAsync(true);

        var ctx1 = new AuthorizationHandlerContext(
            [DefaultRequirement], AuthenticatedUser(userId), BuildHttpContext(endpointDisplayName: "GET /api/apps"));
        var ctx2 = new AuthorizationHandlerContext(
            [DefaultRequirement], AuthenticatedUser(userId), BuildHttpContext(endpointDisplayName: "POST /api/tokens"));

        await _handler.HandleAsync(ctx1);
        await _handler.HandleAsync(ctx2);

        keys.Should().HaveCount(2);
        keys[0].Should().NotBe(keys[1]);
    }

    [Fact]
    public async Task MaxRequests_and_Window_forwarded_to_service()
    {
        var userId = Guid.NewGuid().ToString();
        var user = AuthenticatedUser(userId);
        var httpContext = BuildHttpContext(endpointDisplayName: "GET /api/test");
        var requirement = new RateLimitRequirement(500, TimeSpan.FromMinutes(30));

        int capturedMax = 0;
        TimeSpan capturedWindow = default;
        _rateLimitService
            .Setup(s => s.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), default))
            .Callback<string, int, TimeSpan, CancellationToken>((_, max, win, _) =>
            {
                capturedMax = max;
                capturedWindow = win;
            })
            .ReturnsAsync(true);

        var context = new AuthorizationHandlerContext([requirement], user, httpContext);
        await _handler.HandleAsync(context);

        capturedMax.Should().Be(500);
        capturedWindow.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task Null_remoteIp_falls_back_to_unknown_in_key()
    {
        var user = AnonymousUser();
        // No RemoteIpAddress set — Connection.RemoteIpAddress will be null
        var httpContext = BuildHttpContext(endpointDisplayName: "GET /api/test");

        string? capturedKey = null;
        _rateLimitService
            .Setup(s => s.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), default))
            .Callback<string, int, TimeSpan, CancellationToken>((k, _, _, _) => capturedKey = k)
            .ReturnsAsync(true);

        var context = BuildContext(user, httpContext);
        await _handler.HandleAsync(context);

        capturedKey.Should().StartWith("ip:unknown:");
    }

    [Fact]
    public async Task Non_HttpContext_resource_uses_unknown_endpoint_in_key()
    {
        var userId = Guid.NewGuid().ToString();
        var user = AuthenticatedUser(userId);

        // Pass a plain object as resource — no endpoint available
        var context = new AuthorizationHandlerContext([DefaultRequirement], user, resource: new object());

        string? capturedKey = null;
        _rateLimitService
            .Setup(s => s.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), default))
            .Callback<string, int, TimeSpan, CancellationToken>((k, _, _, _) => capturedKey = k)
            .ReturnsAsync(true);

        await _handler.HandleAsync(context);

        capturedKey.Should().EndWith(":unknown");
    }

    private sealed class EndpointFeature : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; }
    }
}
