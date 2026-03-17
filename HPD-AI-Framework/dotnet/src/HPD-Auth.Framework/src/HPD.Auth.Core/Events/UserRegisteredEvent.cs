using HPD.Events;

namespace HPD.Auth.Core.Events;

/// <summary>
/// Raised when a new user account is successfully created.
/// Subscribers can use this event to send welcome emails, provision resources,
/// trigger onboarding flows, or update analytics.
/// </summary>
public record UserRegisteredEvent : AuthEvent
{
    public required Guid UserId { get; init; }

    public required string Email { get; init; }

    /// <summary>
    /// Registration method used. Examples: "email", "google", "github".
    /// </summary>
    public string? RegistrationMethod { get; init; }

    public override EventPriority Priority => EventPriority.Normal;
}
