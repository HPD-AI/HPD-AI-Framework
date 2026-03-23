namespace HPD.Auth.Core.Options;

/// <summary>
/// Account lockout policy to defend against brute-force attacks.
/// </summary>
public class LockoutPolicyOptions
{
    /// <summary>
    /// Whether account lockout is enabled. Defaults to true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of consecutive failed login attempts before the account
    /// is locked out. Defaults to 5.
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// Duration of the lockout after MaxFailedAttempts is reached.
    /// Defaults to 15 minutes. After this period, the lockout resets automatically.
    /// </summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Whether lockout applies to new (not yet confirmed) accounts.
    /// Defaults to true — locks out any account regardless of email confirmation status.
    /// </summary>
    public bool AllowedForNewUsers { get; set; } = true;
}
