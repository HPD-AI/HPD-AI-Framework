using System;
using System.Collections.Generic;

namespace HPD.Agent.Toolkit.WebSearch;

/// <summary>
/// Standard search result containing items from any web search provider
/// </summary>
public class SearchResult
{
    /// <summary>
    /// The original search query
    /// </summary>
    public string Query { get; set; } = string.Empty;
    
    /// <summary>
    /// List of search result items
    /// </summary>
    public List<SearchItem> Items { get; set; } = new();
    
    /// <summary>
    /// Provider-specific metadata (e.g., total results, suggested queries)
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    /// <summary>
    /// Time taken to execute the search
    /// </summary>
    public TimeSpan ResponseTime { get; set; }
    
    /// <summary>
    /// The provider that performed this search
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of results returned
    /// </summary>
    public int ResultCount => Items.Count;
    
    /// <summary>
    /// Indicates if the search was successful
    /// </summary>
    public bool IsSuccess { get; set; } = true;
    
    /// <summary>
    /// Error message if the search failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Enhanced search result that includes AI-generated answers (Tavily-specific)
/// </summary>
public class AnswerResult : SearchResult
{
    /// <summary>
    /// AI-generated answer to the query
    /// </summary>
    public string Answer { get; set; } = string.Empty;
    
    /// <summary>
    /// URLs of sources used to generate the answer
    /// </summary>
    public List<string> Sources { get; set; } = new();
    
    /// <summary>
    /// Suggested follow-up questions
    /// </summary>
    public List<string> FollowUpQuestions { get; set; } = new();
    
    /// <summary>
    /// Confidence score for the answer (0.0 to 1.0)
    /// </summary>
    public double? ConfidenceScore { get; set; }
}

/// <summary>
/// Individual search result item
/// </summary>
public class SearchItem
{
    /// <summary>
    /// Title of the search result
    /// </summary>
    public string Title { get; set; } = string.Empty;
    
    /// <summary>
    /// URL of the search result
    /// </summary>
    public string Url { get; set; } = string.Empty;
    
    /// <summary>
    /// Short description/snippet from the page
    /// </summary>
    public string Snippet { get; set; } = string.Empty;
    
    /// <summary>
    /// Full page content (if requested and supported)
    /// </summary>
    public string? RawContent { get; set; }
    
    /// <summary>
    /// Relevance score from the search provider (0.0 to 1.0)
    /// </summary>
    public double? Score { get; set; }
    
    /// <summary>
    /// When the content was published or last modified
    /// </summary>
    public DateTime? PublishedDate { get; set; }
    
    /// <summary>
    /// Domain name of the source
    /// </summary>
    public string? Domain { get; set; }
    
    /// <summary>
    /// Language of the content (ISO 639-1 code)
    /// </summary>
    public string? Language { get; set; }
    
    /// <summary>
    /// Provider-specific data that doesn't fit standard fields
    /// </summary>
    public Dictionary<string, object> ProviderData { get; set; } = new();
    
    /// <summary>
    /// Type of content (web, news, video, image, etc.)
    /// </summary>
    public string ContentType { get; set; } = "web";
    
    /// <summary>
    /// Preview image URL if available
    /// </summary>
    public string? ImageUrl { get; set; }
}

/// <summary>
/// Specialized search item for video results
/// </summary>
public class VideoSearchItem : SearchItem
{
    /// <summary>
    /// Duration of the video
    /// </summary>
    public TimeSpan? Duration { get; set; }
    
    /// <summary>
    /// Video thumbnail URL
    /// </summary>
    public string? ThumbnailUrl { get; set; }
    
    /// <summary>
    /// Video platform (YouTube, Vimeo, etc.)
    /// </summary>
    public string? Platform { get; set; }
    
    /// <summary>
    /// Video uploader/channel name
    /// </summary>
    public string? Uploader { get; set; }
    
    /// <summary>
    /// View count if available
    /// </summary>
    public long? ViewCount { get; set; }
}

/// <summary>
/// Specialized search item for shopping results
/// </summary>
public class ShoppingSearchItem : SearchItem
{
    /// <summary>
    /// Product price
    /// </summary>
    public decimal? Price { get; set; }
    
    /// <summary>
    /// Currency code (USD, EUR, etc.)
    /// </summary>
    public string? Currency { get; set; }
    
    /// <summary>
    /// Merchant/seller name
    /// </summary>
    public string? Merchant { get; set; }
    
    /// <summary>
    /// Product availability status
    /// </summary>
    public string? Availability { get; set; }
    
    /// <summary>
    /// Product rating (0.0 to 5.0)
    /// </summary>
    public double? Rating { get; set; }
    
    /// <summary>
    /// Number of reviews
    /// </summary>
    public int? ReviewCount { get; set; }
}
