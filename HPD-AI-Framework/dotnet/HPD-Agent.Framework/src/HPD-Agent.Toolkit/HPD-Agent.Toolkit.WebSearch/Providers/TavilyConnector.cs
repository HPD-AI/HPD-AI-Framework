using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Agent.Toolkit.WebSearch;

/// <summary>
/// Tavily web search connector that provides AI-powered search with answers
/// </summary>
public class TavilyConnector : IWebSearchConnector
{
    private readonly TavilyConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private const string ApiEndpoint = "https://api.tavily.com/search";
    
    /// <inheritdoc />
    public string ProviderName => "tavily";
    
    /// <inheritdoc />
    public bool SupportsNews => true;
    
    /// <inheritdoc />
    public bool SupportsAIAnswer => true;
    
    /// <summary>
    /// Initializes a new instance of the TavilyConnector
    /// </summary>
    /// <param name="config">Tavily configuration</param>
    /// <param name="httpClient">HTTP client for API requests</param>
    /// <param name="logger">Logger instance</param>
    public TavilyConnector(TavilyConfig config, HttpClient? httpClient = null, ILogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger ?? NullLogger.Instance;
        
        // Configure HTTP client
        _httpClient.Timeout = _config.Timeout ?? TimeSpan.FromSeconds(30);
        
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HPD-Agent/1.0");
        }
    }
    
    /// <inheritdoc />
    public async Task<SearchResult> SearchAsync(string query, int count = 5, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));
            
        var request = new TavilySearchRequest
        {
            Query = query,
            MaxResults = Math.Min(count, _config.MaxResults ?? 10),
            SearchDepth = _config.SearchDepth?.ToString()?.ToLowerInvariant(),
            ChunksPerSource = _config.ChunksPerSource,
            IncludeAnswer = _config.IncludeAnswer ?? false,
            IncludeRawContent = _config.IncludeRawContent ?? false,
            IncludeImages = _config.IncludeImages ?? false,
            Topic = _config.Topic,
            TimeRange = _config.TimeRange
        };
        
        try
        {
            var response = await ExecuteSearchAsync(request, cancellationToken);
            return MapToSearchResult(response, query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tavily search failed for query: {Query}", query);
            _config.ErrorHandler?.Invoke(ex);
            
            return new SearchResult
            {
                Query = query,
                ProviderName = ProviderName,
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }
    
    /// <inheritdoc />
    public async Task<SearchResult> SearchNewsAsync(string query, string timeRange = "week", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));
            
        var request = new TavilySearchRequest
        {
            Query = query,
            MaxResults = _config.MaxResults ?? 10,
            SearchDepth = _config.SearchDepth?.ToString()?.ToLowerInvariant(),
            ChunksPerSource = _config.ChunksPerSource,
            IncludeAnswer = _config.IncludeAnswer ?? false,
            IncludeRawContent = _config.IncludeRawContent ?? false,
            IncludeImages = _config.IncludeImages ?? false,
            Topic = "news", // Force news topic
            TimeRange = timeRange
        };
        
        try
        {
            var response = await ExecuteSearchAsync(request, cancellationToken);
            return MapToSearchResult(response, query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tavily news search failed for query: {Query}", query);
            _config.ErrorHandler?.Invoke(ex);
            
            return new SearchResult
            {
                Query = query,
                ProviderName = ProviderName,
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }
    
    /// <inheritdoc />
    public Task<SearchResult> SearchVideosAsync(string query, int count = 5, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Tavily does not support video search. Use Brave or Bing for video search capabilities.");
    }
    
    /// <inheritdoc />
    public async Task<AnswerResult> SearchWithAnswerAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));
            
        var request = new TavilySearchRequest
        {
            Query = query,
            MaxResults = _config.MaxResults ?? 10,
            SearchDepth = "advanced", // Force advanced for better answers
            ChunksPerSource = _config.ChunksPerSource ?? 1,
            IncludeAnswer = true, // Always include answers for this method
            IncludeRawContent = _config.IncludeRawContent ?? true,
            IncludeImages = _config.IncludeImages ?? false,
            Topic = _config.Topic,
            TimeRange = _config.TimeRange
        };
        
        try
        {
            var response = await ExecuteSearchAsync(request, cancellationToken);
            return MapToAnswerResult(response, query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tavily answer search failed for query: {Query}", query);
            _config.ErrorHandler?.Invoke(ex);
            
            return new AnswerResult
            {
                Query = query,
                ProviderName = ProviderName,
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }
    
    /// <inheritdoc />
    public Task<SearchResult> SearchShoppingAsync(string query, int count = 5, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Tavily does not support shopping search. Use Bing for shopping search capabilities.");
    }
    
    /// <inheritdoc />
    public Task<SearchResult> SemanticSearchAsync(string query, int count = 5, CancellationToken cancellationToken = default)
    {
        // Tavily could potentially support semantic search via its advanced search
        // For now, we'll treat it as a regular search with advanced depth
        return SearchAsync(query, count, cancellationToken);
    }
    
    /// <summary>
    /// Executes the search request against the Tavily API
    /// </summary>
    private async Task<TavilySearchResponse> ExecuteSearchAsync(TavilySearchRequest request, CancellationToken cancellationToken)
    {
        // Use AOT-compatible JSON serialization
        var json = JsonSerializer.Serialize(request, TavilyJsonContext.Default.TavilySearchRequest);
        
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint);
        httpRequest.Content = content;
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ApiKey);
        
        var startTime = DateTime.UtcNow;
        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseTime = DateTime.UtcNow - startTime;
        
        response.EnsureSuccessStatusCode();
        
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        
        // Use AOT-compatible JSON deserialization
        var searchResponse = JsonSerializer.Deserialize(responseJson, TavilyJsonContext.Default.TavilySearchResponse);
        
        if (searchResponse == null)
            throw new InvalidOperationException("Failed to deserialize Tavily response");
            
    // No ResponseTimeSeconds in enhanced model; just return the deserialized response
    return searchResponse;
    }
    
    /// <summary>
    /// Maps Tavily response to standard SearchResult
    /// </summary>
    private SearchResult MapToSearchResult(TavilySearchResponse response, string query)
    {
        var items = response.Results?.Select(r => new SearchItem
        {
            Title = r.Title ?? string.Empty,
            Url = r.Url ?? string.Empty,
            Snippet = r.Content ?? string.Empty,
            RawContent = r.RawContent,
            Score = r.Score,
            PublishedDate = DateTime.TryParse(r.PublishedDate, out var dt) ? dt : (DateTime?)null,
            ContentType = "web",
            ProviderData = new Dictionary<string, object>
            {
                ["tavily_score"] = r.Score
            }
        }).ToList() ?? new List<SearchItem>();

        return new SearchResult
        {
            Query = query,
            Items = items,
            ProviderName = ProviderName,
            // No ResponseTime in enhanced model, set to TimeSpan.Zero
            ResponseTime = TimeSpan.Zero,
            Metadata = new Dictionary<string, object>
            {
                ["total_results"] = items.Count,
                ["has_answer"] = !string.IsNullOrEmpty(response.Answer)
            }
        };
    }
    
    /// <summary>
    /// Maps Tavily response to AnswerResult with AI-generated answers
    /// </summary>
    private AnswerResult MapToAnswerResult(TavilySearchResponse response, string query)
    {
        var searchResult = MapToSearchResult(response, query);
        
        return new AnswerResult
        {
            Query = searchResult.Query,
            Items = searchResult.Items,
            ProviderName = searchResult.ProviderName,
            ResponseTime = searchResult.ResponseTime,
            Metadata = searchResult.Metadata,
            Answer = response.Answer ?? string.Empty,
            Sources = response.Results?.Where(r => !string.IsNullOrEmpty(r.Url)).Select(r => r.Url!).ToList() ?? new List<string>(),
            FollowUpQuestions = response.FollowUpQuestions ?? new List<string>()
        };
    }
    
    /// <summary>
    /// Disposes resources
    /// </summary>
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
