using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Cohere;

/// <summary>
/// JSON serialization context for Cohere provider types.
/// Enables AOT-compatible serialization for FFI scenarios.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(CohereProviderConfig))]
internal partial class CohereJsonContext : JsonSerializerContext
{
}
