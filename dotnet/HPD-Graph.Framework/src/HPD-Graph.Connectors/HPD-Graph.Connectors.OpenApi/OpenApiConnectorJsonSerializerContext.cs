using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.OpenApi.Core;
using HPD.Graph.Connectors.OpenApi.Handlers;

namespace HPD.Graph.Connectors.OpenApi;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false)]
[JsonSerializable(typeof(OpenApiCallOperationConfig))]
[JsonSerializable(typeof(OpenApiOperationResponse))]
[JsonSerializable(typeof(OpenApiErrorResponse))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(JsonElement))]
public sealed partial class OpenApiConnectorJsonSerializerContext : JsonSerializerContext;
