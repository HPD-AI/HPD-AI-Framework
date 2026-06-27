using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Groq;

/// <summary>
/// JSON serialization context for Groq provider types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(GroqProviderConfig))]
internal partial class GroqJsonContext : JsonSerializerContext
{
}
