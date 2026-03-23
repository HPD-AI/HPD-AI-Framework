using FluentAssertions;
using HPD.Auth.Core.Options;
using Xunit;

namespace HPD.Auth.Core.Tests.Options;

[Trait("Category", "Options")]
public class LockoutPolicyOptionsTests
{
    [Fact]
    public void LockoutPolicyOptions_Enabled_DefaultsToTrue()
    {
        new LockoutPolicyOptions().Enabled.Should().BeTrue();
    }

    [Fact]
    public void LockoutPolicyOptions_MaxFailedAttempts_DefaultsTo5()
    {
        new LockoutPolicyOptions().MaxFailedAttempts.Should().Be(5);
    }

    [Fact]
    public void LockoutPolicyOptions_Duration_DefaultsTo15Minutes()
    {
        new LockoutPolicyOptions().Duration.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void LockoutPolicyOptions_AllowedForNewUsers_DefaultsToTrue()
    {
        new LockoutPolicyOptions().AllowedForNewUsers.Should().BeTrue();
    }
}
