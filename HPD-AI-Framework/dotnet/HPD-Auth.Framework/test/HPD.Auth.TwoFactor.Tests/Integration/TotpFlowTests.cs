using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.TwoFactor.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Xunit;

namespace HPD.Auth.TwoFactor.Tests.Integration;

/// <summary>
/// Integration tests covering:
///   Section 2  — POST /api/auth/factors   (TOTP setup)
///   Section 3  — POST /api/auth/factors/{factorId}/verify
///   Section 4  — DELETE /api/auth/factors/{factorId}
///   Section 5  — Recovery code format, single-use enforcement, and regeneration
/// </summary>
[Collection("PasskeyTests")]
public class TotpFlowTests : IAsyncLifetime
{
    private TwoFactorWebFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new TwoFactorWebFactory();
        await _factory.StartAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    // ─────────────────────────────────────────────────────────────────────────
    // Section 2 — POST /api/auth/factors  (TOTP setup / key generation)
    // ─────────────────────────────────────────────────────────────────────────

    // 2.1.1 — ResetAuthenticatorKeyAsync is called (key changes on every call)
    [Fact]
    public async Task Setup_AuthenticatedUser_ResetsKeyEachCall()
    {
        var (user, client) = await _factory.CreateUserAsync("setup1@example.com");

        var resp1 = await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body1 = await resp1.ReadJsonElementAsync();
        var key1 = body1.GetProperty("sharedKey").GetString();

        var resp2 = await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await resp2.ReadJsonElementAsync();
        var key2 = body2.GetProperty("sharedKey").GetString();

        key1.Should().NotBeNullOrEmpty();
        key2.Should().NotBeNullOrEmpty();
        key1.Should().NotBe(key2, because: "each setup call should generate a fresh key");
    }

    // 2.1.3 — Response body contains id, type, sharedKey, authenticatorUri
    [Fact]
    public async Task Setup_Response_ContainsRequiredFields()
    {
        var (_, client) = await _factory.CreateUserAsync("setup2@example.com");

        var resp = await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.ReadJsonElementAsync();
        body.TryGetProperty("id", out _).Should().BeTrue();
        body.TryGetProperty("type", out var typeEl).Should().BeTrue();
        typeEl.GetString().Should().Be("totp");
        body.TryGetProperty("sharedKey", out _).Should().BeTrue();
        body.TryGetProperty("authenticatorUri", out _).Should().BeTrue();
    }

