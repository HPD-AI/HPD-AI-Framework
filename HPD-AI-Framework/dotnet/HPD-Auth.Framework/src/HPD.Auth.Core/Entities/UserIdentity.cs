using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HPD.Auth.Core.Entities;

/// <summary>
/// v2.2: Rich external identity storage ( pattern).
/// Replaces reliance on AspNetUserLogins for OAuth provider data.
/// Stores full provider profile including avatar, name, and raw claims as JSONB.
/// v2.3: Added federation tracking fields for LDAP/SCIM sync.
/// </summary>
public class UserIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Multi-tenancy discriminator (matches user's InstanceId).
    /// </summary>
    public Guid InstanceId { get; set; } = Guid.Empty;

    /// <summary>
    /// Reference to the local user account.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// OAuth provider name (e.g., "google", "github", "microsoft").
    /// For SSO: prefixed with "sso:" (e.g., "sso:okta-corp").
    /// </summary>
    [MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// User's ID from the external provider.
    /// </summary>
    [MaxLength(256)]
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Raw identity data from provider (JSONB).
    /// Stores email, name, avatar_url, and all provider-specific claims.
    /// Example: {"email": "user@gmail.com", "name": "John Doe", "picture": "https://..."}
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string IdentityData { get; set; } = "{}";

    /// <summary>
    /// Last time user signed in via this provider.
    /// </summary>
    public DateTime LastSignInAt { get; set; } = DateTime.UtcNow;

    // ─────────────────────────────────────────────────────────────
    // v2.3: Federation Tracking (Sleeper - for LDAP/SCIM sync)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// v2.3 Sleeper: Reference to the federation provider that provisioned this identity.
    /// Maps to AuthComponent.Id when federation is enabled.
    /// </summary>
    public Guid? FederationSourceId { get; set; }

    /// <summary>
    /// v2.3 Sleeper: Last time this identity was synced from the federation source.
    /// </summary>
    public DateTime? LastSyncAt { get; set; }

    /// <summary>
    /// v2.3 Sleeper: Cached provider tokens (JSONB).
    /// Stores access_token, refresh_token, id_token from IdP for token exchange.
    /// Encrypted via Data Protection before storage.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? ProviderTokens { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public ApplicationUser User { get; set; } = null!;
}
