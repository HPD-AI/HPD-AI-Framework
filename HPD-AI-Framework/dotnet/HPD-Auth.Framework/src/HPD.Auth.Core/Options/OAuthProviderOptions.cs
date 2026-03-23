namespace HPD.Auth.Core.Options;

/// <summary>
/// Configuration for a single external OAuth/OIDC provider.
/// Used within OAuthOptions.Providers dictionary.
/// </summary>
public class OAuthProviderOptions
{
    /// <summary>
    /// OAuth Client ID issued by the provider.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth Client Secret issued by the provider.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Additional scopes to request beyond the defaults.
    /// Defaults vary by provider (e.g., Google defaults to "openid email profile").
    /// </summary>
    public IList<string> AdditionalScopes { get; set; } = new List<string>();

    /// <summary>
    /// Whether this provider is enabled. Defaults to true.
    /// Allows disabling a provider without removing its configuration.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional callback/redirect URI override.
    /// Leave null to use the auto-generated default.
    /// </summary>
    public string? CallbackPath { get; set; }
}
