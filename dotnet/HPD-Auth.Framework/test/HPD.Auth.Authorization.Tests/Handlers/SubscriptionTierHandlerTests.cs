using System.Security.Claims;
using FluentAssertions;
using HPD.Auth.Authorization.Handlers;
using HPD.Auth.Authorization.Requirements;
using HPD.Auth.Authorization.Services;
using Microsoft.AspNetCore.Authorization;
using Moq;
using Xunit;

namespace HPD.Auth.Authorization.Tests.Handlers;

[Trait("Category", "Handlers")]
public class SubscriptionTierHandlerTests
{
    private readonly Mock<ISubscriptionService> _subscriptionService = new();
    private readonly SubscriptionTierHandler _handler;

    public SubscriptionTierHandlerTests()
    {
        _handler = new SubscriptionTierHandler(_subscriptionService.Object);
    }

    private static ClaimsPrincipal UserWithClaims(string userId, string? tier = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (tier is not null)
            claims.Add(new Claim("subscription_tier", tier));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private static ClaimsPrincipal AnonymousUser() => new(new ClaimsIdentity());

    private static AuthorizationHandlerContext BuildContext(
        SubscriptionTierRequirement requirement,
        ClaimsPrincipal user)
    {
        return new AuthorizationHandlerContext([requirement], user, null);
    }

    [Fact]
    public async Task Claim_fast_path_succeeds_for_allowed_tier()
    {
        var userId = Guid.NewGuid();
        var user = UserWithClaims(userId.ToString(), tier: "pro");
        var requirement = new SubscriptionTierRequirement("pro");

        var context = BuildContext(requirement, user);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
        _subscriptionService.Verify(
            s => s.GetUserSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Stale_claim_falls_back_to_service()
    {
        var userId = Guid.NewGuid();
        // Claim says "free" but requirement is "pro" — stale
        var user = UserWithClaims(userId.ToString(), tier: "free");
        var requirement = new SubscriptionTierRequirement("pro");

        _subscriptionService
            .Setup(s => s.GetUserSubscriptionAsync(userId, default))
            .ReturnsAsync(new SubscriptionInfo("pro", null));

        var context = BuildContext(requirement, user);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
        _subscriptionService.Verify(
            s => s.GetUserSubscriptionAsync(userId, default),
            Times.Once);
    }

    [Fact]
    public async Task Expired_subscription_fails()
    {
        var userId = Guid.NewGuid();
        var user = UserWithClaims(userId.ToString()); // no claim
        var requirement = new SubscriptionTierRequirement("pro");

        _subscriptionService
            .Setup(s => s.GetUserSubscriptionAsync(userId, default))
            .ReturnsAsync(new SubscriptionInfo("pro", DateTime.UtcNow.AddDays(-1)));

        var context = BuildContext(requirement, user);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Null_subscription_does_not_succeed()
    {
        var userId = Guid.NewGuid();
        var user = UserWithClaims(userId.ToString()); // no claim
        var requirement = new SubscriptionTierRequirement("pro");

        _subscriptionService
            .Setup(s => s.GetUserSubscriptionAsync(userId, default))
            .ReturnsAsync((SubscriptionInfo?)null);

        var context = BuildContext(requirement, user);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeFalse();
    }

    [Fact]
    public async Task Non_expiring_subscription_succeeds()
    {
        var userId = Guid.NewGuid();
        var user = UserWithClaims(userId.ToString()); // no claim
        var requirement = new SubscriptionTierRequirement("enterprise");

        _subscriptionService
            .Setup(s => s.GetUserSubscriptionAsync(userId, default))
            .ReturnsAsync(new SubscriptionInfo("enterprise", null));

        var context = BuildContext(requirement, user);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task No_userId_claim_returns_without_success_or_failure()
    {
        var user = AnonymousUser();
        var requirement = new SubscriptionTierRequirement("pro");

        var context = BuildContext(requirement, user);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeFalse();
        _subscriptionService.Verify(
            s => s.GetUserSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Higher_tier_claim_satisfies_lower_tier_requirement()
    {
        // enterprise satisfies a "pro" minimum because enterprise is in AllowedTiers for "pro"
        var userId = Guid.NewGuid();
        var user = UserWithClaims(userId.ToString(), tier: "enterprise");
        var requirement = new SubscriptionTierRequirement("pro");

        var context = BuildContext(requirement, user);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
        _subscriptionService.Verify(
            s => s.GetUserSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Future_expiry_succeeds()
    {
        var userId = Guid.NewGuid();
        var user = UserWithClaims(userId.ToString()); // no claim
        var requirement = new SubscriptionTierRequirement("pro");

        _subscriptionService
            .Setup(s => s.GetUserSubscriptionAsync(userId, default))
            .ReturnsAsync(new SubscriptionInfo("pro", DateTime.UtcNow.AddDays(30)));

        var context = BuildContext(requirement, user);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Non_guid_userId_claim_throws_FormatException()
    {
        // Guid.Parse throws when the NameIdentifier claim is not a valid GUID.
        var user = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "not-a-guid")],
                authenticationType: "Test"));
        var requirement = new SubscriptionTierRequirement("pro");

        // Claim doesn't match allowed tiers, so the service fallback is hit,
        // which calls Guid.Parse("not-a-guid") and throws.
        var context = BuildContext(requirement, user);

        await _handler.Invoking(h => h.HandleAsync(context))
            .Should().ThrowAsync<FormatException>();
    }
}
