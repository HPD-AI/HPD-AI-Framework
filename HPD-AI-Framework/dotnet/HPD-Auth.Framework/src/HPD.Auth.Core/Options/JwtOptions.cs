namespace HPD.Auth.Core.Options;

/// <summary>
/// Configuration for JWT access token and refresh token issuance and validation.
/// </summary>
public class JwtOptions
{
    /// <summary>
    /// Secret key used to sign JWTs (HS256).
    /// Must be at least 32 characters in production.
    /// For asymmetric signing, use <see cref="RsaPrivateKey"/> instead.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// RSA private key (PEM format) for RS256 signing.
    /// When set, takes priority over <see cref="Secret"/>.
    /// </summary>
    public string? RsaPrivateKey { get; set; }

    /// <summary>
    /// JWT Issuer claim (iss). Typically your API base URL.
    /// Example: "https://api.yourapp.com"
    /// </summary>
    public string Issuer { get; set; } = "HPD.Auth";

    /// <summary>
    /// JWT Audience claim (aud). Typically your app name or front-end URL.
    /// Example: "https://yourapp.com"
    /// </summary>
    public string Audience { get; set; } = "HPD";

    /// <summary>
    /// Lifetime of JWT access tokens. Defaults to 15 minutes.
    /// Short lifetimes limit the blast radius of a leaked token.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Lifetime of refresh tokens. Defaults to 14 days.
    /// Refresh tokens are rotated on every use.
    /// </summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// Whether to validate the token's lifetime during validation.
    /// Only set to false in testing scenarios.
    /// </summary>
    public bool ValidateLifetime { get; set; } = true;

    /// <summary>
    /// Allowed clock skew when validating token expiry.
    /// Accommodates minor clock drift between services.
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);
}
