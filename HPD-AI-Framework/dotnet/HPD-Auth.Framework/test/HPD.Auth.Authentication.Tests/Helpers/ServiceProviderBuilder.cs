using HPD.Auth.Authentication.Extensions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Options;
using HPD.Auth.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.Authentication.Tests.Helpers;

/// <summary>
/// Builds a fully-configured DI service provider for testing TokenService
/// and authentication extensions. Uses in-memory EF Core and the real
/// ITokenService implementation registered by AddAuthentication().
/// </summary>
internal static class ServiceProviderBuilder
{
    /// <summary>
    /// Creates a root ServiceProvider with HPDAuth registered.
    /// Multiple scopes can be created from the same provider — they share the
    /// same in-memory EF database, simulating multiple HTTP requests.
    /// </summary>
    public static ServiceProvider CreateProvider(Action<HPDAuthOptions>? configure = null)
    {
        var services = new ServiceCollection();

        // Required for ASP.NET Identity and authentication middleware logging.
        services.AddLogging();

        // Use a unique DB name per call so tests are isolated.
        var dbName = Guid.NewGuid().ToString();

        services.AddHPDAuth(opts =>
        {
            opts.AppName = dbName; // unique in-memory DB per test
            opts.Jwt.Secret              = TokenServiceFixture.DefaultSecret;
            opts.Jwt.Issuer              = TokenServiceFixture.DefaultIssuer;
            opts.Jwt.Audience            = TokenServiceFixture.DefaultAudience;
            opts.Jwt.AccessTokenLifetime  = TimeSpan.FromMinutes(15);
            opts.Jwt.RefreshTokenLifetime = TimeSpan.FromDays(14);
            configure?.Invoke(opts);
        })
        .AddAuthentication();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a scoped service provider with HPDAuth registered.
    /// Each call creates its own isolated ServiceProvider (and in-memory DB).
    /// Use this for tests that do not require cross-scope token operations.
    /// </summary>
    public static IServiceScope CreateScope(Action<HPDAuthOptions>? configure = null)
    {
        var sp = CreateProvider(configure);
        return sp.CreateScope();
    }

    /// <summary>
    /// Creates a user via UserManager within the scope and returns it.
    /// The user will have a hashed password "Test@1234" so Identity is happy.
    /// </summary>
    public static async Task<ApplicationUser> CreateUserAsync(
        IServiceScope scope,
        Action<ApplicationUser>? configure = null)
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName         = $"{Guid.NewGuid():N}@test.example",
            Email            = $"{Guid.NewGuid():N}@test.example",
            // SingleTenantContext always returns Guid.Empty, so InstanceId must match
            // for the global query filters on RefreshToken/ApplicationUser to work.
            InstanceId       = Guid.Empty,
            SubscriptionTier = "pro",
            IsActive         = true,
            IsDeleted        = false,
            EmailConfirmedAt = DateTime.UtcNow,
            Created          = DateTime.UtcNow,
        };
        configure?.Invoke(user);

        var result = await userManager.CreateAsync(user, "Test@1234!");
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to create test user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        return user;
    }
}
