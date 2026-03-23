using FluentAssertions;
using HPD.Auth.Core.Options;
using Xunit;

namespace HPD.Auth.Core.Tests.Options;

[Trait("Category", "Options")]
public class SecurityOptionsTests
{
    [Fact]
    public void SecurityOptions_RotateRefreshTokens_DefaultsToTrue()
    {
        new SecurityOptions().RotateRefreshTokens.Should().BeTrue();
    }

    [Fact]
    public void SecurityOptions_RevokeSessionsOnPasswordChange_DefaultsToTrue()
    {
        new SecurityOptions().RevokeSessionsOnPasswordChange.Should().BeTrue();
    }

    [Fact]
    public void SecurityOptions_RequireConfirmedEmail_DefaultsToTrue()
    {
        new SecurityOptions().RequireConfirmedEmail.Should().BeTrue();
    }

    [Fact]
    public void SecurityOptions_BindTokenToIp_DefaultsToFalse()
    {
        new SecurityOptions().BindTokenToIp.Should().BeFalse();
    }

    [Fact]
    public void SecurityOptions_SendLoginAlerts_DefaultsToFalse()
    {
        new SecurityOptions().SendLoginAlerts.Should().BeFalse();
    }

    [Fact]
    public void SecurityOptions_RequireConfirmedPhoneNumber_DefaultsToFalse()
    {
        new SecurityOptions().RequireConfirmedPhoneNumber.Should().BeFalse();
    }

    [Fact]
    public void SecurityOptions_MaxConcurrentSessions_DefaultsToZero()
    {
        new SecurityOptions().MaxConcurrentSessions.Should().Be(0);
    }

    [Fact]
    public void SecurityOptions_DataProtectionPurpose_DefaultsToHPDAuthV1()
    {
        new SecurityOptions().DataProtectionPurpose.Should().Be("HPD.Auth.v1");
    }
}
