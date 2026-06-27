using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Xai;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(XaiProviderConfig))]
internal partial class XaiJsonContext : JsonSerializerContext
{
}
