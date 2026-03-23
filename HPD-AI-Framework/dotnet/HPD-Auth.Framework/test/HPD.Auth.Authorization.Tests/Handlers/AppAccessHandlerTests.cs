using System.Security.Claims;
using FluentAssertions;
using HPD.Auth.Authorization.Handlers;
using HPD.Auth.Authorization.Requirements;
using HPD.Auth.Authorization.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Moq;
using Xunit;

namespace HPD.Auth.Authorization.Tests.Handlers;

[Trait("Category", "Handlers")]
public class AppAccessHandlerTests
{
    private readonly Mock<IAppPermissionService> _permissionService = new();
    private readonly AppAccessHandler _handler;

    public AppAccessHandlerTests()
    {
        _handler = new AppAccessHandler(_permissionService.Object);
    }

    private static ClaimsPrincipal AuthenticatedUser(string userId) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            authenticationType: "Test"));

    private static ClaimsPrincipal AnonymousUser() => new(new ClaimsIdentity());

    private static AuthorizationHandlerContext BuildContext(
        AppAccessRequirement requirement,
        ClaimsPrincipal user,
        object? resource = null)
    {
        return new AuthorizationHandlerContext([requirement], user, resource);
    }

    private static DefaultHttpContext BuildHttpContextWithRoute(string routeKey, string routeValue)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues[routeKey] = routeValue;
        return httpContext;
    }

    [Fact]
    public async Task No_userId_claim_does_not_succeed_or_fail()
    {
        var context = BuildContext(new AppAccessRequirement("app-1"), AnonymousUser());

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeFalse();
    }

    [Fact]
    public async Task AppId_from_requirement_takes_priority_over_route()
    {
        var userId = Guid.NewGuid();
        var httpContext = BuildHttpContextWithRoute("appId", "app-B");
        var requirement = new AppAccessRequirement("app-A");
        var user = AuthenticatedUser(userId.ToString());

        _permissionService
            .Setup(s => s.UserHasAppAccessAsync(userId, "app-A", default))
            .ReturnsAsync(true);

        var context = BuildContext(requirement, user, httpContext);
        await _handler.HandleAsync(context);

        _permissionService.Verify(s => s.UserHasAppAccessAsync(userId, "app-A", default), Times.Once);
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task AppId_from_route_used_when_requirement_has_no_appId()
    {
        var userId = Guid.NewGuid();
        var httpContext = BuildHttpContextWithRoute("appId", "app-C");
        var requirement = new AppAccessRequirement(null);
        var user = AuthenticatedUser(userId.ToString());

        _permissionService
            .Setup(s => s.UserHasAppAccessAsync(userId, "app-C", default))
            .ReturnsAsync(true);

        var context = BuildContext(requirement, user, httpContext);
        await _handler.HandleAsync(context);

        _permissionService.Verify(s => s.UserHasAppAccessAsync(userId, "app-C", default), Times.Once);
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_appId_in_requirement_and_route_calls_Fail()
    {
        var userId = Guid.NewGuid();
        var requirement = new AppAccessRequirement(null);
        var user = AuthenticatedUser(userId.ToString());

        // No HttpContext resource — no route value available
        var context = BuildContext(requirement, user, resource: null);
        await _handler.HandleAsync(context);

        context.HasFailed.Should().BeTrue();
        context.FailureReasons.Should().Contain(r => r.Message.Contains("App ID not specified"));
    }

    [Fact]
    public async Task UserHasAppAccess_true_succeeds()
    {
        var userId = Guid.NewGuid();
        var user = AuthenticatedUser(userId.ToString());
        var requirement = new AppAccessRequirement("app-X");

        _permissionService
            .Setup(s => s.UserHasAppAccessAsync(userId, "app-X", default))
            .ReturnsAsync(true);

        var context = BuildContext(requirement, user);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task UserHasAppAccess_false_fails_with_reason()
    {
        var userId = Guid.NewGuid();
        var user = AuthenticatedUser(userId.ToString());
        var requirement = new AppAccessRequirement("app-X");

        _permissionService
            .Setup(s => s.UserHasAppAccessAsync(userId, "app-X", default))
            .ReturnsAsync(false);

        var context = BuildContext(requirement, user);
        await _handler.HandleAsync(context);

        context.HasFailed.Should().BeTrue();
        context.FailureReasons.Should().Contain(r => r.Message.Contains("does not have access"));
    }

    [Fact]
    public async Task HttpContext_resource_with_no_appId_route_value_calls_Fail()
    {
        // Resource IS an HttpContext, but the appId route key is absent.
        // This exercises the branch where context.Resource is HttpContext
        // but GetRouteValue("appId") returns null.
        var userId = Guid.NewGuid();
        var user = AuthenticatedUser(userId.ToString());
        var requirement = new AppAccessRequirement(null);
        var httpContext = new DefaultHttpContext(); // no route values

        var context = BuildContext(requirement, user, httpContext);
        await _handler.HandleAsync(context);

        context.HasFailed.Should().BeTrue();
        context.FailureReasons.Should().Contain(r => r.Message.Contains("App ID not specified"));
    }

    [Fact]
    public async Task Non_guid_userId_claim_throws_FormatException()
    {
        var user = AuthenticatedUser("not-a-guid");
        var requirement = new AppAccessRequirement("app-X");

        var context = BuildContext(requirement, user);

        await _handler.Invoking(h => h.HandleAsync(context))
            .Should().ThrowAsync<FormatException>();
    }
}
