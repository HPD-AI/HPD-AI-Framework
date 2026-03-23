using HPD.Auth.Audit.Extensions;
using HPD.Auth.Authentication.Extensions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HPD.Auth.Tests.Helpers;

/// <summary>
/// Builds a minimal ASP.NET Core test server with the full HPD.Auth endpoint
/// stack registered. Uses <see cref="WebApplication"/> so that all default
/// minimal-API services (JSON, routing, etc.) are pre-configured.
///
/// Each instance uses a unique in-memory database name to keep tests isolated.
/// </summary>
internal sealed class AuthWebApplicationFactory : IAsyncDisposable
{
    private readonly WebApplication _app;

    public AuthWebApplicationFactory(
        string appName,
        bool requireEmailConfirmation = false,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());

        // Use the test server transport instead of real Kestrel
        builder.WebHost.UseTestServer();

        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        builder.Services.AddMemoryCache();
        builder.Services.AddHttpContextAccessor();

        builder.Services
            .AddHPDAuth(o =>
            {
                o.AppName = appName;
                o.Password.RequiredLength = 8;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Features.RequireEmailConfirmation = requireEmailConfirmation;
                o.Features.EnableAuditLog = true;
                o.Jwt.Secret = "SuperSecretKeyForTestingPurposesOnly1234567890!";
                o.Jwt.AccessTokenLifetime = TimeSpan.FromHours(1);
            })
            .AddAuthentication()
            .AddAudit();

        // Allow callers to replace or add services (e.g., mock email sender)
        configureServices?.Invoke(builder.Services);

        _app = builder.Build();

        _app.UseHPDAuth();
        _app.UseAuthEventObserver();
        _app.MapHPDAuthEndpoints();

        _app.StartAsync().GetAwaiter().GetResult();

        // Seed the default "User" role required by SignUpAsync.
        using var scope = _app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var roleExists = roleManager.RoleExistsAsync("User").GetAwaiter().GetResult();
        if (!roleExists)
        {
            var result = roleManager.CreateAsync(new ApplicationRole("User")).GetAwaiter().GetResult();
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Failed to seed 'User' role: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
    }

    /// <summary>The DI container from the running app.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>Creates an <see cref="HttpClient"/> that communicates with the test server.</summary>
    public HttpClient CreateClient()
        => _app.GetTestClient();

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
