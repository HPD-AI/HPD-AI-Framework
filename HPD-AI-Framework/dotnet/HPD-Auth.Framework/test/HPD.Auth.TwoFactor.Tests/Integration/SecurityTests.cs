using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.TwoFactor.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Xunit;

#pragma warning disable CA1825 // Array.Empty

namespace HPD.Auth.TwoFactor.Tests.Integration;

/// <summary>
/// Tests covering:
///   Section 7  — GET /api/auth/factors  (Factor list)
///   Section 9  — Security (auth enforcement, data isolation, last-method protection)
/// </summary>
[Collection("PasskeyTests")]
public class SecurityTests : IClassFixture<TwoFactorWebFactory>
{
    private readonly TwoFactorWebFactory _factory;

    public SecurityTests(TwoFactorWebFactory factory)
    {
        _factory = factory;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 7 — GET /api/auth/factors  (Factor list)
    // ─────────────────────────────────────────────────────────────────────────

    // 7.1 — User with TOTP enrolled: response includes totp entry with isEnabled
    [Fact]
    public async Task ListFactors_UserWithTotp_IncludesTotpEntry()
    {
        var (user, client) = await _factory.CreateUserAsync("list1@example.com");
        await EnrollTotpAsync(user, client);

        var resp = await client.GetAsync("/api/auth/factors");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var factors = await resp.ReadJsonAsync<List<System.Text.Json.JsonElement>>();
        factors.Should().NotBeNull();
        var totp = factors!.FirstOrDefault(f => f.GetProperty("type").GetString() == "totp");
        totp.ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Undefined);
        totp.TryGetProperty("isEnabled", out _).Should().BeTrue();
    }

    // 7.2 — User with no TOTP: TOTP entry absent from list
    [Fact]
    public async Task ListFactors_UserWithNoTotp_NotoTPEntry()
    {
        var (_, client) = await _factory.CreateUserAsync("list2@example.com");

        var resp = await client.GetAsync("/api/auth/factors");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var factors = await resp.ReadJsonAsync<List<System.Text.Json.JsonElement>>();
        factors.Should().NotBeNull();
        var totpEntry = factors!.Any(f => f.GetProperty("type").GetString() == "totp");
        totpEntry.Should().BeFalse();
    }

