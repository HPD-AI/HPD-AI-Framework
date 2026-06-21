using FluentAssertions;
using HPD.Auth.Core.Options;
using HPD.Auth.Extensions;
using HPD.Auth.OAuth.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using HPD.Events;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace HPD.Auth.OAuth.Tests;

/// <summary>
/// Tests for <c>OAuthSchemeRegistrar</c> (internal class) exercised through the
/// public <c>AddHPDAuth().AddOAuth()</c> extension — section 5 in TESTS.md.
///
/// Each test creates an isolated web application with specific <see cref="HPDAuthOptions"/>
/// and inspects <see cref="IAuthenticationSchemeProvider"/> to verify which schemes
/// were (or were not) registered.
/// </summary>
public class OAuthSchemeRegistrarTests : IAsyncDisposable
{
    private WebApplication? _app;

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private async Task<IAuthenticationSchemeProvider> BuildSchemeProviderAsync(
        Action<OAuthOptions> configureOAuth)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();

        // Supply stubs for services registered by other HPD.Auth packages.
        builder.Services.AddScoped<IEventCoordinator>(
            _ => Substitute.For<IEventCoordinator>());
        builder.Services.AddScoped<HPD.Auth.Core.Interfaces.ITokenService>(
            _ => NSubstitute.Substitute.For<HPD.Auth.Core.Interfaces.ITokenService>());

        builder.Services
            .AddHPDAuth(o =>
            {
                o.AppName                           = $"SchemeTest_{Guid.NewGuid():N}";
                o.Password.RequireDigit             = false;
                o.Password.RequireLowercase         = false;
                o.Password.RequireUppercase         = false;
                o.Password.RequireNonAlphanumeric   = false;
                o.Password.RequiredLength           = 1;
                configureOAuth(o.OAuth);
            })
            .AddOAuth();

        builder.Services.AddAuthorization();

        _app = builder.Build();
        await _app.StartAsync();

        return _app.Services.GetRequiredService<IAuthenticationSchemeProvider>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 5 — OAuthSchemeRegistrar (via public AddOAuth() API)
    // ─────────────────────────────────────────────────────────────────────────

    // 57 — Providers["google"] with non-empty ClientId + Enabled=true → scheme registered
    [Fact]
    public async Task Google_EnabledWithClientId_SchemeRegistered()
    {
        var provider = await BuildSchemeProviderAsync(o =>
        {
            o.Providers["google"] = new OAuthProviderOptions
            {
                ClientId     = "test-gcid",
                ClientSecret = "test-gcs",
                Enabled      = true,
            };
        });

        var scheme = await provider.GetSchemeAsync("Google");
        scheme.Should().NotBeNull();
        scheme!.Name.Should().Be("Google");
    }

    // 58 — Providers["google"] with empty ClientId → AddGoogle NOT called
    [Fact]
    public async Task Google_EmptyClientId_SchemeNotRegistered()
    {
        var provider = await BuildSchemeProviderAsync(o =>
        {
            o.Providers["google"] = new OAuthProviderOptions
            {
                ClientId = string.Empty,
                Enabled  = true,
            };
        });

        var scheme = await provider.GetSchemeAsync("Google");
        scheme.Should().BeNull();
    }

    // 59 — Providers["google"] with Enabled=false → AddGoogle NOT called
    [Fact]
    public async Task Google_Disabled_SchemeNotRegistered()
    {
        var provider = await BuildSchemeProviderAsync(o =>
        {
            o.Providers["google"] = new OAuthProviderOptions
            {
                ClientId = "test-gcid",
                Enabled  = false,
            };
        });

        var scheme = await provider.GetSchemeAsync("Google");
        scheme.Should().BeNull();
    }

    // 60 — Providers["github"] configured → AddGitHub called
    [Fact]
    public async Task GitHub_Configured_SchemeRegistered()
    {
        var provider = await BuildSchemeProviderAsync(o =>
        {
            o.Providers["github"] = new OAuthProviderOptions
            {
                ClientId     = "gh-client-id",
                ClientSecret = "gh-client-secret",
            };
        });

        var scheme = await provider.GetSchemeAsync("GitHub");
        scheme.Should().NotBeNull();
    }

    // 61 — Providers["microsoft"] configured → AddMicrosoftAccount called
    [Fact]
    public async Task Microsoft_Configured_SchemeRegistered()
    {
        var provider = await BuildSchemeProviderAsync(o =>
        {
            o.Providers["microsoft"] = new OAuthProviderOptions
            {
                ClientId     = "ms-client-id",
                ClientSecret = "ms-client-secret",
            };
        });

        var scheme = await provider.GetSchemeAsync("Microsoft");
        scheme.Should().NotBeNull();
    }

    // 62 — All providers empty → no provider schemes, no exception
    [Fact]
    public async Task AllProvidersEmpty_NoSchemesRegistered_NoException()
    {
        var provider = await BuildSchemeProviderAsync(_ => { });

        var google    = await provider.GetSchemeAsync("Google");
        var github    = await provider.GetSchemeAsync("GitHub");
        var microsoft = await provider.GetSchemeAsync("Microsoft");

        google.Should().BeNull();
        github.Should().BeNull();
        microsoft.Should().BeNull();
    }

    // 63 — AdditionalScopes non-empty → scheme still registered without exception
    [Fact]
    public async Task Google_AdditionalScopes_SchemeStillRegistered()
    {
        var provider = await BuildSchemeProviderAsync(o =>
        {
            o.Providers["google"] = new OAuthProviderOptions
            {
                ClientId         = "test-gcid",
                ClientSecret     = "test-gcs",
                AdditionalScopes = new List<string> { "https://www.googleapis.com/auth/calendar" },
            };
        });

        var scheme = await provider.GetSchemeAsync("Google");
        scheme.Should().NotBeNull();
    }

    // 64 — CallbackPath set → scheme registered without exception
    [Fact]
    public async Task Google_CustomCallbackPath_SchemeRegistered()
    {
        var provider = await BuildSchemeProviderAsync(o =>
        {
            o.Providers["google"] = new OAuthProviderOptions
            {
                ClientId     = "test-gcid",
                ClientSecret = "test-gcs",
                CallbackPath = "/signin-google-custom",
            };
        });

        var scheme = await provider.GetSchemeAsync("Google");
        scheme.Should().NotBeNull();
    }
}
