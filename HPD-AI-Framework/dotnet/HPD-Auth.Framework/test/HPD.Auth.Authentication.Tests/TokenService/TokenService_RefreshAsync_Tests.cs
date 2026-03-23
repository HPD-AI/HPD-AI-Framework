using FluentAssertions;
using HPD.Auth.Authentication.Tests.Helpers;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Authentication.Tests.TokenService;

/// <summary>
/// Tests 40–50: RefreshAsync happy path and failure cases (TESTS.md §1.5–1.6).
///
/// Multi-scope pattern: each logical "HTTP request" runs in its own DI scope,
/// sharing the same in-memory EF database via a shared ServiceProvider.
/// This avoids EF Core change-tracker identity conflicts when the same entity
/// is Add'd in scope-1 and then Update'd in scope-2.
/// </summary>
[Trait("Category", "TokenService")]
[Trait("Section", "1.5-1.6-RefreshAsync")]
public class TokenService_RefreshAsync_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Test 40 — happy path returns a new TokenResponse
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RefreshAsync_Returns_New_TokenResponse()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        string originalToken;
        using (var scope1 = sp.CreateScope())
        {
            var user     = await ServiceProviderBuilder.CreateUserAsync(scope1);
            var svc      = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var response = await svc.GenerateTokensAsync(user);
            originalToken = response.RefreshToken;
        }

        using var scope2  = sp.CreateScope();
        var svc2    = scope2.ServiceProvider.GetRequiredService<ITokenService>();
        var refreshed = await svc2.RefreshAsync(originalToken);

        refreshed.Should().NotBeNull();
        refreshed!.AccessToken.Should().NotBeNullOrEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 41 — old token is marked IsUsed after refresh
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RefreshAsync_Old_Token_Marked_As_Used()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        string originalToken;
        using (var scope1 = sp.CreateScope())
        {
            var user     = await ServiceProviderBuilder.CreateUserAsync(scope1);
            var svc      = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var response = await svc.GenerateTokensAsync(user);
            originalToken = response.RefreshToken;
        }

        using (var scope2 = sp.CreateScope())
        {
            var svc = scope2.ServiceProvider.GetRequiredService<ITokenService>();
            await svc.RefreshAsync(originalToken);
        }

        using var scope3 = sp.CreateScope();
        var store  = scope3.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored = await store.GetByTokenAsync(originalToken);

        stored!.IsUsed.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 42 — new refresh token differs from original
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RefreshAsync_New_RefreshToken_Is_Different()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        string originalToken;
        using (var scope1 = sp.CreateScope())
        {
            var user     = await ServiceProviderBuilder.CreateUserAsync(scope1);
            var svc      = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var response = await svc.GenerateTokensAsync(user);
            originalToken = response.RefreshToken;
        }

        using var scope2   = sp.CreateScope();
        var svc2    = scope2.ServiceProvider.GetRequiredService<ITokenService>();
        var refreshed = await svc2.RefreshAsync(originalToken);

        refreshed!.RefreshToken.Should().NotBe(originalToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 43 — new refresh token is persisted
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RefreshAsync_New_RefreshToken_Is_Stored()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        string originalToken;
        using (var scope1 = sp.CreateScope())
        {
            var user     = await ServiceProviderBuilder.CreateUserAsync(scope1);
            var svc      = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var response = await svc.GenerateTokensAsync(user);
            originalToken = response.RefreshToken;
        }

        string newToken;
        using (var scope2 = sp.CreateScope())
        {
            var svc      = scope2.ServiceProvider.GetRequiredService<ITokenService>();
            var refreshed = await svc.RefreshAsync(originalToken);
            newToken = refreshed!.RefreshToken;
        }

        using var scope3 = sp.CreateScope();
        var store  = scope3.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored = await store.GetByTokenAsync(newToken);

        stored.Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 44 — unknown token returns null
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RefreshAsync_Returns_Null_For_Unknown_Token()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var result = await svc.RefreshAsync(Convert.ToBase64String(new byte[64]));

        result.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 45 — already-used token returns null
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RefreshAsync_Returns_Null_For_Used_Token()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        string originalToken;
        using (var scope1 = sp.CreateScope())
        {
            var user     = await ServiceProviderBuilder.CreateUserAsync(scope1);
            var svc      = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var response = await svc.GenerateTokensAsync(user);
            originalToken = response.RefreshToken;
        }

        // Mark used in a second scope.
        using (var scope2 = sp.CreateScope())
        {
            var store  = scope2.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
            var stored = await store.GetByTokenAsync(originalToken);
            stored!.IsUsed = true;
            await store.UpdateAsync(stored);
        }

        using var scope3 = sp.CreateScope();
        var result = await scope3.ServiceProvider.GetRequiredService<ITokenService>()
            .RefreshAsync(originalToken);

        result.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 46 — revoked token returns null
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RefreshAsync_Returns_Null_For_Revoked_Token()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        string originalToken;
        using (var scope1 = sp.CreateScope())
        {
            var user     = await ServiceProviderBuilder.CreateUserAsync(scope1);
            var svc      = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var response = await svc.GenerateTokensAsync(user);
            originalToken = response.RefreshToken;
        }

        using (var scope2 = sp.CreateScope())
        {
            var store  = scope2.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
            var stored = await store.GetByTokenAsync(originalToken);
            stored!.IsRevoked = true;
            await store.UpdateAsync(stored);
        }

        using var scope3 = sp.CreateScope();
        var result = await scope3.ServiceProvider.GetRequiredService<ITokenService>()
            .RefreshAsync(originalToken);

        result.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 47 — expired token returns null
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RefreshAsync_Returns_Null_For_Expired_Token()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        string originalToken;
        using (var scope1 = sp.CreateScope())
        {
            var user     = await ServiceProviderBuilder.CreateUserAsync(scope1);
            var svc      = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var response = await svc.GenerateTokensAsync(user);
            originalToken = response.RefreshToken;
        }

        // Back-date expiry in second scope.
        using (var scope2 = sp.CreateScope())
        {
            var store  = scope2.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
            var stored = await store.GetByTokenAsync(originalToken);
            stored!.ExpiresAt = DateTime.UtcNow.AddDays(-1);
            await store.UpdateAsync(stored);
        }

        using var scope3 = sp.CreateScope();
        var result = await scope3.ServiceProvider.GetRequiredService<ITokenService>()
            .RefreshAsync(originalToken);

        result.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 48 — user not found returns null
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RefreshAsync_Returns_Null_When_User_Not_Found()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        string originalToken;
        Guid userId;
        using (var scope1 = sp.CreateScope())
        {
            var user     = await ServiceProviderBuilder.CreateUserAsync(scope1);
            userId       = user.Id;
            var svc      = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var response = await svc.GenerateTokensAsync(user);
            originalToken = response.RefreshToken;
        }

        // Delete user in a second scope.
        using (var scope2 = sp.CreateScope())
        {
            var userManager = scope2.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var u = await userManager.FindByIdAsync(userId.ToString());
            await userManager.DeleteAsync(u!);
        }

        using var scope3 = sp.CreateScope();
        var result = await scope3.ServiceProvider.GetRequiredService<ITokenService>()
            .RefreshAsync(originalToken);

        result.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 49 — IsActive = false returns null
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RefreshAsync_Returns_Null_When_User_IsActive_False()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        string originalToken;
        Guid userId;
        using (var scope1 = sp.CreateScope())
        {
            var user     = await ServiceProviderBuilder.CreateUserAsync(scope1);
            userId       = user.Id;
            var svc      = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var response = await svc.GenerateTokensAsync(user);
            originalToken = response.RefreshToken;
        }

        using (var scope2 = sp.CreateScope())
        {
            var userManager = scope2.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var u = await userManager.FindByIdAsync(userId.ToString());
            u!.IsActive = false;
            await userManager.UpdateAsync(u);
        }

        using var scope3 = sp.CreateScope();
        var result = await scope3.ServiceProvider.GetRequiredService<ITokenService>()
            .RefreshAsync(originalToken);

        result.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 50 — IsDeleted = true returns null
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RefreshAsync_Returns_Null_When_User_IsDeleted_True()
    {
        using var sp = ServiceProviderBuilder.CreateProvider();

        string originalToken;
        Guid userId;
        using (var scope1 = sp.CreateScope())
        {
            var user     = await ServiceProviderBuilder.CreateUserAsync(scope1);
            userId       = user.Id;
            var svc      = scope1.ServiceProvider.GetRequiredService<ITokenService>();
            var response = await svc.GenerateTokensAsync(user);
            originalToken = response.RefreshToken;
        }

        using (var scope2 = sp.CreateScope())
        {
            var userManager = scope2.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var u = await userManager.FindByIdAsync(userId.ToString());
            u!.IsDeleted = true;
            await userManager.UpdateAsync(u);
        }

        using var scope3 = sp.CreateScope();
        var result = await scope3.ServiceProvider.GetRequiredService<ITokenService>()
            .RefreshAsync(originalToken);

        result.Should().BeNull();
    }
}
