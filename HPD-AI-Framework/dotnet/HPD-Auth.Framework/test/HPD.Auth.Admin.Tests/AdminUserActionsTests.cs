using FluentAssertions;
using HPD.Auth.Admin.Tests.Helpers;
using HPD.Auth.Core.Entities;
using Microsoft.AspNetCore.Identity;
using System.Net;
using Xunit;

namespace HPD.Auth.Admin.Tests;

/// <summary>
/// Tests for:
///   POST /api/admin/users/{id}/ban              (section 7)
///   POST /api/admin/users/{id}/unban            (section 8)
///   POST /api/admin/users/{id}/unlock           (section 9)
///   POST /api/admin/users/{id}/verify-email     (section 10)
///   POST /api/admin/users/{id}/invalidate-sessions (section 11)
///   POST /api/admin/users/{id}/enable           (section 12)
///   POST /api/admin/users/{id}/disable          (section 13)
/// </summary>
public class AdminUserActionsTests : IAsyncLifetime
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

    // Helper: refresh user from DB.
    private async Task<ApplicationUser?> ReloadUser(string userId)
    {
        using var scope = _factory._GetScope();
        return await scope.GetService<UserManager<ApplicationUser>>().FindByIdAsync(userId);
    }

    // ── Section 7: Ban ────────────────────────────────────────────────────────

    // 7.1 — duration="1h"
    [Fact]
    public async Task BanUser_1h_LockoutEndIsNowPlus1Hour()
    {
        var user = await _factory.SeedUserAsync("ban1h@example.com");
        var before = DateTimeOffset.UtcNow;

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/ban",
            new { duration = "1h" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReloadUser(user.Id.ToString());
        updated!.LockoutEnd.Should().NotBeNull();
        updated.LockoutEnd!.Value.Should().BeCloseTo(before.AddHours(1), TimeSpan.FromSeconds(5));
    }

    // 7.2 — duration="24h"
    [Fact]
    public async Task BanUser_24h_LockoutEndIsNowPlus24Hours()
    {
        var user = await _factory.SeedUserAsync("ban24h@example.com");
        var before = DateTimeOffset.UtcNow;

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/ban",
            new { duration = "24h" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReloadUser(user.Id.ToString());
        updated!.LockoutEnd!.Value.Should().BeCloseTo(before.AddHours(24), TimeSpan.FromSeconds(5));
    }

    // 7.3 — duration="7d"
    [Fact]
    public async Task BanUser_7d_LockoutEndIsNowPlus7Days()
    {
        var user = await _factory.SeedUserAsync("ban7d@example.com");
        var before = DateTimeOffset.UtcNow;

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/ban",
            new { duration = "7d" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReloadUser(user.Id.ToString());
        updated!.LockoutEnd!.Value.Should().BeCloseTo(before.AddDays(7), TimeSpan.FromSeconds(5));
    }

    // 7.4 — duration="30m"
    [Fact]
    public async Task BanUser_30m_LockoutEndIsNowPlus30Minutes()
    {
        var user = await _factory.SeedUserAsync("ban30m@example.com");
        var before = DateTimeOffset.UtcNow;

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/ban",
            new { duration = "30m" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReloadUser(user.Id.ToString());
        updated!.LockoutEnd!.Value.Should().BeCloseTo(before.AddMinutes(30), TimeSpan.FromSeconds(5));
    }

    // 7.5 — duration="24:00:00" — .NET TimeSpan.Parse treats this as d:hh:mm:ss → 24 days
    [Fact]
    public async Task BanUser_TimeSpanFormat_24_00_00_LockoutEndIsNowPlus24Days()
    {
        var user = await _factory.SeedUserAsync("bantimespan@example.com");
        var before = DateTimeOffset.UtcNow;

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/ban",
            new { duration = "24:00:00" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // TimeSpan.Parse("24:00:00") = 24 days in .NET (d:hh:mm:ss format).
        var updated = await ReloadUser(user.Id.ToString());
        updated!.LockoutEnd!.Value.Should().BeCloseTo(before.AddDays(24), TimeSpan.FromSeconds(5));
    }

    // 7.6 — reason stored in audit log metadata
    [Fact]
    public async Task BanUser_ReasonProvided_ReasonInAuditLog()
    {
        var user = await _factory.SeedUserAsync("banreason@example.com");
        await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/ban",
            new { duration = "1h", reason = "spamming" });

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AccountLockout);
        logs.Should().NotBeEmpty();
        logs.First().Metadata.Should().Contain("spamming");
    }

    // 7.7 — non-existent user → 404
    [Fact]
    public async Task BanUser_NonExistentId_Returns404()
    {
        var resp = await _admin.PostJsonAsync($"/api/admin/users/{Guid.NewGuid()}/ban",
            new { duration = "1h" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 7.8 — security stamp rotated
    [Fact]
    public async Task BanUser_SecurityStampRotated()
    {
        var user = await _factory.SeedUserAsync("banstamp@example.com");
        var stampBefore = user.SecurityStamp;

        await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/ban", new { duration = "1h" });

        var updated = await ReloadUser(user.Id.ToString());
        updated!.SecurityStamp.Should().NotBe(stampBefore);
    }

    // 7.9 — audit log written
    [Fact]
    public async Task BanUser_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("banaudit@example.com");
        await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/ban", new { duration = "1h" });

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AccountLockout);
        logs.Should().NotBeEmpty();
    }

    // ── Section 8: Unban ──────────────────────────────────────────────────────

    // 8.1 — unban clears LockoutEnd and AccessFailedCount
    [Fact]
    public async Task UnbanUser_BannedUser_ClearsLockout()
    {
        var user = await _factory.SeedUserAsync("unban@example.com");
        await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/ban", new { duration = "24h" });

        var resp = await _admin.PostAsync($"/api/admin/users/{user.Id}/unban", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReloadUser(user.Id.ToString());
        updated!.LockoutEnd.Should().BeNull();
        updated.AccessFailedCount.Should().Be(0);
    }

    // 8.2 — non-existent user → 404
    [Fact]
    public async Task UnbanUser_NonExistentId_Returns404()
    {
        var resp = await _admin.PostAsync($"/api/admin/users/{Guid.NewGuid()}/unban", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 8.3 — audit log
    [Fact]
    public async Task UnbanUser_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("unbanaudit@example.com");
        await _admin.PostAsync($"/api/admin/users/{user.Id}/unban", null);

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AccountUnlock);
        logs.Should().NotBeEmpty();
    }

    // ── Section 9: Unlock ─────────────────────────────────────────────────────

    // 9.1 — locked-out user becomes unlocked
    [Fact]
    public async Task UnlockUser_LockedUser_ClearsLockout()
    {
        var user = await _factory.SeedUserAsync("unlock@example.com");
        // Manually lock via UserManager (reload in fresh scope to avoid EF tracking conflict).
        using var scope = _factory._GetScope();
        var um = scope.GetService<UserManager<ApplicationUser>>();
        var fresh = await um.FindByIdAsync(user.Id.ToString());
        await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddHours(1));

        var resp = await _admin.PostAsync($"/api/admin/users/{user.Id}/unlock", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReloadUser(user.Id.ToString());
        updated!.LockoutEnd.Should().BeNull();
        updated.AccessFailedCount.Should().Be(0);
    }

    // 9.2 — non-existent user → 404
    [Fact]
    public async Task UnlockUser_NonExistentId_Returns404()
    {
        var resp = await _admin.PostAsync($"/api/admin/users/{Guid.NewGuid()}/unlock", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 9.3 — audit log
    [Fact]
    public async Task UnlockUser_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("unlockaudit@example.com");
        await _admin.PostAsync($"/api/admin/users/{user.Id}/unlock", null);

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AccountUnlock);
        logs.Should().NotBeEmpty();
    }

    // ── Section 10: Verify-email ──────────────────────────────────────────────

    // 10.1 — unconfirmed → confirmed
    [Fact]
    public async Task VerifyEmail_UnconfirmedUser_EmailConfirmed()
    {
        var user = await _factory.SeedUserAsync("verifyemail@example.com", emailConfirmed: false);

        var resp = await _admin.PostAsync($"/api/admin/users/{user.Id}/verify-email", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReloadUser(user.Id.ToString());
        updated!.EmailConfirmed.Should().BeTrue();
        updated.EmailConfirmedAt.Should().NotBeNull();
    }

    // 10.2 — already confirmed → idempotent 200
    [Fact]
    public async Task VerifyEmail_AlreadyConfirmed_IdempotentOk()
    {
        var user = await _factory.SeedUserAsync("alreadyverified@example.com", emailConfirmed: true);

        var resp = await _admin.PostAsync($"/api/admin/users/{user.Id}/verify-email", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReloadUser(user.Id.ToString());
        updated!.EmailConfirmed.Should().BeTrue();
    }

    // 10.3 — non-existent user → 404
    [Fact]
    public async Task VerifyEmail_NonExistentId_Returns404()
    {
        var resp = await _admin.PostAsync($"/api/admin/users/{Guid.NewGuid()}/verify-email", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 10.4 — audit log
    [Fact]
    public async Task VerifyEmail_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("verifyaudit@example.com", emailConfirmed: false);
        await _admin.PostAsync($"/api/admin/users/{user.Id}/verify-email", null);

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.EmailConfirm);
        logs.Should().NotBeEmpty();
    }

    // ── Section 11: Invalidate-sessions ──────────────────────────────────────

    // 11.1 — security stamp rotated
    [Fact]
    public async Task InvalidateSessions_SecurityStampRotated()
    {
        var user = await _factory.SeedUserAsync("invsession@example.com");
        var stampBefore = user.SecurityStamp;

        var resp = await _admin.PostAsync($"/api/admin/users/{user.Id}/invalidate-sessions", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReloadUser(user.Id.ToString());
        updated!.SecurityStamp.Should().NotBe(stampBefore);
    }

    // 11.2 — non-existent user → 404
    [Fact]
    public async Task InvalidateSessions_NonExistentId_Returns404()
    {
        var resp = await _admin.PostAsync($"/api/admin/users/{Guid.NewGuid()}/invalidate-sessions", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 11.3 — audit log
    [Fact]
    public async Task InvalidateSessions_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("invsessaudit@example.com");
        await _admin.PostAsync($"/api/admin/users/{user.Id}/invalidate-sessions", null);

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AdminForceLogout);
        logs.Should().NotBeEmpty();
    }

    // ── Section 12: Enable ────────────────────────────────────────────────────

    // 12.1 — disabled user → IsActive=true
    [Fact]
    public async Task EnableUser_DisabledUser_IsActiveTrue()
    {
        var user = await _factory.SeedUserAsync("enable@example.com", isActive: false);

        var resp = await _admin.PostAsync($"/api/admin/users/{user.Id}/enable", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReloadUser(user.Id.ToString());
        updated!.IsActive.Should().BeTrue();
    }

    // 12.2 — non-existent user → 404
    [Fact]
    public async Task EnableUser_NonExistentId_Returns404()
    {
        var resp = await _admin.PostAsync($"/api/admin/users/{Guid.NewGuid()}/enable", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 12.3 — audit log
    [Fact]
    public async Task EnableUser_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("enableaudit@example.com", isActive: false);
        await _admin.PostAsync($"/api/admin/users/{user.Id}/enable", null);

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AdminUserEnable);
        logs.Should().NotBeEmpty();
    }

    // ── Section 13: Disable ───────────────────────────────────────────────────

    // 13.1 — active user → IsActive=false, security stamp rotated
    [Fact]
    public async Task DisableUser_ActiveUser_IsActiveFalse()
    {
        var user = await _factory.SeedUserAsync("disable@example.com");
        var stampBefore = user.SecurityStamp;

        var resp = await _admin.PostAsync($"/api/admin/users/{user.Id}/disable", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReloadUser(user.Id.ToString());
        updated!.IsActive.Should().BeFalse();
        updated.SecurityStamp.Should().NotBe(stampBefore);
    }

    // 13.2 — non-existent user → 404
    [Fact]
    public async Task DisableUser_NonExistentId_Returns404()
    {
        var resp = await _admin.PostAsync($"/api/admin/users/{Guid.NewGuid()}/disable", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 13.3 — audit log
    [Fact]
    public async Task DisableUser_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("disableaudit@example.com");
        await _admin.PostAsync($"/api/admin/users/{user.Id}/disable", null);

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AdminUserDisable);
        logs.Should().NotBeEmpty();
    }
}
