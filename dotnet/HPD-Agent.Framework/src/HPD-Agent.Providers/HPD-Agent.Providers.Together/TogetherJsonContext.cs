using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Together;

/// <summary>
/// JSON serialization context for Together provider types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(TogetherProviderConfig))]
internal partial class TogetherJsonContext : JsonSerializerContext
{
}
