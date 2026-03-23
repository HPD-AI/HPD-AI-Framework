namespace HPD.Auth.Core.Options;

/// <summary>
/// Configuration for magic link (passwordless email) sign-in.
/// </summary>
public class MagicLinkOptions
{
    /// <summary>
    /// How long a magic link remains valid after issuance.
    /// Short windows reduce the risk of a leaked link being reused.
    /// Defaults to 15 minutes.
    /// </summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Base URL used when constructing the magic link.
    /// Example: "https://yourapp.com/auth/magic"
    /// The token will be appended as a query parameter.
    /// Leave empty to use the app's base URL.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Whether magic links are single-use (invalidated after first click).
    /// Defaults to true for security.
    /// </summary>
    public bool SingleUse { get; set; } = true;

    /// <summary>
    /// Minimum interval between magic link requests for the same email address.
    /// Prevents email flooding. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(60);
}
