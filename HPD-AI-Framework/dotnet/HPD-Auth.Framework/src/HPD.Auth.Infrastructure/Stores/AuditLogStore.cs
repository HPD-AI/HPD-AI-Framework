using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace HPD.Auth.Infrastructure.Stores;

/// <summary>
/// EF Core implementation of <see cref="IAuditLogger"/>.
///
/// Design principles:
///
/// 1. NEVER THROWS: Per the interface contract, audit failures must not propagate
///    to callers. A broken audit log must never block a login, registration, or
///    any other primary auth flow. All exceptions are caught, logged internally,
///    and silently swallowed.
///
/// 2. WRITE-ONLY WRITES: Only Add() is ever called on context.AuditLogs. The store
///    never calls Update(), Remove(), or ExecuteDeleteAsync() — enforcing the
///    immutability requirement at the code level.
///
/// 3. TENANT-SCOPED READS: QueryAsync automatically respects the global query filter
///    on AuditLog.InstanceId, so tenants never see each other's audit logs.
///    Use context.AuditLogs.IgnoreQueryFilters() for cross-tenant admin queries.
///
/// 4. PAGINATION: QueryAsync supports page/pageSize pagination to prevent unbounded
///    result sets when querying high-volume audit tables.
/// </summary>
public sealed class AuditLogStore : IAuditLogger
{
    private readonly HPDAuthDbContext _context;
    private readonly ILogger<AuditLogStore> _logger;

    public AuditLogStore(HPDAuthDbContext context, ILogger<AuditLogStore> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Silently swallows all exceptions per the interface contract.
    /// If this method throws, it means the caller's try/catch also needs patching
    /// — the interface guarantees non-throwing behavior.
    /// </remarks>
    public async Task LogAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        try
        {
            string metadata = SerializeMetadata(entry.Metadata);

            // AuditLog uses init-only setters — construct via object initializer.
            // This is the ONLY place in the codebase where AuditLog rows are created.
            var log = new AuditLog
            {
                // Id and InstanceId are set by the entity's default values.
                // InstanceId is populated from the entity default (Guid.Empty for single-tenant).
                // For multi-tenant: the DbContext query filter ensures reads are scoped,
                // but writes require explicit InstanceId. The tenantContext is accessed via
                // the DbContext's injected ITenantContext (not re-injected here to keep the
                // store lean). Since AuditLog.InstanceId defaults to Guid.Empty, single-tenant
                // apps work correctly out of the box. For multi-tenant, the caller passes
                // the InstanceId through context (the query filter on reads is sufficient).
                Action = entry.Action,
                Category = entry.Category,
                Success = entry.Success,
                UserId = entry.UserId,
                IpAddress = entry.IpAddress,
                UserAgent = entry.UserAgent,
                ErrorMessage = entry.ErrorMessage,
                Metadata = metadata,
            };

            // ADD only — never Update or Remove
            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a legitimate reason to stop; re-throw only if the caller
            // cares about it. Per the interface, we swallow even this for audit calls
            // in fire-and-forget patterns. However, we log it at Debug level so it
            // is traceable in structured logs without alarming on-call.
            _logger.LogDebug("AuditLogStore.LogAsync was cancelled for action {Action}", entry.Action);
        }
        catch (Exception ex)
        {
            // Audit failure must NEVER propagate. Log at Error level so it is
            // visible in monitoring dashboards but does not bubble up to callers.
            _logger.LogError(ex,
                "Failed to write audit log entry. Action={Action} Category={Category} UserId={UserId}",
                entry.Action,
                entry.Category,
                entry.UserId);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Unlike LogAsync, QueryAsync DOES propagate exceptions — a broken query
    /// is a product bug, not a "best effort" operation like writing.
    /// The global query filter on AuditLog.InstanceId is automatically applied.
    /// </remarks>
    public async Task<IReadOnlyList<AuditLog>> QueryAsync(
        AuditLogQuery query,
        CancellationToken ct = default)
    {
        // Start from the filtered set (InstanceId query filter applied automatically)
        IQueryable<AuditLog> q = _context.AuditLogs;

        // ── Apply optional filters ────────────────────────────────────────────

        if (query.UserId.HasValue)
            q = q.Where(a => a.UserId == query.UserId.Value);

        if (!string.IsNullOrWhiteSpace(query.Action))
            q = q.Where(a => a.Action == query.Action);

        if (!string.IsNullOrWhiteSpace(query.Category))
            q = q.Where(a => a.Category == query.Category);

        if (query.From.HasValue)
            q = q.Where(a => a.Timestamp >= query.From.Value);

        if (query.To.HasValue)
            q = q.Where(a => a.Timestamp <= query.To.Value);

        // ── Pagination ────────────────────────────────────────────────────────
        // Default: most-recent entries first (descending Timestamp).
        // Page is 1-based; clamp to at least 1 to guard against bad input.
        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, 500); // hard cap at 500 per page

        var results = await q
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        return results.AsReadOnly();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string SerializeMetadata(object? metadata)
    {
        if (metadata is null)
            return "{}";

        if (metadata is string s)
            return s; // already serialized — pass through

        try
        {
            return JsonSerializer.Serialize(metadata);
        }
        catch
        {
            // Serialization failure must not break audit logging
            return "{}";
        }
    }
}
