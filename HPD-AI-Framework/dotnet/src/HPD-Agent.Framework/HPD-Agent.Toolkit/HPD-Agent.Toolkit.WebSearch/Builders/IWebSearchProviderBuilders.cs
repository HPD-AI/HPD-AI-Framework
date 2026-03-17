using System;

namespace HPD.Agent.Toolkit.WebSearch;

/// <summary>
/// Base interface for all web search provider builders
/// Provides common configuration options shared across providers
/// </summary>
/// <typeparam name="T">The specific builder type for fluent chaining</typeparam>
public interface IWebSearchProviderBuilder<T> where T : IWebSearchProviderBuilder<T>
{
    /// <summary>
    /// Sets the API key for the provider
    /// </summary>
    /// <param name="apiKey">The API key</param>
    /// <returns>The builder instance for chaining</returns>
    T WithApiKey(string apiKey);
    
    /// <summary>
    /// Sets the timeout for search requests
    /// </summary>
    /// <param name="timeout">Request timeout</param>
    /// <returns>The builder instance for chaining</returns>
    T WithTimeout(TimeSpan timeout);
    
    /// <summary>
    /// Configures retry policy for failed requests
    /// </summary>
    /// <param name="retries">Number of retry attempts</param>
    /// <param name="delay">Delay between retries</param>
    /// <returns>The builder instance for chaining</returns>
    T WithRetryPolicy(int retries, TimeSpan delay);
    
    /// <summary>
    /// Sets error handling callback
    /// </summary>
    /// <param name="errorHandler">Error handling function</param>
    /// <returns>The builder instance for chaining</returns>
    T OnError(Action<Exception> errorHandler);
    
    /// <summary>
    /// Builds the configured web search connector
    /// Internal method used by the framework
    /// </summary>
    /// <returns>The configured connector instance</returns>
    internal IWebSearchConnector Build();
}

/// <summary>
/// Builder interface for Tavily web search provider
/// Focuses on AI-powered search with comprehensive API support
/// </summary>
public interface ITavilyWebSearchBuilder : IWebSearchProviderBuilder<ITavilyWebSearchBuilder>
{
    /// <summary>
    /// Sets the search depth (affects API credit usage)
    /// </summary>
    /// <param name="depth">Basic (1 credit) or Advanced (2 credits)</param>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder WithSearchDepth(TavilySearchDepth depth);
    
    /// <summary>
    /// Sets the search topic category
    /// </summary>
    /// <param name="topic">Topic category (general, news, finance, health, scientific, travel)</param>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder WithTopic(string topic);
    
    /// <summary>
    /// Sets the number of days back for news search
    /// </summary>
    /// <param name="days">Number of days (only valid for news topic)</param>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder WithDays(int days);
    
    /// <summary>
    /// Sets the time range for search results
    /// </summary>
    /// <param name="timeRange">Time range (day, week, month, year or d, w, m, y)</param>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder WithTimeRange(string timeRange);
    
    /// <summary>
    /// Sets the maximum number of search results
    /// </summary>
    /// <param name="maxResults">Maximum results (1-20)</param>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder WithMaxResults(int maxResults);
    
    /// <summary>
    /// Sets the number of content chunks per source (advanced search only)
    /// </summary>
    /// <param name="chunks">Number of chunks (1-3)</param>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder WithChunksPerSource(int chunks);
    
    /// <summary>
    /// Enables or disables AI-generated answers
    /// </summary>
    /// <param name="include">False, true/"basic", or "advanced"</param>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder IncludeAnswers(bool include = true);
    
    /// <summary>
    /// Enables advanced AI-generated answers
    /// </summary>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder IncludeAdvancedAnswers();
    
    /// <summary>
    /// Enables or disables full page content extraction
    /// </summary>
    /// <param name="include">False, true/"markdown", or "text"</param>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder IncludeRawContent(bool include = true);
    
    /// <summary>
    /// Enables raw content in markdown format
    /// </summary>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder IncludeMarkdownContent();
    
