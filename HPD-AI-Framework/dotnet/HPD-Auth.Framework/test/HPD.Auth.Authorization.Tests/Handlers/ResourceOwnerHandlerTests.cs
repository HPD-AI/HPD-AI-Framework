using System.Security.Claims;
using FluentAssertions;
using HPD.Auth.Authorization.Handlers;
using HPD.Auth.Authorization.Requirements;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace HPD.Auth.Authorization.Tests.Handlers;

[Trait("Category", "Handlers")]
public class ResourceOwnerHandlerTests
{
    private readonly ResourceOwnerHandler _handler = new();

    private static ClaimsPrincipal UserWithId(string userId, bool isAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (isAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private static ClaimsPrincipal AnonymousUser() => new(new ClaimsIdentity());

    private sealed class FakeResource(Guid ownerId) : IOwnable
    {
        public Guid OwnerId { get; } = ownerId;
    }

    private static AuthorizationHandlerContext BuildContext(ClaimsPrincipal user, IOwnable resource)
    {
        return new AuthorizationHandlerContext(
            [new ResourceOwnerRequirement()],
            user,
            resource);
    }

    [Fact]
    public async Task Admin_always_succeeds_regardless_of_owner()
    {
        var ownerId = Guid.NewGuid();
        var adminId = Guid.NewGuid().ToString();
        var admin = UserWithId(adminId, isAdmin: true);
        var resource = new FakeResource(ownerId); // different owner

        var context = BuildContext(admin, resource);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Owner_succeeds()
    {
        var ownerId = Guid.NewGuid();
        var user = UserWithId(ownerId.ToString());
        var resource = new FakeResource(ownerId);

        var context = BuildContext(user, resource);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Non_owner_non_admin_does_not_succeed()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid().ToString();
        var user = UserWithId(otherId);
        var resource = new FakeResource(ownerId);

        var context = BuildContext(user, resource);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeFalse();
    }

    [Fact]
    public async Task Admin_who_is_also_owner_succeeds()
    {
        var ownerId = Guid.NewGuid();
        var admin = UserWithId(ownerId.ToString(), isAdmin: true);
        var resource = new FakeResource(ownerId);

        var context = BuildContext(admin, resource);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Unauthenticated_does_not_succeed()
    {
        var resource = new FakeResource(Guid.NewGuid());
        var context = BuildContext(AnonymousUser(), resource);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }
}
