using System.Net;
using System.Text.Json;
using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Models;
using HPD.Auth.Core.Options;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.OAuth.Extensions;
using HPD.Auth.OAuth.Handlers;
using HPD.Auth.OAuth.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HPD.Events;
using NSubstitute;
using Xunit;

namespace HPD.Auth.OAuth.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// ControllableExternalLoginHandler
//
// ExternalLoginHandler is sealed, so NSubstitute cannot proxy it.
// This concrete subclass is not possible either (it's sealed).
// Instead we register a special SignInManager that returns a predetermined
// result by pre-configuring what GetExternalLoginInfoAsync and
// ExternalLoginSignInAsync return.
//
// For callback tests that need a specific ExternalLoginResult we take a
// different approach: override the DI registration with a real handler
// whose SignInManager is configured to produce the desired outcome.
//
// For simplicity we use a thin wrapper that exposes a settable result:
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A real <see cref="ExternalLoginHandler"/> whose callback result can be
/// pre-configured for each test scenario. Because <c>ExternalLoginHandler</c>
/// is <c>sealed</c>, we cannot use NSubstitute to proxy it directly. Instead,
/// we configure its <see cref="SignInManager{TUser}"/> substitute to produce
/// the desired <see cref="SignInResult"/> so that <c>HandleCallbackAsync</c>
/// follows the expected path.
/// </summary>
internal sealed class TestExternalLoginHandlerDriver : IDisposable
{
    public ExternalLoginHandler Handler { get; }

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IServiceProvider _sp;
    private readonly IServiceScope _scope;

    public TestExternalLoginHandlerDriver()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var driverDbName = $"TestDriver_{Guid.NewGuid():N}";
        var driverConnStr = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = $"file:{driverDbName}?mode=memory&cache=shared",
            ForeignKeys = false
        }.ToString();
        var driverKeepAlive = new Microsoft.Data.Sqlite.SqliteConnection(driverConnStr);
        driverKeepAlive.Open();
        services.AddSingleton(driverKeepAlive);
        services.AddDbContext<HPDAuthDbContext>(opts =>
            opts.UseSqlite(driverConnStr));
        services.AddScoped<ITenantContext>(_ => new SingleTenantContext());

        _sp    = services.BuildServiceProvider();
        _scope = _sp.CreateScope();
        var db = _scope.ServiceProvider.GetRequiredService<HPDAuthDbContext>();
        db.Database.EnsureCreated();

        _userManager = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(),
            null, null, null, null, null, null, null, null);

        _signInManager = Substitute.For<SignInManager<ApplicationUser>>(
            _userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null, null, null, null);

        Handler = new ExternalLoginHandler(
            _userManager,
            _signInManager,
            db,
            Substitute.For<IEventCoordinator>(),
            Substitute.For<IAuditLogger>(),
            new HPDAuthOptions { OAuth = { StoreRawProfileData = false } },
            NullLogger<ExternalLoginHandler>.Instance);
    }

    // ── Scenario setup ────────────────────────────────────────────────────────

    public void ConfigureForSuccess(ApplicationUser user, string provider = "Google")
    {
        var info = FakeLoginInfo(provider);
        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.Success);
        _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);
    }

    public void ConfigureFor2FA(ApplicationUser user, string provider = "Google")
    {
        var info = FakeLoginInfo(provider);
        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.TwoFactorRequired);
        _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).Returns(user);
    }

    public void ConfigureForFailure(string errorMessage, string provider = "Google")
    {
        var info = FakeLoginInfo(provider);
        _signInManager.GetExternalLoginInfoAsync().Returns(info);
        _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, false, false)
            .Returns(SignInResult.LockedOut);
    }

    // null info → "External login info not available"
    public void ConfigureForInfoUnavailable()
    {
        _signInManager.GetExternalLoginInfoAsync().Returns((ExternalLoginInfo?)null);
    }

    private static ExternalLoginInfo FakeLoginInfo(string provider)
    {
        var identity  = new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, "test@example.com") },
            "test");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        return new ExternalLoginInfo(principal, provider, $"{provider}-key-1", provider);
    }

    public void Dispose()
    {
        _scope.Dispose();
        ((IDisposable)_sp).Dispose();
    }
}

/// <summary>
/// Integration-style tests for <see cref="HPD.Auth.OAuth.Endpoints.OAuthEndpoints"/>.
/// Covers section 6 of TESTS.md (challenge and callback endpoints).
/// </summary>
public class OAuthEndpointsTests : IAsyncDisposable
{
    private WebApplication _app = null!;
    private TestExternalLoginHandlerDriver _driver = null!;
    private ITokenService _tokenService = null!;
    private HttpClient _client = null!;

    // ── Build the test host ───────────────────────────────────────────────────

