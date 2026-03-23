using HPD.Auth.Core.Entities;

namespace HPD.Auth.Core.Interfaces;

/// <summary>
/// Manages user sessions for device tracking, security auditing,
/// and "sign out all devices" functionality.
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Create a new session for a user after successful authentication.
    /// </summary>
    Task<UserSession> CreateSessionAsync(
        Guid userId,
        SessionContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieve all active (non-revoked, non-expired) sessions for a user.
    /// </summary>
    Task<IReadOnlyList<UserSession>> GetActiveSessionsAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Revoke a specific session by its ID.
    /// </summary>
    Task RevokeSessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Revoke all sessions for a user, optionally keeping one session active
    /// (e.g., the current session when the user changes their password).
    /// </summary>
    Task RevokeAllSessionsAsync(
        Guid userId,
        Guid? exceptSessionId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Context data captured at session creation time.
/// </summary>
public record SessionContext(
    string? IpAddress,
    string? UserAgent,
    string AAL = "aal1",
    TimeSpan? Lifetime = null
);
