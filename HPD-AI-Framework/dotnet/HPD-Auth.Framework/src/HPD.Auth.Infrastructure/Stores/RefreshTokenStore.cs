using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HPD.Auth.Infrastructure.Stores;

/// <summary>
/// EF Core implementation of <see cref="IRefreshTokenStore"/>.
///
/// Handles persistence for <see cref="RefreshToken"/> entities used in the
/// OAuth2-style token refresh flow.
///
/// Security model:
/// - Tokens are single-use: after GetByTokenAsync returns a valid token, the caller
///   must call UpdateAsync with IsUsed = true before issuing a new access token.
///   This prevents refresh token replay attacks.
/// - Tokens are tenant-scoped: the DbContext global query filter on
///   RefreshToken.InstanceId means a token from tenant A cannot be found when
///   the current tenant is B.
/// - RevokeAllForUserAsync covers the "compromised account" scenario: it atomically
///   revokes every active token for a user in a single database round-trip.
///
/// Thread safety:
/// The store is registered as Scoped (one per HTTP request). It is not safe to
/// share a store instance across concurrent requests.
/// </summary>
public sealed class RefreshTokenStore : IRefreshTokenStore
{
    private readonly HPDAuthDbContext _context;

    public RefreshTokenStore(HPDAuthDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses AsNoTracking for read-only lookup. If the caller needs to mutate the
    /// token (e.g., mark IsUsed), they must call UpdateAsync with the modified entity.
    /// The global query filter on InstanceId is automatically applied — tokens from
    /// other tenants are never returned.
    /// </remarks>
    public async Task<RefreshToken?> GetByTokenAsync(
        string token,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        return await _context.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Token == token, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The <paramref name="token"/> entity must have all required properties set
    /// (Token, UserId, InstanceId, JwtId, ExpiresAt) before calling this method.
    /// The store does not validate or generate the token value.
    /// </remarks>
    public async Task CreateAsync(RefreshToken token, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Attaches the entity and marks it as Modified so EF Core generates an UPDATE
    /// statement. If the entity was loaded with AsNoTracking (as GetByTokenAsync does),
    /// the caller must pass the modified entity back to this method — it will be
    /// re-attached for the update.
    ///
    /// Common update patterns:
    /// - Mark used: token.IsUsed = true
    /// - Mark revoked: token.IsRevoked = true; token.RevokedAt = DateTime.UtcNow
    /// </remarks>
    public async Task UpdateAsync(RefreshToken token, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        // If the entity is already tracked, Update() is a no-op for the attach step.
        // If it was loaded with AsNoTracking, this re-attaches it in Modified state.
        _context.RefreshTokens.Update(token);
        await _context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Loads all non-revoked tokens for the user into memory, marks them all revoked,
    /// then calls SaveChangesAsync once. This is:
    /// - Correct for any database row count that fits in memory.
    /// - Atomic: all revocations happen in a single transaction (for real providers).
    /// - Efficient: one SELECT + one batch UPDATE round-trip.
    ///
    /// The global query filter on InstanceId ensures only the current tenant's tokens
    /// are affected — cross-tenant revocation is impossible via this method.
    ///
    /// For very high token counts (millions of tokens per user), consider switching to
    /// ExecuteUpdateAsync (EF Core 7+) to avoid loading all rows into memory.
    /// </remarks>
    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        // Load only non-revoked tokens — already-revoked tokens don't need updating
        var activeTokens = await _context.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync(ct);

        if (activeTokens.Count == 0)
            return; // Nothing to revoke — early exit avoids a pointless SaveChanges

        var now = DateTime.UtcNow;

        foreach (var token in activeTokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = now;
        }

        // Single SaveChanges for atomicity — all tokens revoked together or none
        await _context.SaveChangesAsync(ct);
    }
}
