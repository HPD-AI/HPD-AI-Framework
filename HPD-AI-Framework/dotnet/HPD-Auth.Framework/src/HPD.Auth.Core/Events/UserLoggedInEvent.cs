using HPD.Events;

namespace HPD.Auth.Core.Events;

/// <summary>
/// Raised after a user successfully authenticates.
/// Subscribers can use this event for security monitoring, analytics,
/// or sending login-from-new-device alerts.
/// IP address and User-Agent are carried on <see cref="AuthEvent.AuthContext"/>.
/// </summary>
public record UserLoggedInEvent : AuthEvent
{
    public required Guid UserId { get; init; }

    public required string Email { get; init; }

    /// <summary>
    /// Authentication method used.
    /// Values: "password", "oauth", "passkey", "magic_link"
    /// </summary>
    public string AuthMethod { get; init; } = "password";

    public override EventPriority Priority => EventPriority.Normal;
}
