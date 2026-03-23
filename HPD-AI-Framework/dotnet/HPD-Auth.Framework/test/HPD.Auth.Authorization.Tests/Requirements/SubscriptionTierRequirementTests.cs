using FluentAssertions;
using HPD.Auth.Authorization.Requirements;
using Xunit;

namespace HPD.Auth.Authorization.Tests.Requirements;

[Trait("Category", "Requirements")]
public class SubscriptionTierRequirementTests
{
    [Fact]
    public void Free_allows_free_pro_and_enterprise()
    {
        var req = new SubscriptionTierRequirement("free");
        req.AllowedTiers.Should().BeEquivalentTo(["free", "pro", "enterprise"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Pro_allows_pro_and_enterprise()
    {
        var req = new SubscriptionTierRequirement("pro");
        req.AllowedTiers.Should().BeEquivalentTo(["pro", "enterprise"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Enterprise_allows_only_enterprise()
    {
        var req = new SubscriptionTierRequirement("enterprise");
        req.AllowedTiers.Should().BeEquivalentTo(["enterprise"]);
    }

    [Fact]
    public void Unknown_tier_returns_empty()
    {
        var req = new SubscriptionTierRequirement("platinum");
        req.AllowedTiers.Should().BeEmpty();
    }

    [Fact]
    public void MinimumTier_is_stored_as_supplied()
    {
        var req = new SubscriptionTierRequirement("Pro");
        req.MinimumTier.Should().Be("Pro");
    }

    [Fact]
    public void Case_insensitive_tier_matching()
    {
        var req = new SubscriptionTierRequirement("PRO");
        req.AllowedTiers.Should().BeEquivalentTo(["pro", "enterprise"], o => o.WithStrictOrdering());
    }
}
