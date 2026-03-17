using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace HPD.Auth.Core.Entities;

/// <summary>
/// HPD user entity extending ASP.NET Core Identity.
/// v2.2: Enhanced with multi-tenancy primitives ("Sleeper Cells"), JSONB metadata,
///       RequiredActions workflow, and -compatible EmailConfirmedAt.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    // ─────────────────────────────────────────────────────────────
    // v2.2: Multi-Tenancy Primitives ("Sleeper Cells")
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Multi-tenancy discriminator. Defaults to Guid.Empty for single-tenant apps.
    /// When we scale to SaaS, each tenant gets a unique InstanceId.
    /// </summary>
    public Guid InstanceId { get; set; } = Guid.Empty;

    /// <summary>
    /// Authorization scope ( pattern).
    /// Used to scope tokens to specific apps within a tenant.
    /// Examples: "authenticated", "admin_portal", "mobile_app"
    /// </summary>
    [MaxLength(50)]
    public string? Audience { get; set; }

    // ─────────────────────────────────────────────────────────────
    // v2.2: JSONB Metadata ( Pattern)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// User-controlled metadata (writable by user).
    /// Examples: Theme preferences, Bio, Social links, Avatar URL.
    /// Stored as JSONB for flexible schema-less data.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string UserMetadata { get; set; } = "{}";

    /// <summary>
    /// System/Admin-controlled metadata (NOT writable by user).
    /// Examples: SubscriptionTier, StripeCustomerId, FeatureFlags.
    /// Security-critical: prevents privilege escalation.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string AppMetadata { get; set; } = "{}";

    // ─────────────────────────────────────────────────────────────
    // v2.2: Workflow Engine ( Pattern)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Required actions that must be completed before full access.
    /// Values: "VERIFY_EMAIL", "UPDATE_PASSWORD", "ACCEPT_TOS", "CONFIGURE_2FA"
    /// Login returns partial auth until all actions are completed.
    /// </summary>
    public List<string> RequiredActions { get; set; } = new();

    // ─────────────────────────────────────────────────────────────
    // Profile Information
    // ─────────────────────────────────────────────────────────────

    [PersonalData]
    [MaxLength(100)]
    public string? FirstName { get; set; }

    [PersonalData]
    [MaxLength(100)]
    public string? LastName { get; set; }

    [PersonalData]
    [MaxLength(500)]
    public string? DisplayName { get; set; }

    [PersonalData]
    [MaxLength(2048)]
    public string? AvatarUrl { get; set; }

    // ─────────────────────────────────────────────────────────────
    // Account Status
    // ─────────────────────────────────────────────────────────────

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    // ─────────────────────────────────────────────────────────────
    // Tracking
    // ─────────────────────────────────────────────────────────────

    public DateTime Created { get; set; } = DateTime.UtcNow;

    public DateTime Updated { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    [MaxLength(45)]
    public string? LastLoginIp { get; set; }

    // ─────────────────────────────────────────────────────────────
    // Subscription
    // ─────────────────────────────────────────────────────────────

    [MaxLength(50)]
    public string SubscriptionTier { get; set; } = "free";

    // ─────────────────────────────────────────────────────────────
    // -Compatible Token Response
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Timestamp when the user's email was confirmed.
    /// Used to construct -compatible token responses with confirmed_at.
    /// </summary>
    public DateTime? EmailConfirmedAt { get; set; }

    // ─────────────────────────────────────────────────────────────
    // Computed Properties
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Check if user has pending required actions.
    /// </summary>
    public bool HasPendingActions => RequiredActions.Count > 0;
}
