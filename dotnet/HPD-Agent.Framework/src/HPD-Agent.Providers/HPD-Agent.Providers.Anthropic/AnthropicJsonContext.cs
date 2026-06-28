using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Anthropic;

/// <summary>
/// JSON serialization context for Anthropic provider types.
/// Enables AOT-compatible serialization for FFI scenarios.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AnthropicProviderConfig))]
[JsonSerializable(typeof(AnthropicChatRequestOptions))]
[JsonSerializable(typeof(AnthropicServiceTier))]
[JsonSerializable(typeof(AnthropicThinkingDisplay))]
[JsonSerializable(typeof(AnthropicCacheControlConfig))]
[JsonSerializable(typeof(AnthropicCacheTtl))]
public partial class AnthropicJsonContext : JsonSerializerContext
{
}
