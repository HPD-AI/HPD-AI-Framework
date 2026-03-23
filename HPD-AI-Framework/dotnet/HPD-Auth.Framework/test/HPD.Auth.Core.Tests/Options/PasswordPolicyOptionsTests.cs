using FluentAssertions;
using HPD.Auth.Core.Options;
using Xunit;

namespace HPD.Auth.Core.Tests.Options;

[Trait("Category", "Options")]
public class PasswordPolicyOptionsTests
{
    [Fact]
    public void PasswordPolicyOptions_RequiredLength_DefaultsTo8()
    {
        new PasswordPolicyOptions().RequiredLength.Should().Be(8);
    }

    [Fact]
    public void PasswordPolicyOptions_RequireDigit_DefaultsToTrue()
    {
        new PasswordPolicyOptions().RequireDigit.Should().BeTrue();
    }

    [Fact]
    public void PasswordPolicyOptions_RequireUppercase_DefaultsToTrue()
    {
        new PasswordPolicyOptions().RequireUppercase.Should().BeTrue();
    }

    [Fact]
    public void PasswordPolicyOptions_RequireLowercase_DefaultsToTrue()
    {
        new PasswordPolicyOptions().RequireLowercase.Should().BeTrue();
    }

    [Fact]
    public void PasswordPolicyOptions_RequireNonAlphanumeric_DefaultsToTrue()
    {
        new PasswordPolicyOptions().RequireNonAlphanumeric.Should().BeTrue();
    }

    [Fact]
    public void PasswordPolicyOptions_PasswordHistoryCount_DefaultsTo5()
    {
        new PasswordPolicyOptions().PasswordHistoryCount.Should().Be(5);
    }

    [Fact]
    public void PasswordPolicyOptions_RequiredUniqueChars_DefaultsTo1()
    {
        new PasswordPolicyOptions().RequiredUniqueChars.Should().Be(1);
    }

    [Fact]
    public void PasswordPolicyOptions_MaxPasswordAgeDays_DefaultsToZero()
    {
        new PasswordPolicyOptions().MaxPasswordAgeDays.Should().Be(0);
    }
}
