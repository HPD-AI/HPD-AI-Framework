using FluentAssertions;
using HPD.Auth.Admin.Models;
using HPD.Auth.Admin.Tests.Helpers;
using HPD.Auth.Core.Entities;
using Microsoft.AspNetCore.Identity;
using System.Net;
using Xunit;

namespace HPD.Auth.Admin.Tests;

/// <summary>
/// Tests for:
///   POST   /api/admin/users/{id}/reset-password  (section 14)
///   DELETE /api/admin/users/{id}/password         (section 15)
///   POST   /api/admin/users/{id}/password         (section 16)
/// </summary>
public class AdminPasswordTests : IAsyncLifetime
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

    // ── Section 14: Reset Password ────────────────────────────────────────────

    // 14.1 — valid new password → 200
    [Fact]
    public async Task ResetPassword_ValidPassword_Returns200()
    {
        var user = await _factory.SeedUserAsync("resetpw@example.com", password: "OldPass1!");

        var resp = await _admin.PostJsonAsync(
            $"/api/admin/users/{user.Id}/reset-password",
            new AdminResetPasswordRequest("NewPass1!"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 14.2 — no current password required (token generated internally)
    [Fact]
    public async Task ResetPassword_NoCurrentPasswordRequired_Succeeds()
    {
        // Create user without a password (OAuth-only).
        var user = await _factory.SeedUserAsync("nopwreset@example.com");

        // Should work even though user has no current password.
        var resp = await _admin.PostJsonAsync(
            $"/api/admin/users/{user.Id}/reset-password",
            new AdminResetPasswordRequest("NewPass1!"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 14.3 — temporary=true adds "UPDATE_PASSWORD" to RequiredActions
    [Fact]
    public async Task ResetPassword_TemporaryTrue_AddsUpdatePasswordAction()
    {
        var user = await _factory.SeedUserAsync("temppw@example.com", password: "OldPass1!");

        await _admin.PostJsonAsync(
            $"/api/admin/users/{user.Id}/reset-password",
            new AdminResetPasswordRequest("NewPass1!", Temporary: true));

        var updated = await ReloadUser(user.Id.ToString());
        updated!.RequiredActions.Should().Contain("UPDATE_PASSWORD");
    }

    // 14.4 — temporary=false → RequiredActions unchanged
    [Fact]
    public async Task ResetPassword_TemporaryFalse_RequiredActionsUnchanged()
    {
        var user = await _factory.SeedUserAsync("nottemppw@example.com", password: "OldPass1!");

        await _admin.PostJsonAsync(
            $"/api/admin/users/{user.Id}/reset-password",
            new AdminResetPasswordRequest("NewPass1!", Temporary: false));

        var updated = await ReloadUser(user.Id.ToString());
        updated!.RequiredActions.Should().NotContain("UPDATE_PASSWORD");
    }

    // 14.5 — temporary reset on user who already has "UPDATE_PASSWORD" → not duplicated
    [Fact]
    public async Task ResetPassword_AlreadyHasUpdatePasswordAction_NotDuplicated()
    {
        var user = await _factory.SeedUserAsync("duplacton@example.com", password: "OldPass1!");

        // Reset once as temporary.
        await _admin.PostJsonAsync(
            $"/api/admin/users/{user.Id}/reset-password",
            new AdminResetPasswordRequest("NewPass1!", Temporary: true));

        // Reset again as temporary.
        await _admin.PostJsonAsync(
            $"/api/admin/users/{user.Id}/reset-password",
            new AdminResetPasswordRequest("AnotherPass1!", Temporary: true));

        var updated = await ReloadUser(user.Id.ToString());
        updated!.RequiredActions.Count(a => a == "UPDATE_PASSWORD").Should().Be(1);
    }

    // 14.6 — security stamp rotated
    [Fact]
    public async Task ResetPassword_SecurityStampRotated()
    {
        var user = await _factory.SeedUserAsync("stamppw@example.com", password: "OldPass1!");
        var stampBefore = user.SecurityStamp;

        await _admin.PostJsonAsync(
            $"/api/admin/users/{user.Id}/reset-password",
            new AdminResetPasswordRequest("NewPass1!"));

        var updated = await ReloadUser(user.Id.ToString());
        updated!.SecurityStamp.Should().NotBe(stampBefore);
    }

    // 14.7 — weak password → 400
    [Fact]
    public async Task ResetPassword_WeakPassword_Returns400()
    {
        var user = await _factory.SeedUserAsync("weakpwreset@example.com");

        var resp = await _admin.PostJsonAsync(
            $"/api/admin/users/{user.Id}/reset-password",
            new AdminResetPasswordRequest("x"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 14.8 — non-existent user → 404
    [Fact]
    public async Task ResetPassword_NonExistentId_Returns404()
    {
        var resp = await _admin.PostJsonAsync(
            $"/api/admin/users/{Guid.NewGuid()}/reset-password",
            new AdminResetPasswordRequest("NewPass1!"));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 14.9 — audit log written
    [Fact]
    public async Task ResetPassword_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("auditpwreset@example.com");

        await _admin.PostJsonAsync(
            $"/api/admin/users/{user.Id}/reset-password",
            new AdminResetPasswordRequest("NewPass1!"));

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.AdminPasswordReset);
        logs.Should().NotBeEmpty();
    }

    // ── Section 15: Remove Password ───────────────────────────────────────────

    // 15.1 — user with password → password removed
    [Fact]
    public async Task RemovePassword_UserWithPassword_PasswordRemoved()
    {
        var user = await _factory.SeedUserAsync("removepw@example.com", password: "Password1!");

        var resp = await _admin.DeleteAsync($"/api/admin/users/{user.Id}/password");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // The user should no longer have a password hash.
        var updated = await ReloadUser(user.Id.ToString());
        updated!.PasswordHash.Should().BeNullOrEmpty();
    }

    // 15.2 — user without password — ASP.NET Identity RemovePasswordAsync is a no-op (returns Success).
    [Fact]
    public async Task RemovePassword_UserWithoutPassword_Returns200NoOp()
    {
        var user = await _factory.SeedUserAsync("nopwremove@example.com");

        // Identity RemovePasswordAsync succeeds even when the user has no password.
        var resp = await _admin.DeleteAsync($"/api/admin/users/{user.Id}/password");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // 15.3 — non-existent user → 404
    [Fact]
    public async Task RemovePassword_NonExistentId_Returns404()
    {
        var resp = await _admin.DeleteAsync($"/api/admin/users/{Guid.NewGuid()}/password");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 15.4 — audit log
    [Fact]
    public async Task RemovePassword_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("auditremovepw@example.com", password: "Password1!");
        await _admin.DeleteAsync($"/api/admin/users/{user.Id}/password");

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.PasswordChange);
        logs.Should().NotBeEmpty();
        logs.First().Metadata.Should().Contain("remove_password");
    }

    // ── Section 16: Add Password ──────────────────────────────────────────────

    // 16.1 — OAuth-only user (no password) → password added
    [Fact]
    public async Task AddPassword_OAuthOnlyUser_PasswordAdded()
    {
        var user = await _factory.SeedUserAsync("addpw@example.com");

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/password",
            new { password = "NewPass1!" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await ReloadUser(user.Id.ToString());
        updated!.PasswordHash.Should().NotBeNullOrEmpty();
    }

    // 16.2 — user who already has a password → 400
    [Fact]
    public async Task AddPassword_UserAlreadyHasPassword_Returns400()
    {
        var user = await _factory.SeedUserAsync("alreadypw@example.com", password: "Existing1!");

        var resp = await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/password",
            new { password = "AnotherPass1!" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 16.3 — non-existent user → 404
    [Fact]
    public async Task AddPassword_NonExistentId_Returns404()
    {
        var resp = await _admin.PostJsonAsync($"/api/admin/users/{Guid.NewGuid()}/password",
            new { password = "SomePass1!" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // 16.4 — audit log
    [Fact]
    public async Task AddPassword_AuditLogWritten()
    {
        var user = await _factory.SeedUserAsync("auditaddpw@example.com");
        await _admin.PostJsonAsync($"/api/admin/users/{user.Id}/password",
            new { password = "NewPass1!" });

        var logs = await _factory.GetAuditLogsAsync(userId: user.Id, action: AuditActions.PasswordChange);
        logs.Should().NotBeEmpty();
        logs.First().Metadata.Should().Contain("add_password");
    }
}
