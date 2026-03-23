using HPD.Agent;
using HPD.Agent.Middleware.Document;
using HPD.Agent.TextExtraction;

namespace HPD.Agent.Middleware.Document;

/// <summary>
/// Extension methods for adding document handling middleware to AgentBuilder.
/// 
/// The middleware automatically detects and processes document content from Microsoft.Extensions.AI
/// content types (DataContent, UriContent, HostedFileContent) in chat messages. Documents are
/// extracted to text and replaced with TextContent for processing by the LLM.
/// 
/// Usage example with native content types:
/// <code>
/// using Microsoft.Extensions.AI;
/// 
/// var agent = new AgentBuilder()
///     .WithChatClient(client)
///     .WithDocumentHandling()
///     .Build();
/// 
/// // Attach documents using DataContent
/// var pdfBytes = await File.ReadAllBytesAsync("document.pdf");
/// var message = new ChatMessage(ChatRole.User, 
/// [
///     new TextContent("Please analyze this document"),
///     new DataContent(pdfBytes, "application/pdf") { Name = "document.pdf" }
/// ]);
/// 
/// var response = await agent.RunAsync([message]);
/// </code>
/// </summary>
public static class AgentBuilderDocumentExtensions
{
    /// <summary>
    /// Adds document handling middleware with full-text extraction strategy.
    /// Automatically creates a TextExtractionUtility instance if not already present.
    /// 
    /// The middleware detects DataContent, UriContent, and HostedFileContent in messages,
    /// extracts text, and replaces them with TextContent for LLM processing.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="options">Document handling options (optional)</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithChatClient(client)
    ///     .WithDocumentHandling()
    ///     .Build();
    /// 
    /// // Use native DataContent for documents
    /// var bytes = await File.ReadAllBytesAsync("report.pdf");
    /// var msg = new ChatMessage(ChatRole.User, 
    /// [
    ///     new TextContent("Summarize this"),
    ///     new DataContent(bytes, "application/pdf")
    /// ]);
    /// </code>
    /// </example>
    public static AgentBuilder WithDocumentHandling(
        this AgentBuilder builder,
        DocumentHandlingOptions? options = null)
    {
        // Get or create shared text extractor instance
        builder._textExtractor ??= new TextExtractionUtility();

        var strategy = new FullTextExtractionStrategy(builder._textExtractor);
        var middleware = new DocumentHandlingMiddleware(strategy, options);

        return builder.WithMiddleware(middleware);
    }

    /// <summary>
    /// Adds document handling middleware with full-text extraction strategy.
    /// Uses the provided TextExtractionUtility instance for custom extraction configurations.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="textExtractor">Text extraction utility instance</param>
    /// <param name="options">Document handling options (optional)</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// var customExtractor = new TextExtractionUtility(
    ///     loggerFactory: myLoggerFactory);
    /// 
    /// var agent = new AgentBuilder()
    ///     .WithChatClient(client)
    ///     .WithDocumentHandling(customExtractor)
    ///     .Build();
    /// 
    /// // Documents attached as DataContent will be processed
    /// var docBytes = await File.ReadAllBytesAsync("spec.docx");
    /// var msg = new ChatMessage(ChatRole.User,
    /// [
    ///     new TextContent("Review this specification"),
    ///     new DataContent(docBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document")
    /// ]);
    /// </code>
    /// </example>
    public static AgentBuilder WithDocumentHandling(
        this AgentBuilder builder,
        TextExtractionUtility textExtractor,
        DocumentHandlingOptions? options = null)
    {
        if (textExtractor == null)
            throw new ArgumentNullException(nameof(textExtractor));

        // Store the provided extractor for potential reuse
        builder._textExtractor = textExtractor;

        var strategy = new FullTextExtractionStrategy(textExtractor);
        var middleware = new DocumentHandlingMiddleware(strategy, options);

        return builder.WithMiddleware(middleware);
    }

    /// <summary>
    /// Adds document handling middleware with a custom strategy.
    /// Use this for advanced scenarios where you need custom document processing logic.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="strategy">Custom document processing strategy implementing IDocumentStrategy</param>
    /// <param name="options">Document handling options (optional)</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// // Custom strategy that handles documents differently
    /// public class SummaryOnlyStrategy : IDocumentStrategy
    /// {
    ///     public async Task ProcessDocumentsAsync(
    ///         AgentMiddlewareContext context,
    ///         IEnumerable&lt;AIContent&gt; documentContents,
    ///         DocumentHandlingOptions options,
    ///         CancellationToken cancellationToken)
    ///     {
    ///         // Custom processing logic here
    ///     }
    /// }
    /// 
    /// var agent = new AgentBuilder()
    ///     .WithChatClient(client)
    ///     .WithDocumentHandling(
    ///         new SummaryOnlyStrategy(),
    ///         new DocumentHandlingOptions { MaxDocumentSizeBytes = 5 * 1024 * 1024 })
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithDocumentHandling(
        this AgentBuilder builder,
        IDocumentStrategy strategy,
        DocumentHandlingOptions? options = null)
    {
        if (strategy == null)
            throw new ArgumentNullException(nameof(strategy));

        var middleware = new DocumentHandlingMiddleware(strategy, options);
        return builder.WithMiddleware(middleware);
    }
}
