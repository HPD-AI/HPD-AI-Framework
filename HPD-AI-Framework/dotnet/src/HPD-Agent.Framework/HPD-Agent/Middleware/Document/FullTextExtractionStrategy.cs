using HPD.Agent.Middleware;
using HPD.Agent.TextExtraction;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware.Document;

/// <summary>
/// Extracts full text from document contents and replaces them with TextContent.
/// This is the default document handling strategy.
/// 
/// Supports DataContent (binary data), UriContent (URLs), and HostedFileContent from
/// Microsoft.Extensions.AI. Documents are extracted using TextExtractionUtility and
/// replaced with TextContent formatted with document tags for LLM processing.
/// </summary>
public class FullTextExtractionStrategy : IDocumentStrategy
{
    private readonly TextExtractionUtility _extractor;

    /// <summary>
    /// Creates a new FullTextExtractionStrategy with the specified text extractor.
    /// </summary>
    /// <param name="extractor">Text extraction utility for processing documents</param>
    public FullTextExtractionStrategy(TextExtractionUtility extractor)
    {
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
    }

    /// <summary>
    /// Process document contents by extracting text and replacing with TextContent.
    /// </summary>
    public async Task ProcessDocumentsAsync(
        BeforeIterationContext context,
        IEnumerable<AIContent> documentContents,
        DocumentHandlingOptions options,
        CancellationToken cancellationToken)
    {
        var contents = documentContents.ToArray();
        if (contents.Length == 0)
            return;

        // Process each message to replace document content with extracted text
        await ReplaceDocumentContentsWithTextAsync(context, contents, options, cancellationToken);
    }

    /// <summary>
    /// Replace DataContent and UriContent with TextContent containing extracted text.
    /// </summary>
    private async Task ReplaceDocumentContentsWithTextAsync(
        BeforeIterationContext context,
        AIContent[] documentContents,
        DocumentHandlingOptions options,
        CancellationToken cancellationToken)
    {
        if (context.Messages == null || !context.Messages.Any())
            return;

        var messagesList = context.Messages.ToList();

        for (int msgIndex = 0; msgIndex < messagesList.Count; msgIndex++)
        {
            var message = messagesList[msgIndex];
            var newContents = new List<AIContent>();
            bool modified = false;

            foreach (var content in message.Contents)
            {
                if (content is DataContent dataContent)
                {
                    // Extract text from DataContent
                    var extractedText = await ExtractTextFromDataContentAsync(dataContent, cancellationToken);
                    if (!string.IsNullOrEmpty(extractedText))
                    {
                        // Replace DataContent with TextContent
                        newContents.Add(new TextContent(FormatExtractedText(dataContent.Name, extractedText, options.CustomTagFormat)));
                        modified = true;
                        continue;
                    }
                }
                else if (content is UriContent uriContent)
                {
                    // Download and extract text from URI
                    var extractedText = await ExtractTextFromUriAsync(uriContent, cancellationToken);
                    if (!string.IsNullOrEmpty(extractedText))
                    {
                        newContents.Add(new TextContent(FormatExtractedText(uriContent.Uri.ToString(), extractedText, options.CustomTagFormat)));
                        modified = true;
                        continue;
                    }
                }
                
                // Keep all other content as-is (TextContent, FunctionCallContent, HostedFileContent, etc.)
                newContents.Add(content);
            }

            if (modified)
            {
                // Create new message with replaced contents
                var newMessage = new ChatMessage(message.Role, newContents);
                if (message.AdditionalProperties != null)
                {
                    newMessage.AdditionalProperties = new AdditionalPropertiesDictionary(message.AdditionalProperties);
                }
                if (message.AuthorName != null)
                {
                    newMessage.AuthorName = message.AuthorName;
                }
                messagesList[msgIndex] = newMessage;
            }
        }

        // V2: Messages is mutable - clear and repopulate instead of reassigning
        context.Messages.Clear();
        foreach (var msg in messagesList)
        {
            context.Messages.Add(msg);
        }
    }

    /// <summary>
    /// Extract text from DataContent using TextExtractionUtility.
    /// </summary>
    private async Task<string> ExtractTextFromDataContentAsync(DataContent dataContent, CancellationToken cancellationToken)
    {
        if (dataContent.Data.IsEmpty)
            return string.Empty;

        try
        {
            var result = await _extractor.ExtractTextAsync(
                dataContent.Data,
                dataContent.MediaType,
                dataContent.Name,
                cancellationToken);
            
            return result.ExtractedText ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Download and extract text from UriContent.
    /// </summary>
    private async Task<string> ExtractTextFromUriAsync(UriContent uriContent, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _extractor.ExtractTextAsync(
                uriContent.Uri.ToString(),
                cancellationToken);
            
            return result.ExtractedText ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Format extracted text with document tags.
    /// </summary>
    private static string FormatExtractedText(string? documentName, string extractedText, string? customTagFormat)
    {
        const string DefaultDocumentTagFormat = "\n\n[ATTACHED_DOCUMENT[{0}]]\n{1}\n[/ATTACHED_DOCUMENT]\n\n";
        var format = customTagFormat ?? DefaultDocumentTagFormat;
        return string.Format(format, documentName ?? "document", extractedText);
    }

}