    private async Task InitAsync()
    {
        _driver       = new TestExternalLoginHandlerDriver();
        _tokenService = Substitute.For<ITokenService>();

        _app = BuildApp();
        await _app.StartAsync();
        // Disable automatic redirect following so we can assert on 302 responses.
        _client = new HttpClient(new NoRedirectHandler(_app.GetTestServer().CreateHandler()))
        {
            BaseAddress = new Uri("http://localhost")
        };
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        _driver.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private WebApplication BuildApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();

        builder.Services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        builder.Services.AddHttpContextAccessor();

        // SQLite in-memory DB + Identity
        var dbName = $"OAuthEndpointTest_{Guid.NewGuid():N}";
        var connStr = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = $"file:{dbName}?mode=memory&cache=shared",
            ForeignKeys = false
        }.ToString();
        var keepAlive = new Microsoft.Data.Sqlite.SqliteConnection(connStr);
        keepAlive.Open();
        builder.Services.AddSingleton(keepAlive);
        builder.Services.AddDbContext<HPDAuthDbContext>(
            opts => opts.UseSqlite(connStr),
            ServiceLifetime.Scoped);
        builder.Services.AddScoped<ITenantContext>(_ => new SingleTenantContext());

        builder.Services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<HPDAuthDbContext>();

        // Register the pre-configured handler and token service.
        builder.Services.AddScoped(_ => _driver.Handler);
        builder.Services.AddScoped(_ => _tokenService);
        builder.Services.AddScoped<ExternalProviderService>();

