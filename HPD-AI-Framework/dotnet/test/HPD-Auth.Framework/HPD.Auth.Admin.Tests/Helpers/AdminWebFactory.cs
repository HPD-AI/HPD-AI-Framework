using HPD.Auth.Admin.Extensions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Extensions;
using HPD.Auth.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace HPD.Auth.Admin.Tests.Helpers;

/// <summary>
/// In-process test host for the Admin API.
/// Each instance gets a unique in-memory DB name so tests are fully isolated.
/// </summary>
public class AdminWebFactory : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly string _dbName;

    public AdminWebFactory(string? dbName = null)
    {
        _dbName = dbName ?? $"AdminTest_{Guid.NewGuid():N}";
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
                // Relax password requirements so tests can use simple passwords.
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredLength = 6;
            })
            .AddAdmin();

        // Replace JWT Bearer with a test scheme that accepts any claim principal
        // constructed by the test using TestAuthHandler.
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
            options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
        })
        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
            TestAuthHandler.SchemeName, _ => { });

        builder.Services.AddAuthorization(opts =>
        {
            opts.AddPolicy("RequireAdmin", p => p.RequireRole("Admin"));
        });

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHPDAdminEndpoints();

        return app;
    }

    /// <summary>
    /// Get an HttpClient that sends requests as an authenticated Admin.
    /// </summary>
    public HttpClient CreateAdminClient()
    {
        var testServer = _app.GetTestServer();
        var client = testServer.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "Admin");
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        return client;
    }

    /// <summary>
    /// Get an HttpClient with no authentication headers.
    /// </summary>
    public HttpClient CreateAnonymousClient()
    {
        return _app.GetTestServer().CreateClient();
    }

    /// <summary>
    /// Get an HttpClient authenticated as a regular user (no Admin role).
    /// </summary>
    public HttpClient CreateRegularUserClient()
    {
        var testServer = _app.GetTestServer();
        var client = testServer.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "User");
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        return client;
    }

    /// <summary>
    /// Resolve a scoped service for direct DB / UserManager access in assertions.
    /// </summary>
    public T GetService<T>() where T : notnull
    {
        return _app.Services.GetRequiredService<T>();
    }

    /// <summary>
    /// Create a scope and resolve a service.  Caller is responsible for disposing the scope.
    /// </summary>
    public (IServiceScope Scope, T Service) CreateScope<T>() where T : notnull
    {
        var scope = _app.Services.CreateScope();
        return (scope, scope.ServiceProvider.GetRequiredService<T>());
    }

    /// <summary>
    /// Seed an ApplicationUser into the in-memory database.
    /// Returns the created user.
    /// </summary>
    public async Task<ApplicationUser> SeedUserAsync(
        string email,
        string? password = null,
        bool emailConfirmed = true,
        bool isActive = true,
        string? role = null,
        Action<ApplicationUser>? configure = null)
    {
        using var scope = _app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed,
            IsActive = isActive,
        };
        configure?.Invoke(user);

        IdentityResult result = password is not null
            ? await userManager.CreateAsync(user, password)
            : await userManager.CreateAsync(user);

        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to seed user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        if (emailConfirmed)
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            await userManager.ConfirmEmailAsync(user, token);
            user.EmailConfirmedAt = DateTime.UtcNow;
            await userManager.UpdateAsync(user);
        }

        if (role is not null)
        {
            // Ensure the role exists.
            using var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new ApplicationRole { Name = role });

            await userManager.AddToRoleAsync(user, role);
        }

        return user;
    }

    /// <summary>
    /// Ensure a role exists in the store.
    /// </summary>
    public async Task EnsureRoleAsync(string roleName)
    {
        using var scope = _app.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        if (!await roleManager.RoleExistsAsync(roleName))
            await roleManager.CreateAsync(new ApplicationRole { Name = roleName });
    }

    /// <summary>
    /// Get the audit log entries for a user from the DB.
    /// </summary>
    public async Task<IReadOnlyList<AuditLog>> GetAuditLogsAsync(Guid? userId = null, string? action = null)
    {
        using var scope = _app.Services.CreateScope();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
        return await auditLogger.QueryAsync(new AuditLogQuery(
            UserId: userId,
            Action: action,
            PageSize: 500));
    }

    public async Task StartAsync() => await _app.StartAsync();

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
