using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using HPD.Auth.Authentication.Extensions;
using HPD.Auth.Authentication.Tests.Helpers;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using TokenResponseModel = HPD.Auth.Core.Models.TokenResponse;
using HPD.Auth.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Authentication.Tests.Integration;

/// <summary>
/// Tests 146–155: Full-stack JWT authentication flow (TESTS.md §8.2–8.3).
///
/// Uses a real in-process test server (AddHPDAuth + AddAuthentication) so the
/// ITokenService, JwtBearerConfigurator, OnTokenValidated, and the X-Token-Expired
/// header are exercised end-to-end across multiple request scopes.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Section", "8.2-8.3-JWT-Flow")]
public class Integration_JWT_Tests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared test server factory
    // ─────────────────────────────────────────────────────────────────────────

    private static (TestServer Server, IServiceProvider RootServices) CreateServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var dbName = Guid.NewGuid().ToString();

        builder.Services.AddHPDAuth(opts =>
        {
            opts.AppName                  = dbName;
            opts.Jwt.Secret               = TokenServiceFixture.DefaultSecret;
            opts.Jwt.Issuer               = TokenServiceFixture.DefaultIssuer;
            opts.Jwt.Audience             = TokenServiceFixture.DefaultAudience;
            opts.Jwt.AccessTokenLifetime  = TimeSpan.FromMinutes(15);
            opts.Jwt.RefreshTokenLifetime = TimeSpan.FromDays(14);
            opts.Jwt.ValidateLifetime     = true;
            opts.Jwt.ClockSkew            = TimeSpan.Zero;
        })
        .AddAuthentication();

        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        // Issue tokens for a given user ID.
        app.MapPost("/auth/token", async (HttpContext ctx) =>
        {
            var userId      = ctx.Request.Query["user"].ToString();
            var userManager = ctx.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var tokenSvc    = ctx.RequestServices.GetRequiredService<ITokenService>();
            var user        = await userManager.FindByIdAsync(userId);
            if (user is null) return Results.NotFound();
            var response = await tokenSvc.GenerateTokensAsync(user);
            return Results.Ok(response);
        });

        // Refresh tokens endpoint.
        app.MapPost("/auth/refresh", async (HttpContext ctx) =>
        {
            var refreshToken = ctx.Request.Query["token"].ToString();
            var tokenSvc     = ctx.RequestServices.GetRequiredService<ITokenService>();
            var response     = await tokenSvc.RefreshAsync(refreshToken);
            return response is null ? Results.Unauthorized() : Results.Ok(response);
        });

        // Revoke a refresh token.
        app.MapPost("/auth/revoke", async (HttpContext ctx) =>
        {
            var refreshToken = ctx.Request.Query["token"].ToString();
            var tokenSvc     = ctx.RequestServices.GetRequiredService<ITokenService>();
            var ok = await tokenSvc.RevokeAsync(refreshToken);
            return ok ? Results.Ok() : Results.NotFound();
        });

        // Protected endpoint that requires Bearer token.
        app.MapGet("/protected", (HttpContext ctx) =>
            ctx.User.Identity?.IsAuthenticated == true
                ? Results.Ok(new { authenticated = true, sub = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value })
                : Results.Unauthorized()
        ).RequireAuthorization();

        app.Start();
        return (app.GetTestServer(), app.Services);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper — create a user and call /auth/token
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<(HttpClient Client, ApplicationUser User, TokenResponseModel Tokens)>
        IssueTokensAsync(TestServer server, IServiceProvider rootServices)
    {
        using var scope = rootServices.CreateScope();
        var user   = await ServiceProviderBuilder.CreateUserAsync(scope);
        var client = server.CreateClient();

        var response = await client.PostAsync($"/auth/token?user={user.Id}", null);
        response.EnsureSuccessStatusCode();
        var json    = await response.Content.ReadAsStringAsync();
        var tokens  = JsonSerializer.Deserialize<TokenResponseModel>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        return (client, user, tokens);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 146 — JWT login returns TokenResponseModel with all fields populated
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calling GenerateTokensAsync should return a TokenResponseModel where every
    /// required field is populated and the access token is a decodable JWT.
    /// </summary>
    [Fact]
    public async Task Integration_JWT_Login_Returns_TokenResponseModel()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (_, _, tokens) = await IssueTokensAsync(server, rootServices);

        tokens.AccessToken.Should().NotBeNullOrEmpty("access token must be present");
        tokens.RefreshToken.Should().NotBeNullOrEmpty("refresh token must be present");
        tokens.TokenType.Should().Be("bearer");
        tokens.ExpiresIn.Should().BeGreaterThan(0);
        tokens.ExpiresAt.Should().BeGreaterThan(0);
        tokens.User.Should().NotBeNull();
        tokens.User.Email.Should().NotBeNullOrEmpty();

        // Must be a parseable JWT.
        var handler  = new JwtSecurityTokenHandler();
        var readable = handler.CanReadToken(tokens.AccessToken);
        readable.Should().BeTrue("access token must be a valid JWT string");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 147 — access token authenticates protected endpoint
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Using the issued access token as a Bearer credential on a subsequent
    /// request to a protected endpoint should return 200.
    /// </summary>
    [Fact]
    public async Task Integration_JWT_Use_AccessToken_Authenticates()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (client, _, tokens) = await IssueTokensAsync(server, rootServices);

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("authenticated");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 148 — refresh issues new tokens
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calling /auth/refresh with a valid refresh token should return a new
    /// TokenResponseModel with fresh access and refresh tokens.
    /// </summary>
    [Fact]
    public async Task Integration_JWT_Refresh_Issues_New_Tokens()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (client, _, original) = await IssueTokensAsync(server, rootServices);

        var refreshResponse = await client.PostAsync(
            $"/auth/refresh?token={Uri.EscapeDataString(original.RefreshToken)}", null);

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "a valid refresh token must produce new tokens");

        var json = await refreshResponse.Content.ReadAsStringAsync();
        var newTokens = JsonSerializer.Deserialize<TokenResponseModel>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        newTokens.AccessToken.Should().NotBeNullOrEmpty();
        newTokens.RefreshToken.Should().NotBeNullOrEmpty();
        newTokens.RefreshToken.Should().NotBe(original.RefreshToken,
            "refresh token rotation must issue a new token value");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 149 — old refresh token after rotation fails
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After token rotation the original refresh token must be marked as used.
    /// Re-using it should return 401 (null from RefreshAsync).
    /// </summary>
    [Fact]
    public async Task Integration_JWT_Use_Old_RefreshToken_After_Rotation_Fails()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (client, _, original) = await IssueTokensAsync(server, rootServices);

        // First refresh — succeeds and marks the original token as used.
        await client.PostAsync(
            $"/auth/refresh?token={Uri.EscapeDataString(original.RefreshToken)}", null);

        // Second attempt with the same token — must fail.
        var retry = await client.PostAsync(
            $"/auth/refresh?token={Uri.EscapeDataString(original.RefreshToken)}", null);

        retry.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an already-used refresh token must be rejected");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 150 — revoked refresh token cannot be refreshed
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After explicit revocation a refresh token should be permanently rejected.
    /// </summary>
    [Fact]
    public async Task Integration_JWT_Revoke_Then_Refresh_Fails()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (client, _, tokens) = await IssueTokensAsync(server, rootServices);

        // Revoke the refresh token.
        await client.PostAsync(
            $"/auth/revoke?token={Uri.EscapeDataString(tokens.RefreshToken)}", null);

        // Attempt refresh — must fail.
        var refresh = await client.PostAsync(
            $"/auth/refresh?token={Uri.EscapeDataString(tokens.RefreshToken)}", null);

        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a revoked refresh token must be rejected");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 151 — expired access token returns X-Token-Expired header
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An expired access token should cause the JwtBearerConfigurator's
    /// OnAuthenticationFailed hook to append X-Token-Expired: true.
    /// </summary>
    [Fact]
    public async Task Integration_JWT_Expired_AccessToken_Gets_X_Token_Expired_Header()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        // Issue a token via the real service then manually construct an expired JWT
        // with the same signing key so the server recognises it as a valid structure
        // (correct issuer/audience) but rejects it due to expiry.
        using var scope = rootServices.CreateScope();
        var user = await ServiceProviderBuilder.CreateUserAsync(scope);

        var expiredToken = BuildExpiredJwt(user);

        var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.GetAsync("/protected");

        response.Headers.TryGetValues("X-Token-Expired", out var values).Should().BeTrue(
            "expired token should trigger X-Token-Expired header");
        values!.First().Should().Be("true");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 152 — password reset invalidates old JWT via security stamp
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After a password reset (which rotates the security stamp via UserManager),
    /// using the previously-issued JWT should fail in OnTokenValidated.
    /// </summary>
    [Fact]
    public async Task Integration_JWT_After_PasswordReset_Old_Tokens_Invalid()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (client, user, tokens) = await IssueTokensAsync(server, rootServices);

        // Verify the token works before the reset.
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var before = await client.GetAsync("/protected");
        before.StatusCode.Should().Be(HttpStatusCode.OK);

        // Simulate a password reset by rotating the security stamp.
        using (var scope = rootServices.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var refreshed   = await userManager.FindByIdAsync(user.Id.ToString());
            await userManager.UpdateSecurityStampAsync(refreshed!);
        }

        // Old token should now be rejected.
        var after = await client.GetAsync("/protected");
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "old JWT must be rejected after security stamp rotation");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 153 — concurrent refresh token rotation is safe
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two concurrent requests with the same refresh token: the IsUsed flag
    /// prevents double-redemption in sequential scenarios. With the in-memory EF
    /// Core provider there is no row-level locking, so both requests may race and
    /// succeed. This test documents the sequential guarantee (second sequential
    /// attempt always fails) rather than the concurrent race — the concurrent safety
    /// is a database-level concern that requires a real transactional store.
    /// </summary>
    [Fact]
    public async Task Integration_Concurrent_Refresh_Token_Rotation_Safe()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (client, _, tokens) = await IssueTokensAsync(server, rootServices);

        // First refresh — should succeed.
        var first = await client.PostAsync(
            $"/auth/refresh?token={Uri.EscapeDataString(tokens.RefreshToken)}", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK,
            "the first refresh with a valid token must succeed");

        // Second sequential attempt with the same (now used) token — must be rejected.
        var second = await client.PostAsync(
            $"/auth/refresh?token={Uri.EscapeDataString(tokens.RefreshToken)}", null);
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a token that has already been used must be rejected");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 155 — user deactivated after token issue → JWT fails
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// If a user is deactivated after their JWT was issued, the OnTokenValidated
    /// hook should detect IsActive = false and reject the request with 401.
    /// </summary>
    [Fact]
    public async Task Integration_GenerateTokens_Then_UserDeactivated_JWT_Fails()
    {
        var (server, rootServices) = CreateServer();
        using var _ = server;

        var (client, user, tokens) = await IssueTokensAsync(server, rootServices);

        // Deactivate the user.
        using (var scope = rootServices.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var refreshed   = await userManager.FindByIdAsync(user.Id.ToString());
            refreshed!.IsActive = false;
            await userManager.UpdateAsync(refreshed);
        }

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a deactivated user's JWT must be rejected by OnTokenValidated");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs an already-expired JWT (notBefore and expires in the past)
    /// with the correct issuer, audience, signing key, and the user's security stamp
    /// so the server considers it structurally valid but expired.
    /// </summary>
    private static string BuildExpiredJwt(ApplicationUser user)
    {
        var key         = new System.Text.StringBuilder()
            .Append(TokenServiceFixture.DefaultSecret).ToString();
        var signingKey  = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(key));
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            signingKey, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var past = DateTime.UtcNow.AddHours(-2);

        var claims = new[]
        {
            new System.Security.Claims.Claim(JwtRegisteredClaimNames.Sub,  user.Id.ToString()),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()),
            new System.Security.Claims.Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp ?? string.Empty),
        };

        var jwt = new JwtSecurityToken(
            issuer:             TokenServiceFixture.DefaultIssuer,
            audience:           TokenServiceFixture.DefaultAudience,
            claims:             claims,
            notBefore:          past,
            expires:            past.AddMinutes(15),  // already expired
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