    /// <summary>
    /// Enables raw content in plain text format
    /// </summary>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder IncludeTextContent();
    
    /// <summary>
    /// Enables or disables image results
    /// </summary>
    /// <param name="include">True to include images</param>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder IncludeImages(bool include = true);
    
    /// <summary>
    /// Enables or disables image descriptions
    /// </summary>
    /// <param name="include">True to include AI-generated image descriptions</param>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder IncludeImageDescriptions(bool include = true);
    
    /// <summary>
    /// Enables or disables favicon URLs in results
    /// </summary>
    /// <param name="include">True to include favicon URLs</param>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder IncludeFavicon(bool include = true);
    
    /// <summary>
    /// Sets domains to specifically include in search
    /// </summary>
    /// <param name="domains">List of domains to include</param>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder WithIncludeDomains(params string[] domains);
    
    /// <summary>
    /// Sets domains to exclude from search
    /// </summary>
    /// <param name="domains">List of domains to exclude</param>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder WithExcludeDomains(params string[] domains);
    
    /// <summary>
    /// Sets country code to boost results from specific country
    /// </summary>
    /// <param name="countryCode">Country code (only for general topic)</param>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder WithCountry(string countryCode);
    
    /// <summary>
    /// Enables automatic parameter optimization (BETA)
    /// </summary>
    /// <param name="enable">True to enable auto-optimization</param>
    /// <returns>The builder instance for chaining</returns>
    ITavilyWebSearchBuilder EnableAutoParameters(bool enable = true);
    
}

/// <summary>
/// Builder interface for Brave web search provider
/// Focuses on privacy-respecting search with comprehensive filtering options
/// </summary>
public interface IBraveWebSearchBuilder : IWebSearchProviderBuilder<IBraveWebSearchBuilder>
{
    /// <summary>
    /// Sets the safe search level
    /// </summary>
    /// <param name="safeSearch">Safe search setting</param>
    /// <returns>The builder instance for chaining</returns>
    IBraveWebSearchBuilder WithSafeSearch(BraveSafeSearch safeSearch);
    
    /// <summary>
    /// Sets the target country for results
    /// </summary>
    /// <param name="countryCode">ISO country code</param>
    /// <returns>The builder instance for chaining</returns>
    IBraveWebSearchBuilder WithCountry(string countryCode);
    
    /// <summary>
    /// Sets the search language
    /// </summary>
    /// <param name="language">Language code</param>
    /// <returns>The builder instance for chaining</returns>
    IBraveWebSearchBuilder WithSearchLanguage(string language);
    
    /// <summary>
    /// Sets the UI language for results
    /// </summary>
    /// <param name="uiLanguage">UI language locale</param>
    /// <returns>The builder instance for chaining</returns>
    IBraveWebSearchBuilder WithUILanguage(string uiLanguage);
    
    /// <summary>
    /// Sets content type filters
    /// </summary>
    /// <param name="filter">Comma-separated content types (web,news,videos)</param>
    /// <returns>The builder instance for chaining</returns>
    IBraveWebSearchBuilder WithResultFilter(string filter);
    
    /// <summary>
    /// Sets measurement units for results
    /// </summary>
    /// <param name="units">Metric or Imperial units</param>
    /// <returns>The builder instance for chaining</returns>
    IBraveWebSearchBuilder WithUnits(BraveUnits units);
    
    /// <summary>
    /// Enables or disables spell checking
    /// </summary>
    /// <param name="enable">True to enable spell check</param>
    /// <returns>The builder instance for chaining</returns>
    IBraveWebSearchBuilder EnableSpellCheck(bool enable = true);
    
    /// <summary>
    /// Enables or disables extra snippets for better context
    /// </summary>
    /// <param name="enable">True to enable extra snippets</param>
    /// <returns>The builder instance for chaining</returns>
    IBraveWebSearchBuilder EnableExtraSnippets(bool enable = true);
    
}

