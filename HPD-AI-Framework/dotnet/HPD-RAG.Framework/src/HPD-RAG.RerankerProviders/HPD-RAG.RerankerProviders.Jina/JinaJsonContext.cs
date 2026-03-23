using System.Text.Json.Serialization;

namespace HPD.RAG.RerankerProviders.Jina;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JinaRerankRequest))]
[JsonSerializable(typeof(JinaRerankDocument))]
[JsonSerializable(typeof(JinaRerankDocument[]))]
[JsonSerializable(typeof(JinaRerankResponse))]
[JsonSerializable(typeof(JinaRerankResult))]
[JsonSerializable(typeof(JinaRerankResult[]))]
internal sealed partial class JinaJsonContext : JsonSerializerContext { }

/// <summary>Wire format for Jina AI POST /v1/rerank request.</summary>
internal sealed class JinaRerankRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("query")]
    public required string Query { get; set; }

    [JsonPropertyName("documents")]
    public required JinaRerankDocument[] Documents { get; set; }

    [JsonPropertyName("top_n")]
    public int? TopN { get; set; }
}

internal sealed class JinaRerankDocument
{
    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

/// <summary>Wire format for Jina AI /v1/rerank response.</summary>
internal sealed class JinaRerankResponse
{
    [JsonPropertyName("results")]
    public JinaRerankResult[]? Results { get; set; }
}

internal sealed class JinaRerankResult
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("relevance_score")]
    public double RelevanceScore { get; set; }
}
