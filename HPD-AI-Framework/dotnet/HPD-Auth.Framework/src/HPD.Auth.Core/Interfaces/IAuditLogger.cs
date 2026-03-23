using HPD.Auth.Core.Entities;

namespace HPD.Auth.Core.Interfaces;

/// <summary>
/// Service for writing and querying immutable audit log entries.
/// Implementations must never throw exceptions — audit failures must be swallowed
/// and logged internally so they never break the primary auth flow.
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Write a single audit log entry.
    /// </summary>
    Task LogAsync(AuditLogEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Query audit logs matching the provided criteria.
    /// </summary>
    Task<IReadOnlyList<AuditLog>> QueryAsync(AuditLogQuery query, CancellationToken ct = default);
}

/// <summary>
/// Input record for creating a new audit log entry.
/// </summary>
public record AuditLogEntry(
    string Action,
    string Category,
    bool Success = true,
    Guid? UserId = null,
    string? IpAddress = null,
    string? UserAgent = null,
    string? ErrorMessage = null,
    object? Metadata = null
);

/// <summary>
/// Query parameters for filtering audit log entries.
/// </summary>
public record AuditLogQuery(
    Guid? UserId = null,
    string? Action = null,
    string? Category = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 50
);
