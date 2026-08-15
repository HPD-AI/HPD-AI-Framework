using System.Text.Json.Serialization;

namespace HPD.Gateway;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(GatewayConfiguration))]
[JsonSerializable(typeof(GatewayConfigurationParseResult))]
[JsonSerializable(typeof(GatewayPortableDocumentResult))]
[JsonSerializable(typeof(GatewayCanonicalizationResult))]
[JsonSerializable(typeof(GatewayValidationResult))]
[JsonSerializable(typeof(TrafficAdmissionPlan))]
internal partial class GatewayJsonSerializerContext : JsonSerializerContext;
