namespace HPD.Auth.Admin.Models;

/// <summary>
/// Request body for POST /api/admin/users/{id}/ban.
/// </summary>
public record AdminBanUserRequest(
    /// <summary>
    /// Duration string. Supported formats:
    ///   "24h"     — hours
    ///   "7d"      — days
    ///   "30m"     — minutes
    ///   "24:00:00" — standard TimeSpan format (fallback)
    /// </summary>
    string Duration,

    /// <summary>Optional human-readable reason recorded in the audit log.</summary>
    string? Reason = null
);
