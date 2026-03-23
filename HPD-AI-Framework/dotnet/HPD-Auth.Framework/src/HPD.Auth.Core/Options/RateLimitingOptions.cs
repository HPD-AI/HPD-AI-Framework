namespace HPD.Auth.Core.Options;

/// <summary>
/// Rate limiting configuration for auth endpoints.
/// Prevents brute-force and credential-stuffing attacks.
/// </summary>
public class RateLimitingOptions
{
    /// <summary>
    /// Whether rate limiting is enabled. Defaults to true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of login attempts per IP address within the window.
    /// Defaults to 10.
    /// </summary>
    public int LoginAttemptsPerWindow { get; set; } = 10;

    /// <summary>
    /// Maximum number of registration attempts per IP address within the window.
    /// Defaults to 5.
    /// </summary>
    public int RegisterAttemptsPerWindow { get; set; } = 5;

    /// <summary>
    /// Maximum number of password reset requests per email address within the window.
    /// Defaults to 3.
    /// </summary>
    public int PasswordResetAttemptsPerWindow { get; set; } = 3;

    /// <summary>
    /// The sliding time window for rate limit counters. Defaults to 15 minutes.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Duration of the rate-limit ban after limits are exceeded. Defaults to 1 hour.
    /// </summary>
    public TimeSpan BanDuration { get; set; } = TimeSpan.FromHours(1);
}
