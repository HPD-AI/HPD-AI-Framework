using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.SambaNova;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(SambaNovaProviderConfig))]
internal partial class SambaNovaJsonContext : JsonSerializerContext
{
}
