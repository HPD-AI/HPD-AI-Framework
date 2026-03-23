using HPD.Auth.Admin.Models;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.Admin.Endpoints;

/// <summary>
/// Admin action endpoints — ban, unlock, verify-email, invalidate-sessions, enable/disable.
///
/// Routes registered:
///   POST /api/admin/users/{id}/ban
///   POST /api/admin/users/{id}/unban
///   POST /api/admin/users/{id}/unlock
///   POST /api/admin/users/{id}/verify-email
///   POST /api/admin/users/{id}/invalidate-sessions
///   POST /api/admin/users/{id}/enable
///   POST /api/admin/users/{id}/disable
/// </summary>
public static class AdminUserActionsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
                       .RequireAuthorization("RequireAdmin");

        group.MapPost("/{id}/ban", BanUserAsync)
             .WithName("AdminBanUser")
             .WithSummary("Temporarily ban a user by setting a lockout expiry.");

        group.MapPost("/{id}/unban", UnbanUserAsync)
             .WithName("AdminUnbanUser")
             .WithSummary("Remove a ban by clearing the lockout end date and resetting the failed count.");

        group.MapPost("/{id}/unlock", UnlockUserAsync)
             .WithName("AdminUnlockUser")
             .WithSummary("Unlock a user who was locked out due to failed login attempts.");

        group.MapPost("/{id}/verify-email", VerifyEmailAsync)
             .WithName("AdminVerifyEmail")
             .WithSummary("Admin-confirm a user's email without requiring them to click a link.");

        group.MapPost("/{id}/invalidate-sessions", InvalidateSessionsAsync)
             .WithName("AdminInvalidateSessions")
             .WithSummary("Rotate the security stamp and revoke all active sessions, forcing re-login.");

        group.MapPost("/{id}/enable", EnableUserAsync)
             .WithName("AdminEnableUser")
             .WithSummary("Re-activate a disabled user account.");

        group.MapPost("/{id}/disable", DisableUserAsync)
             .WithName("AdminDisableUser")
             .WithSummary("Disable a user account and force logout on all devices.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> BanUserAsync(
        string id,
        AdminBanUserRequest request,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        TimeSpan duration;
        try
        {
            duration = ParseBanDuration(request.Duration);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Invalid duration format: {ex.Message}" });
        }

        var lockoutEnd = DateTimeOffset.UtcNow.Add(duration);

        var lockResult = await userManager.SetLockoutEndDateAsync(user, lockoutEnd);
        if (!lockResult.Succeeded)
            return Results.BadRequest(lockResult.Errors);

        // Force re-login on all devices.
        await userManager.UpdateSecurityStampAsync(user);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AccountLockout,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new
            {
                adminAction = "ban",
                duration = request.Duration,
                reason = request.Reason,
                lockoutEnd = lockoutEnd
            }
        ), ct);

        return Results.Ok(new { message = $"User banned until {lockoutEnd:O}" });
    }

    private static async Task<IResult> UnbanUserAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var lockResult = await userManager.SetLockoutEndDateAsync(user, null);
        if (!lockResult.Succeeded)
            return Results.BadRequest(lockResult.Errors);

        await userManager.ResetAccessFailedCountAsync(user);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AccountUnlock,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "unban" }
        ), ct);

        return Results.Ok(new { message = "User unbanned." });
    }

    private static async Task<IResult> UnlockUserAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var lockResult = await userManager.SetLockoutEndDateAsync(user, null);
        if (!lockResult.Succeeded)
            return Results.BadRequest(lockResult.Errors);

        await userManager.ResetAccessFailedCountAsync(user);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AccountUnlock,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "unlock" }
        ), ct);

        return Results.Ok(new { message = "User unlocked." });
    }

    private static async Task<IResult> VerifyEmailAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var result = await userManager.ConfirmEmailAsync(user, token);

        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        // Keep EmailConfirmedAt in sync with the -compatible field.
        user.EmailConfirmedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.EmailConfirm,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "verify_email" }
        ), ct);

        return Results.Ok(new { message = "Email verified." });
    }

    private static async Task<IResult> InvalidateSessionsAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        ISessionManager sessionManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        // Rotate security stamp — JWT validators will reject tokens issued before this.
        var stampResult = await userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
            return Results.BadRequest(stampResult.Errors);

        // Revoke all tracked sessions in the session store.
        await sessionManager.RevokeAllSessionsAsync(user.Id, exceptSessionId: null, ct);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AdminForceLogout,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "invalidate_sessions" }
        ), ct);

        return Results.Ok(new { message = "All sessions invalidated." });
    }

    private static async Task<IResult> EnableUserAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        user.IsActive = true;
        user.Updated = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AdminUserEnable,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "enable_user" }
        ), ct);

        return Results.Ok(new { message = "User enabled." });
    }

    private static async Task<IResult> DisableUserAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        user.IsActive = false;
        user.Updated = DateTime.UtcNow;

        // Rotate security stamp to force logout on all active sessions.
        await userManager.UpdateSecurityStampAsync(user);

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AdminUserDisable,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "disable_user" }
        ), ct);

        return Results.Ok(new { message = "User disabled." });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ParseBanDuration helper (internal — also used by AdminUserActionsEndpoints)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a ban duration string into a <see cref="TimeSpan"/>.
    /// Supported formats: "24h" (hours), "7d" (days), "30m" (minutes),
    /// or standard TimeSpan format e.g. "24:00:00".
    /// Throws <see cref="FormatException"/> for unrecognized input.
    /// </summary>
    internal static TimeSpan ParseBanDuration(string duration)
    {
        if (duration.EndsWith('h') && int.TryParse(duration[..^1], out var hours))
            return TimeSpan.FromHours(hours);
        if (duration.EndsWith('d') && int.TryParse(duration[..^1], out var days))
            return TimeSpan.FromDays(days);
        if (duration.EndsWith('m') && int.TryParse(duration[..^1], out var minutes))
            return TimeSpan.FromMinutes(minutes);
        return TimeSpan.Parse(duration);
    }
}
