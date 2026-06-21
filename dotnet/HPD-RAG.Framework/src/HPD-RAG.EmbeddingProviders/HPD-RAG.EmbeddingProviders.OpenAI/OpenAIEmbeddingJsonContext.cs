using System.Text.Json.Serialization;

namespace HPD.RAG.EmbeddingProviders.OpenAI;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OpenAIEmbeddingConfig))]
internal sealed partial class OpenAIEmbeddingJsonContext : JsonSerializerContext { }
