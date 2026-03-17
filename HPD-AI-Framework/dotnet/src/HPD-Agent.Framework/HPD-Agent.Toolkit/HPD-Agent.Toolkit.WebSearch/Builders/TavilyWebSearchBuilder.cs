using Microsoft.Extensions.Configuration;
using System;

namespace HPD.Agent.Toolkit.WebSearch;

/// <summary>
/// Fluent builder for configuring Tavily web search provider
/// </summary>
public class TavilyWebSearchBuilder : ITavilyWebSearchBuilder
{
    private readonly TavilyConfig _config = new();
    private readonly IConfiguration? _configuration;

    public TavilyWebSearchBuilder(IConfiguration? configuration = null)
    {
        _configuration = configuration;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder WithApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be empty", nameof(apiKey));
        _config.ApiKey = apiKey;
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder WithTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive");
        _config.Timeout = timeout;
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder WithRetryPolicy(int retries, TimeSpan delay)
    {
        if (retries < 0)
            throw new ArgumentOutOfRangeException(nameof(retries), "Retry count cannot be negative");
        if (delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay), "Retry delay cannot be negative");
            
        _config.RetryCount = retries;
        _config.RetryDelay = delay;
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder OnError(Action<Exception> errorHandler)
    {
        _config.ErrorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder WithSearchDepth(TavilySearchDepth depth)
    {
        _config.SearchDepth = depth;
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder IncludeAnswers(bool include = true)
    {
        _config.IncludeAnswer = include;
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder IncludeImages(bool include = true)
    {
        _config.IncludeImages = include;
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder IncludeRawContent(bool include = true)
    {
        _config.IncludeRawContent = include;
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder WithTimeRange(string timeRange)
    {
        var validRanges = new[] { "day", "week", "month", "year" };
        if (!string.IsNullOrEmpty(timeRange) && !Array.Exists(validRanges, r => r.Equals(timeRange, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Time range must be one of: {string.Join(", ", validRanges)}", nameof(timeRange));
            
        _config.TimeRange = timeRange?.ToLowerInvariant();
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder WithTopic(string topic)
    {
        var validTopics = new[] { "general", "news", "finance", "health", "scientific", "travel" };
        if (!string.IsNullOrEmpty(topic) && !Array.Exists(validTopics, t => t.Equals(topic, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Topic must be one of: {string.Join(", ", validTopics)}", nameof(topic));
            
        _config.Topic = topic?.ToLowerInvariant();
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder WithDays(int days)
    {
        if (days < 1)
            throw new ArgumentOutOfRangeException(nameof(days), "Days must be positive");
        _config.Days = days;
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder WithIncludeDomains(params string[] domains)
    {
        _config.IncludeDomains = domains;
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder WithExcludeDomains(params string[] domains)
    {
        _config.ExcludeDomains = domains;
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder WithCountry(string countryCode)
    {
        _config.Country = countryCode;
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder EnableAutoParameters(bool enable = true)
    {
        _config.AutoParameters = enable;
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder IncludeAdvancedAnswers()
    {
        _config.IncludeAnswer = "advanced";
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder IncludeMarkdownContent()
    {
        _config.IncludeRawContent = "markdown";
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder IncludeTextContent()
    {
        _config.IncludeRawContent = "text";
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder IncludeImageDescriptions(bool include = true)
    {
        _config.IncludeImageDescriptions = include;
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder IncludeFavicon(bool include = true)
    {
        _config.IncludeFavicon = include;
        return this;
    }
    
    
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder WithChunksPerSource(int chunks)
    {
        if (chunks < 0 || chunks > 3)
            throw new ArgumentOutOfRangeException(nameof(chunks), "Chunks per source must be 0-3");
        _config.ChunksPerSource = chunks;
        return this;
    }
    
    /// <inheritdoc />
    public ITavilyWebSearchBuilder WithMaxResults(int maxResults)
    {
        if (maxResults < 1 || maxResults > 20)
            throw new ArgumentOutOfRangeException(nameof(maxResults), "Max results must be 1-20");
        _config.MaxResults = maxResults;
        return this;
    }
    
    
    
    /// <summary>
    /// Gets the current configuration for validation or inspection
    /// </summary>
    /// <returns>The current configuration</returns>
    public TavilyConfig GetConfig() => _config;
    
    /// <inheritdoc />
    IWebSearchConnector IWebSearchProviderBuilder<ITavilyWebSearchBuilder>.Build()
    {
        // If no API key was provided manually, try to resolve it from configuration
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            _config.ApiKey = _configuration?["Tavily:ApiKey"] ?? string.Empty;
        }

        // Apply the fixed defaults
        ApplyDefaults();
        
        // Validate configuration (this will now throw if the key is missing from both manual and auto-resolution)
        _config.Validate();
        
        // Create and return the connector
        return new TavilyConnector(_config);
    }
    
    /// <summary>
    /// Applies a fixed, sensible default configuration for basic search.
    /// </summary>
    private void ApplyDefaults()
    {
        // Set default timeout if not specified
        if (!_config.Timeout.HasValue)
        {
            _config.Timeout = TimeSpan.FromSeconds(30);
        }
        
        // Default max results if not specified
        if (!_config.MaxResults.HasValue)
        {
            _config.MaxResults = 5;
        }
        
        // Default to basic search depth if not specified
        if (!_config.SearchDepth.HasValue)
        {
            _config.SearchDepth = TavilySearchDepth.Basic;
        }
    }
}
