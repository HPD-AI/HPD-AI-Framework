using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.OVHcloud;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(OVHcloudProviderConfig))]
internal partial class OVHcloudJsonContext : JsonSerializerContext
{
}
