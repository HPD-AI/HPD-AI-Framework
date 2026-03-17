using System;

namespace HPD.Agent.Toolkit.WebSearch;

/// <summary>
/// Configuration container for Tavily web search settings with comprehensive API support
/// </summary>
public class TavilyConfig
{
    // === Core Settings ===
    public string ApiKey { get; set; } = string.Empty;
    public TimeSpan? Timeout { get; set; }
    public int? RetryCount { get; set; }
    public TimeSpan? RetryDelay { get; set; }
    public Action<Exception>? ErrorHandler { get; set; }
    
    // === Search Parameters ===
    public TavilySearchDepth? SearchDepth { get; set; }
    public string? Topic { get; set; } // "general", "news", "finance", "health", "scientific", "travel"
    public int? Days { get; set; } // For news topic
    public string? TimeRange { get; set; } // "day", "week", "month", "year" or "d", "w", "m", "y"
    public int? MaxResults { get; set; }
    public int? ChunksPerSource { get; set; } // 1-3, only for advanced search
    
    // === Content Options ===
    public object? IncludeAnswer { get; set; } // false, true/"basic", "advanced"
    public object? IncludeRawContent { get; set; } // false, true/"markdown", "text"
    public bool? IncludeImages { get; set; }
    public bool? IncludeImageDescriptions { get; set; }
    public bool? IncludeFavicon { get; set; }
    
    // === Filtering ===
    public string[]? IncludeDomains { get; set; }
    public string[]? ExcludeDomains { get; set; }
    public string? Country { get; set; } // Country code for boosting results
    
    // === Beta Features ===
    public bool? AutoParameters { get; set; } // Auto-optimize parameters based on query
    
    /// <summary>
    /// Validates the configuration and throws descriptive errors
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Tavily API key is required");
            
        if (ChunksPerSource.HasValue && (ChunksPerSource < 1 || ChunksPerSource > 3))
            throw new ArgumentOutOfRangeException(nameof(ChunksPerSource), "Chunks per source must be 1-3");
            
        if (MaxResults.HasValue && (MaxResults < 1 || MaxResults > 20))
            throw new ArgumentOutOfRangeException(nameof(MaxResults), "Max results must be 1-20");
            
        if (Days.HasValue && Days < 1)
            throw new ArgumentOutOfRangeException(nameof(Days), "Days must be positive");
            
        if (RetryCount.HasValue && RetryCount < 0)
            throw new ArgumentOutOfRangeException(nameof(RetryCount), "Retry count cannot be negative");
            
        if (Timeout.HasValue && Timeout.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be positive");
            
        // Validate topic values
        if (!string.IsNullOrEmpty(Topic))
        {
            var validTopics = new[] { "general", "news", "finance", "health", "scientific", "travel" };
            if (!Array.Exists(validTopics, t => t.Equals(Topic, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Invalid topic '{Topic}'. Valid topics: {string.Join(", ", validTopics)}", nameof(Topic));
        }
        
        // Validate time range values
        if (!string.IsNullOrEmpty(TimeRange))
        {
            var validRanges = new[] { "day", "week", "month", "year", "d", "w", "m", "y" };
            if (!Array.Exists(validRanges, r => r.Equals(TimeRange, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Invalid time range '{TimeRange}'. Valid ranges: {string.Join(", ", validRanges)}", nameof(TimeRange));
        }
        
        // Days parameter only valid for news topic
        if (Days.HasValue && !string.Equals(Topic, "news", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Days parameter is only valid when topic is 'news'");
            
        // Chunks per source only valid for advanced search
        if (ChunksPerSource.HasValue && SearchDepth != TavilySearchDepth.Advanced)
            throw new InvalidOperationException("ChunksPerSource is only valid when SearchDepth is 'Advanced'");
    }
}

/// <summary>
/// Configuration container for Brave web search settings
/// </summary>
public class BraveConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public TimeSpan? Timeout { get; set; }
    public int? RetryCount { get; set; }
    public TimeSpan? RetryDelay { get; set; }
    public Action<Exception>? ErrorHandler { get; set; }
    public BraveSafeSearch SafeSearch { get; set; }
    public string? Country { get; set; }
    public string? SearchLanguage { get; set; }
    public string? UILanguage { get; set; }
    public string? ResultFilter { get; set; }
    public BraveUnits Units { get; set; }
    public bool SpellCheck { get; set; } = true;
    public bool ExtraSnippets { get; set; } = false;
    
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Brave API key is required");
    }
}

/// <summary>
/// Configuration container for Bing web search settings
/// </summary>
public class BingConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public TimeSpan? Timeout { get; set; }
    public int? RetryCount { get; set; }
    public TimeSpan? RetryDelay { get; set; }
    public Action<Exception>? ErrorHandler { get; set; }
    public string? Endpoint { get; set; }
    public string? Market { get; set; }
    public BingSafeSearch SafeSearch { get; set; }
    public BingFreshness Freshness { get; set; }
    public string? ResponseFilter { get; set; }
    public bool ShoppingSearch { get; set; } = false;
    public bool TextDecorations { get; set; } = true;
    public BingTextFormat TextFormat { get; set; }
    
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Bing API key is required");
    }
}