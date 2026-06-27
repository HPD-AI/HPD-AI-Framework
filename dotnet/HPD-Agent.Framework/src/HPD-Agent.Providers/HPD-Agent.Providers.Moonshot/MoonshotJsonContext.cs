using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Moonshot;

/// <summary>
/// JSON serialization context for Moonshot provider types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(MoonshotProviderConfig))]
internal partial class MoonshotJsonContext : JsonSerializerContext
{
}
