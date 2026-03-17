using System;
using System.Threading;
using System.Threading.Tasks;

namespace HPD.Agent.Toolkit.WebSearch;

/// <summary>
/// Brave web search connector (placeholder implementation)
/// TODO: Implement full Brave API integration
/// </summary>
public class BraveConnector : IWebSearchConnector
{
    /// <inheritdoc />
    public string ProviderName => "brave";
    
    /// <inheritdoc />
    public bool SupportsNews => true;
    
    /// <inheritdoc />
    public bool SupportsAIAnswer => false;
    
    /// <inheritdoc />
    public Task<SearchResult> SearchAsync(string query, int count = 5, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("BraveConnector implementation coming in next iteration");
    }
    
    /// <inheritdoc />
    public Task<SearchResult> SearchNewsAsync(string query, string timeRange = "week", CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("BraveConnector implementation coming in next iteration");
    }
    
    /// <inheritdoc />
    public Task<SearchResult> SearchVideosAsync(string query, int count = 5, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("BraveConnector implementation coming in next iteration");
    }
    
    /// <inheritdoc />
    public Task<AnswerResult> SearchWithAnswerAsync(string query, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Brave does not support AI-generated answers");
    }
    
    /// <inheritdoc />
    public Task<SearchResult> SearchShoppingAsync(string query, int count = 5, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Brave does not support shopping search");
    }
}
