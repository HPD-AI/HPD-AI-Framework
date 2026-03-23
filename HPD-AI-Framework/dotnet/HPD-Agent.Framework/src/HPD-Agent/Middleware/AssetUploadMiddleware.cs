using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware;

/// <summary>
/// Automatically uploads binary assets (DataContent) to IContentStore before LLM processing.
/// Transforms DataContent(bytes) → UriContent(asset://assetId) for efficient storage.
/// </summary>
/// <remarks>
/// <para>
/// This middleware is automatically registered in AgentBuilder when WithContentStore() is used.
/// It checks at runtime if a content store is available — zero cost when not used.
/// </para>
/// <para><b>Behavior:</b></para>
/// <list type="bullet">
/// <item>Scans messages for DataContent with binary data</item>
/// <item>Uploads bytes to IContentStore with scope=sessionId and folder=/uploads tag</item>
/// <item>Replaces DataContent with UriContent using asset:// URI scheme</item>
/// <item>Emits AssetUploadedEvent or AssetUploadFailedEvent for observability</item>
/// </list>
/// <para><b>Zero-cost when unused:</b></para>
/// <para>
/// If the content store or session is null, middleware returns immediately (no-op).
/// </para>
/// </remarks>
public class AssetUploadMiddleware : IAgentMiddleware
{
    private readonly IContentStore? _contentStore;

    /// <summary>
    /// Creates an AssetUploadMiddleware that uploads binary assets to the provided content store.
    /// Pass null to create a no-op middleware (useful for agents without content storage).
    /// </summary>
    public AssetUploadMiddleware(IContentStore? contentStore = null)
    {
        _contentStore = contentStore;
    }

    /// <summary>
    /// Called before processing a user message turn.
    /// Uploads DataContent to IContentStore and replaces with UriContent.
    /// </summary>
    /// <remarks>
    /// Uses BeforeMessageTurnAsync (not BeforeIterationAsync) to upload assets once per user message,
    /// not on every agentic loop iteration. This prevents redundant uploads when the agent
    /// makes multiple LLM calls in a single turn.
    /// </remarks>
    public async Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        // Zero-cost exit when no content store configured
        if (_contentStore == null)
            return;

        var session = context.Session;
        if (session == null)
            return;

        var message = context.UserMessage;
        if (message == null)
            return;

        // Check if message contains DataContent with bytes
        var hasDataBytes = message.Contents.Any(c =>
            c is DataContent data && data.Data.Length > 0);

        if (!hasDataBytes)
            return;

        // Upload assets and build new content list with URIs
        var newContents = new List<AIContent>();

        foreach (var content in message.Contents)
        {
            AIContent transformedContent = content;

            if (content is DataContent data && data.Data.Length > 0)
            {
                try
                {
                    // Upload to content store as session-scoped /uploads content
                    // Name is only set when the content has an explicit filename — unnamed uploads
                    // always use the unnamed-insert path (new entry per upload, no upsert collision).
                    var assetId = await _contentStore.PutAsync(
                        scope: session.Id,
                        data: data.Data.ToArray(),
                        contentType: data.MediaType ?? "application/octet-stream",
                        metadata: new ContentMetadata
                        {
                            Name = ExtractFileName(content),
                            Origin = ContentSource.User,
                            Tags = new Dictionary<string, string>
                            {
                                ["folder"] = "/uploads",
                                ["session"] = session.Id
                            }
                        },
                        cancellationToken: cancellationToken);

                    // Replace with URI reference using asset:// scheme
                    transformedContent = new UriContent(
                        new Uri($"asset://{assetId}"),
                        data.MediaType);

                    context.Emit(new AssetUploadedEvent(
                        AssetId: assetId,
                        MediaType: data.MediaType ?? "application/octet-stream",
                        SizeBytes: data.Data.Length));
                }
                catch (Exception ex)
                {
                    context.Emit(new AssetUploadFailedEvent(
                        MediaType: data.MediaType ?? "application/octet-stream",
                        Error: ex.Message));

                    // Keep original content if upload fails
                    transformedContent = content;
                }
            }

            newContents.Add(transformedContent);
        }

        // Create new message with transformed contents
        var updatedMessage = new ChatMessage(message.Role, newContents)
        {
            AuthorName = message.AuthorName,
            AdditionalProperties = message.AdditionalProperties
        };

        context.UserMessage = updatedMessage;
    }

    private static string? ExtractFileName(AIContent content)
    {
        if (content.AdditionalProperties != null &&
            content.AdditionalProperties.TryGetValue("filename", out var fn))
            return fn?.ToString();
        return null;
    }
}
