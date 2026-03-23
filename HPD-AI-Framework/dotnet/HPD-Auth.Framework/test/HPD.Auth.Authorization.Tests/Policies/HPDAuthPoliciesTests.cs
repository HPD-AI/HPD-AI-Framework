using System.Reflection;
using FluentAssertions;
using HPD.Auth.Authorization.Policies;
using HPD.Auth.Authorization.Requirements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Xunit;

namespace HPD.Auth.Authorization.Tests.Policies;

[Trait("Category", "Policies")]
public class HPDAuthPoliciesTests
{
    private static AuthorizationOptions BuildOptions()
    {
        var options = new AuthorizationOptions();
        HPDAuthPolicies.RegisterPolicies(options);
        return options;
    }

    [Fact]
    public void All_policy_name_constants_match_registered_policies()
    {
        var options = BuildOptions();

        var constants = typeof(HPDAuthPolicies)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        constants.Should().NotBeEmpty();
        foreach (var name in constants)
        {
            options.GetPolicy(name).Should().NotBeNull(
                because: $"HPDAuthPolicies.{name} should be registered");
        }
    }

    [Fact]
    public void RequireAdmin_policy_has_role_requirement()
    {
        var options = BuildOptions();
        var policy = options.GetPolicy(HPDAuthPolicies.RequireAdmin)!;

        var rolesReq = policy.Requirements.OfType<RolesAuthorizationRequirement>().SingleOrDefault();
        rolesReq.Should().NotBeNull();
        rolesReq!.AllowedRoles.Should().Contain("Admin");
    }

    [Fact]
    public void CanInstallApps_has_SubscriptionTierRequirement_free()
    {
        var options = BuildOptions();
        var policy = options.GetPolicy(HPDAuthPolicies.CanInstallApps)!;

        var tierReq = policy.Requirements.OfType<SubscriptionTierRequirement>().SingleOrDefault();
        tierReq.Should().NotBeNull();
        tierReq!.MinimumTier.Should().Be("free");
    }

    [Fact]
    public void ApiAccess_has_RateLimitRequirement_1000_per_hour()
    {
        var options = BuildOptions();
        var policy = options.GetPolicy(HPDAuthPolicies.ApiAccess)!;

        var rlReq = policy.Requirements.OfType<RateLimitRequirement>().SingleOrDefault();
        rlReq.Should().NotBeNull();
        rlReq!.MaxRequests.Should().Be(1000);
        rlReq.Window.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void ApiAccessEnterprise_has_RateLimitRequirement_10000_per_hour()
    {
        var options = BuildOptions();
        var policy = options.GetPolicy(HPDAuthPolicies.ApiAccessEnterprise)!;

        var rlReq = policy.Requirements.OfType<RateLimitRequirement>().SingleOrDefault();
        rlReq.Should().NotBeNull();
        rlReq!.MaxRequests.Should().Be(10000);
    }

    [Fact]
    public void RequireAdminOrModerator_has_both_roles()
    {
        var options = BuildOptions();
        var policy = options.GetPolicy(HPDAuthPolicies.RequireAdminOrModerator)!;

        var rolesReq = policy.Requirements.OfType<RolesAuthorizationRequirement>().SingleOrDefault();
        rolesReq.Should().NotBeNull();
        rolesReq!.AllowedRoles.Should().Contain("Admin").And.Contain("Moderator");
    }

    [Fact]
    public void CanPublishApps_has_Developer_role_and_pro_tier()
    {
        var options = BuildOptions();
        var policy = options.GetPolicy(HPDAuthPolicies.CanPublishApps)!;

        var rolesReq = policy.Requirements.OfType<RolesAuthorizationRequirement>().SingleOrDefault();
        rolesReq.Should().NotBeNull();
        rolesReq!.AllowedRoles.Should().Contain("Developer");

        var tierReq = policy.Requirements.OfType<SubscriptionTierRequirement>().SingleOrDefault();
        tierReq.Should().NotBeNull();
        tierReq!.MinimumTier.Should().Be("pro");
    }

    [Fact]
    public void RequirePremium_has_claim_requirement_for_pro_and_enterprise()
    {
        var options = BuildOptions();
        var policy = options.GetPolicy(HPDAuthPolicies.RequirePremium)!;

        var claimsReq = policy.Requirements.OfType<ClaimsAuthorizationRequirement>().SingleOrDefault();
        claimsReq.Should().NotBeNull();
        claimsReq!.ClaimType.Should().Be("subscription_tier");
        claimsReq.AllowedValues.Should().Contain("pro").And.Contain("enterprise");
    }

    [Fact]
    public void RequireEmailVerified_requires_email_claim()
    {
        var options = BuildOptions();
        var policy = options.GetPolicy(HPDAuthPolicies.RequireEmailVerified)!;

        // Must include a ClaimsAuthorizationRequirement for the email claim type
        var claimsReq = policy.Requirements.OfType<ClaimsAuthorizationRequirement>()
            .SingleOrDefault(r => r.ClaimType == System.Security.Claims.ClaimTypes.Email);
        claimsReq.Should().NotBeNull();

        // And an assertion requirement for email_verified == true
        var assertionReq = policy.Requirements.OfType<AssertionRequirement>().SingleOrDefault();
        assertionReq.Should().NotBeNull();
    }
}
