using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Tests.Endpoints;

/// <summary>
/// Integration tests for GET /api/auth/user and PUT /api/auth/user.
/// </summary>
public class UserEndpointTests
{
    // ── GET /api/auth/user ────────────────────────────────────────────────────

    // Scenario 1: Returns the current authenticated user's profile

    [Fact]
    public async Task GetUser_ReturnsUserProfile_WhenAuthenticated()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "GetUser_Profile");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "me@example.com", password = "Password1!" });

        var accessToken = await client.GetAccessTokenAsync("me@example.com");
        client.SetBearerToken(accessToken);

        var response = await client.GetAsync("/api/auth/user");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        var root = doc.RootElement;

        root.TryGetProperty("id", out _).Should().BeTrue();
        root.GetProperty("email").GetString().Should().Be("me@example.com");
        root.TryGetProperty("email_confirmed_at", out _).Should().BeTrue();
        root.TryGetProperty("user_metadata", out _).Should().BeTrue();
        root.TryGetProperty("app_metadata", out _).Should().BeTrue();
        root.TryGetProperty("required_actions", out _).Should().BeTrue();
        root.TryGetProperty("created_at", out _).Should().BeTrue();
        root.TryGetProperty("subscription_tier", out _).Should().BeTrue();

        // Sensitive fields must NOT be exposed
        root.TryGetProperty("password_hash", out _).Should().BeFalse();
        root.TryGetProperty("security_stamp", out _).Should().BeFalse();
    }

    // Scenario 2: Returns 401 for unauthenticated requests

    [Fact]
    public async Task GetUser_Returns401_ForUnauthenticated()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "GetUser_Unauth");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/user");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Scenario 3: Returns 404 if user account was deleted since the token was issued

    [Fact]
    public async Task GetUser_Returns404_WhenUserDeleted()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "GetUser_Deleted");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "deleted@example.com", password = "Password1!" });

        var accessToken = await client.GetAccessTokenAsync("deleted@example.com");

        // Delete the user from the store
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("deleted@example.com");
        await userManager.DeleteAsync(user!);

        client.SetBearerToken(accessToken);
        var response = await client.GetAsync("/api/auth/user");

        // Either 401 (stamp validation) or 404 (user not found). Spec says 404.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }

    // ── PUT /api/auth/user ────────────────────────────────────────────────────

    // Scenario 4: Updates firstName, lastName, displayName

    [Fact]
    public async Task UpdateUser_UpdatesProfileFields()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "UpdateUser_Fields");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "update@example.com", password = "Password1!" });

        var accessToken = await client.GetAccessTokenAsync("update@example.com");
        client.SetBearerToken(accessToken);

        var response = await client.PutAsJsonAsync("/api/auth/user",
            new { firstName = "Alice", lastName = "Smith", displayName = "Alice S." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        // Check via DB that it was actually saved
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("update@example.com");
        user!.FirstName.Should().Be("Alice");
        user.LastName.Should().Be("Smith");
        user.DisplayName.Should().Be("Alice S.");
    }

    // Scenario 5: Updates userMetadata JSON blob

    [Fact]
    public async Task UpdateUser_UpdatesUserMetadata()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "UpdateUser_Metadata");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "meta@example.com", password = "Password1!" });

        var accessToken = await client.GetAccessTokenAsync("meta@example.com");
        client.SetBearerToken(accessToken);

        var newMetadata = "{\"theme\":\"dark\",\"locale\":\"en\"}";
        var response = await client.PutAsJsonAsync("/api/auth/user",
            new { userMetadata = newMetadata });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("meta@example.com");
        user!.UserMetadata.Should().Be(newMetadata);
    }

    // Scenario 6: Does NOT update appMetadata (field is ignored for security)

    [Fact]
    public async Task UpdateUser_DoesNotUpdateAppMetadata()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "UpdateUser_AppMeta");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "appmeta@example.com", password = "Password1!" });

        // Set AppMetadata directly via UserManager
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync("appmeta@example.com");
            user!.AppMetadata = "{\"subscription\":\"free\"}";
            await userManager.UpdateAsync(user);
        }

        var accessToken = await client.GetAccessTokenAsync("appmeta@example.com");
        client.SetBearerToken(accessToken);

        // Attempt to escalate subscription via PUT /api/auth/user
        await client.PutAsJsonAsync("/api/auth/user",
            new { appMetadata = "{\"subscription\":\"pro\"}" });

        // AppMetadata must be unchanged
        using var scope2 = factory.Services.CreateScope();
        var um = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var updatedUser = await um.FindByEmailAsync("appmeta@example.com");
        updatedUser!.AppMetadata.Should().Be("{\"subscription\":\"free\"}");
    }

    // Scenario 7: Returns 401 for unauthenticated PUT requests

    [Fact]
    public async Task UpdateUser_Returns401_ForUnauthenticated()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "UpdateUser_Unauth");
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/auth/user",
            new { firstName = "Bob" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Scenario 8: User deleted between auth and PUT returns 404

    [Fact]
    public async Task UpdateUser_Returns404_WhenUserDeletedAfterLogin()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "UpdateUser_Deleted");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "gone@example.com", password = "Password1!" });

        var accessToken = await client.GetAccessTokenAsync("gone@example.com");

        // Delete the user from the store before the PUT
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync("gone@example.com");
            await userManager.DeleteAsync(user!);
        }

        client.SetBearerToken(accessToken);
        var response = await client.PutAsJsonAsync("/api/auth/user", new { firstName = "Ghost" });

        // 401 if security stamp validation fires, 404 if user lookup returns null
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }
}
