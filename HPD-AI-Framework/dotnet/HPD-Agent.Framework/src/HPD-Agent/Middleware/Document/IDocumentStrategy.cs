using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware.Document;

/// <summary>
/// Strategy for processing and injecting documents into agent context.
/// Implementations define how documents are extracted, processed, and added to messages.
/// </summary>
public interface IDocumentStrategy
{
    /// <summary>
    /// Process document contents (DataContent, UriContent, HostedFileContent) and modify agent context accordingly.
    /// </summary>
    /// <param name="context">Agent middleware context to modify</param>
    /// <param name="documentContents">Document contents to process (DataContent, UriContent, or HostedFileContent)</param>
    /// <param name="options">Document handling options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task ProcessDocumentsAsync(
        BeforeIterationContext context,
        IEnumerable<AIContent> documentContents,
        DocumentHandlingOptions options,
        CancellationToken cancellationToken);
}
