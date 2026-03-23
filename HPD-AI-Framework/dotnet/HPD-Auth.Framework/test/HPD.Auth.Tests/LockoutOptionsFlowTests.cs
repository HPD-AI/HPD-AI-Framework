using FluentAssertions;
using HPD.Auth.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HPD.Auth.Tests;

/// <summary>
/// Verifies that lockout policy settings configured via HPDAuthOptions are
/// correctly propagated to ASP.NET Core's IdentityOptions.
/// </summary>
public class LockoutOptionsFlowTests
{
    // ── 3.1 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Maps_Lockout_MaxFailedAttempts()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHPDAuth(o =>
        {
            o.AppName = "Lockout_MaxFailed";
            o.Lockout.MaxFailedAttempts = 3;
        });
        var sp = services.BuildServiceProvider();

        var identityOptions = sp.GetRequiredService<IOptions<IdentityOptions>>().Value;

        identityOptions.Lockout.MaxFailedAccessAttempts.Should().Be(3);
    }

    // ── 3.2 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Maps_Lockout_Duration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHPDAuth(o =>
        {
            o.AppName = "Lockout_Duration";
            o.Lockout.Duration = TimeSpan.FromMinutes(30);
        });
        var sp = services.BuildServiceProvider();

        var identityOptions = sp.GetRequiredService<IOptions<IdentityOptions>>().Value;

        identityOptions.Lockout.DefaultLockoutTimeSpan.Should().Be(TimeSpan.FromMinutes(30));
    }

    // ── 3.3 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Maps_Lockout_Enabled_False()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHPDAuth(o =>
        {
            o.AppName = "Lockout_Disabled";
            o.Lockout.Enabled = false;
        });
        var sp = services.BuildServiceProvider();

        var identityOptions = sp.GetRequiredService<IOptions<IdentityOptions>>().Value;

        identityOptions.Lockout.AllowedForNewUsers.Should().BeFalse();
    }
}
