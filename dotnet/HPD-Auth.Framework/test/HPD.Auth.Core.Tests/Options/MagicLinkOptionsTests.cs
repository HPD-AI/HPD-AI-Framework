using FluentAssertions;
using HPD.Auth.Core.Options;
using Xunit;

namespace HPD.Auth.Core.Tests.Options;

[Trait("Category", "Options")]
public class MagicLinkOptionsTests
{
    [Fact]
    public void MagicLinkOptions_TokenLifetime_DefaultsTo15Minutes()
    {
        new MagicLinkOptions().TokenLifetime.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void MagicLinkOptions_SingleUse_DefaultsToTrue()
    {
        new MagicLinkOptions().SingleUse.Should().BeTrue();
    }

    [Fact]
    public void MagicLinkOptions_ResendCooldown_DefaultsTo60Seconds()
    {
        new MagicLinkOptions().ResendCooldown.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void MagicLinkOptions_BaseUrl_DefaultsToEmpty()
    {
        new MagicLinkOptions().BaseUrl.Should().Be(string.Empty);
    }
}
