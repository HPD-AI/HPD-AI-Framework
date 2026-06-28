using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Mistral;

/// <summary>
/// JSON source generation context for AOT compatibility.
/// Enables Native AOT compilation by generating serialization code at build time.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(MistralProviderConfig))]
[JsonSerializable(typeof(MistralChatRequestOptions))]
internal partial class MistralJsonContext : JsonSerializerContext
{
}
