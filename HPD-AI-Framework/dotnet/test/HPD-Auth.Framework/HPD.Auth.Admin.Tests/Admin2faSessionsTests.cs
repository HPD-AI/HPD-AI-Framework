using FluentAssertions;
using HPD.Auth.Admin.Tests.Helpers;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Identity;
using System.Net;
using System.Text.Json;
using Xunit;

namespace HPD.Auth.Admin.Tests;

/// <summary>
/// Tests for:
///   GET    /api/admin/users/{id}/2fa                  (section 26)
///   POST   /api/admin/users/{id}/2fa/disable          (section 27)
///   DELETE /api/admin/users/{id}/2fa/authenticator    (section 28)
///   POST   /api/admin/users/{id}/2fa/recovery-codes   (section 29)
///   GET    /api/admin/users/{id}/sessions             (section 30)
///   DELETE /api/admin/users/{id}/sessions             (section 31)
///   DELETE /api/admin/users/{id}/sessions/{sessionId} (section 32)
/// </summary>
public class Admin2faSessionsTests : IAsyncLifetime
{
    private AdminWebFactory _factory = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        _factory = new AdminWebFactory();
        await _factory.StartAsync();
        _admin = _factory.CreateAdminClient();
    }

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<ApplicationUser?> ReloadUser(string userId)
    {
        using var scope = _factory._GetScope();
        return await scope.GetService<UserManager<ApplicationUser>>().FindByIdAsync(userId);
    }

    // ── Section 26: GET 2FA status ────────────────────────────────────────────

    // 26.1 — user with 2FA disabled / no authenticator (default state)
    [Fact]
    public async Task Get2faStatus_DefaultUser_ReturnsFalseValues()
    {
        var user = await _factory.SeedUserAsync("2fadefault@example.com");

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}/2fa");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("enabled").GetBoolean().Should().BeFalse();
        json.GetProperty("hasAuthenticator").GetBoolean().Should().BeFalse();
        json.GetProperty("recoveryCodesLeft").GetInt32().Should().Be(0);
    }

    // 26.2 — confirmed by test 26.1 above (2FA disabled)

    // 26.3 — non-existent user → 404
    [Fact]
    public async Task Get2faStatus_NonExistentUser_Returns404()
    {
        var resp = await _admin.GetAsync($"/api/admin/users/{Guid.NewGuid()}/2fa");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Section 27: Disable 2FA ───────────────────────────────────────────────

    // 27.1 — user with 2FA enabled → disabled
    [Fact]
    public async Task Disable2fa_EnabledUser_2faDisabled()
    {
        var user = await _factory.SeedUserAsync("disable2fa@example.com");

        // Enable 2FA via UserManager directly.
        using var scope = _factory._GetScope();
        var um = scope.GetService<UserManager<ApplicationUser>>();
        var reloaded = await um.FindByIdAsync(user.Id.ToString());
        await um.SetTwoFactorEnabledAsync(reloaded!, true);

        var resp = await _admin.PostAsync($"/api/admin/users/{user.Id}/2fa/disable", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReloadUser(user.Id.ToString());
        updated!.TwoFactorEnabled.Should().BeFalse();
    }

    // 27.2 — already disabled → idempotent 200
    [Fact]
    public async Task Disable2fa_AlreadyDisabled_IdempotentOk()
    {
        var user = await _factory.SeedUserAsync("2faalready@example.com");
        var resp = await _admin.PostAsync($"/api/admin/users/{user.Id}/2fa/disable", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 27.3 — non-existent user → 404
    [Fact]
    public async Task Disable2fa_NonExistentUser_Returns404()
    {
        var resp = await _admin.PostAsync($"/api/admin/users/{Guid.NewGuid()}/2fa/disable", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 27.4 — audit log
    [Fact]
    public async Task Disable2fa_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("audit2fa@example.com");
        await _admin.PostAsync($"/api/admin/users/{user.Id}/2fa/disable", null);

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.TwoFactorDisable);
        logs.Should().NotBeEmpty();
    }

    // ── Section 28: Reset authenticator ──────────────────────────────────────

    // 28.1 — authenticator key reset
    [Fact]
    public async Task ResetAuthenticator_KeyReset_Returns200()
    {
        var user = await _factory.SeedUserAsync("resetauth@example.com");

        var resp = await _admin.DeleteAsync($"/api/admin/users/{user.Id}/2fa/authenticator");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 28.2 — non-existent user → 404
    [Fact]
    public async Task ResetAuthenticator_NonExistentUser_Returns404()
    {
        var resp = await _admin.DeleteAsync($"/api/admin/users/{Guid.NewGuid()}/2fa/authenticator");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 28.3 — audit log
    [Fact]
    public async Task ResetAuthenticator_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("auditresetauth@example.com");
        await _admin.DeleteAsync($"/api/admin/users/{user.Id}/2fa/authenticator");

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.TwoFactorSetup);
        logs.Should().NotBeEmpty();
        logs.First().Metadata.Should().Contain("reset_authenticator");
    }

    // ── Section 29: Generate recovery codes ──────────────────────────────────

    // 29.1 — default (no body) → 10 codes
    [Fact]
    public async Task GenerateRecoveryCodes_NoBody_Returns10Codes()
    {
        var user = await _factory.SeedUserAsync("recovcodes@example.com");

        var resp = await _admin.PostAsync($"/api/admin/users/{user.Id}/2fa/recovery-codes", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("codes").GetArrayLength().Should().Be(10);
    }

    // 29.2 — count=5
    [Fact]
    public async Task GenerateRecoveryCodes_Count5_Returns5Codes()
    {
        var user = await _factory.SeedUserAsync("recovcodes5@example.com");

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/2fa/recovery-codes",
            new { count = 5 });
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("codes").GetArrayLength().Should().Be(5);
    }

    // 29.3 — count=25 (over max 20) → clamped to 20
    [Fact]
    public async Task GenerateRecoveryCodes_Over20_ClampedTo20()
    {
        var user = await _factory.SeedUserAsync("recovclamp@example.com");

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/2fa/recovery-codes",
            new { count = 25 });
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("codes").GetArrayLength().Should().Be(20);
    }

    // 29.4 — count=0 (under min 1) → clamped to 1
    [Fact]
    public async Task GenerateRecoveryCodes_ZeroCount_ClampedTo1()
    {
        var user = await _factory.SeedUserAsync("recovmin@example.com");

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/2fa/recovery-codes",
            new { count = 0 });
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("codes").GetArrayLength().Should().Be(1);
    }

    // 29.5 — non-existent user → 404
    [Fact]
    public async Task GenerateRecoveryCodes_NonExistentUser_Returns404()
    {
        var resp = await _admin.PostAsync(
            $"/api/admin/users/{Guid.NewGuid()}/2fa/recovery-codes", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 29.6 — codes are unique strings
    [Fact]
    public async Task GenerateRecoveryCodes_CodesAreUnique()
    {
        var user = await _factory.SeedUserAsync("recovunique@example.com");

        var resp = await _admin.PostAsync(
            $"/api/admin/users/{user.Id}/2fa/recovery-codes", null);
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        var codes = json.GetProperty("codes").EnumerateArray()
            .Select(c => c.GetString()).ToList();
        codes.Distinct().Should().HaveCount(codes.Count);
    }

    // 29.7 — audit log
    [Fact]
    public async Task GenerateRecoveryCodes_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("auditrecov@example.com");
        await _admin.PostAsync($"/api/admin/users/{user.Id}/2fa/recovery-codes", null);

        var logs = await _factory.GetAuditLogsAsync(
            userId: user.Id, action: AuditActions.RecoveryCodeRegenerate);
        logs.Should().NotBeEmpty();
    }

    // ── Section 30: GET sessions ──────────────────────────────────────────────

    // 30.1 — user with 2 sessions
    [Fact]
    public async Task GetSessions_UserWith2Sessions_ReturnsBothSessions()
    {
        var user = await _factory.SeedUserAsync("sess2@example.com");

        using var scope = _factory._GetScope();
        var sessionManager = scope.GetService<ISessionManager>();
        await sessionManager.CreateSessionAsync(user.Id, new SessionContext("1.1.1.1", "UA1"));
        await sessionManager.CreateSessionAsync(user.Id, new SessionContext("2.2.2.2", "UA2"));

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}/sessions");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        json.GetArrayLength().Should().Be(2);
    }

    // 30.2 — user with no sessions → empty list
    [Fact]
    public async Task GetSessions_UserWithNoSessions_ReturnsEmptyList()
    {
        var user = await _factory.SeedUserAsync("nosessions@example.com");

        var resp = await _admin.GetAsync($"/api/admin/users/{user.Id}/sessions");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        json.GetArrayLength().Should().Be(0);
    }

    // 30.3 — invalid user ID format → 400
    [Fact]
    public async Task GetSessions_InvalidUserIdFormat_Returns400()
    {
        var resp = await _admin.GetAsync("/api/admin/users/not-a-guid/sessions");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Section 31: DELETE sessions (scope=all) ───────────────────────────────

    // 31.1 — scope=all → all sessions revoked
    [Fact]
    public async Task RevokeSessions_ScopeAll_AllSessionsRevoked()
    {
        var user = await _factory.SeedUserAsync("revokeall@example.com");

        using var setupScope = _factory._GetScope();
        var sm = setupScope.GetService<ISessionManager>();
        await sm.CreateSessionAsync(user.Id, new SessionContext("1.1.1.1", "UA1"));
        await sm.CreateSessionAsync(user.Id, new SessionContext("2.2.2.2", "UA2"));

        var resp = await _admin.DeleteAsync(
            $"/api/admin/users/{user.Id}/sessions?scope=all");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var checkScope = _factory._GetScope();
        var sm2 = checkScope.GetService<ISessionManager>();
        var remaining = await sm2.GetActiveSessionsAsync(user.Id);
        remaining.Should().BeEmpty();
    }

    // 31.2 — scope=others&currentSessionId keeps one session
    [Fact]
    public async Task RevokeSessions_ScopeOthers_KeepsCurrentSession()
    {
        var user = await _factory.SeedUserAsync("revokeothers@example.com");

        using var setupScope = _factory._GetScope();
        var sm = setupScope.GetService<ISessionManager>();
        var session1 = await sm.CreateSessionAsync(user.Id, new SessionContext("1.1.1.1", "UA1"));
        await sm.CreateSessionAsync(user.Id, new SessionContext("2.2.2.2", "UA2"));

        var resp = await _admin.DeleteAsync(
            $"/api/admin/users/{user.Id}/sessions?scope=others&currentSessionId={session1.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var checkScope = _factory._GetScope();
        var sm2 = checkScope.GetService<ISessionManager>();
        var remaining = await sm2.GetActiveSessionsAsync(user.Id);
        remaining.Should().HaveCount(1);
        remaining.Single().Id.Should().Be(session1.Id);
    }

    // 31.3 — scope=others but no currentSessionId → all revoked
    [Fact]
    public async Task RevokeSessions_ScopeOthersNoCurrentId_AllRevoked()
    {
        var user = await _factory.SeedUserAsync("revokeothersnoId@example.com");

        using var setupScope = _factory._GetScope();
        var sm = setupScope.GetService<ISessionManager>();
        await sm.CreateSessionAsync(user.Id, new SessionContext("1.1.1.1", "UA1"));
        await sm.CreateSessionAsync(user.Id, new SessionContext("2.2.2.2", "UA2"));

        var resp = await _admin.DeleteAsync(
            $"/api/admin/users/{user.Id}/sessions?scope=others");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var checkScope = _factory._GetScope();
        var sm2 = checkScope.GetService<ISessionManager>();
        var remaining = await sm2.GetActiveSessionsAsync(user.Id);
        remaining.Should().BeEmpty();
    }

    // 31.4 — non-existent user → 404
    [Fact]
    public async Task RevokeSessions_NonExistentUser_Returns404()
    {
        var resp = await _admin.DeleteAsync(
            $"/api/admin/users/{Guid.NewGuid()}/sessions");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 31.5 — audit log
    [Fact]
    public async Task RevokeSessions_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("auditsessions@example.com");
        await _admin.DeleteAsync($"/api/admin/users/{user.Id}/sessions?scope=all");

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AdminForceLogout);
        logs.Should().NotBeEmpty();
    }

    // ── Section 32: DELETE sessions/{sessionId} ───────────────────────────────

    // 32.1 — valid sessionId → revoked
    [Fact]
    public async Task RevokeSession_ValidSessionId_SessionRevoked()
    {
        var user = await _factory.SeedUserAsync("revokesingle@example.com");

        using var setupScope = _factory._GetScope();
        var sm = setupScope.GetService<ISessionManager>();
        var session = await sm.CreateSessionAsync(user.Id, new SessionContext("1.1.1.1", "UA1"));

        var resp = await _admin.DeleteAsync(
            $"/api/admin/users/{user.Id}/sessions/{session.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var checkScope = _factory._GetScope();
        var sm2 = checkScope.GetService<ISessionManager>();
        var remaining = await sm2.GetActiveSessionsAsync(user.Id);
        remaining.Should().NotContain(s => s.Id == session.Id);
    }

    // 32.2 — invalid sessionId format → 400
    [Fact]
    public async Task RevokeSession_InvalidSessionIdFormat_Returns400()
    {
        var user = await _factory.SeedUserAsync("invalidsess@example.com");

        var resp = await _admin.DeleteAsync(
            $"/api/admin/users/{user.Id}/sessions/not-a-guid");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 32.3 — non-existent user → 404
    [Fact]
    public async Task RevokeSession_NonExistentUser_Returns404()
    {
        var resp = await _admin.DeleteAsync(
            $"/api/admin/users/{Guid.NewGuid()}/sessions/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 32.4 — audit log
    [Fact]
    public async Task RevokeSession_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("auditsingleses@example.com");

        using var setupScope = _factory._GetScope();
        var sm = setupScope.GetService<ISessionManager>();
        var session = await sm.CreateSessionAsync(user.Id, new SessionContext("1.1.1.1", "UA1"));

        await _admin.DeleteAsync($"/api/admin/users/{user.Id}/sessions/{session.Id}");

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.SessionRevoke);
        logs.Should().NotBeEmpty();
    }
}
