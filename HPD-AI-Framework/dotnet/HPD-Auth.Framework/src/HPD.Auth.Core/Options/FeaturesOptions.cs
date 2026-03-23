namespace HPD.Auth.Core.Options;

/// <summary>
/// Feature flags for enabling or disabling optional HPD.Auth capabilities.
/// Allows operators to control the surface area of the auth system.
/// </summary>
public class FeaturesOptions
{
    /// <summary>
    /// Whether user registration (sign-up) is enabled.
    /// Set to false for invite-only or closed-beta scenarios.
    /// Defaults to true.
    /// </summary>
    public bool EnableRegistration { get; set; } = true;

    /// <summary>
    /// Whether email confirmation is required before a user can sign in.
    /// Defaults to true. Required for security — prevents account enumeration.
    /// </summary>
    public bool RequireEmailConfirmation { get; set; } = true;

    /// <summary>
    /// Whether two-factor authentication (TOTP) is available.
    /// Defaults to true.
    /// </summary>
    public bool EnableTwoFactor { get; set; } = true;

    /// <summary>
    /// Whether passkey (FIDO2/WebAuthn) registration and authentication is available.
    /// Defaults to false until the FIDO2 library dependency is configured.
    /// </summary>
    public bool EnablePasskeys { get; set; } = false;

    /// <summary>
    /// Whether magic link (passwordless email) sign-in is available.
    /// Defaults to false.
    /// </summary>
    public bool EnableMagicLink { get; set; } = false;

    /// <summary>
    /// Whether OAuth social login is available.
    /// Defaults to true (providers are further configured in OAuthOptions).
    /// </summary>
    public bool EnableOAuth { get; set; } = true;

    /// <summary>
    /// Whether users can manage their own active sessions (view and revoke).
    /// Defaults to true.
    /// </summary>
    public bool EnableSessionManagement { get; set; } = true;

    /// <summary>
    /// Whether users can delete their own accounts.
    /// Defaults to false — require admin intervention to avoid accidental deletion.
    /// </summary>
    public bool EnableSelfAccountDeletion { get; set; } = false;

    /// <summary>
    /// Whether the audit log is active. Disabling reduces database writes
    /// but eliminates security traceability.
    /// Defaults to true.
    /// </summary>
    public bool EnableAuditLog { get; set; } = true;
}
