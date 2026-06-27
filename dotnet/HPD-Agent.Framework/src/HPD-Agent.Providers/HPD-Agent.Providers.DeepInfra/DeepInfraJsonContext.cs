using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.DeepInfra;

/// <summary>
/// JSON serialization context for DeepInfra provider types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(DeepInfraProviderConfig))]
internal partial class DeepInfraJsonContext : JsonSerializerContext
{
}
