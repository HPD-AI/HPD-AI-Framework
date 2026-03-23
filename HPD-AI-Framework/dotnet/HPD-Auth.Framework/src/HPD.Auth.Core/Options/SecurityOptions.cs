namespace HPD.Auth.Core.Options;

/// <summary>
/// General security hardening configuration.
/// </summary>
public class SecurityOptions
{
    /// <summary>
    /// Whether to send a security alert email when a user logs in
    /// from an unrecognized IP address or device.
    /// Defaults to false.
    /// </summary>
    public bool SendLoginAlerts { get; set; } = false;

    /// <summary>
    /// Whether to enforce email confirmation before allowing login.
    /// Mirrors FeaturesOptions.RequireEmailConfirmation but allows
    /// security policy to be set independently.
    /// Defaults to true.
    /// </summary>
    public bool RequireConfirmedEmail { get; set; } = true;

    /// <summary>
    /// Whether to require a confirmed phone number for 2FA-protected accounts.
    /// Defaults to false.
    /// </summary>
    public bool RequireConfirmedPhoneNumber { get; set; } = false;

    /// <summary>
    /// Whether refresh tokens are rotated on every use (one-time use).
    /// Strongly recommended: true. Prevents token replay attacks.
    /// Defaults to true.
    /// </summary>
    public bool RotateRefreshTokens { get; set; } = true;

    /// <summary>
    /// Whether to revoke all existing sessions when a user changes their password.
    /// Defaults to true.
    /// </summary>
    public bool RevokeSessionsOnPasswordChange { get; set; } = true;

    /// <summary>
    /// Whether to include the user's IP address in JWT claims.
    /// Adds "ip" claim. If true, tokens become IP-bound and may fail
    /// for mobile users on cellular networks.
    /// Defaults to false.
    /// </summary>
    public bool BindTokenToIp { get; set; } = false;

    /// <summary>
    /// Maximum number of concurrent active sessions allowed per user.
    /// 0 means unlimited. Defaults to 0.
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = 0;

    /// <summary>
    /// Token purpose string used for ASP.NET Data Protection operations.
    /// Should be unique per application to prevent cross-app token reuse.
    /// </summary>
    public string DataProtectionPurpose { get; set; } = "HPD.Auth.v1";
}
