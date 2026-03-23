using FluentAssertions;
using HPD.Auth.Core.Entities;
using Xunit;

namespace HPD.Auth.Core.Tests.Entities;

[Trait("Category", "Entity")]
public class AuditConstantsTests
{
    [Fact]
    public void AuditCategories_Authentication_EqualsDotSeparatedString()
    {
        AuditCategories.Authentication.Should().Be("authentication");
    }

    [Fact]
    public void AuditActions_UserLogin_EqualsUserDotLogin()
    {
        AuditActions.UserLogin.Should().Be("user.login");
    }

    [Fact]
    public void AuditActions_AdminPasswordReset_EqualsAdminDotPassword_Reset()
    {
        AuditActions.AdminPasswordReset.Should().Be("admin.password_reset");
    }
}
