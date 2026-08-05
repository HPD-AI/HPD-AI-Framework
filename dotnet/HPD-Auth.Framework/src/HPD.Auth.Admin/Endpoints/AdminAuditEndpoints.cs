using HPD.Auth.Core.Interfaces;
using HPD.Auth.ControlPlane;
using HPD.Auth.Core.Audit;
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
    public static void Map(RouteGroupBuilder root, IEndpointRouteBuilder app)
    {
        root.MapGet("/audit-logs", QueryAuditLogsAsync)
                  .RequireHPDControlPlaneCapability(app, HPDAuthAdminCapabilities.AuditRead)
                  .WithName("AdminQueryAuditLogs")
                  .WithSummary(
                      "Query audit logs with optional filters. " +
                      "Supports filtering by userId, action, category, ipAddress, date range, and pagination.");

        root.MapGet("/users/{id}/audit-logs", GetUserAuditLogsAsync)
                  .RequireHPDControlPlaneCapability(app, HPDAuthAdminCapabilities.AuditRead)
                  .WithName("AdminGetUserAuditLogs")
                  .WithSummary("Retrieve the audit trail for a specific user.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> QueryAuditLogsAsync(
        IAuthAuditReader auditReader,
        IAuthAuditWriter auditWriter,
        IAuthCorrelationContext correlationContext,
        Guid? userId = null,
        string? action = null,
        string? category = null,
        string? correlationId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int page = 1,
        int per_page = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        per_page = Math.Clamp(per_page, 1, 200);

        var query = new AuthAuditQuery
        {
            SubjectUserId = userId,
            Action = action,
            Category = category,
            CorrelationId = correlationId,
            From = from,
            To = to,
            Offset = (page - 1) * per_page,
            Limit = per_page
        };

        var logs = await auditReader.ReadAsync(query, ct);
        await AdminAuditMapper.WriteAsync(auditWriter, AdminAuditOperation.AuditList, correlationContext, cancellationToken: ct);

        return Results.Ok(new
        {
            logs,
            page,
            perPage = per_page,
            count = logs.Length
        });
    }

    private static async Task<IResult> GetUserAuditLogsAsync(
        string id,
        IAuthAuditReader auditReader,
        IAuthAuditWriter auditWriter,
        IAuthCorrelationContext correlationContext,
        string? action = null,
        string? category = null,
        string? correlationId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int page = 1,
        int per_page = 50,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var userId))
            return Results.BadRequest(new { error = "Invalid user ID format." });

        page = Math.Max(1, page);
        per_page = Math.Clamp(per_page, 1, 200);

        var query = new AuthAuditQuery
        {
            SubjectUserId = userId,
            Action = action,
            Category = category,
            CorrelationId = correlationId,
            From = from,
            To = to,
            Offset = (page - 1) * per_page,
            Limit = per_page
        };

        var logs = await auditReader.ReadAsync(query, ct);
        await AdminAuditMapper.WriteAsync(auditWriter, AdminAuditOperation.AuditUserList, correlationContext, userId, cancellationToken: ct);

        return Results.Ok(new
        {
            logs,
            page,
            perPage = per_page,
            count = logs.Length
        });
    }
}
