using System.Text.Json.Serialization;

namespace HPD.RAG.RerankerProviders.HuggingFace;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HuggingFaceRerankRequest))]
[JsonSerializable(typeof(HuggingFaceRerankResult))]
[JsonSerializable(typeof(HuggingFaceRerankResult[]))]
internal sealed partial class HuggingFaceJsonContext : JsonSerializerContext { }

/// <summary>Wire format for HuggingFace TEI POST /rerank request.</summary>
internal sealed class HuggingFaceRerankRequest
{
    [JsonPropertyName("query")]
    public required string Query { get; set; }

    [JsonPropertyName("texts")]
    public required string[] Texts { get; set; }

    [JsonPropertyName("truncate")]
    public bool Truncate { get; set; } = true;
}

/// <summary>
/// Wire format for a single item in the HuggingFace TEI /rerank response array.
/// TEI returns: [{"index": 0, "score": 0.98}, ...]
/// </summary>
internal sealed class HuggingFaceRerankResult
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("score")]
    public double Score { get; set; }
}