/// <summary>
/// Builder interface for Bing web search provider
/// Focuses on comprehensive search with shopping and enterprise features
/// </summary>
public interface IBingWebSearchBuilder : IWebSearchProviderBuilder<IBingWebSearchBuilder>
{
    /// <summary>
    /// Sets the Bing Search API endpoint
    /// </summary>
    /// <param name="endpoint">API endpoint URL</param>
    /// <returns>The builder instance for chaining</returns>
    IBingWebSearchBuilder WithEndpoint(string endpoint);
    
    /// <summary>
    /// Sets the market/region for localized results
    /// </summary>
    /// <param name="market">Market code (e.g., en-US)</param>
    /// <returns>The builder instance for chaining</returns>
    IBingWebSearchBuilder WithMarket(string market);
    
    /// <summary>
    /// Sets the safe search level
    /// </summary>
    /// <param name="safeSearch">Safe search setting</param>
    /// <returns>The builder instance for chaining</returns>
    IBingWebSearchBuilder WithSafeSearch(BingSafeSearch safeSearch);
    
    /// <summary>
    /// Sets content freshness filter
    /// </summary>
    /// <param name="freshness">Freshness requirement</param>
    /// <returns>The builder instance for chaining</returns>
    IBingWebSearchBuilder WithFreshness(BingFreshness freshness);
    
    /// <summary>
    /// Sets response content filters
    /// </summary>
    /// <param name="filter">Comma-separated response types</param>
    /// <returns>The builder instance for chaining</returns>
    IBingWebSearchBuilder WithResponseFilter(string filter);
    
    /// <summary>
    /// Enables or disables shopping search capability
    /// </summary>
    /// <param name="enable">True to enable shopping search</param>
    /// <returns>The builder instance for chaining</returns>
    IBingWebSearchBuilder EnableShoppingSearch(bool enable = true);
    
    /// <summary>
    /// Enables or disables text decorations in results
    /// </summary>
    /// <param name="enable">True to enable text decorations</param>
    /// <returns>The builder instance for chaining</returns>
    IBingWebSearchBuilder WithTextDecorations(bool enable = true);
    
    /// <summary>
    /// Sets the text format for results
    /// </summary>
    /// <param name="format">Text format preference</param>
    /// <returns>The builder instance for chaining</returns>
    IBingWebSearchBuilder WithTextFormat(BingTextFormat format);
    
}

// Enums for configuration options

/// <summary>
/// Tavily search depth options affecting API credit usage
/// </summary>
public enum TavilySearchDepth
{
    /// <summary>Basic search using 1 API credit</summary>
    Basic,
    /// <summary>Advanced search using 2 API credits</summary>
    Advanced
}

/// <summary>
/// Brave safe search levels
/// </summary>
public enum BraveSafeSearch
{
    /// <summary>No safe search filtering</summary>
    Off,
    /// <summary>Moderate safe search filtering</summary>
    Moderate,
    /// <summary>Strict safe search filtering</summary>
    Strict
}

/// <summary>
/// Brave measurement unit preferences
/// </summary>
public enum BraveUnits
{
    /// <summary>Metric units (meters, celsius, etc.)</summary>
    Metric,
    /// <summary>Imperial units (feet, fahrenheit, etc.)</summary>
    Imperial
}

/// <summary>
/// Bing safe search levels
/// </summary>
public enum BingSafeSearch
{
    /// <summary>No safe search filtering</summary>
    Off,
    /// <summary>Moderate safe search filtering</summary>
    Moderate,
    /// <summary>Strict safe search filtering</summary>
    Strict
}

/// <summary>
/// Bing content freshness requirements
/// </summary>
public enum BingFreshness
{
    /// <summary>Content from the last day</summary>
    Day,
    /// <summary>Content from the last week</summary>
    Week,
    /// <summary>Content from the last month</summary>
    Month
}

/// <summary>
/// Bing text format preferences
/// </summary>
public enum BingTextFormat
{
    /// <summary>Plain text format</summary>
    Raw,
    /// <summary>HTML formatted text</summary>
    HTML
}
