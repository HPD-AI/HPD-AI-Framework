using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using HPD.Auth.Authentication.Tests.Helpers;
using HPD.Auth.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Authentication.Tests.TokenService;

/// <summary>
/// Tests 30–39: Refresh token persistence and cookie-only mode (TESTS.md §1.3–1.4).
/// </summary>
[Trait("Category", "TokenService")]
[Trait("Section", "1.3-RefreshToken-Persistence")]
public class TokenService_GenerateTokensAsync_RefreshToken_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Test 30 — refresh token entity is stored after generation
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_RefreshToken_Is_Stored()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        var store   = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored  = await store.GetByTokenAsync(response.RefreshToken);

        stored.Should().NotBeNull();
        stored!.IsUsed.Should().BeFalse();
        stored.IsRevoked.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 31 — stored UserId matches
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_RefreshToken_Has_Correct_UserId()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        var store  = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored = await store.GetByTokenAsync(response.RefreshToken);

        stored!.UserId.Should().Be(user.Id);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 32 — stored JwtId matches jti in JWT
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_RefreshToken_JwtId_Matches_Jti()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        var handler = new JwtSecurityTokenHandler();
        var jwt     = handler.ReadJwtToken(response.AccessToken);
        var jti     = jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;

        var store  = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored = await store.GetByTokenAsync(response.RefreshToken);

        stored!.JwtId.Should().Be(jti);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 33 — stored ExpiresAt matches config
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_RefreshToken_ExpiresAt_Matches_Config()
    {
        var lifetime = TimeSpan.FromDays(7);
        using var scope = ServiceProviderBuilder.CreateScope(opts =>
        {
            opts.Jwt.RefreshTokenLifetime = lifetime;
        });

        var before   = DateTime.UtcNow;
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);
        var after    = DateTime.UtcNow;

        var store  = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored = await store.GetByTokenAsync(response.RefreshToken);

        stored!.ExpiresAt.Should()
            .BeCloseTo(before + lifetime, precision: TimeSpan.FromSeconds(5));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 34 — stored InstanceId matches user
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_RefreshToken_InstanceId_Matches_User()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        var store  = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored = await store.GetByTokenAsync(response.RefreshToken);

        stored!.InstanceId.Should().Be(user.InstanceId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 35 — each call generates a unique refresh token
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_Each_Call_Generates_Unique_RefreshToken()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var response1 = await svc.GenerateTokensAsync(user);
        var response2 = await svc.GenerateTokensAsync(user);

        response1.RefreshToken.Should().NotBe(response2.RefreshToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 36 — refresh token base64-decoded is 64 bytes
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_RefreshToken_Is_Base64_64Bytes()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        var bytes = Convert.FromBase64String(response.RefreshToken);
        bytes.Should().HaveCount(64);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 37 — cookie-only mode: AccessToken is empty string
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_CookieOnlyMode_AccessToken_Is_Empty()
    {
        using var scope = ServiceProviderBuilder.CreateScope(opts =>
        {
            opts.Jwt.Secret = null;
        });

        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.AccessToken.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 38 — cookie-only mode: refresh token is still stored
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_CookieOnlyMode_RefreshToken_Is_Still_Stored()
    {
        using var scope = ServiceProviderBuilder.CreateScope(opts =>
        {
            opts.Jwt.Secret = null;
        });

        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        var store  = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored = await store.GetByTokenAsync(response.RefreshToken);

        stored.Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 39 — cookie-only mode: UserDto is still populated
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_CookieOnlyMode_UserDto_Is_Still_Populated()
    {
        using var scope = ServiceProviderBuilder.CreateScope(opts =>
        {
            opts.Jwt.Secret = null;
        });

        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.User.Should().NotBeNull();
        response.User.Id.Should().Be(user.Id);
        response.User.Email.Should().Be(user.Email);
    }
}
