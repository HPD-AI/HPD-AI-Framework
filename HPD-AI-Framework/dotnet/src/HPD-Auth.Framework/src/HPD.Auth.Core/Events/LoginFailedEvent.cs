using HPD.Events;

namespace HPD.Auth.Core.Events;

/// <summary>
/// Raised when an authentication attempt fails.
/// Subscribers can use this event to detect brute-force attacks,
/// trigger CAPTCHA challenges, or alert security teams.
/// Note: UserId is intentionally absent — the user may not exist.
/// IP address is carried on <see cref="AuthEvent.AuthContext"/>.
/// </summary>
public record LoginFailedEvent : AuthEvent
{
    /// <summary>
    /// Email address that was used in the failed login attempt.
    /// May not correspond to an existing user (enumeration resistance applies).
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// Reason for the failure.
    /// Examples: "invalid_password", "user_not_found", "account_locked",
    ///           "email_not_confirmed", "account_disabled"
    /// </summary>
    public required string Reason { get; init; }

    public override EventPriority Priority => EventPriority.Control;
}
