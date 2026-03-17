using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.Admin.Endpoints;

/// <summary>
/// Admin two-factor authentication management endpoints.
///
/// Routes registered:
///   GET    /api/admin/users/{id}/2fa
///   POST   /api/admin/users/{id}/2fa/disable
///   DELETE /api/admin/users/{id}/2fa/authenticator
///   POST   /api/admin/users/{id}/2fa/recovery-codes
/// </summary>
public static class AdminUser2faEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
                       .RequireAuthorization("RequireAdmin");

        group.MapGet("/{id}/2fa", Get2faStatusAsync)
             .WithName("AdminGet2faStatus")
             .WithSummary("Get the 2FA status for a user, including authenticator presence and recovery code count.");

        group.MapPost("/{id}/2fa/disable", Disable2faAsync)
             .WithName("AdminDisable2fa")
             .WithSummary("Disable two-factor authentication for a user.");

        group.MapDelete("/{id}/2fa/authenticator", ResetAuthenticatorAsync)
             .WithName("AdminResetAuthenticator")
             .WithSummary("Reset the TOTP authenticator key, forcing re-enrollment.");

        group.MapPost("/{id}/2fa/recovery-codes", GenerateRecoveryCodesAsync)
             .WithName("AdminGenerateRecoveryCodes")
             .WithSummary("Generate new recovery codes for a user. Shown only once — treat as secrets.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> Get2faStatusAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var enabled = await userManager.GetTwoFactorEnabledAsync(user);
        var recoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user);
        var authenticatorKey = await userManager.GetAuthenticatorKeyAsync(user);
        var hasAuthenticator = !string.IsNullOrEmpty(authenticatorKey);

        return Results.Ok(new
        {
            enabled,
            recoveryCodesLeft,
            hasAuthenticator
        });
    }

    private static async Task<IResult> Disable2faAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var result = await userManager.SetTwoFactorEnabledAsync(user, false);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.TwoFactorDisable,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "disable_2fa" }
        ), ct);

        return Results.Ok(new { message = "Two-factor authentication disabled." });
    }

    private static async Task<IResult> ResetAuthenticatorAsync(
        string id,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        var result = await userManager.ResetAuthenticatorKeyAsync(user);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.TwoFactorSetup,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "reset_authenticator" }
        ), ct);

        return Results.Ok(new { message = "Authenticator key reset. User must re-enroll their authenticator app." });
    }

    private static async Task<IResult> GenerateRecoveryCodesAsync(
        string id,
        GenerateRecoveryCodesRequest? request,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        int count = request?.Count ?? 10;
        count = Math.Clamp(count, 1, 20);

        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, count);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.RecoveryCodeRegenerate,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "generate_recovery_codes", count }
        ), ct);

        // Recovery codes are shown only once — the caller must distribute them securely.
        return Results.Ok(new { codes, message = "Recovery codes generated. These are shown only once." });
    }
}

/// <summary>
/// Optional body for POST /api/admin/users/{id}/2fa/recovery-codes.
/// If omitted, 10 codes are generated.
/// </summary>
internal record GenerateRecoveryCodesRequest(int Count = 10);
