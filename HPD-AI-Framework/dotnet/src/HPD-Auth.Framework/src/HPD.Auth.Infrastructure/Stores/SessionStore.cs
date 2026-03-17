using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HPD.Auth.Infrastructure.Stores;

/// <summary>
/// EF Core implementation of <see cref="ISessionManager"/>.
///
/// Manages the <see cref="UserSession"/> lifecycle:
/// create → query active → revoke (single or bulk).
///
/// Design decisions:
///
/// 1. DEFAULT LIFETIME: 14 days if <see cref="SessionContext.Lifetime"/> is not
///    specified. This matches the common industry default (GitHub, Stripe, ).
///
/// 2. TENANT SCOPING: The DbContext global query filter on UserSession.InstanceId
///    automatically scopes all reads to the current tenant. Sessions are created
///    with InstanceId defaulting to Guid.Empty (single-tenant). For multi-tenant
///    deployments, the tenant's InstanceId must be set at session creation time.
///    The store reads the InstanceId from the DbContext's injected ITenantContext.
///
/// 3. AAL STORAGE: The Authenticator Assurance Level (aal1/aal2/aal3) is stored on
///    the session entity so that step-up authentication can verify the current
///    session's assurance level without re-querying the auth methods used.
///
/// 4. DEVICE INFO: UserAgent is stored raw. A higher-level service can parse it
///    into a human-readable DeviceInfo string using a UA-parser library before
///    calling CreateSessionAsync. The store does not perform UA parsing.
///
/// 5. BULK REVOKE: RevokeAllSessionsAsync loads all matching sessions into memory
///    and marks them revoked before calling SaveChangesAsync once. This is correct
///    for the in-memory provider and for databases with reasonable session counts.
///    For very high-volume scenarios, switch to ExecuteUpdateAsync (EF Core 7+).
/// </summary>
public sealed class SessionStore : ISessionManager
{
    private static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromDays(14);

    private readonly HPDAuthDbContext _context;
    private readonly ITenantContext _tenantContext;

    public SessionStore(HPDAuthDbContext context, ITenantContext tenantContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Creates and immediately persists a new session. The session's InstanceId is
    /// set from the injected <see cref="ITenantContext"/> so the row belongs to the
    /// current tenant.
    /// </remarks>
    public async Task<UserSession> CreateSessionAsync(
        Guid userId,
        SessionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var now = DateTime.UtcNow;
        var lifetime = context.Lifetime ?? DefaultSessionLifetime;

        var session = new UserSession
        {
            // Id is set by the entity default (Guid.NewGuid())
            InstanceId = _tenantContext.InstanceId,
            UserId = userId,
            AAL = context.AAL,
            IpAddress = context.IpAddress,
            UserAgent = context.UserAgent,
            CreatedAt = now,
            LastActiveAt = now,
            ExpiresAt = now.Add(lifetime),
            IsRevoked = false,
            SessionState = "active",
        };

        _context.UserSessions.Add(session);
        await _context.SaveChangesAsync(ct);

        return session;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns sessions that are:
    /// - Not revoked (IsRevoked == false)
    /// - Not expired (ExpiresAt > UtcNow)
    ///
    /// The global query filter on InstanceId is automatically applied by the
    /// DbContext, so no explicit tenant filter is needed here.
    /// </remarks>
    public async Task<IReadOnlyList<UserSession>> GetActiveSessionsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var sessions = await _context.UserSessions
            .Where(s => s.UserId == userId
                     && !s.IsRevoked
                     && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastActiveAt)
            .AsNoTracking()
            .ToListAsync(ct);

        return sessions.AsReadOnly();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Revokes a single session by ID. If the session is not found (already revoked,
    /// wrong tenant, or does not exist), the operation is a no-op — no exception is
    /// thrown. This is intentional: revoke is idempotent.
    /// </remarks>
    public async Task RevokeSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        // FindAsync respects the global query filter, so a session from another tenant
        // will not be found and this call will silently no-op — correct behavior.
        var session = await _context.UserSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session is null || session.IsRevoked)
            return; // Already revoked or not found — idempotent

        var now = DateTime.UtcNow;
        session.IsRevoked = true;
        session.RevokedAt = now;
        session.SessionState = "logged_out";

        await _context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Revokes all non-revoked sessions for the user, optionally keeping one session
    /// active (pass the current session's ID as <paramref name="exceptSessionId"/>).
    ///
    /// Use cases:
    /// - Password change: revoke all sessions except the current one.
    /// - Admin force-logout: revoke ALL sessions (exceptSessionId = null).
    /// - Account compromise: revoke everything immediately.
    ///
    /// All revocations happen in a single SaveChangesAsync call for atomicity.
    /// </remarks>
    public async Task RevokeAllSessionsAsync(
        Guid userId,
        Guid? exceptSessionId = null,
        CancellationToken ct = default)
    {
        // Load all non-revoked sessions for this user (tenant-scoped by query filter)
        var query = _context.UserSessions
            .Where(s => s.UserId == userId && !s.IsRevoked);

        if (exceptSessionId.HasValue)
            query = query.Where(s => s.Id != exceptSessionId.Value);

        var sessions = await query.ToListAsync(ct);

        if (sessions.Count == 0)
            return; // Nothing to revoke

        var now = DateTime.UtcNow;

        foreach (var session in sessions)
        {
            session.IsRevoked = true;
            session.RevokedAt = now;
            session.SessionState = "logged_out";
        }

        // Single SaveChanges for the entire batch — atomic and efficient
        await _context.SaveChangesAsync(ct);
    }
}
