using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HPD.Auth.Tests.Endpoints;

/// <summary>
/// Integration tests for POST /api/auth/token
/// (grant_type=password and grant_type=refresh_token).
///
/// Note: TokenRequest uses PascalCase properties. ASP.NET minimal APIs deserialize
/// with camelCase naming policy, so we send camelCase in request bodies.
/// Response DTOs use [JsonPropertyName] snake_case attributes so GetProperty calls use snake_case.
/// </summary>
public class TokenEndpointTests
{
    // ── grant_type=password ───────────────────────────────────────────────────

    // Scenario 1: Returns TokenResponse for valid credentials

    [Fact]
    public async Task Token_Password_ReturnsTokenResponse_ForValidCredentials()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_ValidCreds");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "user@example.com", password = "Password1!" });

        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "password", username = "user@example.com", password = "Password1!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        var root = doc.RootElement;

        root.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("token_type").GetString().Should().Be("bearer");
        root.GetProperty("expires_in").GetInt32().Should().BeGreaterThan(0);
        root.GetProperty("expires_at").GetInt64().Should().BeGreaterThan(0);
        root.GetProperty("refresh_token").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("user").GetProperty("email").GetString().Should().Be("user@example.com");
    }

    // Scenario 2: Returns 400 invalid_grant for wrong password

    [Fact]
    public async Task Token_Password_Returns400_ForWrongPassword()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_WrongPwd");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "user@example.com", password = "Password1!" });

        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "password", username = "user@example.com", password = "WrongPassword" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
        doc.RootElement.GetProperty("errorDescription").GetString().Should()
            .ContainEquivalentOf("Invalid email or password");
    }

    // Scenario 3: Returns 423 Locked for a locked-out account

    [Fact]
    public async Task Token_Password_Returns423_ForLockedOutAccount()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_Locked");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "locked@example.com", password = "Password1!" });

        // Trigger lockout by repeated failures (default MaxFailedAttempts = 5)
        for (int i = 0; i < 6; i++)
        {
            await client.PostAsJsonAsync("/api/auth/token",
                new { grantType = "password", username = "locked@example.com", password = "BadPassword" });
        }

        // Now attempt with correct password — should be locked
        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "password", username = "locked@example.com", password = "Password1!" });

        response.StatusCode.Should().Be((HttpStatusCode)423);
    }

    // Scenario 4: Returns requiresTwoFactor=true when 2FA is enabled

    [Fact]
    public async Task Token_Password_ReturnsTwoFactor_When2FAEnabled()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_2FA");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "twofactor@example.com", password = "Password1!" });

        // Enable 2FA: set an authenticator key (gives user a valid TOTP provider)
        // then enable TwoFactor. IsTwoFactorEnabledAsync requires at least one valid provider.
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("twofactor@example.com");
        user.Should().NotBeNull();
        await userManager.ResetAuthenticatorKeyAsync(user!);   // seeds the TOTP key
        await userManager.SetTwoFactorEnabledAsync(user!, true);

        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "password", username = "twofactor@example.com", password = "Password1!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("requires_two_factor").GetBoolean().Should().BeTrue();
        doc.RootElement.TryGetProperty("access_token", out _).Should().BeFalse();
    }

    // Scenario 5: Returns 400 when grant_type is missing

    [Fact]
    public async Task Token_Returns400_WhenGrantTypeMissing()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_NoGrantType");
        var client = factory.CreateClient();

        // No grantType field at all
        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { username = "u@example.com", password = "p" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        var error = doc.RootElement.GetProperty("error").GetString();
        error.Should().BeOneOf("unsupported_grant_type", "invalid_request");
    }

    // ── grant_type=refresh_token ──────────────────────────────────────────────

    // Scenario 6: Returns new TokenResponse for a valid refresh token

    [Fact]
    public async Task Token_Refresh_ReturnsNewTokenResponse_ForValidToken()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Refresh_Valid");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "refresh@example.com", password = "Password1!" });

        var loginDoc = await client.LoginAsync("refresh@example.com");
        var refreshToken = loginDoc.RootElement.GetProperty("refresh_token").GetString()!;

        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "refresh_token", refreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        var root = doc.RootElement;
        root.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("refresh_token").GetString().Should().NotBeNullOrEmpty()
            .And.NotBe(refreshToken); // token is rotated
        root.GetProperty("user").GetProperty("email").GetString().Should().Be("refresh@example.com");
    }

    // Scenario 7: Returns 400 for an already-used refresh token

    [Fact]
    public async Task Token_Refresh_Returns400_ForAlreadyUsedToken()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Refresh_UsedToken");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "replay@example.com", password = "Password1!" });

        var loginDoc = await client.LoginAsync("replay@example.com");
        var refreshToken = loginDoc.RootElement.GetProperty("refresh_token").GetString()!;

        // Use the token once
        await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "refresh_token", refreshToken });

        // Try to use it again
        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "refresh_token", refreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
        doc.RootElement.GetProperty("errorDescription").GetString().Should()
            .ContainEquivalentOf("already been used");
    }

    // Scenario 8: Returns 400 for an expired/invalid refresh token

    [Fact]
    public async Task Token_Refresh_Returns400_ForExpiredToken()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Refresh_ExpiredToken");
        var client = factory.CreateClient();

        // Use a fake/random token that doesn't exist in the store
        var refreshToken = "completely-invalid-token-xyz";
        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "refresh_token", refreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    // Scenario 9: Returns 400 for a revoked refresh token

    [Fact]
    public async Task Token_Refresh_Returns400_ForRevokedToken()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Refresh_RevokedToken");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "revoke@example.com", password = "Password1!" });

        var loginDoc = await client.LoginAsync("revoke@example.com");
        var refreshToken = loginDoc.RootElement.GetProperty("refresh_token").GetString()!;

        // Revoke via logout
        var accessToken = loginDoc.RootElement.GetProperty("access_token").GetString()!;
        client.SetBearerToken(accessToken);
        await client.PostAsJsonAsync("/api/auth/logout",
            new { scope = "local", refreshToken });
        client.DefaultRequestHeaders.Authorization = null;

        // Now try to use the revoked token
        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "refresh_token", refreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    // ── Form-encoded body ─────────────────────────────────────────────────────

    // Scenario 10: Form-encoded grant_type=password happy path

    [Fact]
    public async Task Token_FormEncoded_Password_ReturnsTokenResponse()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_Form_Password");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "form@example.com", password = "Password1!" });

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "password"),
            new KeyValuePair<string, string>("username", "form@example.com"),
            new KeyValuePair<string, string>("password", "Password1!"),
        });

        var response = await client.PostAsync("/api/auth/token", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("refresh_token").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("user").GetProperty("email").GetString().Should().Be("form@example.com");
    }

    // Scenario 11: Form-encoded grant_type=refresh_token

    [Fact]
    public async Task Token_FormEncoded_Refresh_ReturnsNewTokenResponse()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_Form_Refresh");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "formrefresh@example.com", password = "Password1!" });

        var loginDoc = await client.LoginAsync("formrefresh@example.com");
        var refreshToken = loginDoc.RootElement.GetProperty("refresh_token").GetString()!;

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
        });

        var response = await client.PostAsync("/api/auth/token", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        // Token is rotated
        doc.RootElement.GetProperty("refresh_token").GetString().Should().NotBe(refreshToken);
    }

    // Scenario 12: Form-encoded with missing grant_type returns unsupported_grant_type

    [Fact]
    public async Task Token_FormEncoded_MissingGrantType_Returns400()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_Form_NoGrant");
        var client = factory.CreateClient();

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username", "u@example.com"),
            new KeyValuePair<string, string>("password", "Password1!"),
        });

        var response = await client.PostAsync("/api/auth/token", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        var error = doc.RootElement.GetProperty("error").GetString();
        error.Should().BeOneOf("unsupported_grant_type", "invalid_request");
    }

    // ── grant_type via query string ───────────────────────────────────────────

    // Scenario 13: grant_type=password passed as URL query string

    [Fact]
    public async Task Token_QueryString_GrantType_Password_ReturnsTokenResponse()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_QS_Password");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "qs@example.com", password = "Password1!" });

        // JSON body has no grantType field — it must be read from the query string
        var response = await client.PostAsJsonAsync(
            "/api/auth/token?grant_type=password",
            new { username = "qs@example.com", password = "Password1!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
    }

    // ── body.Email alias ──────────────────────────────────────────────────────

    // Scenario 14: 'email' field accepted as alias for 'username'

    [Fact]
    public async Task Token_Password_EmailAlias_ReturnsTokenResponse()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_EmailAlias");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "alias@example.com", password = "Password1!" });

        // Send 'email' instead of 'username'
        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "password", email = "alias@example.com", password = "Password1!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("user").GetProperty("email").GetString().Should().Be("alias@example.com");
    }

    // ── Null / malformed body ─────────────────────────────────────────────────

    // Scenario 15: Empty JSON body returns invalid_request

    [Fact]
    public async Task Token_NullBody_Returns400_InvalidRequest()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_NullBody");
        var client = factory.CreateClient();

        // Send empty string body with JSON content type — body deserializes to null
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/token")
        {
            Content = new StringContent("", Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_request");
    }

    // Scenario 16: Malformed JSON body returns invalid_request

    [Fact]
    public async Task Token_MalformedJson_Returns400_InvalidRequest()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_BadJson");
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/token")
        {
            Content = new StringContent("{not valid json", Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_request");
    }

    // ── Missing credentials ───────────────────────────────────────────────────

    // Scenario 17: grant_type=password with no username/password returns invalid_request

    [Fact]
    public async Task Token_Password_MissingCredentials_Returns400()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_MissingCreds");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "password" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_request");
    }

    // ── Unknown email ─────────────────────────────────────────────────────────

    // Scenario 18: grant_type=password with email that doesn't exist returns invalid_grant

    [Fact]
    public async Task Token_Password_UnknownEmail_Returns400_InvalidGrant()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_UnknownEmail");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "password", username = "nobody@example.com", password = "Password1!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        // Same error as wrong password — prevents user enumeration
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
        doc.RootElement.GetProperty("errorDescription").GetString().Should()
            .ContainEquivalentOf("Invalid email or password");
    }

    // ── Unrecognized grant_type ───────────────────────────────────────────────

    // Scenario 19: Unrecognized grant_type value returns unsupported_grant_type

    [Fact]
    public async Task Token_UnrecognizedGrantType_Returns400_UnsupportedGrantType()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_BadGrantType");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "magic_link", username = "u@example.com", password = "p" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("unsupported_grant_type");
    }

    // ── Missing refresh_token field ───────────────────────────────────────────

    // Scenario 20: grant_type=refresh_token with no refresh_token field returns invalid_request

    [Fact]
    public async Task Token_Refresh_MissingToken_Returns400_InvalidRequest()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_Refresh_NoToken");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "refresh_token" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_request");
    }

    // ── LastLoginAt DB update ─────────────────────────────────────────────────

    // Scenario 21: LastLoginAt is updated in the database after successful password login

    [Fact]
    public async Task Token_Password_UpdatesLastLoginAt_InDatabase()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_LastLogin");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "lastlogin@example.com", password = "Password1!" });

        var before = DateTime.UtcNow;

        await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "password", username = "lastlogin@example.com", password = "Password1!" });

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("lastlogin@example.com");

        user!.LastLoginAt.Should().NotBeNull();
        user.LastLoginAt!.Value.Should().BeOnOrAfter(before);
    }

    // ── Security stamp invalidates refresh token ───────────────────────────────

    // Scenario 22: Refresh token is rejected after the security stamp changes

    [Fact]
    public async Task Token_Refresh_Returns400_AfterSecurityStampChanges()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_StampChange");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "stamp@example.com", password = "Password1!" });

        var loginDoc = await client.LoginAsync("stamp@example.com");
        var refreshToken = loginDoc.RootElement.GetProperty("refresh_token").GetString()!;

        // Simulate any operation that rotates the security stamp (e.g. global logout, password reset)
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync("stamp@example.com");
            await userManager.UpdateSecurityStampAsync(user!);
        }

        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "refresh_token", refreshToken });

        // The refresh token store validates the SecurityStamp embedded in the token
        // against the current stamp — mismatch means invalid_grant
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    // ── expires_in accuracy ───────────────────────────────────────────────────

    // Scenario 23: expires_in matches configured AccessTokenLifetime (1 hour = 3600s)

    [Fact]
    public async Task Token_Password_ExpiresIn_MatchesConfiguredLifetime()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Token_ExpiresIn");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "expiry@example.com", password = "Password1!" });

        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "password", username = "expiry@example.com", password = "Password1!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        // Factory sets AccessTokenLifetime = 1 hour
        doc.RootElement.GetProperty("expires_in").GetInt32().Should().Be(3600);

        // expires_at should be a Unix timestamp roughly 1 hour from now
        var expiresAt = doc.RootElement.GetProperty("expires_at").GetInt64();
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        expiresAt.Should().BeGreaterThan(nowUnix + 3590); // at least ~1 hour ahead
        expiresAt.Should().BeLessThan(nowUnix + 3610);    // not more than ~1 hour + 10s
    }

    // ── Lockout can be disabled ───────────────────────────────────────────────

    // Scenario 24: When AllowedForNewUsers=false, repeated failures do not lock the account

    [Fact]
    public async Task Token_Password_NoLockout_WhenLockoutDisabled()
    {
        await using var factory = new AuthWebApplicationFactory(
            appName: "Token_LockoutOff",
            configureServices: services =>
                services.Configure<IdentityOptions>(o =>
                {
                    o.Lockout.AllowedForNewUsers = false;
                    o.Lockout.MaxFailedAccessAttempts = 1; // would lock after 1 failure if enabled
                }));

        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "nolockout@example.com", password = "Password1!" });

        // Trigger what would be a lockout (MaxFailedAccessAttempts = 1, but lockout is off)
        await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "password", username = "nolockout@example.com", password = "WrongPassword" });

        // Should still succeed — account was not locked
        var response = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "password", username = "nolockout@example.com", password = "Password1!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
    }
}
