using HPD.Events;

namespace HPD.Auth.Core.Events;

/// <summary>
/// Raised when a specific user session is revoked.
/// Subscribers can use this event to notify connected WebSocket clients,
/// clear server-side session caches, or update device dashboards.
/// </summary>
public record SessionRevokedEvent : AuthEvent
{
    public required Guid UserId { get; init; }

    /// <summary>
    /// The ID of the session that was revoked.
    /// </summary>
    public required Guid SessionId { get; init; }

    /// <summary>
    /// Who or what triggered the revocation.
    /// Values: "user" (user signed out), "admin" (admin forced logout), "system" (security stamp invalidation)
    /// </summary>
    public required string RevokedBy { get; init; }

    public override EventPriority Priority => EventPriority.Control;
}
