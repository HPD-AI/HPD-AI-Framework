using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HPD.Auth.Core.Entities;

/// <summary>
/// Tracks active user sessions for device management and security.
/// v2.2: Added InstanceId and AAL (Authenticator Assurance Level) for step-up authentication.
/// v2.3: Added SLO (Single Logout) sleeper fields for future IdP capabilities.
/// </summary>
public class UserSession
{
    /// <summary>
    /// Unique session identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// v2.2: Multi-tenancy discriminator.
    /// </summary>
    public Guid InstanceId { get; set; } = Guid.Empty;

    /// <summary>
    /// User who owns this session.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// v2.2: Authenticator Assurance Level ( pattern).
    /// Enables step-up authentication for sensitive operations.
    /// - aal1: Password or Social Login
    /// - aal2: Password + TOTP/SMS/Email OTP
    /// - aal3: Password + Hardware Key/Passkey
    /// </summary>
    [MaxLength(10)]
    public string AAL { get; set; } = "aal1";

    // ─────────────────────────────────────────────────────────────
    // v2.3: Single Logout (SLO) Sleeper Fields
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// v2.3 Sleeper: External IdP session identifier (SAML SessionIndex).
    /// </summary>
    [MaxLength(256)]
    public string? BrokerSessionId { get; set; }

    /// <summary>
    /// v2.3 Sleeper: External IdP user identifier.
    /// </summary>
    [MaxLength(256)]
    public string? BrokerUserId { get; set; }

    /// <summary>
    /// v2.3 Sleeper: Which SSO provider created this session (FK to SSOProviders.Id).
    /// </summary>
    public Guid? SSOProviderId { get; set; }

    /// <summary>
    /// v2.3 Sleeper: Session validity start (SAML NotBefore assertion time).
    /// </summary>
    public DateTime? NotBefore { get; set; }

    /// <summary>
    /// v2.3 Sleeper: Session hard expiry from IdP (SAML NotOnOrAfter).
    /// </summary>
    public DateTime? NotAfter { get; set; }

    /// <summary>
    /// v2.3 Sleeper: OAuth client that created this session (FK to ServiceClients.Id).
    /// </summary>
    public Guid? OAuthClientId { get; set; }

    /// <summary>
    /// v2.3 Sleeper: OAuth scopes granted to this session (space-separated).
    /// </summary>
    [MaxLength(2000)]
    public string? Scopes { get; set; }

    /// <summary>
    /// v2.3 Sleeper: Which clients (Service Providers) are using this session.
    /// JSONB map of ClientId to last access timestamp.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? ClientSessions { get; set; }

    /// <summary>
    /// v2.3 Sleeper: Session state for logout flow.
    /// Values: "active", "logging_out", "logged_out"
    /// </summary>
    [MaxLength(20)]
    public string SessionState { get; set; } = "active";

    // ─────────────────────────────────────────────────────────────
    // Core Session Fields
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// IP address when session was created.
    /// </summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// Full user agent string.
    /// </summary>
    [MaxLength(1024)]
    public string? UserAgent { get; set; }

    /// <summary>
    /// Parsed human-readable device description (e.g., "Chrome on Windows 11").
    /// </summary>
    [MaxLength(500)]
    public string? DeviceInfo { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public bool IsRevoked { get; set; } = false;

    public DateTime? RevokedAt { get; set; }

    // Navigation
    public ApplicationUser User { get; set; } = null!;
}
