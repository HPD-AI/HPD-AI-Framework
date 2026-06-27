using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Fireworks;

/// <summary>
/// JSON serialization context for Fireworks provider types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(FireworksProviderConfig))]
internal partial class FireworksJsonContext : JsonSerializerContext
{
}
