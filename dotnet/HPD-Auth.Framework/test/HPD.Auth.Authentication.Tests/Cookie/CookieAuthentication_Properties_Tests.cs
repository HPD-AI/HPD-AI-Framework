using FluentAssertions;
using HPD.Auth.Authentication.Tests.Helpers;
using HPD.Auth.Core.Options;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HPD.Auth.Authentication.Tests.Cookie;

/// <summary>
/// Tests 90–103: Cookie properties and security-stamp validation (TESTS.md §4.2–4.3).
///
/// Cookie options are inspected by resolving IOptionsMonitor&lt;CookieAuthenticationOptions&gt;
/// from the DI container after calling AddHPDAuth().AddAuthentication().
/// </summary>
[Trait("Category", "Cookie")]
[Trait("Section", "4.2-Cookie-Properties")]
public class CookieAuthentication_Properties_Tests
{
    private static CookieAuthenticationOptions ResolveOptions(Action<HPDAuthOptions>? configure = null)
    {
        using var scope = ServiceProviderBuilder.CreateScope(configure);
        var monitor = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        return monitor.Get(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 90 — CookieName from config
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Cookie_Name_Uses_CookieName_From_Config()
    {
        var opts = ResolveOptions(o => o.Cookie.CookieName = ".MyApp.Auth");
        opts.Cookie.Name.Should().Be(".MyApp.Auth");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 91 — CookieName fallback to {AppName}.Auth
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Cookie_Name_Falls_Back_To_AppName()
    {
        var opts = ResolveOptions(o =>
        {
            o.AppName = "MyApp";
            o.Cookie.CookieName = string.Empty;
        });

        opts.Cookie.Name.Should().Be("MyApp.Auth");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 92 — HttpOnly = true
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Cookie_HttpOnly_Is_True()
    {
        var opts = ResolveOptions();
        opts.Cookie.HttpOnly.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 93 — SecurePolicy = Always
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Cookie_SecurePolicy_Is_Always()
    {
        var opts = ResolveOptions();
        opts.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 94 — SameSite matches config
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Cookie_SameSite_Matches_Config()
    {
        var opts = ResolveOptions(o => o.Cookie.SameSite = SameSiteMode.Strict);
        opts.Cookie.SameSite.Should().Be(SameSiteMode.Strict);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 95 — IsEssential = true
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Cookie_IsEssential_Is_True()
    {
        var opts = ResolveOptions();
        opts.Cookie.IsEssential.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 96 — LoginPath
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Cookie_LoginPath_Is_Auth_Login()
    {
        var opts = ResolveOptions();
        opts.LoginPath.Value.Should().Be("/auth/login");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 97 — LogoutPath
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Cookie_LogoutPath_Is_Auth_Logout()
    {
        var opts = ResolveOptions();
        opts.LogoutPath.Value.Should().Be("/auth/logout");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 98 — AccessDeniedPath
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Cookie_AccessDeniedPath_Is_Auth_Access_Denied()
    {
        var opts = ResolveOptions();
        opts.AccessDeniedPath.Value.Should().Be("/auth/access-denied");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 99 — ExpireTimeSpan matches SlidingExpiration config
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Cookie_ExpireTimeSpan_Matches_SlidingExpiration_Config()
    {
        var opts = ResolveOptions(o => o.Cookie.SlidingExpiration = TimeSpan.FromDays(7));
        opts.ExpireTimeSpan.Should().Be(TimeSpan.FromDays(7));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 100 — SlidingExpiration matches UseSlidingExpiration config
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Cookie_SlidingExpiration_Matches_UseSlidingExpiration_Config()
    {
        var opts = ResolveOptions(o => o.Cookie.UseSlidingExpiration = false);
        opts.SlidingExpiration.Should().BeFalse();
    }
}
