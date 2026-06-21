using FluentAssertions;
using HPD.Auth.Core.Options;
using Xunit;

namespace HPD.Auth.Core.Tests.Options;

[Trait("Category", "Options")]
public class SmsOptionsTests
{
    [Fact]
    public void SmsOptions_Enabled_DefaultsToFalse()
    {
        new SmsOptions().Enabled.Should().BeFalse();
    }

    [Fact]
    public void SmsOptions_OtpLength_DefaultsTo6()
    {
        new SmsOptions().OtpLength.Should().Be(6);
    }

    [Fact]
    public void SmsOptions_OtpLifetime_DefaultsTo10Minutes()
    {
        new SmsOptions().OtpLifetime.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void SmsOptions_ResendCooldown_DefaultsTo60Seconds()
    {
        new SmsOptions().ResendCooldown.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void SmsOptions_SenderId_DefaultsToNull()
    {
        new SmsOptions().SenderId.Should().BeNull();
    }
}
