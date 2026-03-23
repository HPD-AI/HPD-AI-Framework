using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Providers.Reranker;

namespace HPD.RAG.RerankerProviders.HuggingFace;

/// <summary>
/// Reranker backed by a HuggingFace Text Embeddings Inference (TEI) /rerank endpoint.
/// The TEI server is self-hosted; Endpoint is required. ApiKey is optional (for secured deployments).
/// </summary>
public sealed class HuggingFaceReranker : IReranker
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;

    public HuggingFaceReranker(HttpClient httpClient, RerankerConfig config)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.Endpoint))
            throw new InvalidOperationException(
                "HuggingFaceReranker requires a non-empty Endpoint in RerankerConfig pointing to the TEI /rerank route " +
                "(e.g. http://localhost:8080/rerank).");

        _httpClient = httpClient;
        _endpoint = config.Endpoint;

        // Optional bearer token for secured TEI deployments
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config.ApiKey);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MragSearchResultDto>> RerankAsync(
        string query,
        IReadOnlyList<MragSearchResultDto> results,
        int topN,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count == 0 || topN <= 0)
            return Array.Empty<MragSearchResultDto>();

        var texts = new string[results.Count];
        for (int i = 0; i < results.Count; i++)
            texts[i] = results[i].Content;

        var request = new HuggingFaceRerankRequest
        {
            Query = query,
            Texts = texts,
            Truncate = true
        };

        var requestJson = JsonSerializer.Serialize(request, HuggingFaceJsonContext.Default.HuggingFaceRerankRequest);
        using var httpContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(_endpoint, httpContent, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var rerankResults = JsonSerializer.Deserialize(responseBody, HuggingFaceJsonContext.Default.HuggingFaceRerankResultArray);

        if (rerankResults is null || rerankResults.Length == 0)
            return Array.Empty<MragSearchResultDto>();

        // Sort by score descending first, then trim to topN
        Array.Sort(rerankResults, static (a, b) => b.Score.CompareTo(a.Score));

        var effectiveTopN = Math.Min(topN, rerankResults.Length);
        var reranked = new List<MragSearchResultDto>(effectiveTopN);

        for (int i = 0; i < effectiveTopN; i++)
        {
            var item = rerankResults[i];
            if (item.Index < 0 || item.Index >= results.Count)
                continue;

            reranked.Add(results[item.Index] with { Score = item.Score });
        }

        return reranked.AsReadOnly();
    }
}
