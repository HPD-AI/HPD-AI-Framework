using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.GoogleAI;

/// <summary>
/// Selects which Google platform adapter backs the provider-created chat client.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoogleAIPlatform>))]
public enum GoogleAIPlatform
{
    /// <summary>
    /// Use the Gemini Developer API with API-key authentication.
    /// </summary>
    GeminiDeveloperApi = 0,

    /// <summary>
    /// Use Vertex AI through Google Cloud credentials.
    /// </summary>
    VertexAI = 1
}

/// <summary>
/// Google AI provider-specific configuration.
/// </summary>
public class GoogleAIProviderConfig
{
    /// <summary>
    /// Selects the Google platform adapter used to construct chat clients.
    /// Runtime model-call behavior belongs in ChatClientConfig.
    /// </summary>
    public GoogleAIPlatform Platform { get; set; } = GoogleAIPlatform.GeminiDeveloperApi;

    /// <summary>
    /// Optional Google API version, such as "v1", "v1beta", or "v1beta1".
    /// </summary>
    public string? ApiVersion { get; set; }

    /// <summary>
    /// Google Cloud project id used by Vertex AI.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// Google Cloud region used by Vertex AI.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Enables Vertex AI Express mode, which uses API-key authentication.
    /// </summary>
    public bool ExpressMode { get; set; }

    /// <summary>
    /// Optional credentials file path for Vertex AI Application Default Credentials.
    /// </summary>
    public string? CredentialsFile { get; set; }

    /// <summary>
    /// Controls whether the underlying adapter validates supplied access tokens.
    /// </summary>
    public bool ValidateAccessToken { get; set; } = true;
}
