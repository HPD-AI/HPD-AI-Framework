using System.Text.Json.Serialization;

namespace HPD.RAG.RerankerProviders.OnnxRuntime;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OnnxRuntimeRerankerConfig))]
internal sealed partial class OnnxRuntimeJsonContext : JsonSerializerContext { }
