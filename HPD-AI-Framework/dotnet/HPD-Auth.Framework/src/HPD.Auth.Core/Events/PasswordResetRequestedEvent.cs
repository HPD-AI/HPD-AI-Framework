using HPD.Events;

namespace HPD.Auth.Core.Events;

/// <summary>
/// Raised when a user requests a password reset link.
/// Subscribers can use this event for auditing, rate-limiting enforcement,
/// or triggering the email delivery pipeline.
/// </summary>
public record PasswordResetRequestedEvent : AuthEvent
{
    public required Guid UserId { get; init; }

    public required string Email { get; init; }

    public override EventPriority Priority => EventPriority.Normal;
}