        // Authentication pipeline.
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme          = "TestCookies";
            options.DefaultChallengeScheme = "TestCookies";
        })
        .AddCookie("TestCookies");

        builder.Services.AddAuthorization();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<HPDAuthDbContext>().Database.EnsureCreated();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHPDOAuthEndpoints();

        return app;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 6.1 — Challenge endpoint: GET /auth/{provider}
    // ─────────────────────────────────────────────────────────────────────────

    // 65 — known provider "google" → accepted (not 400)
    [Fact]
    public async Task Challenge_KnownProvider_Google_NotBadRequest()
    {
        await InitAsync();

        var resp = await _client.GetAsync("/auth/google");

        ((int)resp.StatusCode).Should().NotBe(400);
    }

    // 66 — lowercase "github" normalized to "GitHub" → accepted
    [Fact]
    public async Task Challenge_LowercaseGithub_NormalizesAndNotBadRequest()
    {
        await InitAsync();

        var resp = await _client.GetAsync("/auth/github");

        ((int)resp.StatusCode).Should().NotBe(400);
    }

    // 67 — unknown provider → 400
    [Fact]
    public async Task Challenge_UnknownProvider_Returns400()
    {
        await InitAsync();

        var resp = await _client.GetAsync("/auth/stripe");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 68 — returnUrl query param → request not rejected
    [Fact]
    public async Task Challenge_ReturnUrl_NotRejected()
    {
        await InitAsync();

        var resp = await _client.GetAsync("/auth/google?returnUrl=%2Fdashboard");

        ((int)resp.StatusCode).Should().NotBe(400);
    }

    // 69 — useCookies=false → request not rejected
    [Fact]
    public async Task Challenge_UseCookiesFalse_NotRejected()
    {
        await InitAsync();

        var resp = await _client.GetAsync("/auth/google?useCookies=false");

        ((int)resp.StatusCode).Should().NotBe(400);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 6.2 — Callback endpoint: GET /auth/{provider}/callback
    // ─────────────────────────────────────────────────────────────────────────

    // 70 — remoteError present → redirect to /auth/error
    [Fact]
    public async Task Callback_RemoteError_RedirectsToErrorPage()
    {
        await InitAsync();
        _driver.ConfigureForInfoUnavailable(); // not reached, but avoids null-ref

        var resp = await _client.GetAsync(
            "/auth/google/callback?remoteError=access_denied");

        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        resp.Headers.Location?.ToString().Should().StartWith("/auth/error");
        resp.Headers.Location?.ToString().Should().Contain("access_denied");
    }

    // 71 — unknown provider → 400
    [Fact]
    public async Task Callback_UnknownProvider_Returns400()
    {
        await InitAsync();

        var resp = await _client.GetAsync("/auth/stripe/callback");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 72 — success + useCookies=true → redirect to returnUrl
    [Fact]
    public async Task Callback_SuccessWithCookies_RedirectsToReturnUrl()
    {
        await InitAsync();

        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "cb@example.com" };
        _driver.ConfigureForSuccess(user);

        var resp = await _client.GetAsync(
            "/auth/google/callback?useCookies=true&returnUrl=%2Fdashboard");

        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        resp.Headers.Location?.ToString().Should().Be("/dashboard");
    }

    // 73 — success + useCookies=false → 200 JSON with token
    [Fact]
    public async Task Callback_SuccessWithJwt_Returns200WithTokenJson()
    {
        await InitAsync();

        var user   = new ApplicationUser { Id = Guid.NewGuid(), Email = "jwt@example.com" };
        var tokens = new TokenResponse
        {
            AccessToken  = "access_token_value",
            RefreshToken = "refresh_token_value",
            ExpiresIn    = 3600,
            ExpiresAt    = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            User = new UserTokenDto
            {
                Id               = user.Id,
                Email            = "jwt@example.com",
                EmailConfirmedAt = null,
                UserMetadata     = JsonSerializer.Deserialize<JsonElement>("{}"),
                AppMetadata      = JsonSerializer.Deserialize<JsonElement>("{}"),
                RequiredActions  = new List<string>(),
                CreatedAt        = DateTime.UtcNow,
                SubscriptionTier = "free",
            }
        };

        _driver.ConfigureForSuccess(user);
        _tokenService.GenerateTokensAsync(user, Arg.Any<CancellationToken>())
            .Returns(tokens);

        var resp = await _client.GetAsync("/auth/google/callback?useCookies=false");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("access_token_value");
    }

    // 74 — requires_two_factor → redirect to /auth/2fa
    [Fact]
    public async Task Callback_RequiresTwoFactor_RedirectsTo2faPage()
    {
        await InitAsync();

        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "2fa@example.com" };
        _driver.ConfigureFor2FA(user);

        var resp = await _client.GetAsync(
            "/auth/google/callback?returnUrl=%2Fhome&useCookies=true");

        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        resp.Headers.Location?.ToString().Should().StartWith("/auth/2fa");
    }

    // 75 — other failure → redirect to /auth/error
    [Fact]
    public async Task Callback_OtherFailure_RedirectsToErrorPage()
    {
        await InitAsync();

        _driver.ConfigureForFailure("Account is locked out");

        var resp = await _client.GetAsync("/auth/google/callback");

        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        resp.Headers.Location?.ToString().Should().StartWith("/auth/error");
    }

    // 76 — absolute external returnUrl rejected → redirects to "/"
    [Fact]
    public async Task Callback_AbsoluteExternalReturnUrl_RedirectsToRoot()
    {
        await InitAsync();

        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "safe@example.com" };
        _driver.ConfigureForSuccess(user);

        var externalUrl = Uri.EscapeDataString("https://evil.example.com/steal");
        var resp = await _client.GetAsync(
            $"/auth/google/callback?useCookies=true&returnUrl={externalUrl}");

        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        resp.Headers.Location?.ToString().Should().Be("/");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gap 5 — IsLocalUrl protocol-relative URL attacks
    // ─────────────────────────────────────────────────────────────────────────

    // Gap 5a — "//evil.com" protocol-relative returnUrl → redirects to "/"
    [Fact]
    public async Task Callback_ProtocolRelativeReturnUrl_RedirectsToRoot()
    {
        await InitAsync();

        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "proto@example.com" };
        _driver.ConfigureForSuccess(user);

        var protocolRelative = Uri.EscapeDataString("//evil.example.com/steal");
        var resp = await _client.GetAsync(
            $"/auth/google/callback?useCookies=true&returnUrl={protocolRelative}");

        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        resp.Headers.Location?.ToString().Should().Be("/");
    }

    // Gap 5b — "/\evil.com" backslash protocol-relative returnUrl → redirects to "/"
    [Fact]
    public async Task Callback_BackslashProtocolRelativeReturnUrl_RedirectsToRoot()
    {
        await InitAsync();

        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "bslash@example.com" };
        _driver.ConfigureForSuccess(user);

        var backslashUrl = Uri.EscapeDataString("/\\evil.example.com");
        var resp = await _client.GetAsync(
            $"/auth/google/callback?useCookies=true&returnUrl={backslashUrl}");

        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        resp.Headers.Location?.ToString().Should().Be("/");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gap 10 — remoteError with special characters is URL-encoded in redirect
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_RemoteErrorWithSpecialChars_EncodedInRedirectUrl()
    {
        await InitAsync();
        _driver.ConfigureForInfoUnavailable();

        var resp = await _client.GetAsync(
            "/auth/google/callback?remoteError=access+denied+%26+more");

        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = resp.Headers.Location?.ToString();
        location.Should().StartWith("/auth/error");
        // The message should be URL-encoded (no raw spaces or ampersands in the query value).
        location.Should().NotContain(" ");
    }
}

/// <summary>
/// DelegatingHandler that returns redirect responses without following them,
/// so tests can assert on Location headers.
/// </summary>
internal sealed class NoRedirectHandler : DelegatingHandler
{
    public NoRedirectHandler(HttpMessageHandler inner) : base(inner) { }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => base.SendAsync(request, cancellationToken);
}
