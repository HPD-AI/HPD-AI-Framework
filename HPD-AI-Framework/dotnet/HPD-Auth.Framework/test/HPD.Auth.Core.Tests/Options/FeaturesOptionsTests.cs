using FluentAssertions;
using HPD.Auth.Core.Options;
using Xunit;

namespace HPD.Auth.Core.Tests.Options;

[Trait("Category", "Options")]
public class FeaturesOptionsTests
{
    [Fact]
    public void FeaturesOptions_EnableRegistration_DefaultsToTrue()
    {
        new FeaturesOptions().EnableRegistration.Should().BeTrue();
    }

    [Fact]
    public void FeaturesOptions_EnablePasskeys_DefaultsToFalse()
    {
        new FeaturesOptions().EnablePasskeys.Should().BeFalse();
    }

    [Fact]
    public void FeaturesOptions_EnableMagicLink_DefaultsToFalse()
    {
        new FeaturesOptions().EnableMagicLink.Should().BeFalse();
    }

    [Fact]
    public void FeaturesOptions_EnableSelfAccountDeletion_DefaultsToFalse()
    {
        new FeaturesOptions().EnableSelfAccountDeletion.Should().BeFalse();
    }

    [Fact]
    public void FeaturesOptions_EnableAuditLog_DefaultsToTrue()
    {
        new FeaturesOptions().EnableAuditLog.Should().BeTrue();
    }

    [Fact]
    public void FeaturesOptions_RequireEmailConfirmation_DefaultsToTrue()
    {
        new FeaturesOptions().RequireEmailConfirmation.Should().BeTrue();
    }

    [Fact]
    public void FeaturesOptions_EnableTwoFactor_DefaultsToTrue()
    {
        new FeaturesOptions().EnableTwoFactor.Should().BeTrue();
    }

    [Fact]
    public void FeaturesOptions_EnableOAuth_DefaultsToTrue()
    {
        new FeaturesOptions().EnableOAuth.Should().BeTrue();
    }

    [Fact]
    public void FeaturesOptions_EnableSessionManagement_DefaultsToTrue()
    {
        new FeaturesOptions().EnableSessionManagement.Should().BeTrue();
    }
}
