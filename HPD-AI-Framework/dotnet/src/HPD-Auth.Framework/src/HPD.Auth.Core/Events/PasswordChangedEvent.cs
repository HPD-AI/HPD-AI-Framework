using HPD.Events;

namespace HPD.Auth.Core.Events;

/// <summary>
/// Raised when a user's password is successfully changed.
/// Subscribers should revoke all existing sessions (unless SecurityOptions.RevokeSessionsOnPasswordChange
/// is handled at the service layer) and optionally send a security notification email.
/// </summary>
public record PasswordChangedEvent : AuthEvent
{
    public required Guid UserId { get; init; }

    public override EventPriority Priority => EventPriority.Normal;
}
