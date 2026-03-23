using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Bedrock;

/// <summary>
/// JSON source generation context for AOT compatibility.
/// Enables Native AOT compilation by generating serialization code at build time.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BedrockProviderConfig))]
internal partial class BedrockJsonContext : JsonSerializerContext
{
}
