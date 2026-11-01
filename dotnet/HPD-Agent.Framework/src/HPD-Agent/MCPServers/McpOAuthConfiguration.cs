using System.Text.Json.Serialization;

namespace HPD.Agent.MCP;

/// <summary>Selects the OAuth client-registration mechanism.</summary>
public enum McpOAuthClientRegistrationMode
{
    /// <summary>Use a Client ID Metadata Document.</summary>
    ClientIdMetadataDocument,
    /// <summary>Use application-provided registration credentials.</summary>
    PreRegistered,
    /// <summary>Use compatibility-only Dynamic Client Registration.</summary>
    DynamicRegistration
}

/// <summary>Configures OAuth registration without containing persisted token state.</summary>
public sealed class McpOAuthOptions
{
    /// <summary>Gets or sets the registration mechanism.</summary>
    public McpOAuthClientRegistrationMode RegistrationMode { get; set; } =
        McpOAuthClientRegistrationMode.ClientIdMetadataDocument;
    /// <summary>Gets or sets whether compatibility-only DCR is explicitly authorized.</summary>
    public bool AllowDynamicRegistration { get; set; }
    /// <summary>Gets or sets the application redirect URI.</summary>
    public Uri? RedirectUri { get; set; }
    /// <summary>Gets or sets the HTTPS Client ID Metadata Document URI.</summary>
    public Uri? ClientIdMetadataDocument { get; set; }
    /// <summary>Gets or sets a pre-registered client identifier.</summary>
    public string? ClientId { get; set; }
    /// <summary>Gets or sets a secret-store key for a pre-registered client secret.</summary>
    public string? ClientSecretKey { get; set; }
    /// <summary>Gets or sets requested scopes.</summary>
    public IReadOnlyList<string> Scopes { get; set; } = [];
}
