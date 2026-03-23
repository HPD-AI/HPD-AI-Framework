using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.Admin.Endpoints;

/// <summary>
/// Admin audit log query endpoints.
///
/// Routes registered:
///   GET /api/admin/audit-logs
///   GET /api/admin/users/{id}/audit-logs
/// </summary>
public static class AdminAuditEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var adminGroup = app.MapGroup("/api/admin")
                            .RequireAuthorization("RequireAdmin");

        adminGroup.MapGet("/audit-logs", QueryAuditLogsAsync)
                  .WithName("AdminQueryAuditLogs")
                  .WithSummary(
                      "Query audit logs with optional filters. " +
                      "Supports filtering by userId, action, category, ipAddress, date range, and pagination.");

        adminGroup.MapGet("/users/{id}/audit-logs", GetUserAuditLogsAsync)
                  .WithName("AdminGetUserAuditLogs")
                  .WithSummary("Retrieve the audit trail for a specific user.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> QueryAuditLogsAsync(
        IAuditLogger auditLogger,
        Guid? userId = null,
        string? action = null,
        string? category = null,
        string? ipAddress = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int per_page = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        per_page = Math.Clamp(per_page, 1, 500);

        var query = new AuditLogQuery(
            UserId: userId,
            Action: action,
            Category: category,
            From: from,
            To: to,
            Page: page,
            PageSize: per_page
        );

        var logs = await auditLogger.QueryAsync(query, ct);

        // Note: ipAddress filtering is not supported by the AuditLogQuery record
        // as defined in IAuditLogger. Apply in-memory if provided.
        if (!string.IsNullOrWhiteSpace(ipAddress))
            logs = logs.Where(l => l.IpAddress == ipAddress).ToList().AsReadOnly();

        return Results.Ok(new
        {
            logs,
            page,
            perPage = per_page,
            count = logs.Count
        });
    }

    private static async Task<IResult> GetUserAuditLogsAsync(
        string id,
        IAuditLogger auditLogger,
        string? action = null,
        string? category = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int per_page = 50,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var userId))
            return Results.BadRequest(new { error = "Invalid user ID format." });

        page = Math.Max(1, page);
        per_page = Math.Clamp(per_page, 1, 500);

        var query = new AuditLogQuery(
            UserId: userId,
            Action: action,
            Category: category,
            From: from,
            To: to,
            Page: page,
            PageSize: per_page
        );

        var logs = await auditLogger.QueryAsync(query, ct);

        return Results.Ok(new
        {
            logs,
            page,
            perPage = per_page,
            count = logs.Count
        });
    }
}
