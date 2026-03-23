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
public class FeatureFlagHandlerTests
{
    private readonly Mock<IFeatureFlagService> _featureFlags = new();
    private readonly FeatureFlagHandler _handler;
    private static readonly FeatureFlagRequirement DefaultRequirement = new("my-feature");

    public FeatureFlagHandlerTests()
    {
        _handler = new FeatureFlagHandler(_featureFlags.Object);
    }

    private static ClaimsPrincipal BuildUser(
        string? userId = null,
        string? tier = null,
        IEnumerable<string>? roles = null)
    {
        var claims = new List<Claim>();
        if (userId is not null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (tier is not null)
            claims.Add(new Claim("subscription_tier", tier));
        foreach (var role in roles ?? [])
            claims.Add(new Claim(ClaimTypes.Role, role));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private static AuthorizationHandlerContext BuildContext(ClaimsPrincipal user) =>
        new([DefaultRequirement], user, null);

    [Fact]
    public async Task Enabled_flag_succeeds()
    {
        var user = BuildUser(userId: "u-1");

        _featureFlags
            .Setup(s => s.IsEnabledAsync("my-feature", It.IsAny<FeatureContext>(), default))
            .ReturnsAsync(true);

        var context = BuildContext(user);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Disabled_flag_does_not_succeed()
    {
        var user = BuildUser(userId: "u-1");

        _featureFlags
            .Setup(s => s.IsEnabledAsync("my-feature", It.IsAny<FeatureContext>(), default))
            .ReturnsAsync(false);

        var context = BuildContext(user);
        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeFalse();
    }

    [Fact]
    public async Task FeatureContext_UserId_built_from_NameIdentifier_claim()
    {
        var user = BuildUser(userId: "u-123");
        FeatureContext? captured = null;

        _featureFlags
            .Setup(s => s.IsEnabledAsync("my-feature", It.IsAny<FeatureContext>(), default))
            .Callback<string, FeatureContext, CancellationToken>((_, ctx, _) => captured = ctx)
            .ReturnsAsync(true);

        var context = BuildContext(user);
        await _handler.HandleAsync(context);

        captured!.UserId.Should().Be("u-123");
    }

    [Fact]
    public async Task FeatureContext_SubscriptionTier_built_from_claim()
    {
        var user = BuildUser(userId: "u-1", tier: "enterprise");
        FeatureContext? captured = null;

        _featureFlags
            .Setup(s => s.IsEnabledAsync("my-feature", It.IsAny<FeatureContext>(), default))
            .Callback<string, FeatureContext, CancellationToken>((_, ctx, _) => captured = ctx)
            .ReturnsAsync(true);

        var context = BuildContext(user);
        await _handler.HandleAsync(context);

        captured!.SubscriptionTier.Should().Be("enterprise");
    }

    [Fact]
    public async Task FeatureContext_SubscriptionTier_defaults_to_free_when_claim_absent()
    {
        var user = BuildUser(userId: "u-1"); // no tier claim
        FeatureContext? captured = null;

        _featureFlags
            .Setup(s => s.IsEnabledAsync("my-feature", It.IsAny<FeatureContext>(), default))
            .Callback<string, FeatureContext, CancellationToken>((_, ctx, _) => captured = ctx)
            .ReturnsAsync(true);

        var context = BuildContext(user);
        await _handler.HandleAsync(context);

        captured!.SubscriptionTier.Should().Be("free");
    }

    [Fact]
    public async Task FeatureContext_Roles_built_from_all_role_claims()
    {
        var user = BuildUser(userId: "u-1", roles: ["Admin", "Developer"]);
        FeatureContext? captured = null;

        _featureFlags
            .Setup(s => s.IsEnabledAsync("my-feature", It.IsAny<FeatureContext>(), default))
            .Callback<string, FeatureContext, CancellationToken>((_, ctx, _) => captured = ctx)
            .ReturnsAsync(true);

        var context = BuildContext(user);
        await _handler.HandleAsync(context);

        captured!.Roles.Should().Contain("Admin").And.Contain("Developer");
    }

    [Fact]
    public async Task Correct_feature_key_forwarded_to_service()
    {
        var user = BuildUser(userId: "u-1");
        string? capturedKey = null;

        _featureFlags
            .Setup(s => s.IsEnabledAsync(It.IsAny<string>(), It.IsAny<FeatureContext>(), default))
            .Callback<string, FeatureContext, CancellationToken>((key, _, _) => capturedKey = key)
            .ReturnsAsync(true);

        var context = new AuthorizationHandlerContext(
            [new FeatureFlagRequirement("dark-mode")], user, null);
        await _handler.HandleAsync(context);

        capturedKey.Should().Be("dark-mode");
    }

    [Fact]
    public async Task FeatureContext_UserId_is_null_for_anonymous_user()
    {
        // No NameIdentifier claim — userId will be null in FeatureContext
        var user = BuildUser(); // no userId
        FeatureContext? captured = null;

        _featureFlags
            .Setup(s => s.IsEnabledAsync("my-feature", It.IsAny<FeatureContext>(), default))
            .Callback<string, FeatureContext, CancellationToken>((_, ctx, _) => captured = ctx)
            .ReturnsAsync(true);

        var context = BuildContext(user);
        await _handler.HandleAsync(context);

        captured!.UserId.Should().BeNull();
    }
}
