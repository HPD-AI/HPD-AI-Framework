using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace HPD.Auth.Infrastructure.Data;

/// <summary>
/// EF Core DbContext for HPD.Auth.
///
/// Extends IdentityDbContext with all custom HPD entities. Key design decisions:
///
/// 1. TENANT ISOLATION: Every entity with InstanceId has a global query filter scoped
///    to _tenantContext.InstanceId. This means all LINQ queries are automatically
///    tenant-scoped without any developer effort. Use IgnoreQueryFilters() for
///    admin/cross-tenant operations.
///
/// 2. TENANT-SCOPED UNIQUENESS: The default ASP.NET Identity unique indexes on
///    NormalizedEmail and NormalizedUserName are global. We replace them with
///    composite (InstanceId, NormalizedEmail) and (InstanceId, NormalizedUserName)
///    indexes so two tenants can have users with the same email.
///
/// 3. AUDIT IMMUTABILITY: AuditLog rows must never be updated or deleted.
///    This is a business/policy rule enforced at the application layer (AuditLogStore
///    only calls Add, never Update or Remove). EF Core does not have a built-in
///    "immutable table" concept for in-memory providers.
///
/// 4. DATA PROTECTION: Implements IDataProtectionKeyContext so ASP.NET Data Protection
///    persists encryption keys to the database. Enables automatic key rotation across
///    load-balanced deployments.
///
/// 5. JSONB ANNOTATIONS: String columns holding JSON are annotated with
///    [Column(TypeName = "jsonb")]. The in-memory provider ignores this annotation;
///    it becomes meaningful when migrating to PostgreSQL.
/// </summary>
public class HPDAuthDbContext
    : IdentityDbContext<
        ApplicationUser,
        ApplicationRole,
        Guid,
        IdentityUserClaim<Guid>,
        IdentityUserRole<Guid>,
        IdentityUserLogin<Guid>,
        IdentityRoleClaim<Guid>,
        IdentityUserToken<Guid>>,
      IDataProtectionKeyContext
{
    private readonly ITenantContext _tenantContext;

    // ─────────────────────────────────────────────────────────────
    // Custom DbSets (beyond what IdentityDbContext provides)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Refresh tokens issued alongside access tokens.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Active user sessions for device management and SLO.</summary>
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    /// <summary>External identity links (OAuth/SAML provider profiles).</summary>
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();

    /// <summary>FIDO2/WebAuthn passkey credentials.</summary>
    public DbSet<UserPasskey> UserPasskeys => Set<UserPasskey>();

    /// <summary>
    /// Immutable audit log. Write only — never update or delete rows.
    /// See AuditLogStore for the enforced write-only pattern.
    /// </summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>Dynamically configured OAuth/SAML providers per tenant.</summary>
    public DbSet<SSOProvider> SSOProviders => Set<SSOProvider>();

    /// <summary>Per-tenant branding configuration. InstanceId is the PK.</summary>
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();

    /// <summary>
    /// ASP.NET Data Protection key storage.
    /// Implements IDataProtectionKeyContext so keys are persisted to the DB,
    /// enabling automatic rotation across load-balanced nodes.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // ─────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the DbContext with tenant resolution.
    /// </summary>
    /// <param name="options">EF Core options (provider, connection string, etc.).</param>
    /// <param name="tenantContext">
    /// Resolves the current tenant's InstanceId. In single-tenant mode this always
    /// returns Guid.Empty. In multi-tenant SaaS this is resolved from the JWT claim
    /// or HTTP header. Injected by DI — never hardcoded.
    /// </param>
    // Track which SQLite in-memory databases have had their schema created.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _initializedDatabases = new();

    public HPDAuthDbContext(
        DbContextOptions<HPDAuthDbContext> options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));

        // Auto-create schema for SQLite in-memory databases.
        // EnsureCreated is idempotent but expensive to call every time,
        // so we track initialization per connection string.
        var connStr = Database.GetConnectionString();
        if (connStr != null && connStr.Contains("mode=memory", StringComparison.OrdinalIgnoreCase))
        {
            _initializedDatabases.GetOrAdd(connStr, _ =>
            {
                Database.EnsureCreated();
                return true;
            });
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Model Configuration
    // ─────────────────────────────────────────────────────────────

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Always call base first — IdentityDbContext configures its own tables
        base.OnModelCreating(builder);

        ConfigureApplicationUser(builder);
        ConfigureApplicationRole(builder);
        ConfigureRefreshToken(builder);
        ConfigureUserSession(builder);
        ConfigureUserIdentity(builder);
        ConfigureUserPasskey(builder);
        ConfigureAuditLog(builder);
        ConfigureSSOProvider(builder);
        ConfigureTenantSettings(builder);
    }

    // ─────────────────────────────────────────────────────────────
    // Per-Entity Configuration Methods
    // ─────────────────────────────────────────────────────────────

    private void ConfigureApplicationUser(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>(entity =>
        {
            // ── Tenant isolation ──────────────────────────────────
            entity.HasQueryFilter(u => u.InstanceId == _tenantContext.InstanceId);

            // ── Drop the default GLOBAL unique indexes from IdentityDbContext ──
            // ASP.NET Identity creates global unique indexes on NormalizedEmail and
            // NormalizedUserName. In a multi-tenant database these indexes would
            // prevent two tenants from registering the same email address.
            // We replace them with tenant-scoped composite indexes below.
            entity.HasIndex(u => u.NormalizedEmail).IsUnique(false);
            entity.HasIndex(u => u.NormalizedUserName).IsUnique(false);

            // ── Tenant-scoped unique indexes ──────────────────────
            // These enforce uniqueness within a single tenant while allowing
            // the same email/username to exist in different tenants.
            entity.HasIndex(u => new { u.InstanceId, u.NormalizedEmail })
                  .IsUnique()
                  .HasDatabaseName("IX_AspNetUsers_InstanceId_NormalizedEmail");

            entity.HasIndex(u => new { u.InstanceId, u.NormalizedUserName })
                  .IsUnique()
                  .HasDatabaseName("IX_AspNetUsers_InstanceId_NormalizedUserName");

            // ── RequiredActions: stored as a JSON array in a single column ──
            // EF Core's in-memory provider handles List<string> natively.
            // For PostgreSQL this will need a JSON or text[] column conversion.
            // The value comparer is required so EF's change tracker detects in-place
            // mutations (e.g., list.Remove / list.Add) and marks the entity dirty.
            entity.Property(u => u.RequiredActions)
                  .HasConversion(
                      v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                      v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>())
                  .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                      (a, b) => a != null && b != null && a.SequenceEqual(b),
                      v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                      v => v.ToList()));
        });
    }

    private void ConfigureApplicationRole(ModelBuilder builder)
    {
        builder.Entity<ApplicationRole>(entity =>
        {
            entity.HasQueryFilter(r => r.InstanceId == _tenantContext.InstanceId);
        });
    }

    private void ConfigureRefreshToken(ModelBuilder builder)
    {
        builder.Entity<RefreshToken>(entity =>
        {
            // ── Tenant isolation ──────────────────────────────────
            entity.HasQueryFilter(t => t.InstanceId == _tenantContext.InstanceId);

            // ── FK: RefreshToken → ApplicationUser ────────────────
            entity.HasOne(t => t.User)
                  .WithMany()
                  .HasForeignKey(t => t.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            // ── Index on Token string for fast lookups ─────────────
            entity.HasIndex(t => t.Token)
                  .IsUnique()
                  .HasDatabaseName("IX_RefreshTokens_Token");

            // ── Index to support fast per-user token queries ───────
            entity.HasIndex(t => new { t.UserId, t.IsRevoked })
                  .HasDatabaseName("IX_RefreshTokens_UserId_IsRevoked");
        });
    }

    private void ConfigureUserSession(ModelBuilder builder)
    {
        builder.Entity<UserSession>(entity =>
        {
            // ── Tenant isolation ──────────────────────────────────
            entity.HasQueryFilter(s => s.InstanceId == _tenantContext.InstanceId);

            // ── FK: UserSession → ApplicationUser ─────────────────
            entity.HasOne(s => s.User)
                  .WithMany()
                  .HasForeignKey(s => s.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            // ── Index for active-session queries ───────────────────
            entity.HasIndex(s => new { s.UserId, s.IsRevoked, s.ExpiresAt })
                  .HasDatabaseName("IX_UserSessions_UserId_IsRevoked_ExpiresAt");
        });
    }

    private void ConfigureUserIdentity(ModelBuilder builder)
    {
        builder.Entity<UserIdentity>(entity =>
        {
            // ── Tenant isolation ──────────────────────────────────
            entity.HasQueryFilter(i => i.InstanceId == _tenantContext.InstanceId);

            // ── FK: UserIdentity → ApplicationUser ───────────────
            entity.HasOne(i => i.User)
                  .WithMany()
                  .HasForeignKey(i => i.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            // ── Composite unique index: one identity per provider per tenant ──
            // Prevents the same external provider account from being linked to
            // multiple local users within a single tenant.
            entity.HasIndex(i => new { i.InstanceId, i.Provider, i.ProviderId })
                  .IsUnique()
                  .HasDatabaseName("IX_UserIdentities_InstanceId_Provider_ProviderId");
        });
    }

    private void ConfigureUserPasskey(ModelBuilder builder)
    {
        builder.Entity<UserPasskey>(entity =>
        {
            // UserPasskey does NOT get a global query filter by InstanceId because
            // passkey lookups during authentication happen by CredentialId alone
            // (before the user is identified). Tenant filtering is applied in the
            // passkey service layer after the user is resolved.

            // ── FK: UserPasskey → ApplicationUser ─────────────────
            entity.HasOne(p => p.User)
                  .WithMany()
                  .HasForeignKey(p => p.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            // ── CredentialId must be globally unique ───────────────
            // FIDO2 credential IDs are globally unique by spec (random bytes).
            entity.HasIndex(p => p.CredentialId)
                  .IsUnique()
                  .HasDatabaseName("IX_UserPasskeys_CredentialId");
        });

    }

    private void ConfigureAuditLog(ModelBuilder builder)
    {
        builder.Entity<AuditLog>(entity =>
        {
            // ── Tenant isolation ──────────────────────────────────
            entity.HasQueryFilter(a => a.InstanceId == _tenantContext.InstanceId);

            // ── IMMUTABILITY NOTE ─────────────────────────────────
            // AuditLog rows are write-once and must never be updated or deleted.
            // EF Core does not provide a built-in "immutable table" mechanism
            // for the in-memory provider. Immutability is enforced at the store
            // layer: AuditLogStore only calls context.AuditLogs.Add() and
            // SaveChangesAsync(). It never calls Update() or Remove().
            // For PostgreSQL, consider adding row-level security policies or
            // a trigger that raises an error on UPDATE/DELETE.

            // All properties use init-only setters (defined on the entity itself)
            // which provides compile-time protection against accidental mutation.

            // ── Indexes for audit queries ──────────────────────────
            entity.HasIndex(a => new { a.InstanceId, a.UserId, a.Timestamp })
                  .HasDatabaseName("IX_AuditLogs_InstanceId_UserId_Timestamp");

            entity.HasIndex(a => new { a.InstanceId, a.Category, a.Timestamp })
                  .HasDatabaseName("IX_AuditLogs_InstanceId_Category_Timestamp");

            entity.HasIndex(a => new { a.InstanceId, a.Action, a.Timestamp })
                  .HasDatabaseName("IX_AuditLogs_InstanceId_Action_Timestamp");
        });
    }

    private void ConfigureSSOProvider(ModelBuilder builder)
    {
        builder.Entity<SSOProvider>(entity =>
        {
            // ── Tenant isolation ──────────────────────────────────
            entity.HasQueryFilter(p => p.InstanceId == _tenantContext.InstanceId);

            // ── Unique provider per tenant ─────────────────────────
            entity.HasIndex(p => new { p.InstanceId, p.ProviderId })
                  .IsUnique()
                  .HasDatabaseName("IX_SSOProviders_InstanceId_ProviderId");
        });
    }

    private void ConfigureTenantSettings(ModelBuilder builder)
    {
        builder.Entity<TenantSettings>(entity =>
        {
            // ── InstanceId IS the primary key ──────────────────────
            // No separate Id column. One row per tenant.
            // Single-tenant apps use Guid.Empty as the key.
            entity.HasKey(t => t.InstanceId);

            // ── Tenant isolation ──────────────────────────────────
            // The query filter here means: context.TenantSettings automatically
            // returns only the current tenant's row without any extra Where clause.
            entity.HasQueryFilter(t => t.InstanceId == _tenantContext.InstanceId);
        });
    }
}
