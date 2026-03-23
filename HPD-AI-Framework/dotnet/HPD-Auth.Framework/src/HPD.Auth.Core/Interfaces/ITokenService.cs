using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Models;

namespace HPD.Auth.Core.Interfaces;

/// <summary>
/// Service for generating, refreshing, and revoking JWT access tokens and their
/// associated refresh tokens.
///
/// <para>
/// Token lifecycle:
/// 1. <see cref="GenerateTokensAsync"/> — issued on successful login/signup.
/// 2. <see cref="RefreshAsync"/> — client exchanges an expiring refresh token for a new pair.
/// 3. <see cref="RevokeAsync"/> — explicit logout (revoke one device).
/// 4. <see cref="RevokeAllForUserAsync"/> — password change / admin force-logout (all devices).
/// </para>
///
/// <para>
/// Refresh tokens are single-use. On each call to <see cref="RefreshAsync"/> the
/// old token is marked as used and a brand-new pair is returned. This prevents
/// replay attacks from stolen refresh tokens.
/// </para>
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates a JWT access token and a new refresh token for the given user.
    /// Standard claims (sub, email, jti, instance_id, subscription_tier, roles) are
    /// always included. Additional claims may be injected via
    /// <see cref="Options.HPDAuthOptions.AdditionalClaimsFactory"/>.
    /// </summary>
    /// <param name="user">The authenticated user for whom tokens are being issued.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="TokenResponse"/> containing the access token, refresh token,
    /// expiration metadata, and an embedded <see cref="UserTokenDto"/>.
    /// </returns>
    Task<TokenResponse> GenerateTokensAsync(ApplicationUser user, CancellationToken ct = default);

    /// <summary>
    /// Rotates a refresh token: validates the supplied token, marks it as used,
    /// finds the associated user, and issues a fresh token pair.
    /// Returns null if the token is invalid, already used, revoked, expired,
    /// or if the associated user is inactive or deleted.
    /// </summary>
    /// <param name="refreshToken">The opaque refresh token string to redeem.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A new <see cref="TokenResponse"/>, or <c>null</c> if validation fails.
    /// </returns>
    Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>
    /// Revokes a single refresh token (explicit logout from one device).
    /// </summary>
    /// <param name="refreshToken">The opaque refresh token string to revoke.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the token was found and revoked; <c>false</c> if the token
    /// does not exist (already purged or was never issued).
    /// </returns>
    Task<bool> RevokeAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>
    /// Revokes ALL active refresh tokens for a user in a single batch.
    /// Called on password change, admin force-logout, and suspected compromise.
    /// Security stamps should be updated separately to invalidate in-flight JWTs
    /// and existing cookies.
    /// </summary>
    /// <param name="userId">ID of the user whose tokens are to be revoked.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
}
