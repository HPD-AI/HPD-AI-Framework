using System.Text.Json;
using FluentAssertions;
using HPD.Auth.Authentication.Tests.Helpers;
using HPD.Auth.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Authentication.Tests.TokenService;

/// <summary>
/// Tests 15–29: TokenResponse shape returned by GenerateTokensAsync (TESTS.md §1.2).
/// </summary>
[Trait("Category", "TokenService")]
[Trait("Section", "1.2-TokenResponse-Shape")]
public class TokenService_GenerateTokensAsync_TokenResponse_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Test 15 — ExpiresIn is total seconds of AccessTokenLifetime
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_TokenResponse_ExpiresIn_Is_Seconds()
    {
        var lifetime = TimeSpan.FromMinutes(30);
        using var scope = ServiceProviderBuilder.CreateScope(opts =>
        {
            opts.Jwt.AccessTokenLifetime = lifetime;
        });

        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.ExpiresIn.Should().Be((int)lifetime.TotalSeconds);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 16 — ExpiresAt is a Unix timestamp approximately UtcNow + ExpiresIn
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_TokenResponse_ExpiresAt_Is_Unix_Timestamp()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var before   = DateTimeOffset.UtcNow;
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);
        var after    = DateTimeOffset.UtcNow;

        var expectedMin = before.AddSeconds(response.ExpiresIn - 5).ToUnixTimeSeconds();
        var expectedMax = after.AddSeconds(response.ExpiresIn + 5).ToUnixTimeSeconds();

        response.ExpiresAt.Should().BeGreaterThanOrEqualTo(expectedMin)
                           .And.BeLessThanOrEqualTo(expectedMax);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 17 — TokenType is "bearer"
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_TokenResponse_TokenType_Is_bearer()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.TokenType.Should().Be("bearer");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 18 — AccessToken is non-empty
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_TokenResponse_Has_NonEmpty_AccessToken()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.AccessToken.Should().NotBeNullOrEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 19 — RefreshToken is non-empty
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_TokenResponse_Has_NonEmpty_RefreshToken()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.RefreshToken.Should().NotBeNullOrEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 20 — User.Id matches
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_TokenResponse_User_Id_Matches()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.User.Id.Should().Be(user.Id);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 21 — User.Email matches
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_TokenResponse_User_Email_Matches()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user     = await ServiceProviderBuilder.CreateUserAsync(scope);
        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.User.Email.Should().Be(user.Email);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 22 — EmailConfirmedAt set when confirmed
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_TokenResponse_User_EmailConfirmedAt_Set_When_Confirmed()
    {
        var confirmedAt = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        using var scope = ServiceProviderBuilder.CreateScope();
        var user = await ServiceProviderBuilder.CreateUserAsync(scope,
            u => u.EmailConfirmedAt = confirmedAt);

        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.User.EmailConfirmedAt.Should().Be(confirmedAt);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 23 — EmailConfirmedAt null when not confirmed
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_TokenResponse_User_EmailConfirmedAt_Null_When_Not_Confirmed()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user = await ServiceProviderBuilder.CreateUserAsync(scope,
            u => u.EmailConfirmedAt = null);

        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.User.EmailConfirmedAt.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 24 — UserMetadata is a JsonElement with expected property
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_TokenResponse_User_UserMetadata_Is_JsonElement()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user = await ServiceProviderBuilder.CreateUserAsync(scope,
            u => u.UserMetadata = """{"theme":"dark"}""");

        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.User.UserMetadata.ValueKind.Should().Be(JsonValueKind.Object);
        response.User.UserMetadata.GetProperty("theme").GetString().Should().Be("dark");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 25 — AppMetadata is a JsonElement with expected property
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_TokenResponse_User_AppMetadata_Is_JsonElement()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user = await ServiceProviderBuilder.CreateUserAsync(scope,
            u => u.AppMetadata = """{"plan":"pro"}""");

        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.User.AppMetadata.ValueKind.Should().Be(JsonValueKind.Object);
        response.User.AppMetadata.GetProperty("plan").GetString().Should().Be("pro");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 26 — Invalid JSON in UserMetadata falls back to empty object
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_TokenResponse_User_InvalidJson_Falls_Back_To_EmptyObject()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user = await ServiceProviderBuilder.CreateUserAsync(scope,
            u => u.UserMetadata = "not-json");

        var svc = scope.ServiceProvider.GetRequiredService<ITokenService>();
        Core.Models.TokenResponse? response = null;

        Func<Task> act = async () => response = await svc.GenerateTokensAsync(user);
        await act.Should().NotThrowAsync();

        response!.User.UserMetadata.ValueKind.Should().Be(JsonValueKind.Object);
        response.User.UserMetadata.EnumerateObject().Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 27 — RequiredActions matches
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_TokenResponse_User_RequiredActions_Matches()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user = await ServiceProviderBuilder.CreateUserAsync(scope, u =>
        {
            u.RequiredActions = new List<string> { "VERIFY_EMAIL" };
        });

        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.User.RequiredActions.Should().Contain("VERIFY_EMAIL");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 28 — CreatedAt matches user.Created
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_TokenResponse_User_CreatedAt_Matches()
    {
        var created = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using var scope = ServiceProviderBuilder.CreateScope();
        var user = await ServiceProviderBuilder.CreateUserAsync(scope,
            u => u.Created = created);

        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.User.CreatedAt.Should().Be(created);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 29 — SubscriptionTier matches
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GenerateTokensAsync_TokenResponse_User_SubscriptionTier_Matches()
    {
        using var scope = ServiceProviderBuilder.CreateScope();
        var user = await ServiceProviderBuilder.CreateUserAsync(scope,
            u => u.SubscriptionTier = "enterprise");

        var svc      = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var response = await svc.GenerateTokensAsync(user);

        response.User.SubscriptionTier.Should().Be("enterprise");
    }
}
