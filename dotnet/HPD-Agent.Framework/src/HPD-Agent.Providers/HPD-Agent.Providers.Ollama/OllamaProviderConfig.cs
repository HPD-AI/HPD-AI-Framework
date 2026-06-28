namespace HPD.Agent.Providers.Ollama;

/// <summary>
/// Ollama-specific provider construction configuration.
/// </summary>
public sealed class OllamaProviderConfig
{
    /// <summary>
    /// Timeout, in milliseconds, applied to the underlying HTTP client.
    /// Runtime model behavior belongs in ChatDefaults or AgentRunConfig.Chat.
    /// </summary>
    public int? TimeoutMs { get; set; }
}
