using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware.Document;

/// <summary>
/// Middleware for handling document attachments using Microsoft.Extensions.AI content types.
/// Detects DataContent, UriContent, and HostedFileContent in messages and processes them
/// according to the provided strategy (typically extracting text and converting to TextContent).
///
/// Usage with AgentBuilder extensions (recommended):
/// <code>
/// var agent = new AgentBuilder()
///     .WithChatClient(client)
///     .WithDocumentHandling() // Uses default full-text extraction
///     .Build();
/// </code>
///
/// Direct usage:
/// <code>
/// var extractor = new TextExtractionUtility();
/// var strategy = new FullTextExtractionStrategy(extractor);
/// var middleware = new DocumentHandlingMiddleware(strategy,
///     new DocumentHandlingOptions { CustomTagFormat = "[DOC[{0}]]\n{1}\n[/DOC]" });
/// 
/// var agent = new AgentBuilder()
///     .WithChatClient(client)
///     .WithMiddleware(middleware)
///     .Build();
/// </code>
/// </summary>
public class DocumentHandlingMiddleware : IAgentMiddleware
{
    private readonly IDocumentStrategy _strategy;
    private readonly DocumentHandlingOptions _options;

    /// <summary>
    /// Creates a new DocumentHandlingMiddleware with the specified strategy and options.
    /// </summary>
    /// <param name="strategy">Strategy for processing documents</param>
    /// <param name="options">Document handling options (optional)</param>
    public DocumentHandlingMiddleware(
        IDocumentStrategy strategy,
        DocumentHandlingOptions? options = null)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _options = options ?? new DocumentHandlingOptions();
    }

    /// <summary>
    /// Called before each LLM iteration.
    /// Checks for DataContent, UriContent, or HostedFileContent in messages and processes them if present.
    /// </summary>
    public async Task BeforeIterationAsync(
        BeforeIterationContext context,
        CancellationToken cancellationToken)
    {
        // Extract document content from all messages
        var documentContents = ExtractDocumentContents(context);
        if (!documentContents.Any())
            return;

        // Process documents using strategy
        await _strategy.ProcessDocumentsAsync(
            context,
            documentContents,
            _options,
            cancellationToken);
    }

    /// <summary>
    /// Extract document content from messages (DataContent, UriContent, HostedFileContent).
    /// </summary>
    private IEnumerable<AIContent> ExtractDocumentContents(BeforeIterationContext context)
    {
        if (context.Messages == null)
            return Enumerable.Empty<AIContent>();

        return context.Messages
            .SelectMany(m => m.Contents)
            .Where(c => c is DataContent || c is UriContent || c is HostedFileContent);
    }
}
