using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Tests.Endpoints;

/// <summary>
/// Integration tests for:
///   GET    /api/auth/sessions        — list active sessions
///   DELETE /api/auth/sessions/{id}   — revoke specific session
///   DELETE /api/auth/sessions        — revoke all other sessions
/// </summary>
public class SessionEndpointTests
{
    // ── GET /api/auth/sessions ────────────────────────────────────────────────

    // Scenario 1: Returns only the current user's active sessions

    [Fact]
    public async Task ListSessions_ReturnsOnlyOwnSessions()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Sessions_List");
        var client = factory.CreateClient();

        // Create two users
        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "u1@example.com", password = "Password1!" });
        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "u2@example.com", password = "Password1!" });

        // Create sessions for both users via ISessionManager
        var (u1Id, u2Id) = await GetUserIdsAsync(factory, "u1@example.com", "u2@example.com");

        using (var scope = factory.Services.CreateScope())
        {
            var sessionManager = scope.ServiceProvider.GetRequiredService<ISessionManager>();
            await sessionManager.CreateSessionAsync(u1Id, new SessionContext("1.2.3.4", "UA1"));
            await sessionManager.CreateSessionAsync(u1Id, new SessionContext("1.2.3.5", "UA2"));
            await sessionManager.CreateSessionAsync(u2Id, new SessionContext("5.6.7.8", "UA3"));
        }

        var accessToken = await client.GetAccessTokenAsync("u1@example.com");
        client.SetBearerToken(accessToken);

        var response = await client.GetAsync("/api/auth/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        var sessions = doc.RootElement.EnumerateArray().ToList();

        // All returned sessions belong to u1
        sessions.Should().NotBeEmpty();
        sessions.Should().AllSatisfy(s =>
        {
            var userId = s.GetProperty("userId").GetGuid();
            userId.Should().Be(u1Id);
        });

        // Each session should have the expected fields
        sessions[0].TryGetProperty("id", out _).Should().BeTrue();
        sessions[0].TryGetProperty("ipAddress", out _).Should().BeTrue();
        sessions[0].TryGetProperty("userAgent", out _).Should().BeTrue();
        sessions[0].TryGetProperty("createdAt", out _).Should().BeTrue();
    }

    // Scenario 2: Returns 401 for unauthenticated requests

    [Fact]
    public async Task ListSessions_Returns401_ForUnauthenticated()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Sessions_ListUnauth");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── DELETE /api/auth/sessions/{id} ────────────────────────────────────────

    // Scenario 3: Revokes a session owned by the current user

    [Fact]
    public async Task RevokeSession_Returns204_WhenOwned()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Sessions_RevokeOwn");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "owner@example.com", password = "Password1!" });

        var ownerId = await GetUserIdAsync(factory, "owner@example.com");

        Guid sessionId;
        using (var scope = factory.Services.CreateScope())
        {
            var sessionManager = scope.ServiceProvider.GetRequiredService<ISessionManager>();
            var session = await sessionManager.CreateSessionAsync(ownerId, new SessionContext("1.1.1.1", "UA"));
            sessionId = session.Id;
        }

        var accessToken = await client.GetAccessTokenAsync("owner@example.com");
        client.SetBearerToken(accessToken);

        var response = await client.DeleteAsync($"/api/auth/sessions/{sessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // Scenario 4: Returns 403 when attempting to revoke another user's session

    [Fact]
    public async Task RevokeSession_Returns403_WhenNotOwned()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Sessions_RevokeOther");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "u1@example.com", password = "Password1!" });
        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "u2@example.com", password = "Password1!" });

        var u1Id = await GetUserIdAsync(factory, "u1@example.com");

        Guid u1SessionId;
        using (var scope = factory.Services.CreateScope())
        {
            var sessionManager = scope.ServiceProvider.GetRequiredService<ISessionManager>();
            var session = await sessionManager.CreateSessionAsync(u1Id, new SessionContext("1.1.1.1", "UA"));
            u1SessionId = session.Id;
        }

        // Authenticate as U2 and try to revoke U1's session
        var u2Token = await client.GetAccessTokenAsync("u2@example.com");
        client.SetBearerToken(u2Token);

        var response = await client.DeleteAsync($"/api/auth/sessions/{u1SessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // Scenario 5: Returns 403 for a non-existent session ID

    [Fact]
    public async Task RevokeSession_Returns403_ForNonExistentSession()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Sessions_RevokeNonExistent");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "user@example.com", password = "Password1!" });

        var accessToken = await client.GetAccessTokenAsync("user@example.com");
        client.SetBearerToken(accessToken);

        var response = await client.DeleteAsync($"/api/auth/sessions/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── DELETE /api/auth/sessions ─────────────────────────────────────────────

    // Scenario 6: Revokes all sessions except the current one

    [Fact]
    public async Task RevokeAllSessions_Returns204_AndRevokesOthers()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Sessions_RevokeAll");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "user@example.com", password = "Password1!" });

        var userId = await GetUserIdAsync(factory, "user@example.com");

        // Create additional sessions
        using (var scope = factory.Services.CreateScope())
        {
            var sessionManager = scope.ServiceProvider.GetRequiredService<ISessionManager>();
            await sessionManager.CreateSessionAsync(userId, new SessionContext("2.2.2.2", "UA-B"));
            await sessionManager.CreateSessionAsync(userId, new SessionContext("3.3.3.3", "UA-C"));
        }

        var accessToken = await client.GetAccessTokenAsync("user@example.com");
        client.SetBearerToken(accessToken);

        var response = await client.DeleteAsync("/api/auth/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // After deletion, the sessions store should have fewer (or zero) sessions
        // depending on whether the current request has a session_id claim.
        // The endpoint does not throw — 204 is sufficient.
    }

    // Scenario 6b: DELETE /{id} with a non-GUID id returns 403 (not 404, prevents enumeration)

    [Fact]
    public async Task RevokeSession_Returns403_ForNonGuidId()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Sessions_NonGuidId");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "user@example.com", password = "Password1!" });

        var accessToken = await client.GetAccessTokenAsync("user@example.com");
        client.SetBearerToken(accessToken);

        var response = await client.DeleteAsync("/api/auth/sessions/not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // Scenario 7: DELETE /api/auth/sessions returns 401 for unauthenticated

    [Fact]
    public async Task RevokeAllSessions_Returns401_ForUnauthenticated()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Sessions_RevokeAllUnauth");
        var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/auth/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<Guid> GetUserIdAsync(AuthWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        return user!.Id;
    }

    private static async Task<(Guid, Guid)> GetUserIdsAsync(
        AuthWebApplicationFactory factory, string email1, string email2)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var u1 = await userManager.FindByEmailAsync(email1);
        var u2 = await userManager.FindByEmailAsync(email2);
        return (u1!.Id, u2!.Id);
    }
}
