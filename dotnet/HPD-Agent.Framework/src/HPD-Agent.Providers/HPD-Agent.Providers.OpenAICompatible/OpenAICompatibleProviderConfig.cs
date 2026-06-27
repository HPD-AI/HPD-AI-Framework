using System.Collections.Generic;

namespace HPD.Agent.Providers.OpenAICompatible;

/// <summary>
/// Common provider-specific options for OpenAI-compatible chat-completions providers.
/// </summary>
public class OpenAICompatibleProviderConfig
{
    public float? Temperature { get; set; }
    public float? TopP { get; set; }
    public int? MaxOutputTokens { get; set; }
    public List<string>? StopSequences { get; set; }
    public long? Seed { get; set; }
    public string? ResponseFormat { get; set; }
    public string? ToolChoice { get; set; }
}

