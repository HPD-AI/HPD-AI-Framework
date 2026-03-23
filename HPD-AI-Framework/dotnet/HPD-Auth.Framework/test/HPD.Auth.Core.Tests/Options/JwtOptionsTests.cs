using FluentAssertions;
using HPD.Auth.Core.Options;
using Xunit;

namespace HPD.Auth.Core.Tests.Options;

[Trait("Category", "Options")]
public class JwtOptionsTests
{
    [Fact]
    public void JwtOptions_AccessTokenLifetime_DefaultsTo15Minutes()
    {
        new JwtOptions().AccessTokenLifetime.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void JwtOptions_RefreshTokenLifetime_DefaultsTo14Days()
    {
        new JwtOptions().RefreshTokenLifetime.Should().Be(TimeSpan.FromDays(14));
    }

    [Fact]
    public void JwtOptions_ValidateLifetime_DefaultsToTrue()
    {
        new JwtOptions().ValidateLifetime.Should().BeTrue();
    }

    [Fact]
    public void JwtOptions_Issuer_DefaultsToHPDAuth()
    {
        new JwtOptions().Issuer.Should().Be("HPD.Auth");
    }

    [Fact]
    public void JwtOptions_ClockSkew_DefaultsTo30Seconds()
    {
        new JwtOptions().ClockSkew.Should().Be(TimeSpan.FromSeconds(30));
    }
}
