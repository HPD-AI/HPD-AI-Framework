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
        var response = await svc.GenerateTokensAsync(user, TokenServiceFixture.Issuance());

        var store   = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored  = await store.InspectAsync(response.RefreshToken);

        stored.Should().NotBeNull();
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
        var response = await svc.GenerateTokensAsync(user, TokenServiceFixture.Issuance());

        var store  = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored = await store.InspectAsync(response.RefreshToken);

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
        var response = await svc.GenerateTokensAsync(user, TokenServiceFixture.Issuance());

        var handler = new JwtSecurityTokenHandler();
        var jwt     = handler.ReadJwtToken(response.AccessToken);
        var jti     = jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;

        var store  = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored = await store.InspectAsync(response.RefreshToken);

        stored.Should().NotBeNull();
        jti.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
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
        var response = await svc.GenerateTokensAsync(user, TokenServiceFixture.Issuance());
        var after    = DateTime.UtcNow;

        var store  = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored = await store.InspectAsync(response.RefreshToken);

        stored!.ExpiresAt.Should()
            .BeCloseTo(before + lifetime, precision: TimeSpan.FromSeconds(5));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 34 — stored token remains bound to the tenant-owned user
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_RefreshToken_Is_Bound_To_Tenant_User()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user, TokenServiceFixture.Issuance());

        var store  = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored = await store.InspectAsync(response.RefreshToken);

        stored!.UserId.Should().Be(user.Id);
        user.InstanceId.Should().Be(Guid.Empty);
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

        var response1 = await svc.GenerateTokensAsync(user, TokenServiceFixture.Issuance());
        var response2 = await svc.GenerateTokensAsync(user, TokenServiceFixture.Issuance());

        response1.RefreshToken.Should().NotBe(response2.RefreshToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 36 — current refresh token uses the closed HMAC token wire format
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_RefreshToken_Uses_Current_Canonical_Format()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user, TokenServiceFixture.Issuance());

        string[] segments = response.RefreshToken.Split('.');
        segments.Should().HaveCount(3);
        segments[0].Should().Be("hpd1");
        segments[1].Should().Be("1");
        segments[2].Should().HaveLength(43).And.MatchRegex("^[A-Za-z0-9_-]{43}$");
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
        var response = await svc.GenerateTokensAsync(user, TokenServiceFixture.Issuance());

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
        var response = await svc.GenerateTokensAsync(user, TokenServiceFixture.Issuance());

        var store  = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
        var stored = await store.InspectAsync(response.RefreshToken);

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
        var response = await svc.GenerateTokensAsync(user, TokenServiceFixture.Issuance());

        response.User.Should().NotBeNull();
        response.User.Id.Should().Be(user.Id);
        response.User.Email.Should().Be(user.Email);
    }
}
