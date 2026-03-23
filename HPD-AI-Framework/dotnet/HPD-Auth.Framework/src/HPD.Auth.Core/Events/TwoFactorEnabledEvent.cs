using HPD.Events;

namespace HPD.Auth.Core.Events;

/// <summary>
/// Raised when a user successfully enables two-factor authentication.
/// Subscribers can use this event to send a security confirmation email,
/// update the user's security score, or remove "CONFIGURE_2FA" from RequiredActions.
/// </summary>
public record TwoFactorEnabledEvent : AuthEvent
{
    public required Guid UserId { get; init; }

    /// <summary>
    /// The 2FA method that was enabled.
    /// Values: "totp", "passkey", "totp_disabled"
    /// </summary>
    public required string Method { get; init; }

    public override EventPriority Priority => EventPriority.Normal;
}
