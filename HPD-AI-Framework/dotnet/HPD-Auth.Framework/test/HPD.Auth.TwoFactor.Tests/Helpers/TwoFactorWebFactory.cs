using HPD.Auth.Audit.Extensions;
using HPD.Auth.Authentication.Extensions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Extensions;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.TwoFactor.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HPD.Auth.TwoFactor.Tests.Helpers;

/// <summary>
/// In-process test host for the TwoFactor API.
/// Each instance uses a unique in-memory DB name for full test isolation.
///
/// Implements <see cref="IAsyncLifetime"/> so it can be used as an
/// <c>IClassFixture&lt;TwoFactorWebFactory&gt;</c> — xUnit calls
/// <see cref="InitializeAsync"/> before the first test in the class and
/// <see cref="DisposeAsync"/> after the last, starting and stopping the
/// app exactly once per test class.
/// </summary>
public class TwoFactorWebFactory : IAsyncLifetime
{
    private readonly WebApplication _app;
    private readonly string _dbName;

    public string AppName => _dbName;

    public TwoFactorWebFactory() : this($"TwoFactorTest_{Guid.NewGuid():N}") { }

    internal TwoFactorWebFactory(string dbName)
    {
        _dbName = dbName;
        _app = BuildApp(_dbName);
    }

    private static WebApplication BuildApp(string dbName)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();

        builder.Services.AddLogging();
        builder.Services.AddHttpContextAccessor();

        builder.Services
            .AddHPDAuth(o =>
            {
                o.AppName = dbName;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredLength = 6;
                o.Jwt.Secret = "test-secret-32-chars-minimum-len!!";
            })
            .AddAudit()
            .AddAuthentication()
            .AddTwoFactor();

        // Replace JWT Bearer with a test scheme that accepts any principal
        // constructed by the test using TestAuthHandler.
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
            options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
        })
        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
            TestAuthHandler.SchemeName, _ => { });

        builder.Services.AddAuthorization();

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHPDTwoFactorEndpoints();

        return app;
    }

    // ── Client helpers ────────────────────────────────────────────────────────

    /// <summary>Creates an HttpClient authenticated as the given user ID.</summary>
    public HttpClient CreateAuthenticatedClient(Guid userId)
    {
        var client = _app.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        return client;
    }

    /// <summary>Creates an HttpClient with no authentication.</summary>
    public HttpClient CreateAnonymousClient()
        => _app.GetTestServer().CreateClient();

    // ── Service helpers ───────────────────────────────────────────────────────

    public T GetService<T>() where T : notnull
        => _app.Services.GetRequiredService<T>();

    public IServiceScope CreateServiceScope()
        => _app.Services.CreateScope();

    // ── Seed helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an ApplicationUser in the in-memory DB and returns the user plus
    /// a pre-configured HttpClient that sends requests as that user.
    /// </summary>
    public async Task<(ApplicationUser User, HttpClient Client)> CreateUserAsync(
        string email = "user@example.com",
        string? password = "Password1",
        bool emailConfirmed = true,
        Action<ApplicationUser>? configure = null)
    {
        using var scope = _app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed,
            IsActive = true,
        };
        configure?.Invoke(user);

        IdentityResult result = password is not null
            ? await userManager.CreateAsync(user, password)
            : await userManager.CreateAsync(user);

        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to seed user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        var client = CreateAuthenticatedClient(user.Id);
        return (user, client);
    }

    /// <summary>
    /// Sets up TOTP for a user and returns the unformatted key.
    /// Calls ResetAuthenticatorKeyAsync and GetAuthenticatorKeyAsync directly.
    /// </summary>
    public async Task<string> SetupTotpForUserAsync(ApplicationUser user)
    {
        using var scope = _app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await userManager.ResetAuthenticatorKeyAsync(user);
        var key = await userManager.GetAuthenticatorKeyAsync(user);

        return key ?? throw new InvalidOperationException("Failed to generate authenticator key.");
    }

    /// <summary>
    /// Enables 2FA and generates recovery codes for a user, returning the codes.
    /// </summary>
    public async Task<IEnumerable<string>> EnableTotpAndGetCodesAsync(ApplicationUser user)
    {
        using var scope = _app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await userManager.SetTwoFactorEnabledAsync(user, true);
        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

        return codes ?? throw new InvalidOperationException("Failed to generate recovery codes.");
    }

    /// <summary>Retrieves audit log entries from the DB.</summary>
    public async Task<IReadOnlyList<AuditLog>> GetAuditLogsAsync(Guid? userId = null, string? action = null)
    {
        using var scope = _app.Services.CreateScope();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
        return await auditLogger.QueryAsync(new AuditLogQuery(
            UserId: userId,
            Action: action,
            PageSize: 500));
    }

    // IAsyncLifetime — used when this factory is consumed as IClassFixture<T>.
    public async Task InitializeAsync() => await _app.StartAsync();

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // Kept for tests that still manage the lifecycle manually.
    public Task StartAsync() => _app.StartAsync();
}
