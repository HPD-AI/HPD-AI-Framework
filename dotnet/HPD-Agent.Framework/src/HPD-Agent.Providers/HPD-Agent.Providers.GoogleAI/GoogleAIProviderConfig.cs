using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.GoogleAI;

/// <summary>
/// Google AI provider-specific configuration.
/// </summary>
public class GoogleAIProviderConfig : global::HPD.Agent.IProviderConfig
{
    /// <summary>
    /// Optional Google API version, such as "v1", "v1beta", or "v1beta1".
    /// </summary>
    public string? ApiVersion { get; set; }

    /// <summary>
    /// Controls whether the underlying adapter validates supplied access tokens.
    /// </summary>
    public bool ValidateAccessToken { get; set; } = true;
}
