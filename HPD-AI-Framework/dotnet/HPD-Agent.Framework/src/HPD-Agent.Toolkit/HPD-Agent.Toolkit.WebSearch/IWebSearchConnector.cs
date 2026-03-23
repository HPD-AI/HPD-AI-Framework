using System;
using System.Threading;
using System.Threading.Tasks;

namespace HPD.Agent.Toolkit.WebSearch;

/// <summary>
/// Core interface for web search providers that supports both generic and provider-specific capabilities.
/// Unlike Semantic Kernel's generic ITextSearch, this preserves unique provider features.
/// </summary>
public interface IWebSearchConnector
{
    /// <summary>
    /// The name of the search provider (e.g., "tavily", "brave", "bing")
    /// </summary>
    string ProviderName { get; }
    
    /// <summary>
    /// Indicates whether this provider supports news search functionality
    /// </summary>
    bool SupportsNews { get; }
    
    /// <summary>
    /// Indicates whether this provider supports AI-generated answers
    /// </summary>
    bool SupportsAIAnswer { get; }
    
    // === Universal Capabilities (All Providers) ===
    
    /// <summary>
    /// Performs a general web search
    /// </summary>
    Task<SearchResult> SearchAsync(string query, int count = 5, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Searches for recent news articles
    /// </summary>
    Task<SearchResult> SearchNewsAsync(string query, string timeRange = "week", CancellationToken cancellationToken = default);
    
    // === Provider-Specific Capabilities ===
    
    /// <summary>
    /// Searches for videos (Brave, Bing only)
    /// Throws NotSupportedException for providers that don't support video search
    /// </summary>
    Task<SearchResult> SearchVideosAsync(string query, int count = 5, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets AI-generated answers with cited sources (Tavily only)
    /// Throws NotSupportedException for providers that don't support AI answers
    /// </summary>
    Task<AnswerResult> SearchWithAnswerAsync(string query, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Searches for shopping deals and products (Bing only)
    /// Throws NotSupportedException for providers that don't support shopping search
    /// </summary>
    Task<SearchResult> SearchShoppingAsync(string query, int count = 5, CancellationToken cancellationToken = default);
}