    // 7.5 — Unauthenticated request returns 401
    [Fact]
    public async Task ListFactors_Unauthenticated_Returns401()
    {
        var client = _factory.CreateAnonymousClient();
        var resp = await client.GetAsync("/api/auth/factors");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 7.6 — Factor list is scoped to current user: User A cannot see User B's factors
    [Fact]
    public async Task ListFactors_ScopedToUser_CannotSeeOtherUserFactors()
    {
        var (userB, _) = await _factory.CreateUserAsync("listb@example.com");
        var (_, clientA) = await _factory.CreateUserAsync("lista@example.com");

        // Enroll TOTP for user B.
        var clientB = _factory.CreateAuthenticatedClient(userB.Id);
        await EnrollTotpAsync(userB, clientB);

        // User A's factor list should NOT contain User B's TOTP.
        var resp = await clientA.GetAsync("/api/auth/factors");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var factors = await resp.ReadJsonAsync<List<System.Text.Json.JsonElement>>();
        // The list for user A should be empty (or have A's own factors).
        factors.Should().NotBeNull();
        // If any TOTP factor is present, its ID must contain userA's ID, not userB's.
        var totpFactors = factors!.Where(f =>
        {
            f.TryGetProperty("type", out var t);
            return t.GetString() == "totp";
        }).ToList();

        foreach (var totp in totpFactors)
        {
            var id = totp.GetProperty("id").GetString()!;
            id.Should().NotContain(userB.Id.ToString());
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 9.1 — Authentication Enforcement
    // ─────────────────────────────────────────────────────────────────────────

    // 9.1.1 — GET /api/auth/factors without token → 401
    [Fact]
    public async Task AuthEnforcement_GetFactors_NoToken_Returns401()
    {
        var client = _factory.CreateAnonymousClient();
        (await client.GetAsync("/api/auth/factors")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    // 9.1.2 — POST /api/auth/factors without token → 401
    [Fact]
    public async Task AuthEnforcement_PostFactors_NoToken_Returns401()
    {
        var client = _factory.CreateAnonymousClient();
        (await client.PostJsonAsync("/api/auth/factors", new { type = "totp" })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    // 9.1.3 — POST /api/auth/factors/{id}/challenge without token → 401
    [Fact]
    public async Task AuthEnforcement_Challenge_NoToken_Returns401()
    {
        var client = _factory.CreateAnonymousClient();
        (await client.PostJsonAsync($"/api/auth/factors/totp:{Guid.NewGuid()}/challenge", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    // 9.1.4 — POST /api/auth/factors/{id}/verify without token → 401
    [Fact]
    public async Task AuthEnforcement_VerifyFactor_NoToken_Returns401()
    {
        var client = _factory.CreateAnonymousClient();
        (await client.PostJsonAsync($"/api/auth/factors/totp:{Guid.NewGuid()}/verify", new { code = "123456" })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    // 9.1.5 — DELETE /api/auth/factors/{id} without token → 401
    [Fact]
    public async Task AuthEnforcement_DeleteFactor_NoToken_Returns401()
    {
        var client = _factory.CreateAnonymousClient();
        (await client.DeleteAsync($"/api/auth/factors/totp:{Guid.NewGuid()}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    // 9.1.6 — GET /api/auth/passkeys without token → 401
    [Fact]
    public async Task AuthEnforcement_GetPasskeys_NoToken_Returns401()
    {
        var client = _factory.CreateAnonymousClient();
        (await client.GetAsync("/api/auth/passkeys")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    // 9.1.7 — PATCH /api/auth/passkeys/{id} without token → 401
    [Fact]
    public async Task AuthEnforcement_PatchPasskey_NoToken_Returns401()
    {
        var client = _factory.CreateAnonymousClient();
        (await client.PatchJsonAsync($"/api/auth/passkeys/{WebEncoders.Base64UrlEncode(new byte[16])}", new { name = "x" })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    // 9.1.8 — DELETE /api/auth/passkeys/{id} without token → 401
    [Fact]
    public async Task AuthEnforcement_DeletePasskey_NoToken_Returns401()
    {
        var client = _factory.CreateAnonymousClient();
        (await client.DeleteAsync($"/api/auth/passkeys/{WebEncoders.Base64UrlEncode(new byte[16])}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    // 9.1.9 — POST /api/auth/2fa/verify is anonymous (no token required for the cookie-based 2FA session)
    [Fact]
    public async Task AuthEnforcement_TwoFactorVerify_AllowsAnonymous()
    {
        var client = _factory.CreateAnonymousClient();
        // The endpoint is AllowAnonymous so it must not return 401.
        // It may return 400 (no session cookie) but not 401.
        var resp = await client.PostJsonAsync("/api/auth/2fa/verify", new { code = "123456" });
        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    // 9.1.10 — POST /api/auth/passkey/authenticate/options is anonymous
    [Fact]
    public async Task AuthEnforcement_PasskeyAuthOptions_AllowsAnonymous()
    {
        var client = _factory.CreateAnonymousClient();
        var resp = await client.PostJsonAsync("/api/auth/passkey/authenticate/options", new { });
        // May return 500 if no IPasskeyHandler is registered, but not 401.
        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    // 9.1.11 — POST /api/auth/passkey/authenticate/complete is anonymous
    [Fact]
    public async Task AuthEnforcement_PasskeyAuthComplete_AllowsAnonymous()
    {
        var client = _factory.CreateAnonymousClient();
        var resp = await client.PostJsonAsync("/api/auth/passkey/authenticate/complete",
            new { credentialJson = "{}" });
        // May return 500/400 if handler missing, but not 401.
        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 9.2 — Data Isolation
    // ─────────────────────────────────────────────────────────────────────────

    // 9.2.1 — User A tries to verify factor ID totp:{UserB.Id} → 404
    [Fact]
    public async Task DataIsolation_UserA_CannotVerifyUserBFactor()
    {
        var (userA, clientA) = await _factory.CreateUserAsync("isola@example.com");
        var (userB, _) = await _factory.CreateUserAsync("isolb@example.com");

        var resp = await clientA.PostJsonAsync(
            $"/api/auth/factors/totp:{userB.Id}/verify",
            new { code = "123456" });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 9.2.3 — Factor list always scoped to authenticated user
    [Fact]
    public async Task DataIsolation_FactorList_ReturnsOnlyOwnFactors()
    {
        var (userA, clientA) = await _factory.CreateUserAsync("scopa@example.com");
        var (userB, _) = await _factory.CreateUserAsync("scopb@example.com");

        // Enroll TOTP for user B.
        var clientB = _factory.CreateAuthenticatedClient(userB.Id);
        await EnrollTotpAsync(userB, clientB);

        // User A's factor list should not contain user B's factor.
        var resp = await clientA.GetAsync("/api/auth/factors");
        var factors = await resp.ReadJsonAsync<List<System.Text.Json.JsonElement>>();

        var userBFactorId = $"totp:{userB.Id}";
        var hasUserBFactor = factors!.Any(f =>
        {
            f.TryGetProperty("id", out var idEl);
            return idEl.GetString() == userBFactorId;
        });
        hasUserBFactor.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 9.3 — Last-Method Protection
    // ─────────────────────────────────────────────────────────────────────────

    // 9.3.1 — Deleting TOTP when no password and no passkeys → 400 last_auth_method
    [Fact]
    public async Task LastMethod_DeleteTotp_NoPasswordNoPasskeys_Returns400()
    {
        var (user, client) = await _factory.CreateUserAsync("last1@example.com", password: null);
        // Set up TOTP without password via UserManager directly.
        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        await um.ResetAuthenticatorKeyAsync(freshUser!);

        var resp = await client.DeleteAsync($"/api/auth/factors/totp:{user.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await resp.ReadJsonElementAsync();
        body.GetProperty("error").GetString().Should().Be("last_auth_method");
    }

    // 9.3.3 — Deleting TOTP when password exists → allowed (204)
    [Fact]
    public async Task LastMethod_DeleteTotp_WithPassword_Returns204()
    {
        var (user, client) = await _factory.CreateUserAsync("last3@example.com", "Password1");
        await EnrollTotpAsync(user, client);

        var resp = await client.DeleteAsync($"/api/auth/factors/totp:{user.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 7.3/7.4 — Passkey entries in factor list
    // ─────────────────────────────────────────────────────────────────────────

    // 7.3 — User with passkeys: each passkey appears as type="passkey" with id, friendlyName, createdAt
    [Fact]
    public async Task ListFactors_UserWithPasskey_IncludesPasskeyEntry()
    {
        var (user, client) = await _factory.CreateUserAsync("listpk1@example.com");

        // Seed a passkey via UserManager.
        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        var credentialId = Guid.NewGuid().ToByteArray();
        var passkey = new UserPasskeyInfo(
            credentialId,
            publicKey: new byte[65],
            createdAt: DateTimeOffset.UtcNow,
            signCount: 0,
            transports: Array.Empty<string>(),
            isUserVerified: true,
            isBackupEligible: false,
            isBackedUp: false,
            attestationObject: Array.Empty<byte>(),
            clientDataJson: Array.Empty<byte>())
        { Name = "My YubiKey" };
        await um.AddOrUpdatePasskeyAsync(freshUser!, passkey);

        var resp = await client.GetAsync("/api/auth/factors");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var factors = await resp.ReadJsonAsync<List<System.Text.Json.JsonElement>>();
        factors.Should().NotBeNull();

        var passkeyFactor = factors!.FirstOrDefault(f =>
        {
            f.TryGetProperty("type", out var t);
            return t.GetString() == "passkey";
        });
        passkeyFactor.ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Undefined,
            because: "a seeded passkey should appear in the factor list");
        passkeyFactor.TryGetProperty("id", out _).Should().BeTrue();
        passkeyFactor.TryGetProperty("friendlyName", out _).Should().BeTrue();
        passkeyFactor.TryGetProperty("createdAt", out _).Should().BeTrue();
    }

    // 7.4 — User with both TOTP and passkeys: both appear in the list
    [Fact]
    public async Task ListFactors_UserWithTotpAndPasskey_BothAppear()
    {
        var (user, client) = await _factory.CreateUserAsync("listboth@example.com");

        // Enroll TOTP.
        await EnrollTotpAsync(user, client);

        // Seed a passkey.
        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        var credentialId = Guid.NewGuid().ToByteArray();
        var passkey = new UserPasskeyInfo(
            credentialId,
            publicKey: new byte[65],
            createdAt: DateTimeOffset.UtcNow,
            signCount: 0,
            transports: Array.Empty<string>(),
            isUserVerified: true,
            isBackupEligible: false,
            isBackedUp: false,
            attestationObject: Array.Empty<byte>(),
            clientDataJson: Array.Empty<byte>())
        { Name = "Touch ID" };
        await um.AddOrUpdatePasskeyAsync(freshUser!, passkey);

        var resp = await client.GetAsync("/api/auth/factors");
        var factors = await resp.ReadJsonAsync<List<System.Text.Json.JsonElement>>();
        factors.Should().NotBeNull();

        var hasTotpFactor = factors!.Any(f =>
        {
            f.TryGetProperty("type", out var t);
            return t.GetString() == "totp";
        });
        var hasPasskeyFactor = factors!.Any(f =>
        {
            f.TryGetProperty("type", out var t);
            return t.GetString() == "passkey";
        });

        hasTotpFactor.Should().BeTrue("TOTP factor should be in the list");
        hasPasskeyFactor.Should().BeTrue("passkey factor should be in the list");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 9.3.4 — Deleting TOTP when passkeys exist → allowed (204)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LastMethod_DeleteTotp_WhenPasskeysExist_Returns204()
    {
        // Create user without a password so the passkey is the only other method.
        var (user, client) = await _factory.CreateUserAsync("last4@example.com", password: null);

        // Set up TOTP directly (no password needed for key generation).
        using var setupScope = _factory.CreateServiceScope();
        var umSetup = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await umSetup.FindByIdAsync(user.Id.ToString());
        await umSetup.ResetAuthenticatorKeyAsync(freshUser!);
        await umSetup.SetTwoFactorEnabledAsync(freshUser!, true);

        // Seed a passkey so the user has an alternative auth method.
        var credentialId = Guid.NewGuid().ToByteArray();
        var passkey = new UserPasskeyInfo(
            credentialId,
            publicKey: new byte[65],
            createdAt: DateTimeOffset.UtcNow,
            signCount: 0,
            transports: Array.Empty<string>(),
            isUserVerified: true,
            isBackupEligible: false,
            isBackedUp: false,
            attestationObject: Array.Empty<byte>(),
            clientDataJson: Array.Empty<byte>())
        { Name = "Backup Key" };
        await umSetup.AddOrUpdatePasskeyAsync(freshUser!, passkey);

        // Deleting TOTP should be allowed (passkey remains as an auth method).
        var resp = await client.DeleteAsync($"/api/auth/factors/totp:{user.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section 9.2.2 — User A tries to delete passkey owned by User B → 404
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DataIsolation_UserA_CannotDeleteUserBPasskey()
    {
        var (userA, clientA) = await _factory.CreateUserAsync("pkisola@example.com");
        var (userB, _) = await _factory.CreateUserAsync("pkisolb@example.com");

        // Seed a passkey for user B.
        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUserB = await um.FindByIdAsync(userB.Id.ToString());
        var credentialId = Guid.NewGuid().ToByteArray();
        var passkey = new UserPasskeyInfo(
            credentialId,
            publicKey: new byte[65],
            createdAt: DateTimeOffset.UtcNow,
            signCount: 0,
            transports: Array.Empty<string>(),
            isUserVerified: true,
            isBackupEligible: false,
            isBackedUp: false,
            attestationObject: Array.Empty<byte>(),
            clientDataJson: Array.Empty<byte>())
        { Name = "User B Key" };
        await um.AddOrUpdatePasskeyAsync(freshUserB!, passkey);

        // User A tries to delete user B's passkey.
        var idBase64 = WebEncoders.Base64UrlEncode(credentialId);
        var resp = await clientA.DeleteAsync($"/api/auth/passkeys/{idBase64}");
        // GetPasskeyAsync is scoped to the authenticated user (A), so B's passkey won't be found → 404.
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task EnrollTotpAsync(ApplicationUser user, HttpClient client)
    {
        await client.PostJsonAsync("/api/auth/factors", new { type = "totp" });

        using var scope = _factory.CreateServiceScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await um.FindByIdAsync(user.Id.ToString());
        var key = await um.GetAuthenticatorKeyAsync(freshUser!);
        var code = TotpHelper.GenerateCode(key!);

        await client.PostJsonAsync($"/api/auth/factors/totp:{user.Id}/verify", new { code });
    }
}
