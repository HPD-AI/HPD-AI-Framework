using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Auth.Core.Models;

/// <summary>
/// -compatible token response returned after successful authentication
/// or token refresh. Property names use snake_case to comply with OAuth 2.0
/// conventions (RFC 6749) and match the  GoTrue wire format.
/// </summary>
public sealed class TokenResponse
{
    /// <summary>
    /// JWT access token. Short-lived (default 15 minutes).
    /// Clients must include this in the Authorization: Bearer header.
    /// </summary>
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>
    /// Token type. Always "bearer" per RFC 6750.
    /// </summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "bearer";

    /// <summary>
    /// Number of seconds until the access token expires.
    /// Clients should use this to schedule proactive refresh.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    /// <summary>
    /// Unix timestamp (seconds since epoch) when the access token expires.
    /// Allows clients to check expiry without parsing the JWT.
    /// </summary>
    [JsonPropertyName("expires_at")]
    public required long ExpiresAt { get; init; }

    /// <summary>
    /// Opaque refresh token. Long-lived (default 14 days), single-use.
    /// On redemption the old token is marked used and a new pair is issued.
    /// Store securely — treat with the same care as a password.
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; init; }

    /// <summary>
    /// Embedded user object. Avoids a round-trip to /auth/user immediately
    /// after login — matches  GoTrue and ADR-003 Section 9.3.
    /// </summary>
    [JsonPropertyName("user")]
    public required UserTokenDto User { get; init; }
}

/// <summary>
/// Lightweight user projection embedded inside <see cref="TokenResponse"/>.
/// Contains only the fields a client needs immediately after authentication.
/// Full user data is available via GET /api/auth/user.
/// </summary>
public sealed class UserTokenDto
{
    /// <summary>
    /// User's unique identifier (UUID).
    /// </summary>
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    /// <summary>
    /// User's email address.
    /// </summary>
    [JsonPropertyName("email")]
    public required string Email { get; init; }

    /// <summary>
    /// Timestamp when the user confirmed their email address.
    /// Null if email has not been confirmed yet.
    /// </summary>
    [JsonPropertyName("email_confirmed_at")]
    public required DateTime? EmailConfirmedAt { get; init; }

    /// <summary>
    /// User-controlled metadata (writable by the user).
    /// Returned as a raw JSON element to preserve arbitrary structure.
    /// </summary>
    [JsonPropertyName("user_metadata")]
    public required JsonElement UserMetadata { get; init; }

    /// <summary>
    /// System/Admin-controlled metadata (not writable by the user).
    /// Security-critical: prevents privilege escalation.
    /// Returned as a raw JSON element to preserve arbitrary structure.
    /// </summary>
    [JsonPropertyName("app_metadata")]
    public required JsonElement AppMetadata { get; init; }

    /// <summary>
    /// Actions that must be completed before the user is granted full access.
    /// Examples: "VERIFY_EMAIL", "UPDATE_PASSWORD", "ACCEPT_TOS", "CONFIGURE_2FA".
    /// </summary>
    [JsonPropertyName("required_actions")]
    public required List<string> RequiredActions { get; init; }

    /// <summary>
    /// Timestamp when the user account was created (UTC).
    /// </summary>
    [JsonPropertyName("created_at")]
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// User's current subscription tier (e.g., "free", "pro", "enterprise").
    /// </summary>
    [JsonPropertyName("subscription_tier")]
    public required string SubscriptionTier { get; init; }
}
