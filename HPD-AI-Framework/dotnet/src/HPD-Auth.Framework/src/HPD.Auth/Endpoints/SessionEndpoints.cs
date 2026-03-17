using System.Security.Claims;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.Endpoints;

/// <summary>
/// Session management endpoints — list and revoke active sessions.
///
/// Routes registered:
///   GET    /api/auth/sessions        — list current user's active sessions
///   DELETE /api/auth/sessions/{id}   — revoke a specific session (own only)
///   DELETE /api/auth/sessions        — revoke all other sessions
/// </summary>
public static class SessionEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/sessions")
                       .RequireAuthorization();

        group.MapGet("/", ListSessionsAsync)
             .WithName("AuthListSessions")
             .WithSummary("List the current user's active sessions.");

        group.MapDelete("/{id}", RevokeSessionAsync)
             .WithName("AuthRevokeSession")
             .WithSummary("Revoke a specific session by ID. Only the owning user may revoke their own sessions.");

        group.MapDelete("/", RevokeOtherSessionsAsync)
             .WithName("AuthRevokeOtherSessions")
             .WithSummary("Revoke all sessions except the current one.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/auth/sessions
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> ListSessionsAsync(
        ClaimsPrincipal principal,
        ISessionManager sessionManager,
        IAuditLogger auditLogger,
        CancellationToken ct = default)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return Results.Unauthorized();

        var sessions = await sessionManager.GetActiveSessionsAsync(userId.Value, ct);

        return Results.Ok(sessions.Select(s => new SessionResponse(
            Id: s.Id,
            UserId: s.UserId,
            IpAddress: s.IpAddress,
            UserAgent: s.UserAgent,
            DeviceInfo: s.DeviceInfo,
            AAL: s.AAL,
            CreatedAt: s.CreatedAt,
            LastActiveAt: s.LastActiveAt,
            ExpiresAt: s.ExpiresAt
        )));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/auth/sessions/{id}
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> RevokeSessionAsync(
        string id,
        ClaimsPrincipal principal,
        ISessionManager sessionManager,
        IAuditLogger auditLogger,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return Results.Unauthorized();

        if (!Guid.TryParse(id, out var sessionId))
            return Results.StatusCode(403); // Consistent with not-found — prevents id enumeration

        // Verify the session belongs to the current user.
        var sessions = await sessionManager.GetActiveSessionsAsync(userId.Value, ct);
        var session = sessions.FirstOrDefault(s => s.Id == sessionId);

        if (session is null)
        {
            // Either not found or belongs to a different user — return 403 to prevent
            // leaking session existence of other users.
            return Results.StatusCode(403);
        }

        await sessionManager.RevokeSessionAsync(sessionId, ct);

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.SessionRevoke,
            Category: AuditCategories.Authentication,
            Success: true,
            UserId: userId.Value,
            IpAddress: ipAddress,
            Metadata: new { session_id = sessionId }
        ), ct);

        return Results.NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/auth/sessions
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> RevokeOtherSessionsAsync(
        ClaimsPrincipal principal,
        ISessionManager sessionManager,
        IAuditLogger auditLogger,
        HttpContext httpContext,
        CancellationToken ct = default)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return Results.Unauthorized();

        // Extract the current session ID from the claims if present, so we can
        // keep the current session alive when scope=others.
        var currentSessionIdClaim = principal.FindFirstValue("session_id");
        Guid? currentSessionId = currentSessionIdClaim is not null && Guid.TryParse(currentSessionIdClaim, out var sid)
            ? sid
            : null;

        await sessionManager.RevokeAllSessionsAsync(userId.Value, currentSessionId, ct);

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        await auditLogger.LogAsync(new AuditLogEntry(
            Action: AuditActions.SessionRevokeAll,
            Category: AuditCategories.Authentication,
            Success: true,
            UserId: userId.Value,
            IpAddress: ipAddress,
            Metadata: new { scope = "others", kept_session = currentSessionId }
        ), ct);

        return Results.NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? principal.FindFirstValue("sub");

        return raw is not null && Guid.TryParse(raw, out var id) ? id : null;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Session list item returned by GET /api/auth/sessions.</summary>
public record SessionResponse(
    Guid Id,
    Guid UserId,
    string? IpAddress,
    string? UserAgent,
    string? DeviceInfo,
    string AAL,
    DateTime CreatedAt,
    DateTime LastActiveAt,
    DateTime ExpiresAt
);

/// <summary>DELETE /api/auth/sessions request body.</summary>
public record RevokeSessionsRequest(string? Scope = "others");
