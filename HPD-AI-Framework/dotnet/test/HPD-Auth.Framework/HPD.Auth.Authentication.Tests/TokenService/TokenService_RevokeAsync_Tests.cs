using FluentAssertions;
using HPD.Auth.Authentication.Tests.Helpers;
using HPD.Auth.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Authentication.Tests.TokenService;

/// <summary>
/// Tests 51–59: RevokeAsync and RevokeAllForUserAsync (TESTS.md §1.7–1.8).
///
/// Each test simulates multiple HTTP requests by using separate DI scopes
/// from the same ServiceProvider. This avoids EF Core change-tracker conflicts
/// that occur when the same DbContext instance is used for both Add and Update
/// on the same entity — a situation that does not happen in production where
/// each request gets a fresh scoped DbContext.
/// </summary>
[Trait("Category", "TokenService")]
[Trait("Section", "1.7-1.8-RevokeAsync")]
public class TokenService_RevokeAsync_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Test 51 — RevokeAsync returns true for a valid token
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RevokeAsync_Returns_True_For_Valid_Token()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        string refreshTokenValue;
        using (var scope1 = sp.CreateScope())
        {
            var user     = await ServiceProviderBuilder.CreateUserAsync(scope1);
            var svc      = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var response = await svc.GenerateTokensAsync(user);
            refreshTokenValue = response.RefreshToken;
        }

        using var scope2 = sp.CreateScope();
        var svc2   = scope2.ServiceProvider.GetRequiredService<ITokenService>();
        var result = await svc2.RevokeAsync(refreshTokenValue);

        result.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 52 — RevokeAsync marks IsRevoked = true
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RevokeAsync_Marks_Token_IsRevoked_True()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        string refreshTokenValue;
        using (var scope1 = sp.CreateScope())
        {
            var user     = await ServiceProviderBuilder.CreateUserAsync(scope1);
            var svc      = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var response = await svc.GenerateTokensAsync(user);
            refreshTokenValue = response.RefreshToken;
        }

        using (var scope2 = sp.CreateScope())
        {
            var svc = scope2.ServiceProvider.GetRequiredService<ITokenService>();
            await svc.RevokeAsync(refreshTokenValue);
        }

        using var scope3 = sp.CreateScope();
        var store  = scope3.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored = await store.GetByTokenAsync(refreshTokenValue);

        stored!.IsRevoked.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 53 — RevokeAsync sets RevokedAt
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RevokeAsync_Sets_RevokedAt()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        string refreshTokenValue;
        using (var scope1 = sp.CreateScope())
        {
            var user     = await ServiceProviderBuilder.CreateUserAsync(scope1);
            var svc      = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var response = await svc.GenerateTokensAsync(user);
            refreshTokenValue = response.RefreshToken;
        }

        using (var scope2 = sp.CreateScope())
        {
            var svc = scope2.ServiceProvider.GetRequiredService<ITokenService>();
            await svc.RevokeAsync(refreshTokenValue);
        }

        using var scope3 = sp.CreateScope();
        var store  = scope3.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored = await store.GetByTokenAsync(refreshTokenValue);

        stored!.RevokedAt.Should().NotBeNull();
        stored.RevokedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 54 — RevokeAsync returns false for an unknown token
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RevokeAsync_Returns_False_For_Unknown_Token()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var result = await svc.RevokeAsync(Convert.ToBase64String(new byte[64]));

        result.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 55 — revoked token cannot be refreshed
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RevokeAsync_Revoked_Token_Cannot_Be_Refreshed()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        string refreshTokenValue;
        using (var scope1 = sp.CreateScope())
        {
            var user     = await ServiceProviderBuilder.CreateUserAsync(scope1);
            var svc      = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var response = await svc.GenerateTokensAsync(user);
            refreshTokenValue = response.RefreshToken;
        }

        using (var scope2 = sp.CreateScope())
        {
            var svc = scope2.ServiceProvider.GetRequiredService<ITokenService>();
            await svc.RevokeAsync(refreshTokenValue);
        }

        using var scope3 = sp.CreateScope();
        var svc3   = scope3.ServiceProvider.GetRequiredService<ITokenService>();
        var result = await svc3.RefreshAsync(refreshTokenValue);

        result.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 56 — RevokeAllForUserAsync marks all tokens for user as revoked
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RevokeAllForUserAsync_Marks_All_Tokens_Revoked()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        Guid userId;
        string t1Val, t2Val, t3Val;

        using (var scope1 = sp.CreateScope())
        {
            var user = await ServiceProviderBuilder.CreateUserAsync(scope1);
            userId   = user.Id;
            var svc  = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var r1   = await svc.GenerateTokensAsync(user);
            t1Val    = r1.RefreshToken;
            var r2   = await svc.GenerateTokensAsync(user);
            t2Val    = r2.RefreshToken;
            var r3   = await svc.GenerateTokensAsync(user);
            t3Val    = r3.RefreshToken;
        }

        using (var scope2 = sp.CreateScope())
        {
            var svc = scope2.ServiceProvider.GetRequiredService<ITokenService>();
            await svc.RevokeAllForUserAsync(userId);
        }

        using var scope3 = sp.CreateScope();
        var store = scope3.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var e1 = await store.GetByTokenAsync(t1Val);
        var e2 = await store.GetByTokenAsync(t2Val);
        var e3 = await store.GetByTokenAsync(t3Val);

        e1!.IsRevoked.Should().BeTrue();
        e2!.IsRevoked.Should().BeTrue();
        e3!.IsRevoked.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 57 — RevokeAllForUserAsync does not affect other users
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RevokeAllForUserAsync_Does_Not_Affect_Other_Users()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        Guid userAId;
        string rBVal;

        using (var scope1 = sp.CreateScope())
        {
            var userA = await ServiceProviderBuilder.CreateUserAsync(scope1);
            var userB = await ServiceProviderBuilder.CreateUserAsync(scope1);
            userAId   = userA.Id;
            var svc   = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            await svc.GenerateTokensAsync(userA);
            var rB = await svc.GenerateTokensAsync(userB);
            rBVal  = rB.RefreshToken;
        }

        using (var scope2 = sp.CreateScope())
        {
            var svc = scope2.ServiceProvider.GetRequiredService<ITokenService>();
            await svc.RevokeAllForUserAsync(userAId);
        }

        using var scope3 = sp.CreateScope();
        var store = scope3.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var tB = await store.GetByTokenAsync(rBVal);

        tB!.IsRevoked.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 58 — already-revoked tokens stay revoked, no exception
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RevokeAllForUserAsync_Already_Revoked_Tokens_Remain_Revoked()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        Guid userId;
        string t1Val, t2Val;

        using (var scope1 = sp.CreateScope())
        {
            var user = await ServiceProviderBuilder.CreateUserAsync(scope1);
            userId   = user.Id;
            var svc  = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var r1   = await svc.GenerateTokensAsync(user);
            t1Val    = r1.RefreshToken;
            var r2   = await svc.GenerateTokensAsync(user);
            t2Val    = r2.RefreshToken;
        }

        // Revoke t1 in its own scope.
        using (var scope2 = sp.CreateScope())
        {
            var svc = scope2.ServiceProvider.GetRequiredService<ITokenService>();
            await svc.RevokeAsync(t1Val);
        }

        // RevokeAll — t1 already revoked, t2 should be revoked, no exception.
        Func<Task> act = async () =>
        {
            using var scope3 = sp.CreateScope();
            var svc = scope3.ServiceProvider.GetRequiredService<ITokenService>();
            await svc.RevokeAllForUserAsync(userId);
        };
        await act.Should().NotThrowAsync();

        using var scope4 = sp.CreateScope();
        var store = scope4.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var e1 = await store.GetByTokenAsync(t1Val);
        var e2 = await store.GetByTokenAsync(t2Val);

        e1!.IsRevoked.Should().BeTrue();
        e2!.IsRevoked.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 59 — after RevokeAllForUserAsync, RefreshAsync returns null
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RevokeAllForUserAsync_After_Revoke_All_Refresh_Returns_Null()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        Guid userId;
        string r1Val, r2Val;

        using (var scope1 = sp.CreateScope())
        {
            var user = await ServiceProviderBuilder.CreateUserAsync(scope1);
            userId   = user.Id;
            var svc  = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var r1   = await svc.GenerateTokensAsync(user);
            r1Val    = r1.RefreshToken;
            var r2   = await svc.GenerateTokensAsync(user);
            r2Val    = r2.RefreshToken;
        }

        using (var scope2 = sp.CreateScope())
        {
            var svc = scope2.ServiceProvider.GetRequiredService<ITokenService>();
            await svc.RevokeAllForUserAsync(userId);
        }

        using var scope3 = sp.CreateScope();
        var svc3    = scope3.ServiceProvider.GetRequiredService<ITokenService>();
        var result1 = await svc3.RefreshAsync(r1Val);
        var result2 = await svc3.RefreshAsync(r2Val);

        result1.Should().BeNull();
        result2.Should().BeNull();
    }
}
