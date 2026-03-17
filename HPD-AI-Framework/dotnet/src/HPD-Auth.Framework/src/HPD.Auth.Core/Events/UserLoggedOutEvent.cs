using HPD.Events;

namespace HPD.Auth.Core.Events;

/// <summary>
/// Raised when a user explicitly signs out.
/// Subscribers can use this event to clean up per-session state
/// or notify connected clients via SignalR/webhooks.
/// </summary>
public record UserLoggedOutEvent : AuthEvent
{
    public required Guid UserId { get; init; }

    /// <summary>
    /// The ID of the session that was terminated.
    /// </summary>
    public required Guid SessionId { get; init; }

    public override EventPriority Priority => EventPriority.Normal;
}
