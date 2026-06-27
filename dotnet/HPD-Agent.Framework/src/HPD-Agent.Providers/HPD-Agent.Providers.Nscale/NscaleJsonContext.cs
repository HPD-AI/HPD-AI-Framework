using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Nscale;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(NscaleProviderConfig))]
internal partial class NscaleJsonContext : JsonSerializerContext
{
}
