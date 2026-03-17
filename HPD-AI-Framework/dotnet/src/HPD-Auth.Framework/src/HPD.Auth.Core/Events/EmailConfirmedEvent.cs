using HPD.Events;

namespace HPD.Auth.Core.Events;

/// <summary>
/// Raised when a user successfully confirms their email address.
/// Subscribers can use this event to complete onboarding,
/// remove "VERIFY_EMAIL" from RequiredActions, or send a confirmation notification.
/// </summary>
public record EmailConfirmedEvent : AuthEvent
{
    public required Guid UserId { get; init; }

    public required string Email { get; init; }

    public override EventPriority Priority => EventPriority.Normal;
}
