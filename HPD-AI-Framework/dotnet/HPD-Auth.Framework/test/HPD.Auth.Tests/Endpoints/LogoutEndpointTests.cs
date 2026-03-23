using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Tests.Endpoints;

/// <summary>
/// Integration tests for POST /api/auth/logout.
/// </summary>
public class LogoutEndpointTests
{
    // ── Scenario 1 ────────────────────────────────────────────────────────────
    // Local scope: signs out without updating SecurityStamp

    [Fact]
    public async Task Logout_Local_Returns200()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Logout_Local");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "user@example.com", password = "Password1!" });

        var loginDoc = await client.LoginAsync("user@example.com");
        var accessToken = loginDoc.RootElement.GetProperty("access_token").GetString()!;
        var stampBefore = await GetSecurityStampAsync(factory, "user@example.com");

        client.SetBearerToken(accessToken);
        var response = await client.PostAsJsonAsync("/api/auth/logout", new { scope = "local" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // SecurityStamp is NOT updated for local logout
        var stampAfter = await GetSecurityStampAsync(factory, "user@example.com");
        stampAfter.Should().Be(stampBefore);
    }

    // ── Scenario 2 ────────────────────────────────────────────────────────────
    // Global scope: updates SecurityStamp (forces all devices out)

    [Fact]
    public async Task Logout_Global_UpdatesSecurityStamp()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Logout_Global");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "user@example.com", password = "Password1!" });

        var loginDoc = await client.LoginAsync("user@example.com");
        var accessToken = loginDoc.RootElement.GetProperty("access_token").GetString()!;
        var stampBefore = await GetSecurityStampAsync(factory, "user@example.com");

        client.SetBearerToken(accessToken);
        var response = await client.PostAsJsonAsync("/api/auth/logout", new { scope = "global" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // SecurityStamp MUST have changed
        var stampAfter = await GetSecurityStampAsync(factory, "user@example.com");
        stampAfter.Should().NotBe(stampBefore);
    }

    // ── Scenario 3 ────────────────────────────────────────────────────────────
    // Others scope: revokes other sessions, keeps current

    [Fact]
    public async Task Logout_Others_Returns200()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Logout_Others");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "user@example.com", password = "Password1!" });

        var loginDoc = await client.LoginAsync("user@example.com");
        var accessToken = loginDoc.RootElement.GetProperty("access_token").GetString()!;

        client.SetBearerToken(accessToken);
        var response = await client.PostAsJsonAsync("/api/auth/logout", new { scope = "others" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Scenario 4 ────────────────────────────────────────────────────────────
    // Revokes the provided refresh token on any logout scope

    [Fact]
    public async Task Logout_RevokesProvidedRefreshToken()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Logout_RevokeToken");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "user@example.com", password = "Password1!" });

        var loginDoc = await client.LoginAsync("user@example.com");
        var accessToken = loginDoc.RootElement.GetProperty("access_token").GetString()!;
        var refreshToken = loginDoc.RootElement.GetProperty("refresh_token").GetString()!;

        client.SetBearerToken(accessToken);
        var logoutResponse = await client.PostAsJsonAsync("/api/auth/logout",
            new { scope = "local", refreshToken });
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Attempt to use the revoked refresh token
        client.DefaultRequestHeaders.Authorization = null;
        var refreshResponse = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "refresh_token", refreshToken });

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var doc = await refreshResponse.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    // ── Scenario 5 ────────────────────────────────────────────────────────────
    // Returns 401 for unauthenticated requests

    [Fact]
    public async Task Logout_Returns401_ForUnauthenticated()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Logout_Unauth");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/logout", new { scope = "local" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Scenario 6 ────────────────────────────────────────────────────────────
    // No body at all (null LogoutRequest) defaults to scope=local

    [Fact]
    public async Task Logout_NoBody_Returns200_DefaultsToLocalScope()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Logout_NoBody");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "user@example.com", password = "Password1!" });

        var loginDoc = await client.LoginAsync("user@example.com");
        var accessToken = loginDoc.RootElement.GetProperty("access_token").GetString()!;
        var stampBefore = await GetSecurityStampAsync(factory, "user@example.com");

        client.SetBearerToken(accessToken);

        // POST with no body at all
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // SecurityStamp unchanged — local scope only
        var stampAfter = await GetSecurityStampAsync(factory, "user@example.com");
        stampAfter.Should().Be(stampBefore);
    }

    // ── Scenario 7 ────────────────────────────────────────────────────────────
    // scope=others with no session_id claim: revokes all refresh tokens for the user

    [Fact]
    public async Task Logout_Others_NoSessionIdClaim_RevokesAllRefreshTokens()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Logout_Others_NoSession");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "user@example.com", password = "Password1!" });

        var loginDoc = await client.LoginAsync("user@example.com");
        var accessToken = loginDoc.RootElement.GetProperty("access_token").GetString()!;
        var refreshToken = loginDoc.RootElement.GetProperty("refresh_token").GetString()!;

        // JWT from LoginAsync won't contain a session_id claim unless the token service
        // embeds one. Either way, scope=others should revoke all refresh tokens.
        client.SetBearerToken(accessToken);
        var response = await client.PostAsJsonAsync("/api/auth/logout", new { scope = "others" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        client.DefaultRequestHeaders.Authorization = null;

        // The refresh token issued before logout is now revoked
        var refreshResponse = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "refresh_token", refreshToken });
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var doc = await refreshResponse.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static async Task<string?> GetSecurityStampAsync(
        AuthWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        return user?.SecurityStamp;
    }
}
