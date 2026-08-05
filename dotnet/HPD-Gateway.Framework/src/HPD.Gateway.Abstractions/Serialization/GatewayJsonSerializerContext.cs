using System.Text.Json.Serialization;
using HPD.Gateway.Abstractions;

namespace HPD.Gateway.Abstractions.Serialization;

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
public partial class GatewayJsonSerializerContext : JsonSerializerContext;
