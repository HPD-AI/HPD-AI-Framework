using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace HPD.Auth.Tests.Endpoints;

/// <summary>
/// Integration tests for:
///   POST /api/auth/recover  (password reset request)
///   POST /api/auth/verify   (type=recovery and type=signup)
///   POST /api/auth/resend   (resend confirmation email)
/// </summary>
public class PasswordEndpointTests
{
    // ── POST /api/auth/recover ────────────────────────────────────────────────

    // Scenario 1: Returns 200 regardless of whether email exists (no information leak)

    [Fact]
    public async Task Recover_Returns200_ForNonExistentEmail()
    {
        var emailSender = new TestEmailSender();
        await using var factory = BuildWithEmailSender("Recover_NoLeak", emailSender);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/recover",
            new { email = "nonexistent@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("message").GetString().Should()
            .ContainEquivalentOf("If your email is registered");

        emailSender.PasswordResetsSent.Should().BeEmpty();
    }

    // Scenario 2: Sends reset email to a real, confirmed address

    [Fact]
    public async Task Recover_SendsResetEmail_ForConfirmedUser()
    {
        var emailSender = new TestEmailSender();
        await using var factory = BuildWithEmailSender("Recover_Confirmed", emailSender);
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "real@example.com", password = "Password1!" });

        // Manually confirm the email via UserManager
        await ConfirmEmailAsync(factory, "real@example.com");

        var response = await client.PostAsJsonAsync("/api/auth/recover",
            new { email = "real@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        emailSender.PasswordResetsSent.Should().ContainSingle(e => e.Email == "real@example.com");
    }

    // Scenario 3: Does NOT send email for unconfirmed address

    [Fact]
    public async Task Recover_DoesNotSendEmail_ForUnconfirmedUser()
    {
        var emailSender = new TestEmailSender();
        await using var factory = BuildWithEmailSender("Recover_Unconfirmed", emailSender);
        var client = factory.CreateClient();

        // Sign up but do NOT confirm email
        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "unconfirmed@example.com", password = "Password1!" });

