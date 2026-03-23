using Microsoft.AspNetCore.Http;

namespace HPD.Auth.Core.Options;

/// <summary>
/// Configuration for authentication cookie behavior.
/// Named HPDCookieOptions to avoid naming conflict with
/// Microsoft.AspNetCore.Http.CookieOptions.
/// </summary>
public class HPDCookieOptions
{
    /// <summary>
    /// Name of the authentication cookie.
    /// </summary>
    public string CookieName { get; set; } = ".HPD.Auth";

    /// <summary>
    /// Cookie SameSite policy.
    /// Defaults to Lax — safe for standard web apps and most OAuth flows.
    /// </summary>
    public SameSiteMode SameSite { get; set; } = SameSiteMode.Lax;

    /// <summary>
    /// Whether the cookie requires HTTPS. Always enable in production.
    /// </summary>
    public bool SecurePolicy { get; set; } = true;

    /// <summary>
    /// Whether the cookie is inaccessible to JavaScript (HttpOnly).
    /// Strongly recommended: true. Prevents XSS token theft.
    /// </summary>
    public bool HttpOnly { get; set; } = true;

    /// <summary>
    /// Sliding expiration window. Cookie validity is refreshed on each request.
    /// </summary>
    public TimeSpan SlidingExpiration { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// Absolute maximum lifetime of the auth cookie.
    /// </summary>
    public TimeSpan AbsoluteExpiration { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Whether to use sliding expiration (renew cookie on each request).
    /// </summary>
    public bool UseSlidingExpiration { get; set; } = true;

    /// <summary>
    /// Cookie domain. Leave null to default to the current request domain.
    /// Set to ".yourapp.com" to share across subdomains.
    /// </summary>
    public string? Domain { get; set; }

    /// <summary>
    /// Cookie path. Defaults to "/" (available site-wide).
    /// </summary>
    public string Path { get; set; } = "/";
}
