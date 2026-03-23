using FluentAssertions;
using HPD.Auth.Core.Options;
using HPD.Auth.Extensions;
using HPD.Auth.OAuth.Extensions;
using HPD.Auth.OAuth.Handlers;
using HPD.Auth.OAuth.Services;
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
/// End-to-end wiring smoke tests — section 9 of TESTS.md.
/// Verifies that the DI container, route table, and scheme provider are
/// correctly assembled by the <c>AddOAuth()</c> / <c>MapHPDOAuthEndpoints()</c>
/// extension methods.
/// </summary>
public class IntegrationSmokeTests : IAsyncDisposable
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WebApplicationBuilder CreateBuilder(Action<HPDAuthOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddHttpContextAccessor();

        // IEventCoordinator is registered by HPD.Auth.Audit; supply a stub here.
        builder.Services.AddScoped<IEventCoordinator>(
            _ => Substitute.For<IEventCoordinator>());
        // ITokenService is registered by HPD.Auth.Authentication; supply a stub here
        // so the minimal API parameter inference does not treat it as a body parameter.
        builder.Services.AddScoped<HPD.Auth.Core.Interfaces.ITokenService>(
            _ => NSubstitute.Substitute.For<HPD.Auth.Core.Interfaces.ITokenService>());

        builder.Services
            .AddHPDAuth(o =>
            {
                o.AppName = $"SmokeTest_{Guid.NewGuid():N}";
                o.Password.RequireDigit             = false;
                o.Password.RequireLowercase         = false;
                o.Password.RequireUppercase         = false;
                o.Password.RequireNonAlphanumeric   = false;
                o.Password.RequiredLength           = 1;
                configure?.Invoke(o);
            })
            .AddOAuth();

        builder.Services.AddAuthentication(opts =>
        {
            opts.DefaultScheme          = "TestCookies";
            opts.DefaultChallengeScheme = "TestCookies";
        })
        .AddCookie("TestCookies");

        builder.Services.AddAuthorization();

        return builder;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 89 — DI container resolves ExternalLoginHandler and ExternalProviderService
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DI_ResolvesExternalLoginHandlerAndProviderService_WithoutException()
    {
        _app = CreateBuilder().Build();
        await _app.StartAsync();

        using var scope = _app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var act1 = () => sp.GetRequiredService<ExternalLoginHandler>();
        var act2 = () => sp.GetRequiredService<ExternalProviderService>();

        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 90 — Routes /auth/google and /auth/google/callback are registered
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Routes_GoogleAndCallbackEndpoints_Registered()
    {
        var builder = CreateBuilder();
        _app = builder.Build();

        // Map endpoints so their patterns are visible in EndpointDataSource.
        _app.MapHPDOAuthEndpoints();

        await _app.StartAsync();

        // Retrieve the EndpointDataSource and verify the route patterns.
        var dataSource = _app.Services.GetRequiredService<EndpointDataSource>();
        var patterns   = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        patterns.Should().Contain(p => p != null && p.Contains("auth/{provider}") &&
                                       !p.Contains("callback"),
            because: "the challenge endpoint GET /auth/{provider} must be registered");

        patterns.Should().Contain(p => p != null && p.Contains("auth/{provider}/callback"),
            because: "the callback endpoint GET /auth/{provider}/callback must be registered");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 91 — OAuthSchemeRegistrar with Google config → scheme listed
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SchemeProvider_WithGoogleConfig_ListsGoogleScheme()
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

        var appName = $"SchemeTest_{Guid.NewGuid():N}";
        builder.Services
            .AddHPDAuth(o =>
            {
                o.AppName = appName;
                o.OAuth.Providers["google"] = new OAuthProviderOptions
                {
                    ClientId     = "test-google-client-id",
                    ClientSecret = "test-google-client-secret",
                };
            })
            .AddOAuth();

        builder.Services.AddAuthorization();

        _app = builder.Build();
        await _app.StartAsync();

        var schemeProvider = _app.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var allSchemes     = await schemeProvider.GetAllSchemesAsync();

        allSchemes.Should().Contain(s => s.Name == "Google",
            because: "AddOAuth() should have called AddGoogle() for the configured provider");
    }
}
