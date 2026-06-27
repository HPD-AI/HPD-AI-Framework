using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Hyperbolic;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(HyperbolicProviderConfig))]
internal partial class HyperbolicJsonContext : JsonSerializerContext
{
}
