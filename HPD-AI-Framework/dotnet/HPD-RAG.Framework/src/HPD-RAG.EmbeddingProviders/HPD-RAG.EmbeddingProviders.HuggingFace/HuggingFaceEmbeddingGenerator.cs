using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.RAG.EmbeddingProviders.HuggingFace;

/// <summary>
/// IEmbeddingGenerator implementation for the HuggingFace Inference API
/// feature-extraction pipeline. Posts inputs to the pre-configured HttpClient
/// and returns float[] embeddings.
/// </summary>
internal sealed class HuggingFaceEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly HttpClient _httpClient;
    private readonly string _modelId;
    private EmbeddingGeneratorMetadata? _metadata;

    public HuggingFaceEmbeddingGenerator(HttpClient httpClient, string modelId)
    {
        _httpClient = httpClient;
        _modelId = modelId;
    }

    public EmbeddingGeneratorMetadata Metadata =>
        _metadata ??= new EmbeddingGeneratorMetadata(
            providerName: "huggingface",
            providerUri: _httpClient.BaseAddress,
            defaultModelId: _modelId);

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = values.ToList();
        if (inputs.Count == 0)
            return new GeneratedEmbeddings<Embedding<float>>();

        // HuggingFace feature-extraction: POST { "inputs": [...] }
        var request = new HuggingFaceEmbeddingRequest { Inputs = inputs };
        using var response = await _httpClient.PostAsJsonAsync(
            string.Empty,
            request,
            HuggingFaceEmbeddingRequestJsonContext.Default.HuggingFaceEmbeddingRequest,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        // Response is float[][] — one float[] per input
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var vectors = JsonSerializer.Deserialize(content,
            HuggingFaceEmbeddingRequestJsonContext.Default.ListListSingle);

        if (vectors is null || vectors.Count != inputs.Count)
            throw new InvalidOperationException(
                $"HuggingFace API returned {vectors?.Count ?? 0} embeddings for {inputs.Count} inputs.");

        var embeddings = new GeneratedEmbeddings<Embedding<float>>(
            vectors.Select(v => new Embedding<float>(v.ToArray())));

        return embeddings;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() => _httpClient.Dispose();
}

/// <summary>Request body for HuggingFace feature-extraction endpoint.</summary>
internal sealed class HuggingFaceEmbeddingRequest
{
    [JsonPropertyName("inputs")]
    public List<string> Inputs { get; set; } = [];
}

[JsonSerializable(typeof(HuggingFaceEmbeddingRequest))]
[JsonSerializable(typeof(List<List<float>>), TypeInfoPropertyName = "ListListSingle")]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class HuggingFaceEmbeddingRequestJsonContext : JsonSerializerContext
{
}
