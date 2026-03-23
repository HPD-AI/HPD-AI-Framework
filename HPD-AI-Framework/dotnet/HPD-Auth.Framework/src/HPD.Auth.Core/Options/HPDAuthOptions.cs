using System.Security.Claims;
using HPD.Auth.Core.Entities;

namespace HPD.Auth.Core.Options;

/// <summary>
/// Root configuration object for the HPD.Auth library.
/// Bind this to your appsettings.json under the "HPDAuth" section:
/// <code>
/// services.Configure&lt;HPDAuthOptions&gt;(configuration.GetSection("HPDAuth"));
/// </code>
/// </summary>
public class HPDAuthOptions
{
    /// <summary>
    /// Application display name used in emails and audit logs.
    /// Defaults to "HPD".
    /// </summary>
    public string AppName { get; set; } = "HPD";

    /// <summary>
    /// Database connection and migration options.
    /// </summary>
    public DatabaseOptions Database { get; set; } = new();

    /// <summary>
    /// JWT access and refresh token issuance options.
    /// </summary>
    public JwtOptions Jwt { get; set; } = new();

    /// <summary>
    /// Authentication cookie options.
    /// Named Cookie on this root; the nested type is HPDCookieOptions to avoid
    /// collision with Microsoft.AspNetCore.Http.CookieOptions.
    /// </summary>
    public HPDCookieOptions Cookie { get; set; } = new();

    /// <summary>
    /// Password complexity and history policy.
    /// </summary>
    public PasswordPolicyOptions Password { get; set; } = new();

    /// <summary>
    /// Account lockout policy.
    /// </summary>
    public LockoutPolicyOptions Lockout { get; set; } = new();

    /// <summary>
    /// Rate limiting for auth endpoints.
    /// </summary>
    public RateLimitingOptions RateLimiting { get; set; } = new();

    /// <summary>
    /// External OAuth/OIDC provider configuration.
    /// </summary>
    public OAuthOptions OAuth { get; set; } = new();

    /// <summary>
    /// Feature flags enabling or disabling optional capabilities.
    /// </summary>
    public FeaturesOptions Features { get; set; } = new();

    /// <summary>
    /// Magic link (passwordless email) configuration.
    /// </summary>
    public MagicLinkOptions MagicLink { get; set; } = new();

    /// <summary>
    /// Security hardening options.
    /// </summary>
    public SecurityOptions Security { get; set; } = new();

    /// <summary>
    /// SMS OTP delivery options.
    /// </summary>
    public SmsOptions Sms { get; set; } = new();

    /// <summary>
    /// Optional factory for injecting additional claims into JWT access tokens.
    /// Called after the standard claims (sub, email, roles) are added.
    /// Example: adding a "plan" claim from the user's SubscriptionTier.
    /// </summary>
    public Func<ApplicationUser, IList<Claim>, Task>? AdditionalClaimsFactory { get; set; }
}
