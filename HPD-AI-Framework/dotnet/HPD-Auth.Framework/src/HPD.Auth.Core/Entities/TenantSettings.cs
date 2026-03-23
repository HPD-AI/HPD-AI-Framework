using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HPD.Auth.Core.Entities;

/// <summary>
/// v2.2: Per-tenant branding and configuration settings.
/// Enables white-labeling: custom app name, logo, email sender, legal URLs.
/// One row per InstanceId; single-tenant apps use Guid.Empty.
/// InstanceId is the primary key (no separate Id column — exactly one row per tenant).
/// </summary>
public class TenantSettings
{
    /// <summary>
    /// Multi-tenancy discriminator AND primary key.
    /// Single-tenant apps use Guid.Empty.
    /// </summary>
    public Guid InstanceId { get; set; } = Guid.Empty;

    // ─────────────────────────────────────────────────────────────
    // Display / Branding
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Tenant display name (e.g., "Acme Corp").
    /// Used in emails, UI, and OpenAPI docs.
    /// </summary>
    [MaxLength(200)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Logo URL for emails and UI.
    /// </summary>
    [MaxLength(2048)]
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Favicon URL.
    /// </summary>
    [MaxLength(2048)]
    public string? FaviconUrl { get; set; }

    // ─────────────────────────────────────────────────────────────
    // Theme Colors
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Primary brand color in hex format (e.g., "#003366").
    /// </summary>
    [MaxLength(7)]
    public string? PrimaryColor { get; set; }

    /// <summary>
    /// Secondary/accent color in hex format.
    /// </summary>
    [MaxLength(7)]
    public string? AccentColor { get; set; }

    // ─────────────────────────────────────────────────────────────
    // Email Sender Configuration ( pattern)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Email "From" display name (e.g., "Acme Support").
    /// </summary>
    [MaxLength(200)]
    public string? EmailFromName { get; set; }

    /// <summary>
    /// Email "From" address (e.g., "noreply@acme.com").
    /// </summary>
    [MaxLength(320)]
    public string? EmailFromAddress { get; set; }

    // ─────────────────────────────────────────────────────────────
    // URLs
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Primary site URL for redirects and email links.
    /// </summary>
    [MaxLength(2048)]
    public string? SiteUrl { get; set; }

    /// <summary>
    /// Support email for user inquiries.
    /// </summary>
    [MaxLength(320)]
    public string? SupportEmail { get; set; }

    // ─────────────────────────────────────────────────────────────
    // Extended Settings (JSONB for future flexibility)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Additional branding/config as JSONB.
    /// Examples: EmailTemplateOverrides, CustomCSS, FeatureFlags.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string Settings { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
