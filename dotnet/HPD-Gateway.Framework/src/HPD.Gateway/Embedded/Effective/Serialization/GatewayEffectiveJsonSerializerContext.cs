using System.Text.Json.Serialization;

namespace HPD.Gateway;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(GatewayAppliedRuntimeSnapshot))]
[JsonSerializable(typeof(GatewayAppliedRuntimeObservation))]
internal partial class GatewayEffectiveJsonSerializerContext : JsonSerializerContext;
