using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Tests;

/// <summary>
/// Additional depth test for UserManager (test 7.1).
/// </summary>
public class UserManagerTests
{
    // ── 7.1 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void UserManager_PasswordHasher_Is_Registered_And_Functional()
    {
        var sp = ServiceProviderBuilder.Build(appName: "UserMgr_Hasher");
        using var scope = sp.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "test", Email = "test@example.com" };

        var hash = userManager.PasswordHasher.HashPassword(user, "TestPassword123!");

        hash.Should().NotBeNullOrEmpty();
    }
}
