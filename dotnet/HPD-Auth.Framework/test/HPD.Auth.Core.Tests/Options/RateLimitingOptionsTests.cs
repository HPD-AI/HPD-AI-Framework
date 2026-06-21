using FluentAssertions;
using HPD.Auth.Core.Options;
using Xunit;

namespace HPD.Auth.Core.Tests.Options;

[Trait("Category", "Options")]
public class RateLimitingOptionsTests
{
    [Fact]
    public void RateLimitingOptions_Enabled_DefaultsToTrue()
    {
        new RateLimitingOptions().Enabled.Should().BeTrue();
    }

    [Fact]
    public void RateLimitingOptions_LoginAttemptsPerWindow_DefaultsTo10()
    {
        new RateLimitingOptions().LoginAttemptsPerWindow.Should().Be(10);
    }

    [Fact]
    public void RateLimitingOptions_Window_DefaultsTo15Minutes()
    {
        new RateLimitingOptions().Window.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void RateLimitingOptions_RegisterAttemptsPerWindow_DefaultsTo5()
    {
        new RateLimitingOptions().RegisterAttemptsPerWindow.Should().Be(5);
    }

    [Fact]
    public void RateLimitingOptions_PasswordResetAttemptsPerWindow_DefaultsTo3()
    {
        new RateLimitingOptions().PasswordResetAttemptsPerWindow.Should().Be(3);
    }

    [Fact]
    public void RateLimitingOptions_BanDuration_DefaultsTo1Hour()
    {
        new RateLimitingOptions().BanDuration.Should().Be(TimeSpan.FromHours(1));
    }
}
