using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.RAG.RerankerProviders.Cohere;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CohereRerankerConfig))]
[JsonSerializable(typeof(CohereRerankRequest))]
[JsonSerializable(typeof(CohereRerankResponse))]
[JsonSerializable(typeof(CohereRerankResult))]
[JsonSerializable(typeof(CohereRerankResult[]))]
internal sealed partial class CohereJsonContext : JsonSerializerContext { }

/// <summary>Wire format for Cohere /v2/rerank request.</summary>
internal sealed class CohereRerankRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("query")]
    public required string Query { get; set; }

    [JsonPropertyName("documents")]
    public required string[] Documents { get; set; }

    [JsonPropertyName("top_n")]
    public int? TopN { get; set; }
}

/// <summary>Wire format for Cohere /v2/rerank response.</summary>
internal sealed class CohereRerankResponse
{
    [JsonPropertyName("results")]
    public CohereRerankResult[]? Results { get; set; }
}

internal sealed class CohereRerankResult
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("relevance_score")]
    public double RelevanceScore { get; set; }
}
