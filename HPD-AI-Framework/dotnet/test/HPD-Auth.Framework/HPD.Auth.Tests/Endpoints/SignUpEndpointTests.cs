using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Audit.Observers;
using HPD.Auth.Core.Events;
using HPD.Events;
using HPD.Auth.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace HPD.Auth.Tests.Endpoints;

/// <summary>
/// Integration tests for POST /api/auth/signup.
/// </summary>
public class SignUpEndpointTests
{
    // ── Scenario 1 ────────────────────────────────────────────────────────────
    // Creates user and returns tokens when RequireEmailConfirmation = false

    [Fact]
    public async Task SignUp_ReturnsTokens_WhenEmailConfirmationDisabled()
    {
        await using var factory = new AuthWebApplicationFactory(
            appName: "Signup_NoConfirm",
            requireEmailConfirmation: false);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "new@example.com", password = "Password1!" });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var doc = await response.ReadJsonAsync();
        var root = doc.RootElement;

        root.TryGetProperty("access_token", out _).Should().BeTrue();
        root.TryGetProperty("refresh_token", out _).Should().BeTrue();
        root.TryGetProperty("expires_in", out _).Should().BeTrue();
        root.TryGetProperty("expires_at", out _).Should().BeTrue();
        root.TryGetProperty("user", out var userProp).Should().BeTrue();
        userProp.TryGetProperty("id", out _).Should().BeTrue();
        userProp.GetProperty("email").GetString().Should().Be("new@example.com");
    }

    // ── Scenario 2 ────────────────────────────────────────────────────────────
    // Sends confirmation email when RequireEmailConfirmation = true

    [Fact]
    public async Task SignUp_SendsConfirmationEmail_WhenEmailConfirmationEnabled()
    {
        var emailSender = new TestEmailSender();

        await using var factory = new AuthWebApplicationFactory(
            appName: "Signup_WithConfirm",
            requireEmailConfirmation: true,
            configureServices: services =>
                services.Replace(ServiceDescriptor.Scoped<IHPDAuthEmailSender>(_ => emailSender)));

        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "confirm@example.com", password = "Password1!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.TryGetProperty("message", out var msg).Should().BeTrue();
        msg.GetString().Should().Contain("check your email");

        // No tokens returned
        doc.RootElement.TryGetProperty("access_token", out _).Should().BeFalse();

        // Email was sent
        emailSender.ConfirmationsSent.Should().ContainSingle(e => e.Email == "confirm@example.com");
    }

    // ── Scenario 3 ────────────────────────────────────────────────────────────
    // Returns 400 for duplicate email

    [Fact]
    public async Task SignUp_Returns400_ForDuplicateEmail()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Signup_Duplicate");
        var client = factory.CreateClient();

        // First registration succeeds
        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "existing@example.com", password = "Password1!" });

        // Second registration with same email
        var response = await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "existing@example.com", password = "Password1!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("validation_failed");
        var errorDesc = doc.RootElement.GetProperty("errorDescription").GetString()!;
        (errorDesc.ToLower().Contains("already taken") || errorDesc.ToLower().Contains("is already"))
            .Should().BeTrue("error_description should indicate duplicate email");
    }

    // ── Scenario 4 ────────────────────────────────────────────────────────────
    // Returns 400 for missing required fields

    [Fact]
    public async Task SignUp_Returns400_ForMissingEmail()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Signup_MissingEmail");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "", password = "Password1!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_request");
    }

    // ── Scenario 5 ────────────────────────────────────────────────────────────
    // Returns 400 when password does not meet policy

    [Fact]
    public async Task SignUp_Returns400_WhenPasswordTooShort()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Signup_ShortPassword");
        var client = factory.CreateClient();

        // 3 chars — fails RequiredLength=8 from the factory default
        var response = await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "a@b.com", password = "abc" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("validation_failed");
    }

    // ── Scenario 6 ────────────────────────────────────────────────────────────
    // firstName and lastName are stored in the database after signup

    [Fact]
    public async Task SignUp_StoresFirstNameAndLastName_InDatabase()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Signup_Names");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "named@example.com", password = "Password1!", firstName = "Alice", lastName = "Smith" });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("named@example.com");
        user!.FirstName.Should().Be("Alice");
        user.LastName.Should().Be("Smith");
    }

    // ── Scenario 7 ────────────────────────────────────────────────────────────
    // UserRegisteredEvent is published even when RequireEmailConfirmation = true

    [Fact]
    public async Task SignUp_PublishesUserRegisteredEvent_WhenEmailConfirmationRequired()
    {
        var spy = new SpyUserRegisteredObserver();

        await using var factory = new AuthWebApplicationFactory(
            appName: "Signup_EventConfirm",
            requireEmailConfirmation: true,
            configureServices: services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IHPDAuthEmailSender>(_ => new TestEmailSender()));
                services.AddScoped<IAuthEventObserver<UserRegisteredEvent>>(_ => spy);
            });

        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "event@example.com", password = "Password1!" });

        spy.Received.Should().ContainSingle(e => e.Email == "event@example.com");
    }

    // ── Scenario 8 ────────────────────────────────────────────────────────────
    // Missing password returns invalid_request

    [Fact]
    public async Task SignUp_Returns400_WhenPasswordMissing()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Signup_NoPassword");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "nopwd@example.com", password = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_request");
    }

    // ── Scenario 9 ────────────────────────────────────────────────────────────
    // Invalid email format (no @) returns validation_failed

    [Fact]
    public async Task SignUp_Returns400_ForInvalidEmailFormat()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Signup_BadEmail");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "notanemail", password = "Password1!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("validation_failed");
    }
}

/// <summary>
/// Records every UserRegisteredEvent for assertion in tests.
/// </summary>
internal sealed class SpyUserRegisteredObserver : IAuthEventObserver<UserRegisteredEvent>
{
    public List<UserRegisteredEvent> Received { get; } = [];

    public bool ShouldProcess(UserRegisteredEvent evt) => true;

    public Task OnEventAsync(UserRegisteredEvent evt, CancellationToken ct = default)
    {
        Received.Add(evt);
        return Task.CompletedTask;
    }
}
