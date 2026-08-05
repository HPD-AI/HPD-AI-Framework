using System.Text.Json.Serialization;

namespace HPD.Gateway.Effective.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(GatewayEffectiveSnapshot))]
public partial class GatewayEffectiveJsonSerializerContext : JsonSerializerContext;
