namespace HPD.Auth.Core.Entities;

/// <summary>
/// Represents a refresh token issued to a user alongside an access token.
/// Token is a base64-encoded random byte sequence.
/// v2.2: Added InstanceId for multi-tenancy.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The refresh token value (base64-encoded random bytes).
    /// </summary>
    public string Token { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    /// <summary>
    /// Multi-tenancy discriminator. Defaults to Guid.Empty for single-tenant apps.
    /// </summary>
    public Guid InstanceId { get; set; } = Guid.Empty;

    /// <summary>
    /// Links this refresh token to the access token's JTI claim.
    /// Used to validate that both tokens were issued together.
    /// </summary>
    public string JwtId { get; set; } = string.Empty;

    /// <summary>
    /// The user's SecurityStamp at the time this token was issued.
    /// On refresh, compared against the current stamp — a mismatch means the
    /// stamp was rotated (e.g., global logout, password reset) and this token
    /// must be rejected, even if it hasn't been used or revoked yet.
    /// </summary>
    public string SecurityStamp { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsUsed { get; set; } = false;

    public bool IsRevoked { get; set; } = false;

    public DateTime? RevokedAt { get; set; }

    // Navigation
    public ApplicationUser User { get; set; } = null!;
}
