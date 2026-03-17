using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.Admin.Endpoints;

/// <summary>
/// Admin session management endpoints.
///
/// Routes registered:
///   GET    /api/admin/users/{id}/sessions
///   DELETE /api/admin/users/{id}/sessions          (revoke all or all-but-one)
///   DELETE /api/admin/users/{id}/sessions/{sessionId}
/// </summary>
public static class AdminUserSessionsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
                       .RequireAuthorization("RequireAdmin");

        group.MapGet("/{id}/sessions", GetSessionsAsync)
             .WithName("AdminGetUserSessions")
             .WithSummary("List all active sessions for a user.");

        group.MapDelete("/{id}/sessions", RevokeSessionsAsync)
             .WithName("AdminRevokeSessions")
             .WithSummary(
                 "Revoke sessions for a user. " +
                 "scope=all (default) revokes everything; " +
                 "scope=others keeps the session identified by currentSessionId.");

        group.MapDelete("/{id}/sessions/{sessionId}", RevokeSessionAsync)
             .WithName("AdminRevokeSession")
             .WithSummary("Revoke a specific session by its ID.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetSessionsAsync(
        string id,
        ISessionManager sessionManager,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var userId))
            return Results.BadRequest(new { error = "Invalid user ID format." });

        var sessions = await sessionManager.GetActiveSessionsAsync(userId, ct);
        return Results.Ok(sessions);
    }

    private static async Task<IResult> RevokeSessionsAsync(
        string id,
        ISessionManager sessionManager,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        string scope = "all",
        Guid? currentSessionId = null,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var userId))
            return Results.BadRequest(new { error = "Invalid user ID format." });

        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        // scope=others keeps the identified current session alive.
        Guid? exceptId = scope.Equals("others", StringComparison.OrdinalIgnoreCase)
            ? currentSessionId
            : null;

        await sessionManager.RevokeAllSessionsAsync(userId, exceptId, ct);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.AdminForceLogout,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: userId,
            Metadata: new { adminAction = "revoke_sessions", scope, exceptId }
        ), ct);

        return Results.Ok(new { message = $"Sessions revoked (scope={scope})." });
    }

    private static async Task<IResult> RevokeSessionAsync(
        string id,
        string sessionId,
        ISessionManager sessionManager,
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(sessionId, out var sessionGuid))
            return Results.BadRequest(new { error = "Invalid session ID format." });

        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return Results.NotFound();

        await sessionManager.RevokeSessionAsync(sessionGuid, ct);

        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.SessionRevoke,
            Category: AuditCategories.Admin,
            Success: true,
            UserId: user.Id,
            Metadata: new { adminAction = "revoke_session", sessionId }
        ), ct);

        return Results.Ok(new { message = "Session revoked." });
    }
}
