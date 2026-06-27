using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Cerebras;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(CerebrasProviderConfig))]
internal partial class CerebrasJsonContext : JsonSerializerContext
{
}
