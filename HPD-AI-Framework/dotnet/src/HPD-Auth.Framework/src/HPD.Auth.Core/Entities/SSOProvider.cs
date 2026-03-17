using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HPD.Auth.Core.Entities;

/// <summary>
/// v2.2: Dynamic OAuth provider configuration.
/// Allows runtime management of OAuth providers without app restarts.
/// Credentials are encrypted via ASP.NET Data Protection.
/// v2.3: Added SAML-specific fields for enterprise SSO.
/// </summary>
public class SSOProvider
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Multi-tenancy discriminator.
    /// </summary>
    public Guid InstanceId { get; set; } = Guid.Empty;

    /// <summary>
    /// Provider identifier (e.g., "google", "github", "microsoft", "apple").
    /// For SAML: use descriptive name (e.g., "okta-corp", "azure-ad").
    /// </summary>
    [MaxLength(50)]
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth Client ID (for OIDC/OAuth2).
    /// </summary>
    [MaxLength(256)]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth Client Secret (encrypted via Data Protection before storage).
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// OAuth scopes as a space-separated string (e.g., "openid email profile").
    /// </summary>
    [MaxLength(1000)]
    public string Scopes { get; set; } = string.Empty;

    // ─────────────────────────────────────────────────────────────
    // v2.3: SAML-Specific Fields (Sleeper - for Enterprise SSO)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// v2.3 Sleeper: SAML Entity ID (SP or IdP identifier).
    /// Example: "https://idp.example.com/saml/metadata"
    /// </summary>
    [MaxLength(2048)]
    public string? EntityId { get; set; }

    /// <summary>
    /// v2.3 Sleeper: Full SAML metadata XML.
    /// </summary>
    public string? MetadataXml { get; set; }

    /// <summary>
    /// v2.3 Sleeper: SAML attribute mapping configuration (JSONB).
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? AttributeMapping { get; set; }

    /// <summary>
    /// v2.3 Sleeper: SAML NameID format preference.
    /// Example: "urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"
    /// </summary>
    [MaxLength(256)]
    public string? NameIdFormat { get; set; }

    /// <summary>
    /// v2.3 Sleeper: X.509 signing certificate (PEM format).
    /// </summary>
    public string? SigningCertificate { get; set; }

    /// <summary>
    /// Whether this provider is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    public DateTime Created { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
