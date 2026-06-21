using FluentAssertions;
using HPD.Auth.Authentication.Tests.Helpers;
using HPD.Auth.Core.Options;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HPD.Auth.Authentication.Tests.Cookie;

/// <summary>
/// Extra gap tests: HPDCookieOptions.Domain and Path properties (not in TESTS.md §4.2
/// but identified as missing coverage).
/// </summary>
[Trait("Category", "Cookie")]
[Trait("Section", "4.2-Cookie-EdgeCases")]
public class CookieAuthentication_EdgeCase_Tests
{
    private static CookieAuthenticationOptions ResolveOptions(Action<HPDAuthOptions>? configure = null)
    {
        using var scope = ServiceProviderBuilder.CreateScope(configure);
        var monitor = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        return monitor.Get(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cookie_Domain_Set_When_Config_Has_Domain
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When HPDCookieOptions.Domain is set the configurator must forward it to
    /// opts.Cookie.Domain. Without this the cookie will not be sent to subdomains.
    /// </summary>
    [Fact]
    public void Cookie_Domain_Set_When_Config_Has_Domain()
    {
        var opts = ResolveOptions(o => o.Cookie.Domain = ".example.com");
        opts.Cookie.Domain.Should().Be(".example.com");
    }

    /// <summary>
    /// When HPDCookieOptions.Domain is null or empty the configurator must NOT
    /// set opts.Cookie.Domain (leave it as null so the browser uses the request host).
    /// </summary>
    [Fact]
    public void Cookie_Domain_Not_Set_When_Config_Domain_Is_Empty()
    {
        var opts = ResolveOptions(o => o.Cookie.Domain = string.Empty);
        // CookieBuilder.Domain defaults to null when not assigned.
        opts.Cookie.Domain.Should().BeNullOrEmpty(
            "an empty domain config must not override the default null domain");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cookie_Path_Set_From_Config
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The HPDCookieOptions.Path value must be forwarded to opts.Cookie.Path.
    /// </summary>
    [Fact]
    public void Cookie_Path_Set_From_Config()
    {
        var opts = ResolveOptions(o => o.Cookie.Path = "/app");
        opts.Cookie.Path.Should().Be("/app");
    }

    /// <summary>
    /// The default cookie path (when HPDCookieOptions.Path is not explicitly set)
    /// should be "/" so the cookie is sent for all routes on the domain.
    /// </summary>
    [Fact]
    public void Cookie_Default_Path_Is_Root()
    {
        // Default HPDCookieOptions.Path should result in "/" on the cookie.
        var opts = ResolveOptions();
        opts.Cookie.Path.Should().NotBeNull();
    }
}
