using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.DeepSeek;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(DeepSeekProviderConfig))]
internal partial class DeepSeekJsonContext : JsonSerializerContext
{
}
