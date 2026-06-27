using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Venice;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(VeniceProviderConfig))]
internal partial class VeniceJsonContext : JsonSerializerContext
{
}
