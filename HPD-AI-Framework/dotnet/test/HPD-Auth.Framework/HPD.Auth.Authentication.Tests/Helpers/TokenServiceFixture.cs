using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Options;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace HPD.Auth.Authentication.Tests.Helpers;

/// <summary>
/// Shared factory helpers for building a TokenService under test.
/// Uses Moq stubs for UserManager and IRefreshTokenStore so tests
/// can be pure unit tests without a running database.
/// </summary>
internal static class TokenServiceFixture
{
    // ─────────────────────────────────────────────────────────────────────────
    // Default test secret — 32+ chars, arbitrary value safe for HS256.
    // ─────────────────────────────────────────────────────────────────────────
    public const string DefaultSecret = "super-secret-key-for-hpd-auth-tests-32chars!";
    public const string DefaultIssuer   = "https://test.hpdauth.example";
    public const string DefaultAudience = "hpd-test-app";

    // ─────────────────────────────────────────────────────────────────────────
    // Default options
    // ─────────────────────────────────────────────────────────────────────────
    public static HPDAuthOptions DefaultOptions(Action<HPDAuthOptions>? configure = null)
    {
        var opts = new HPDAuthOptions
        {
            AppName = "TestApp",
        };
        opts.Jwt.Secret              = DefaultSecret;
        opts.Jwt.Issuer              = DefaultIssuer;
        opts.Jwt.Audience            = DefaultAudience;
        opts.Jwt.AccessTokenLifetime  = TimeSpan.FromMinutes(15);
        opts.Jwt.RefreshTokenLifetime = TimeSpan.FromDays(14);
        configure?.Invoke(opts);
        return opts;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // In-memory refresh token store — backs tests that need real persistence.
    // ─────────────────────────────────────────────────────────────────────────
    public static InMemoryRefreshTokenStore CreateStore() => new();

    // ─────────────────────────────────────────────────────────────────────────
    // Build a minimal UserManager mock.
    // ─────────────────────────────────────────────────────────────────────────
    public static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var mgr   = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        return mgr;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Build a default ApplicationUser suitable for most tests.
    // ─────────────────────────────────────────────────────────────────────────
    public static ApplicationUser DefaultUser(Action<ApplicationUser>? configure = null)
    {
        var user = new ApplicationUser
        {
            Id               = Guid.NewGuid(),
            UserName         = "testuser@example.com",
            Email            = "testuser@example.com",
            InstanceId       = Guid.NewGuid(),
            SubscriptionTier = "pro",
            IsActive         = true,
            IsDeleted        = false,
            EmailConfirmedAt = DateTime.UtcNow,
            Created          = DateTime.UtcNow,
            SecurityStamp    = Guid.NewGuid().ToString(),
        };
        configure?.Invoke(user);
        return user;
    }
}
