using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Providers.Reranker;

namespace HPD.RAG.RerankerProviders.Jina;

/// <summary>
/// Reranker backed by the Jina AI /v1/rerank REST API (https://api.jina.ai/v1/rerank).
/// </summary>
public sealed class JinaReranker : IReranker
{
    private const string DefaultEndpoint = "https://api.jina.ai/v1/rerank";
    private const string DefaultModel = "jina-reranker-v2-base-multilingual";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _endpoint;

    public JinaReranker(HttpClient httpClient, RerankerConfig config)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("JinaReranker requires a non-empty ApiKey in RerankerConfig.");

        _httpClient = httpClient;
        _apiKey = config.ApiKey;
        _model = string.IsNullOrWhiteSpace(config.ModelName) ? DefaultModel : config.ModelName;
        _endpoint = string.IsNullOrWhiteSpace(config.Endpoint) ? DefaultEndpoint : config.Endpoint;
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

        var effectiveTopN = Math.Min(topN, results.Count);

        var documents = new JinaRerankDocument[results.Count];
        for (int i = 0; i < results.Count; i++)
            documents[i] = new JinaRerankDocument { Text = results[i].Content };

        var request = new JinaRerankRequest
        {
            Model = _model,
            Query = query,
            Documents = documents,
            TopN = effectiveTopN
        };

        var requestJson = JsonSerializer.Serialize(request, JinaJsonContext.Default.JinaRerankRequest);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        httpRequest.Content = content;

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var rerankResponse = JsonSerializer.Deserialize(responseBody, JinaJsonContext.Default.JinaRerankResponse);

        if (rerankResponse?.Results is null || rerankResponse.Results.Length == 0)
            return Array.Empty<MragSearchResultDto>();

        var reranked = new List<MragSearchResultDto>(rerankResponse.Results.Length);
        foreach (var result in rerankResponse.Results)
        {
            if (result.Index < 0 || result.Index >= results.Count)
                continue;

            reranked.Add(results[result.Index] with { Score = result.RelevanceScore });
        }

        // Sort defensively descending by score
        reranked.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        return reranked.AsReadOnly();
    }
}
