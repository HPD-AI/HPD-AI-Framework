using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.Infrastructure.Stores;
using HPD.Auth.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Tests;

/// <summary>
/// Verifies that AddHPDAuth() registers all expected services into the DI container.
/// Each test builds a minimal ServiceProvider and asserts that the service resolves
/// to the correct concrete type.
/// </summary>
public class AddHPDAuthRegistrationTests
{
    // ── 1.1 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Registers_IAuditLogger()
    {
        var sp = ServiceProviderBuilder.Build(appName: "Reg_AuditLogger");
        using var scope = sp.CreateScope();

        var service = scope.ServiceProvider.GetService<IAuditLogger>();

        service.Should().NotBeNull();
        service.Should().BeOfType<AuditLogStore>();
    }

    // ── 1.2 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Registers_ISessionManager()
    {
        var sp = ServiceProviderBuilder.Build(appName: "Reg_SessionManager");
        using var scope = sp.CreateScope();

        var service = scope.ServiceProvider.GetService<ISessionManager>();

        service.Should().NotBeNull();
        service.Should().BeOfType<SessionStore>();
    }

    // ── 1.3 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Registers_IRefreshTokenStore()
    {
        var sp = ServiceProviderBuilder.Build(appName: "Reg_RefreshTokenStore");
        using var scope = sp.CreateScope();

        var service = scope.ServiceProvider.GetService<IRefreshTokenStore>();

        service.Should().NotBeNull();
        service.Should().BeOfType<RefreshTokenStore>();
    }

    // ── 1.4 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Registers_ITenantContext_As_SingleTenantContext()
    {
        var sp = ServiceProviderBuilder.Build(appName: "Reg_TenantContext");
        using var scope = sp.CreateScope();

        var tenantContext = scope.ServiceProvider.GetService<ITenantContext>();

        tenantContext.Should().NotBeNull();
        tenantContext.Should().BeOfType<SingleTenantContext>();
    }

    // ── 1.5 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Registers_HPDAuthDbContext()
    {
        var sp = ServiceProviderBuilder.Build(appName: "Reg_DbContext");
        using var scope = sp.CreateScope();

        var dbContext = scope.ServiceProvider.GetService<HPDAuthDbContext>();

        dbContext.Should().NotBeNull();
    }

    // ── 1.6 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Registers_UserManager()
    {
        var sp = ServiceProviderBuilder.Build(appName: "Reg_UserManager");
        using var scope = sp.CreateScope();

        var userManager = scope.ServiceProvider.GetService<UserManager<ApplicationUser>>();

        userManager.Should().NotBeNull();
    }

    // ── 1.7 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Registers_SignInManager()
    {
        var sp = ServiceProviderBuilder.Build(appName: "Reg_SignInManager");
        using var scope = sp.CreateScope();

        var signInManager = scope.ServiceProvider.GetService<SignInManager<ApplicationUser>>();

        signInManager.Should().NotBeNull();
    }

    // ── 1.8 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Registers_RoleManager()
    {
        var sp = ServiceProviderBuilder.Build(appName: "Reg_RoleManager");
        using var scope = sp.CreateScope();

        var roleManager = scope.ServiceProvider.GetService<RoleManager<ApplicationRole>>();

        roleManager.Should().NotBeNull();
    }
}
