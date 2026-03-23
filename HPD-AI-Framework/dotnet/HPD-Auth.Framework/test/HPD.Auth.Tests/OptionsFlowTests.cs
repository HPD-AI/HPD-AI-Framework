using FluentAssertions;
using HPD.Auth.Core.Options;
using HPD.Auth.Extensions;
using HPD.Auth.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HPD.Auth.Tests;

/// <summary>
/// Verifies that password policy settings configured via HPDAuthOptions are
/// correctly propagated to ASP.NET Core's IdentityOptions.
/// </summary>
public class OptionsFlowTests
{
    // ── 2.1 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Maps_Password_RequiredLength()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHPDAuth(o =>
        {
            o.AppName = "OptionsFlow_RequiredLength";
            o.Password.RequiredLength = 14;
        });
        var sp = services.BuildServiceProvider();

        var identityOptions = sp.GetRequiredService<IOptions<IdentityOptions>>().Value;

        identityOptions.Password.RequiredLength.Should().Be(14);
    }

    // ── 2.2 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Maps_Password_RequireDigit_False()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHPDAuth(o =>
        {
            o.AppName = "OptionsFlow_RequireDigit";
            o.Password.RequireDigit = false;
        });
        var sp = services.BuildServiceProvider();

        var identityOptions = sp.GetRequiredService<IOptions<IdentityOptions>>().Value;

        identityOptions.Password.RequireDigit.Should().BeFalse();
    }

    // ── 2.3 ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true,  false, true)]
    [InlineData(true,  true,  true)]
    public void AddHPDAuth_Maps_Password_Complexity_Flags(bool upper, bool lower, bool nonAlpha)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHPDAuth(o =>
        {
            o.AppName = $"OptionsFlow_Complexity_{upper}_{lower}_{nonAlpha}";
            o.Password.RequireUppercase = upper;
            o.Password.RequireLowercase = lower;
            o.Password.RequireNonAlphanumeric = nonAlpha;
        });
        var sp = services.BuildServiceProvider();

        var identityOptions = sp.GetRequiredService<IOptions<IdentityOptions>>().Value;

        identityOptions.Password.RequireUppercase.Should().Be(upper);
        identityOptions.Password.RequireLowercase.Should().Be(lower);
        identityOptions.Password.RequireNonAlphanumeric.Should().Be(nonAlpha);
    }

    // ── 2.4 — RequiredUniqueChars propagates ─────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Maps_Password_RequiredUniqueChars()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHPDAuth(o =>
        {
            o.AppName = "OptionsFlow_UniqueChars";
            o.Password.RequiredUniqueChars = 4;
        });
        var sp = services.BuildServiceProvider();

        var identityOptions = sp.GetRequiredService<IOptions<IdentityOptions>>().Value;

        identityOptions.Password.RequiredUniqueChars.Should().Be(4);
    }

    // ── 2.5 — Features.RequireEmailConfirmation propagates to SignIn ─────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddHPDAuth_Maps_Features_RequireEmailConfirmation(bool requireConfirmed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHPDAuth(o =>
        {
            o.AppName = $"OptionsFlow_EmailConfirm_{requireConfirmed}";
            o.Features.RequireEmailConfirmation = requireConfirmed;
        });
        var sp = services.BuildServiceProvider();

        var identityOptions = sp.GetRequiredService<IOptions<IdentityOptions>>().Value;

        identityOptions.SignIn.RequireConfirmedEmail.Should().Be(requireConfirmed);
    }

    // ── 2.6 — User.RequireUniqueEmail is hardcoded to true ───────────────────────

    [Fact]
    public void AddHPDAuth_Sets_RequireUniqueEmail_True()
    {
        var sp = ServiceProviderBuilder.Build(appName: "OptionsFlow_UniqueEmail");

        var identityOptions = sp.GetRequiredService<IOptions<IdentityOptions>>().Value;

        identityOptions.User.RequireUniqueEmail.Should().BeTrue();
    }

    // ── 2.7 — HPDAuthOptions resolves directly as a singleton ────────────────────

    [Fact]
    public void AddHPDAuth_Registers_HPDAuthOptions_As_Singleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHPDAuth(o =>
        {
            o.AppName = "OptionsFlow_DirectSingleton";
            o.Password.RequiredLength = 20;
        });
        var sp = services.BuildServiceProvider();

        var opts = sp.GetService<HPDAuthOptions>();

        opts.Should().NotBeNull();
        opts!.AppName.Should().Be("OptionsFlow_DirectSingleton");
        opts.Password.RequiredLength.Should().Be(20);
    }

    // ── 2.8 — HPDAuthOptions singleton and IOptions<HPDAuthOptions> are same values

    [Fact]
    public void AddHPDAuth_HPDAuthOptions_Singleton_And_IOptions_Agree()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHPDAuth(o =>
        {
            o.AppName = "OptionsFlow_Agreement";
            o.Password.RequiredLength = 18;
        });
        var sp = services.BuildServiceProvider();

        var direct = sp.GetRequiredService<HPDAuthOptions>();
        var viaIOptions = sp.GetRequiredService<IOptions<HPDAuthOptions>>().Value;

        direct.AppName.Should().Be(viaIOptions.AppName);
        direct.Password.RequiredLength.Should().Be(viaIOptions.Password.RequiredLength);
    }
}
