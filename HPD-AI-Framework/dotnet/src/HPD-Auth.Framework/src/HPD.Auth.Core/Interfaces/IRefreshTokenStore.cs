using HPD.Auth.Core.Entities;

namespace HPD.Auth.Core.Interfaces;

/// <summary>
/// Persistence operations for refresh tokens.
///
/// Refresh tokens are long-lived credentials that allow clients to obtain new
/// access tokens without re-authenticating. Security requirements:
///
/// - Tokens are single-use (IsUsed is set after redemption).
/// - Tokens can be revoked individually or in bulk (e.g., on password change).
/// - All tokens are scoped to a tenant (InstanceId) via the DbContext query filter.
///
/// The store does NOT generate tokens — that is the responsibility of the token
/// service. The store only persists and retrieves them.
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>
    /// Retrieves a refresh token by its token string value.
    /// Returns null if the token does not exist (invalid, expired lookup, etc.).
    /// The caller is responsible for checking IsUsed, IsRevoked, and ExpiresAt.
    /// </summary>
    /// <param name="token">The base64-encoded token string to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="RefreshToken"/> entity, or null if not found.</returns>
    Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Persists a new refresh token to the store.
    /// The token entity must be fully populated before calling this method
    /// (Id, Token, UserId, InstanceId, JwtId, ExpiresAt must all be set).
    /// </summary>
    /// <param name="token">The refresh token entity to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CreateAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>
    /// Persists changes to an existing refresh token (e.g., marking IsUsed = true
    /// after redemption, or setting RevokedAt after manual revocation).
    /// The entity must already exist in the store.
    /// </summary>
    /// <param name="token">The modified refresh token entity.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>
    /// Revokes all non-revoked refresh tokens belonging to the specified user.
    /// Used when:
    /// - User changes password (invalidate all existing sessions).
    /// - Admin forces logout of all devices.
    /// - Security alert: suspected account compromise.
    ///
    /// All tokens are marked IsRevoked = true and RevokedAt = UtcNow in a single
    /// batch, then saved with a single SaveChangesAsync call for efficiency.
    /// </summary>
    /// <param name="userId">The ID of the user whose tokens to revoke.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
}
