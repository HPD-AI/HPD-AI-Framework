namespace HPD.Auth.Core.Options;

/// <summary>
/// Configuration for external OAuth/OIDC provider integrations.
/// Each provider is keyed by its canonical name (e.g., "google", "github").
/// </summary>
public class OAuthOptions
{
    /// <summary>
    /// Per-provider OAuth configuration.
    /// Keys are provider names: "google", "github", "microsoft", "apple", etc.
    /// </summary>
    public Dictionary<string, OAuthProviderOptions> Providers { get; set; } = new();

    /// <summary>
    /// Whether to automatically create a local user account when a new
    /// OAuth identity is encountered. Defaults to true.
    /// Set to false if you want to control user provisioning manually.
    /// </summary>
    public bool AutoProvisionUsers { get; set; } = true;

    /// <summary>
    /// Whether to automatically link an OAuth login to an existing local account
    /// with the same email address. Defaults to true.
    /// Set to false in high-security environments to prevent account takeover
    /// via unverified provider emails.
    /// </summary>
    public bool AutoLinkAccounts { get; set; } = true;

    /// <summary>
    /// Whether to store raw OAuth profile claims in UserIdentities.IdentityData (JSONB).
    /// Defaults to true. Useful for debugging and future claim migrations.
    /// </summary>
    public bool StoreRawProfileData { get; set; } = true;
}
