using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Replicate;

/// <summary>
/// JSON serialization context for Replicate provider types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ReplicateProviderConfig))]
internal partial class ReplicateJsonContext : JsonSerializerContext
{
}
