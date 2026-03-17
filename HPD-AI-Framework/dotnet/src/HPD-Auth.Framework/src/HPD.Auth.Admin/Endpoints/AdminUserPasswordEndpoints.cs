using HPD.Auth.Admin.Models;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.Admin.Endpoints;

/// <summary>
/// Admin password management endpoints.
///
/// Routes registered:
///   POST   /api/admin/users/{id}/reset-password   (no current password required)
///   DELETE /api/admin/users/{id}/password          (remove password for OAuth-only users)
///   POST   /api/admin/users/{id}/password          (add password to OAuth-only user)
/// </summary>
public static class AdminUserPasswordEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
                       .RequireAuthorization("RequireAdmin");

        group.MapPost("/{id}/reset-password", ResetPasswordAsync)
             .WithName("AdminResetPassword")
             .WithSummary("Force-reset a user's password without requiring the current password. " +
                          "Optionally mark the new password as temporary to require change on next login.");

        group.MapDelete("/{id}/password", RemovePasswordAsync)
             .WithName("AdminRemovePassword")
             .WithSummary("Remove a user's local password, converting them to an OAuth-only account.");

        group.MapPost("/{id}/password", AddPasswordAsync)
             .WithName("AdminAddPassword")
             .WithSummary("Add a local password to an OAuth-only user account.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> ResetPasswordAsync(
        string id,
        AdminResetPasswordRequest request,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        // Generate a password-reset token on behalf of the admin.
        // No current password is required — admin authority is sufficient.
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, request.Password);

        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        // If temporary, require the user to change their password on next login.
        if (request.Temporary)
        {
            if (!user.RequiredActions.Contains("UPDATE_PASSWORD"))
                user.RequiredActions.Add("UPDATE_PASSWORD");

            await userManager.UpdateAsync(user);
        }

        // Rotate security stamp to force re-login with the new password.
        await userManager.UpdateSecurityStampAsync(user);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AdminPasswordReset,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "reset_password", temporary = request.Temporary }
        ), ct);

        return Results.Ok(new { message = "Password reset successful." });
    }

    private static async Task<IResult> RemovePasswordAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var result = await userManager.RemovePasswordAsync(user);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.PasswordChange,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "remove_password" }
        ), ct);

        return Results.Ok(new { message = "Password removed." });
    }

    private static async Task<IResult> AddPasswordAsync(
        string id,
        AddPasswordRequest request,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var result = await userManager.AddPasswordAsync(user, request.Password);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.PasswordChange,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "add_password" }
        ), ct);

        return Results.Ok(new { message = "Password added." });
    }
}

/// <summary>
/// Request body for POST /api/admin/users/{id}/password (add password).
/// </summary>
internal record AddPasswordRequest(string Password);
