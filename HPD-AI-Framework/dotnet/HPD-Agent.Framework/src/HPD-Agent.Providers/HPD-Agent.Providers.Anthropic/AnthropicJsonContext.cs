using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Anthropic;

/// <summary>
/// JSON serialization context for Anthropic provider types.
/// Enables AOT-compatible serialization for FFI scenarios.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AnthropicProviderConfig))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public partial class AnthropicJsonContext : JsonSerializerContext
{
}