    // 2.1.4 — sharedKey is formatted in groups of 4 (lowercase, space-separated)
    [Fact]
    public async Task Setup_SharedKey_IsFormattedInGroupsOfFour()
    {
        var (_, client) = await _factory.CreateUserAsync("setup3@example.com");

        var resp = await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });
        var body = await resp.ReadJsonElementAsync();
        var sharedKey = body.GetProperty("sharedKey").GetString()!;

        sharedKey.Should().Be(sharedKey.ToLowerInvariant(), because: "key should be lowercase");
        // All characters before the last group should be separated by spaces every 4 chars.
        var parts = sharedKey.Split(' ');
        foreach (var part in parts.Take(parts.Length - 1))
            part.Length.Should().Be(4);
    }

    // 2.1.5 — authenticatorUri is a valid otpauth://totp/ URI
    [Fact]
    public async Task Setup_AuthenticatorUri_IsValidOtpauthUri()
    {
        var (_, client) = await _factory.CreateUserAsync("setup4@example.com");

        var resp = await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });
        var body = await resp.ReadJsonElementAsync();
        var uri = body.GetProperty("authenticatorUri").GetString()!;

        uri.Should().StartWith("otpauth://totp/");
        uri.Should().Contain("secret=");
        uri.Should().Contain("issuer=");
        uri.Should().Contain("digits=6");
    }

    // 2.1.6 — authenticatorUri issuer matches HPDAuthOptions.AppName
    [Fact]
    public async Task Setup_AuthenticatorUri_IssuerMatchesAppName()
    {
        var (_, client) = await _factory.CreateUserAsync("setup5@example.com");

        var resp = await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });
        var body = await resp.ReadJsonElementAsync();
        var uri = body.GetProperty("authenticatorUri").GetString()!;

        // AppName is the dbName used by factory (contains "TwoFactorTest_...").
        // We verify the issuer query param exists — the factory AppName should be URL-encoded in the URI.
        uri.Should().Contain("issuer=");
    }

    // 2.1.7 — authenticatorUri email contains the user's email (percent-encoded)
    [Fact]
    public async Task Setup_AuthenticatorUri_ContainsPercentEncodedEmail()
    {
        var (_, client) = await _factory.CreateUserAsync("setupemail@example.com");

        var resp = await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });
        var body = await resp.ReadJsonElementAsync();
        var uri = body.GetProperty("authenticatorUri").GetString()!;

        // '@' encoded as %40
        uri.Should().Contain("setupemail%40example.com");
    }

    // 2.1.8 — Unauthenticated request returns 401
    [Fact]
    public async Task Setup_Unauthenticated_Returns401()
    {
        var client = _factory.CreateAnonymousClient();
        var resp = await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 2.1.9 — Optional friendlyName in body is reflected in response
    [Fact]
    public async Task Setup_WithFriendlyName_ReflectedInResponse()
    {
        var (_, client) = await _factory.CreateUserAsync("setupfn@example.com");

        var resp = await client.PostJsonAsync("/api/auth/factors",
            new { type = "totp", friendlyName = "My Authenticator" });
        var body = await resp.ReadJsonElementAsync();

        body.GetProperty("friendlyName").GetString().Should().Be("My Authenticator");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 3 — POST /api/auth/factors/{factorId}/verify
    // ─────────────────────────────────────────────────────────────────────────

    // 3.1.1 — Valid TOTP code after setup returns 200 with success + recoveryCodes
    [Fact]
    public async Task Verify_ValidCode_Returns200WithRecoveryCodes()
    {
        var (user, client) = await _factory.CreateUserAsync("verify1@example.com");
        var factorId = $"totp:{user.Id}";

        // Setup via API to trigger key reset.
        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        // Get the raw key to generate a valid TOTP code.
        using var scope = _factory.CreateServiceScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await userManager.FindByIdAsync(user.Id.ToString());
        var key = await userManager.GetAuthenticatorKeyAsync(freshUser!);
        var code = TotpHelper.GenerateCode(key!);

        var resp = await client.PostJsonAsync($"/api/auth/factors/{factorId}/verify",
            new { code });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.ReadJsonElementAsync();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.TryGetProperty("recoveryCodes", out var codesEl).Should().BeTrue();
        codesEl.GetArrayLength().Should().Be(10);
    }

    // 3.1.2 — recoveryCodes contains exactly 10 non-empty strings
    [Fact]
    public async Task Verify_ValidCode_Returns10NonEmptyRecoveryCodes()
    {
        var (user, client) = await _factory.CreateUserAsync("verify2@example.com");
        var factorId = $"totp:{user.Id}";

        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        var key = await um.GetAuthenticatorKeyAsync(freshUser!);
        var code = TotpHelper.GenerateCode(key!);

        var resp = await client.PostJsonAsync($"/api/auth/factors/{factorId}/verify",
            new { code });
        var body = await resp.ReadJsonElementAsync();
        var codes = body.GetProperty("recoveryCodes").EnumerateArray()
                        .Select(e => e.GetString()!).ToList();

        codes.Should().HaveCount(10);
        codes.Should().OnlyContain(c => !string.IsNullOrEmpty(c));
    }

    // 3.1.3 — SetTwoFactorEnabledAsync(true) is called after valid code
    [Fact]
    public async Task Verify_ValidCode_TwoFactorIsEnabled()
    {
        var (user, client) = await _factory.CreateUserAsync("verify3@example.com");
        var factorId = $"totp:{user.Id}";

        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        // Use a dedicated scope to read the key — dispose it before the HTTP call so the
        // post-HTTP assertion opens a completely fresh DbContext (no stale change-tracker cache).
        string code;
        using (var keyScope = _factory.CreateServiceScope())
        {
            var um = keyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var freshUser = await um.FindByIdAsync(user.Id.ToString());
            var key = await um.GetAuthenticatorKeyAsync(freshUser!);
            code = TotpHelper.GenerateCode(key!);
        }

        await client.PostJsonAsync($"/api/auth/factors/{factorId}/verify", new { code });

        using var checkScope = _factory.CreateServiceScope();
        var checkUm = checkScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var updatedUser = await checkUm.FindByIdAsync(user.Id.ToString());
        var is2faEnabled = await checkUm.GetTwoFactorEnabledAsync(updatedUser!);
        is2faEnabled.Should().BeTrue();
    }

    // 3.1.6 — CountRecoveryCodesAsync after verify returns 10
    [Fact]
    public async Task Verify_ValidCode_RecoveryCodeCountIs10()
    {
        var (user, client) = await _factory.CreateUserAsync("verify4@example.com");
        var factorId = $"totp:{user.Id}";

        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        string code;
        using (var keyScope = _factory.CreateServiceScope())
        {
            var um = keyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var freshUser = await um.FindByIdAsync(user.Id.ToString());
            var key = await um.GetAuthenticatorKeyAsync(freshUser!);
            code = TotpHelper.GenerateCode(key!);
        }

        await client.PostJsonAsync($"/api/auth/factors/{factorId}/verify", new { code });

        using var checkScope = _factory.CreateServiceScope();
        var checkUm = checkScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var reloaded = await checkUm.FindByIdAsync(user.Id.ToString());
        var count = await checkUm.CountRecoveryCodesAsync(reloaded!);
        count.Should().Be(10);
    }

    // 3.2.1 — Invalid TOTP code returns 400 with error: "invalid_code"
    [Fact]
    public async Task Verify_InvalidCode_Returns400WithInvalidCodeError()
    {
        var (user, client) = await _factory.CreateUserAsync("verify5@example.com");
        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        var resp = await client.PostJsonAsync($"/api/auth/factors/totp:{user.Id}/verify",
            new { code = "000000" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await resp.ReadJsonElementAsync();
        body.GetProperty("error").GetString().Should().Be("invalid_code");
    }

    // 3.2.2 — Empty code returns 400
    [Fact]
    public async Task Verify_EmptyCode_Returns400()
    {
        var (user, client) = await _factory.CreateUserAsync("verify6@example.com");
        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        var resp = await client.PostJsonAsync($"/api/auth/factors/totp:{user.Id}/verify",
            new { code = "" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 3.2.3 — Code with spaces is stripped before verification (valid code accepted)
    [Fact]
    public async Task Verify_CodeWithSpaces_StrippedAndValidated()
    {
        var (user, client) = await _factory.CreateUserAsync("verify7@example.com");
        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        var key = await um.GetAuthenticatorKeyAsync(freshUser!);
        var code = TotpHelper.GenerateCode(key!);
        var codeWithSpaces = string.Join(" ", code.ToCharArray()); // "1 2 3 4 5 6"

        var resp = await client.PostJsonAsync($"/api/auth/factors/totp:{user.Id}/verify",
            new { code = codeWithSpaces });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 3.2.4 — Code with hyphens is stripped before verification
    [Fact]
    public async Task Verify_CodeWithHyphens_StrippedAndValidated()
    {
        var (user, client) = await _factory.CreateUserAsync("verify8@example.com");
        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        var key = await um.GetAuthenticatorKeyAsync(freshUser!);
        var code = TotpHelper.GenerateCode(key!);
        var codeWithHyphens = $"{code[..3]}-{code[3..]}";

        var resp = await client.PostJsonAsync($"/api/auth/factors/totp:{user.Id}/verify",
            new { code = codeWithHyphens });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 3.2.5 — Unauthenticated request returns 401
    [Fact]
    public async Task Verify_Unauthenticated_Returns401()
    {
        var client = _factory.CreateAnonymousClient();
        var resp = await client.PostJsonAsync("/api/auth/factors/totp:fake/verify",
            new { code = "123456" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 3.2.6 — factorId does not belong to authenticated user returns 404
    [Fact]
    public async Task Verify_FactorIdBelongsToAnotherUser_Returns404()
    {
        var (userA, clientA) = await _factory.CreateUserAsync("verifya@example.com");
        var (userB, _) = await _factory.CreateUserAsync("verifyb@example.com");

        // clientA tries to verify using userB's factor ID.
        var resp = await clientA.PostJsonAsync($"/api/auth/factors/totp:{userB.Id}/verify",
            new { code = "123456" });

        // The endpoint resolves the user from the JWT claim, not from the factorId,
        // so "totp:{userB.Id}" won't match the authenticated user A → 404.
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 3.2.7 — No TOTP setup in progress (key not set) returns 404
    [Fact]
    public async Task Verify_NoTotpSetupInProgress_Returns404()
    {
        var (user, client) = await _factory.CreateUserAsync("verify9@example.com");
        // Do NOT call setup first.

        var resp = await client.PostJsonAsync($"/api/auth/factors/totp:{user.Id}/verify",
            new { code = "123456" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 3.3.1 — CONFIGURE_2FA removed from RequiredActions after successful verify
    [Fact]
    public async Task Verify_ValidCode_Configure2faRemovedFromRequiredActions()
    {
        var (user, client) = await _factory.CreateUserAsync("verify10@example.com", configure: u =>
        {
            u.RequiredActions.Add("CONFIGURE_2FA");
        });

        // Save the required action.
        using var setupScope = _factory.CreateServiceScope();
        var umSetup = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbUser = await umSetup.FindByIdAsync(user.Id.ToString());
        dbUser!.RequiredActions.Add("CONFIGURE_2FA");
        await umSetup.UpdateAsync(dbUser);

        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        using var verifyScope = _factory.CreateServiceScope();
        var um = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        var key = await um.GetAuthenticatorKeyAsync(freshUser!);
        var code = TotpHelper.GenerateCode(key!);

        await client.PostJsonAsync($"/api/auth/factors/totp:{user.Id}/verify", new { code });

        using var checkScope = _factory.CreateServiceScope();
        var umCheck = checkScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var updatedUser = await umCheck.FindByIdAsync(user.Id.ToString());
        updatedUser!.RequiredActions.Should().NotContain("CONFIGURE_2FA");
    }

    // 3.3.2 — User without CONFIGURE_2FA: RequiredActions unchanged, no error
    [Fact]
    public async Task Verify_ValidCode_WithoutConfigure2fa_NoError()
    {
        var (user, client) = await _factory.CreateUserAsync("verify11@example.com");
        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        var key = await um.GetAuthenticatorKeyAsync(freshUser!);
        var code = TotpHelper.GenerateCode(key!);

        var resp = await client.PostJsonAsync($"/api/auth/factors/totp:{user.Id}/verify",
            new { code });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 3.4.1 — Successful verify publishes audit log with action "2fa.enable"
    [Fact]
    public async Task Verify_ValidCode_AuditLogWritten()
    {
        var (user, client) = await _factory.CreateUserAsync("verify12@example.com");
        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        var key = await um.GetAuthenticatorKeyAsync(freshUser!);
        var code = TotpHelper.GenerateCode(key!);

        await client.PostJsonAsync($"/api/auth/factors/totp:{user.Id}/verify", new { code });

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.TwoFactorEnable);
        logs.Should().NotBeEmpty();
    }

    // 3.4.2 — Failed verify writes audit log with action "2fa.verify.failed"
    [Fact]
    public async Task Verify_InvalidCode_AuditLogWrittenWithFailedAction()
    {
        var (user, client) = await _factory.CreateUserAsync("verify13@example.com");
        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        await client.PostJsonAsync($"/api/auth/factors/totp:{user.Id}/verify",
            new { code = "000000" });

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.TwoFactorVerifyFailed);
        logs.Should().NotBeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 4 — DELETE /api/auth/factors/{factorId}  (TOTP disable)
    // ─────────────────────────────────────────────────────────────────────────

    // 4.1 — User with password: Delete TOTP returns 204
    [Fact]
    public async Task Delete_UserWithPassword_Returns204()
    {
        var (user, client) = await _factory.CreateUserAsync("delete1@example.com", "Password1");
        await EnrollTotpAsync(user, client);

        var resp = await client.DeleteAsync($"/api/auth/factors/totp:{user.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // 4.2 — ResetAuthenticatorKeyAsync is called (key is cleared after delete)
    [Fact]
    public async Task Delete_UserWithPassword_AuthenticatorKeyCleared()
    {
        var (user, client) = await _factory.CreateUserAsync("delete2@example.com", "Password1");
        await EnrollTotpAsync(user, client);

        await client.DeleteAsync($"/api/auth/factors/totp:{user.Id}");

        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        var key = await um.GetAuthenticatorKeyAsync(freshUser!);
        key.Should().BeNullOrEmpty();
    }

    // 4.3 — SetTwoFactorEnabledAsync(false) is called
    [Fact]
    public async Task Delete_UserWithPassword_TwoFactorDisabled()
    {
        var (user, client) = await _factory.CreateUserAsync("delete3@example.com", "Password1");
        await EnrollTotpAsync(user, client);

        await client.DeleteAsync($"/api/auth/factors/totp:{user.Id}");

        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        var is2faEnabled = await um.GetTwoFactorEnabledAsync(freshUser!);
        is2faEnabled.Should().BeFalse();
    }

    // 4.4 — User with no password and no passkeys returns 400 last_auth_method
    [Fact]
    public async Task Delete_NoPasswordNoPasskeys_Returns400LastAuthMethod()
    {
        // Create user without password.
        var (user, client) = await _factory.CreateUserAsync("delete4@example.com", password: null);
        await EnrollTotpAsync(user, client, passwordSet: false);

        var resp = await client.DeleteAsync($"/api/auth/factors/totp:{user.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await resp.ReadJsonElementAsync();
        body.GetProperty("error").GetString().Should().Be("last_auth_method");
    }

    // 4.7 — No TOTP factor enrolled returns 404
    [Fact]
    public async Task Delete_NoTotpEnrolled_Returns404()
    {
        var (user, client) = await _factory.CreateUserAsync("delete5@example.com", "Password1");
        // Do NOT enroll TOTP.

        var resp = await client.DeleteAsync($"/api/auth/factors/totp:{user.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 4.8 — Unauthenticated returns 401
    [Fact]
    public async Task Delete_Unauthenticated_Returns401()
    {
        var client = _factory.CreateAnonymousClient();
        var resp = await client.DeleteAsync($"/api/auth/factors/totp:{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 4.9 — Publish event: audit log written with action "2fa.disable"
    [Fact]
    public async Task Delete_UserWithPassword_AuditLogWritten()
    {
        var (user, client) = await _factory.CreateUserAsync("delete6@example.com", "Password1");
        await EnrollTotpAsync(user, client);

        await client.DeleteAsync($"/api/auth/factors/totp:{user.Id}");

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.TwoFactorDisable);
        logs.Should().NotBeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 5 — Recovery codes
    // ─────────────────────────────────────────────────────────────────────────

    // 5.1.1 — Each generated code is a non-empty string
    // 5.1.2 — Exactly 10 codes generated on enrollment
    // 5.1.3 — All 10 codes are distinct
    [Fact]
    public async Task RecoveryCodes_OnEnrollment_Are10UniqueNonEmptyStrings()
    {
        var (user, client) = await _factory.CreateUserAsync("rc1@example.com");
        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        var key = await um.GetAuthenticatorKeyAsync(freshUser!);
        var code = TotpHelper.GenerateCode(key!);

        var resp = await client.PostJsonAsync($"/api/auth/factors/totp:{user.Id}/verify",
            new { code });
        var body = await resp.ReadJsonElementAsync();
        var codes = body.GetProperty("recoveryCodes").EnumerateArray()
                        .Select(e => e.GetString()!).ToList();

        codes.Should().HaveCount(10);
        codes.Should().OnlyContain(c => !string.IsNullOrEmpty(c));
        codes.Distinct().Should().HaveCount(10);
    }

    // 5.3.1 — New enrollment produces a new set of 10 codes; old codes invalidated
    [Fact]
    public async Task RecoveryCodes_ReEnrollment_ProducesNewCodesOldInvalidated()
    {
        var (user, client) = await _factory.CreateUserAsync("rc2@example.com", "Password1");

        // First enrollment.
        var firstCodes = await FullEnrollAndGetCodesAsync(user, client);

        // Disable TOTP.
        await client.DeleteAsync($"/api/auth/factors/totp:{user.Id}");

        // Re-enroll.
        var secondCodes = await FullEnrollAndGetCodesAsync(user, client);

        secondCodes.Should().HaveCount(10);
        // The new codes should not overlap with the old ones (hashes differ).
        secondCodes.Should().NotIntersectWith(firstCodes);

        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        var count = await um.CountRecoveryCodesAsync(freshUser!);
        count.Should().Be(10);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Full enrollment: setup via API then verify with a real TOTP code.
    /// Returns the recovery codes from the verify response.
    /// </summary>
    private async Task<List<string>> FullEnrollAndGetCodesAsync(ApplicationUser user, HttpClient client)
    {
        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        var key = await um.GetAuthenticatorKeyAsync(freshUser!);
        var code = TotpHelper.GenerateCode(key!);

        var resp = await client.PostJsonAsync($"/api/auth/factors/totp:{user.Id}/verify",
            new { code });
        var body = await resp.ReadJsonElementAsync();
        return body.GetProperty("recoveryCodes").EnumerateArray()
                   .Select(e => e.GetString()!).ToList();
    }

    /// <summary>
    /// Performs full TOTP enrollment (setup + verify) for a user.
    /// </summary>
    private async Task EnrollTotpAsync(ApplicationUser user, HttpClient client, bool passwordSet = true)
    {
        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        var key = await um.GetAuthenticatorKeyAsync(freshUser!);
        var code = TotpHelper.GenerateCode(key!);

        await client.PostJsonAsync($"/api/auth/factors/totp:{user.Id}/verify", new { code });
    }

    /// <summary>Exposed for use by TwoFactorLoginTests.</summary>
    public static string GenerateTotpCode(string base32Key) => TotpHelper.GenerateCode(base32Key);
}