        var response = await client.PostAsJsonAsync("/api/auth/recover",
            new { email = "unconfirmed@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        emailSender.PasswordResetsSent.Should().BeEmpty();
    }

    // ── POST /api/auth/verify — type=recovery ─────────────────────────────────

    // Scenario 4: Resets password with valid token

    [Fact]
    public async Task Verify_Recovery_ResetsPassword_WithValidToken()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Verify_Recovery");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "user@example.com", password = "OldPass1!" });

        await ConfirmEmailAsync(factory, "user@example.com");

        // Generate a real reset token via UserManager
        var resetToken = await GeneratePasswordResetTokenAsync(factory, "user@example.com");

        var response = await client.PostAsJsonAsync("/api/auth/verify",
            new { type = "recovery", token = resetToken, email = "user@example.com", newPassword = "NewPass1!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("message").GetString().Should()
            .Be("Password has been reset successfully.");

        // User can now log in with the new password
        var loginResp = await client.PostAsJsonAsync("/api/auth/token",
            new { grantType = "password", username = "user@example.com", password = "NewPass1!" });
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Scenario 5: Returns 400 for invalid or expired reset token

    [Fact]
    public async Task Verify_Recovery_Returns400_ForInvalidToken()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Verify_BadToken");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "user@example.com", password = "Password1!" });

        var response = await client.PostAsJsonAsync("/api/auth/verify",
            new { type = "recovery", token = "invalid-token-xyz", email = "user@example.com", newPassword = "NewPass1!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    // Scenario 6: SecurityStamp is updated after successful reset

    [Fact]
    public async Task Verify_Recovery_UpdatesSecurityStamp_AfterReset()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Verify_StampUpdate");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "stamp@example.com", password = "Password1!" });

        await ConfirmEmailAsync(factory, "stamp@example.com");

        var stampBefore = await GetSecurityStampAsync(factory, "stamp@example.com");
        var resetToken = await GeneratePasswordResetTokenAsync(factory, "stamp@example.com");

        await client.PostAsJsonAsync("/api/auth/verify",
            new { type = "recovery", token = resetToken, email = "stamp@example.com", newPassword = "NewPass1!" });

        var stampAfter = await GetSecurityStampAsync(factory, "stamp@example.com");
        stampAfter.Should().NotBe(stampBefore);
    }

    // ── POST /api/auth/verify — type=signup ───────────────────────────────────

    // Scenario 7: Confirms email with valid token

    [Fact]
    public async Task Verify_Signup_ConfirmsEmail_WithValidToken()
    {
        var emailSender = new TestEmailSender();
        await using var factory = BuildWithEmailSender("Verify_Signup", emailSender,
            requireEmailConfirmation: true);
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "new@example.com", password = "Password1!" });

        emailSender.ConfirmationsSent.Should().ContainSingle();
        var token = emailSender.ConfirmationsSent[0].Token;

        var response = await client.PostAsJsonAsync("/api/auth/verify",
            new { type = "signup", token, email = "new@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("message").GetString().Should().Be("Email confirmed successfully.");

        // Email should now be confirmed
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("new@example.com");
        user!.EmailConfirmed.Should().BeTrue();
        user.EmailConfirmedAt.Should().NotBeNull();
    }

    // Scenario 8: Returns 400 for invalid confirmation token

    [Fact]
    public async Task Verify_Signup_Returns400_ForInvalidToken()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Verify_BadConfirm");
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "new@example.com", password = "Password1!" });

        var response = await client.PostAsJsonAsync("/api/auth/verify",
            new { type = "signup", token = "bad-token", email = "new@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");

        // Email still not confirmed
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("new@example.com");
        user!.EmailConfirmed.Should().BeFalse();
    }

    // ── POST /api/auth/resend ─────────────────────────────────────────────────

    // Scenario 9: Sends confirmation email for unconfirmed user

    [Fact]
    public async Task Resend_SendsEmail_ForUnconfirmedUser()
    {
        var emailSender = new TestEmailSender();
        await using var factory = BuildWithEmailSender("Resend_Unconfirmed", emailSender,
            requireEmailConfirmation: false);
        var client = factory.CreateClient();

        // Create user (without email confirmation requirement)
        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "unconf@example.com", password = "Password1!" });

        // User exists but EmailConfirmed is false (no confirmation was sent at signup)
        var response = await client.PostAsJsonAsync("/api/auth/resend",
            new { email = "unconf@example.com", type = "signup" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        emailSender.ConfirmationsSent.Should().ContainSingle(e => e.Email == "unconf@example.com");
    }

    // Scenario 10: Rate-limits repeated resend requests (5-minute cooldown)

    [Fact]
    public async Task Resend_Returns429_OnSecondRequest_WithinCooldown()
    {
        var emailSender = new TestEmailSender();
        await using var factory = BuildWithEmailSender("Resend_RateLimit", emailSender,
            requireEmailConfirmation: false);
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "ratelimit@example.com", password = "Password1!" });

        // First resend — succeeds
        var first = await client.PostAsJsonAsync("/api/auth/resend",
            new { email = "ratelimit@example.com", type = "signup" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Immediate second resend — should be rate-limited
        var second = await client.PostAsJsonAsync("/api/auth/resend",
            new { email = "ratelimit@example.com", type = "signup" });
        second.StatusCode.Should().Be((HttpStatusCode)429);
    }

    // ── POST /api/auth/recover — additional ───────────────────────────────────

    // Scenario 11: Empty email field returns 200 (no leak)

    [Fact]
    public async Task Recover_Returns200_ForEmptyEmail()
    {
        var emailSender = new TestEmailSender();
        await using var factory = BuildWithEmailSender("Recover_EmptyEmail", emailSender);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/recover",
            new { email = "" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        emailSender.PasswordResetsSent.Should().BeEmpty();
    }

    // ── POST /api/auth/verify — type=email_change ─────────────────────────────

    // Scenario 12: type=email_change with valid token confirms email (same path as signup)

    [Fact]
    public async Task Verify_EmailChange_ConfirmsEmail_WithValidToken()
    {
        var emailSender = new TestEmailSender();
        await using var factory = BuildWithEmailSender("Verify_EmailChange", emailSender,
            requireEmailConfirmation: true);
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "change@example.com", password = "Password1!" });

        emailSender.ConfirmationsSent.Should().ContainSingle();
        var token = emailSender.ConfirmationsSent[0].Token;

        // Use type=email_change — code routes to HandleEmailChangeVerifyAsync which
        // delegates to HandleSignupVerifyAsync
        var response = await client.PostAsJsonAsync("/api/auth/verify",
            new { type = "email_change", token, email = "change@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("message").GetString().Should().Be("Email confirmed successfully.");

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("change@example.com");
        user!.EmailConfirmed.Should().BeTrue();
    }

    // ── POST /api/auth/verify — missing field 400s ────────────────────────────

    // Scenario 13: type=recovery with missing email returns invalid_request

    [Fact]
    public async Task Verify_Recovery_Returns400_WhenEmailMissing()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Verify_RecovNoEmail");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/verify",
            new { type = "recovery", token = "sometoken", newPassword = "NewPass1!" });
        // email field omitted

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_request");
    }

    // Scenario 14: type=recovery with missing newPassword returns invalid_request

    [Fact]
    public async Task Verify_Recovery_Returns400_WhenNewPasswordMissing()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Verify_RecovNoPwd");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/verify",
            new { type = "recovery", token = "sometoken", email = "user@example.com" });
        // newPassword field omitted

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_request");
    }

    // Scenario 15: type=recovery with unknown email returns invalid_grant

    [Fact]
    public async Task Verify_Recovery_Returns400_WhenEmailNotFound()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Verify_RecovNoUser");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/verify",
            new { type = "recovery", token = "sometoken", email = "nobody@example.com", newPassword = "NewPass1!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    // Scenario 16: type=signup with missing email returns invalid_request

    [Fact]
    public async Task Verify_Signup_Returns400_WhenEmailMissing()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Verify_SignupNoEmail");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/verify",
            new { type = "signup", token = "sometoken" });
        // email field omitted

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_request");
    }

    // Scenario 17: Unknown type value returns invalid_request

    [Fact]
    public async Task Verify_UnknownType_Returns400()
    {
        await using var factory = new AuthWebApplicationFactory(appName: "Verify_BadType");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/verify",
            new { type = "magic_link", token = "sometoken", email = "u@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var doc = await response.ReadJsonAsync();
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_request");
    }

    // ── POST /api/auth/resend — additional ────────────────────────────────────

    // Scenario 18: Already-confirmed user silently returns 200 without sending email

    [Fact]
    public async Task Resend_Returns200_WhenUserAlreadyConfirmed_NoEmailSent()
    {
        var emailSender = new TestEmailSender();
        await using var factory = BuildWithEmailSender("Resend_Confirmed", emailSender);
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "confirmed@example.com", password = "Password1!" });

        // Confirm the email via UserManager
        await ConfirmEmailAsync(factory, "confirmed@example.com");

        // Resend for an already-confirmed user
        var response = await client.PostAsJsonAsync("/api/auth/resend",
            new { email = "confirmed@example.com", type = "signup" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // No email sent because user.EmailConfirmed == true
        emailSender.ConfirmationsSent.Should().BeEmpty();
    }

    // Scenario 19: type=signup and type=email_change have separate rate-limit buckets

    [Fact]
    public async Task Resend_SignupAndEmailChange_HaveSeparateRateLimits()
    {
        var emailSender = new TestEmailSender();
        await using var factory = BuildWithEmailSender("Resend_SeparateBuckets", emailSender);
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/signup",
            new { email = "buckets@example.com", password = "Password1!" });

        // First resend with type=signup — hits the bucket resend:signup:buckets@example.com
        var first = await client.PostAsJsonAsync("/api/auth/resend",
            new { email = "buckets@example.com", type = "signup" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Immediate resend with type=email_change — different cache key, should NOT be 429
        var second = await client.PostAsJsonAsync("/api/auth/resend",
            new { email = "buckets@example.com", type = "email_change" });
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second resend with same type=signup — same bucket, should be 429
        var third = await client.PostAsJsonAsync("/api/auth/resend",
            new { email = "buckets@example.com", type = "signup" });
        third.StatusCode.Should().Be((HttpStatusCode)429);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AuthWebApplicationFactory BuildWithEmailSender(
        string appName,
        TestEmailSender emailSender,
        bool requireEmailConfirmation = false)
    {
        return new AuthWebApplicationFactory(appName, requireEmailConfirmation,
            configureServices: services =>
                services.Replace(ServiceDescriptor.Scoped<IHPDAuthEmailSender>(_ => emailSender)));
    }

    private static async Task ConfirmEmailAsync(AuthWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null) return;
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await userManager.ConfirmEmailAsync(user, token);
        user.EmailConfirmedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
    }

    private static async Task<string> GeneratePasswordResetTokenAsync(
        AuthWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        return await userManager.GeneratePasswordResetTokenAsync(user!);
    }

    private static async Task<string?> GetSecurityStampAsync(
        AuthWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        return user?.SecurityStamp;
    }
}
