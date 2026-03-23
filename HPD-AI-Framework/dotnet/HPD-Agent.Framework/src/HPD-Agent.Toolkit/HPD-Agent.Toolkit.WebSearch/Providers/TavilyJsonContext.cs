using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Toolkit.WebSearch;

/// <summary>
/// AOT-compatible JSON serialization context for Tavily API models
/// </summary>
[JsonSerializable(typeof(TavilySearchRequest))]
[JsonSerializable(typeof(TavilySearchResponse))]
[JsonSerializable(typeof(TavilyResult))]
[JsonSerializable(typeof(TavilyImageResult))]
[JsonSerializable(typeof(TavilyErrorResponse))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(List<TavilyResult>))]
[JsonSerializable(typeof(List<TavilyImageResult>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
public partial class TavilyJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Comprehensive request model for Tavily Search API with all supported parameters
/// </summary>
public class TavilySearchRequest
{
    /// <summary>
    /// The search query (required)
    /// </summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;
    
    /// <summary>
    /// Search depth: "basic" or "advanced" (default: "basic")
    /// Advanced search uses 2 API credits vs 1 for basic
    /// </summary>
    [JsonPropertyName("search_depth")]
    public string? SearchDepth { get; set; }
    
    /// <summary>
    /// Topic category: "general", "news", "finance", "health", "scientific", "travel" (default: "general")
    /// </summary>
    [JsonPropertyName("topic")]
    public string? Topic { get; set; }
    
    /// <summary>
    /// Number of days back for news search (only when topic is "news")
    /// </summary>
    [JsonPropertyName("days")]
    public int? Days { get; set; }
    
    /// <summary>
    /// Time range: "day", "week", "month", "year" or "d", "w", "m", "y"
    /// </summary>
    [JsonPropertyName("time_range")]
    public string? TimeRange { get; set; }
    
    /// <summary>
    /// Maximum number of search results (0-20, default: 5)
    /// </summary>
    [JsonPropertyName("max_results")]
    public int? MaxResults { get; set; }
    
    /// <summary>
    /// Number of content chunks per source (1-3, only for advanced search)
    /// </summary>
    [JsonPropertyName("chunks_per_source")]
    public int? ChunksPerSource { get; set; }
    
    /// <summary>
    /// Include query-related images in response
    /// </summary>
    [JsonPropertyName("include_images")]
    public bool? IncludeImages { get; set; }
    
    /// <summary>
    /// Include image descriptions with images
    /// </summary>
    [JsonPropertyName("include_image_descriptions")]
    public bool? IncludeImageDescriptions { get; set; }
    
    /// <summary>
    /// Include AI-generated answer: false, true/"basic", or "advanced"
    /// </summary>
    [JsonPropertyName("include_answer")]
    public object? IncludeAnswer { get; set; }
    
    /// <summary>
    /// Include raw HTML content: false, true/"markdown", or "text"
    /// </summary>
    [JsonPropertyName("include_raw_content")]
    public object? IncludeRawContent { get; set; }
    
    /// <summary>
    /// List of domains to specifically include
    /// </summary>
    [JsonPropertyName("include_domains")]
    public string[]? IncludeDomains { get; set; }
    
    /// <summary>
    /// List of domains to specifically exclude
    /// </summary>
    [JsonPropertyName("exclude_domains")]
    public string[]? ExcludeDomains { get; set; }
    
    /// <summary>
    /// Country code to boost results from specific country (only for general topic)
    /// </summary>
    [JsonPropertyName("country")]
    public string? Country { get; set; }
    
    /// <summary>
    /// Include favicon URLs in results
    /// </summary>
    [JsonPropertyName("include_favicon")]
    public bool? IncludeFavicon { get; set; }
    
    /// <summary>
    /// Enable automatic parameter optimization based on query (BETA)
    /// </summary>
    [JsonPropertyName("auto_parameters")]
    public bool? AutoParameters { get; set; }
}

/// <summary>
/// Response model for Tavily Search API
/// </summary>
public class TavilySearchResponse
{
    /// <summary>
    /// The original search query
    /// </summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;
    
    /// <summary>
    /// AI-generated answer (if include_answer was true)
    /// </summary>
    [JsonPropertyName("answer")]
    public string? Answer { get; set; }
    
    /// <summary>
    /// List of search results ranked by relevancy
    /// </summary>
    [JsonPropertyName("results")]
    public List<TavilyResult> Results { get; set; } = new();
    
    /// <summary>
    /// Query-related images (if include_images was true)
    /// Can be List<string> or List<TavilyImageResult> depending on include_image_descriptions
    /// </summary>
    [JsonPropertyName("images")]
    public List<object>? Images { get; set; }
    
    /// <summary>
    /// Response time in seconds
    /// </summary>
    [JsonPropertyName("response_time")]
    public double ResponseTime { get; set; } = 0;
    
    /// <summary>
    /// Auto-detected parameters (if auto_parameters was enabled)
    /// </summary>
    [JsonPropertyName("auto_parameters")]
    public Dictionary<string, object>? AutoParameters { get; set; }
    
    /// <summary>
    /// Follow-up questions (when available)
    /// </summary>
    [JsonPropertyName("follow_up_questions")]
    public List<string>? FollowUpQuestions { get; set; }
    
    /// <summary>
    /// Computed response time as TimeSpan
    /// </summary>
    [JsonIgnore]
    public TimeSpan ResponseTimeSpan
    {
        get
        {
            return TimeSpan.FromSeconds(ResponseTime);
        }
    }
}

/// <summary>
/// Individual search result from Tavily API
/// </summary>
public class TavilyResult
{
    /// <summary>
    /// Title of the search result
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
    
    /// <summary>
    /// URL of the search result
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
    
    /// <summary>
    /// Most query-related content snippet
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// Relevance score (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }
    
    /// <summary>
    /// Full raw HTML content (if include_raw_content was true)
    /// </summary>
    [JsonPropertyName("raw_content")]
    public string? RawContent { get; set; }
    
    /// <summary>
    /// Publication date (for news results)
    /// </summary>
    [JsonPropertyName("published_date")]
    public string? PublishedDate { get; set; }
    
    /// <summary>
    /// Favicon URL (if include_favicon was true)
    /// </summary>
    [JsonPropertyName("favicon")]
    public string? Favicon { get; set; }
}

/// <summary>
/// Image result with AI-generated description
/// </summary>
public class TavilyImageResult
{
    /// <summary>
    /// URL of the image
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
    
    /// <summary>
    /// AI-generated description of the image
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Error response from Tavily API
/// </summary>
public class TavilyErrorResponse
{
    /// <summary>
    /// Error type
    /// </summary>
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;
    
    /// <summary>
    /// Error message
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// HTTP status code
    /// </summary>
    [JsonPropertyName("code")]
    public int? Code { get; set; }
}
