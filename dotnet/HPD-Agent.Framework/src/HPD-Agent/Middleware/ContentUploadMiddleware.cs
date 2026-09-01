using Microsoft.Extensions.AI;
using HPD.Agent.Providers;

namespace HPD.Agent.Middleware;

/// <summary>
/// Intelligently routes DataContent uploads between provider-native HostedFileClient
/// and framework IContentStore based on provider capabilities and UploadStrategy.
/// 
/// Transforms DataContent into either:
/// - HostedFileContent (provider-native) when provider supports it
/// - UriContent(hpd-content://id) (framework storage) as fallback
/// </summary>
/// <remarks>
/// <para>
/// This middleware is automatically registered in AgentBuilder. It checks at runtime
/// if a content store or resolved hosted-file client is available — zero cost when unused.
/// </para>
/// <para><b>Behavior:</b></para>
/// <list type="bullet">
/// <item>Scans messages for DataContent with binary data</item>
/// <item>Uses the run's resolved Hosted Files family client when available</item>
/// <item>Routes based on UploadStrategy (Auto/Hosted/Local) and provider capability</item>
/// <item>Uploads via HostedFileClient or IContentStore accordingly</item>
/// <item>Emits corresponding upload events for observability</item>
/// </list>
/// <para><b>Smart Fallback:</b></para>
/// <para>
/// In Auto mode, if HostedFileClient upload fails, automatically falls back to IContentStore.
/// If both fail, keeps original DataContent (no-op).
/// </para>
/// </remarks>
public class ContentUploadMiddleware : IAgentMiddleware
{
    private readonly IContentStore? _contentStore;

    /// <summary>
    /// Creates a ContentUploadMiddleware with an optional content store.
    /// </summary>
    /// <param name="contentStore">Optional content store for local/framework-managed uploads</param>
    /// <remarks>
    /// Without a content store or a run-resolved hosted-file client, the middleware is a no-op.
    /// When both are available, it routes according to <see cref="UploadStrategy"/>.
    /// </remarks>
    public ContentUploadMiddleware(IContentStore? contentStore = null)
    {
        _contentStore = contentStore;
    }

    public async Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        var session = context.Session;
        if (session == null)
            return;

        var message = context.UserInputMessages.FirstOrDefault();
        if (message == null)
            return;

        // Check if message contains DataContent with bytes
        var hasDataBytes = message.Contents.Any(c =>
            c is DataContent data && data.Data.Length > 0);

        if (!hasDataBytes)
            return;

        // Zero-cost exit when no upload path is configured for this agent/run.
        if (_contentStore == null
            && context.RunConfig.Clients.HostedFiles?.Override?.Client == null
            && context.ClientSet == null)
        {
            return;
        }

        // Get upload strategy from run config if available
        var strategy = context.RunConfig?.UploadStrategy ?? UploadStrategy.Auto;

        // Upload content and build new content list with URIs or hosted references
        var newContents = new List<AIContent>();

        foreach (var content in message.Contents)
        {
            AIContent transformedContent = content;

            if (content is DataContent data && data.Data.Length > 0)
            {
                transformedContent = await RouteUploadAsync(
                    context,
                    data,
                    strategy,
                    cancellationToken);
            }

            newContents.Add(transformedContent);
        }

        // Create new message with transformed contents
        var updatedMessage = new ChatMessage(message.Role, newContents)
        {
            AuthorName = message.AuthorName,
            AdditionalProperties = message.AdditionalProperties,
            CreatedAt = message.CreatedAt,
            MessageId = message.MessageId,
            RawRepresentation = message.RawRepresentation
        };

        context.UserInputMessages[0] = updatedMessage;
    }

    private async Task<AIContent> RouteUploadAsync(
        BeforeMessageTurnContext context,
        DataContent data,
        UploadStrategy strategy,
        CancellationToken cancellationToken)
    {
        var mediaType = data.MediaType ?? "application/octet-stream";

        // Determine if the current agent/run has a hosted file client available.
        var hostedFileClient = await GetHostedFileClientAsync(context, cancellationToken).ConfigureAwait(false);
        var canUseHosted = hostedFileClient != null;
        var canUseLocal = _contentStore != null;

        var useHosted = strategy switch
        {
            UploadStrategy.Hosted => true,
            UploadStrategy.Local => false,
            UploadStrategy.Auto => canUseHosted,  // Prefer hosted if available
            _ => false
        };

        // Validate strategy can be satisfied
        if (strategy == UploadStrategy.Hosted && !canUseHosted)
        {
            await context.PublishAsync(new HostedFileUploadFailedEvent(
                ErrorMessage: "UploadStrategy.Hosted requested but current provider does not support HostedFileClient"));
            return data;  // Keep original
        }

        if (strategy == UploadStrategy.Local && !canUseLocal)
        {
            await context.PublishAsync(new ContentUploadFailedEvent(
                ErrorMessage: "UploadStrategy.Local requested but no IContentStore configured"));
            return data;  // Keep original
        }

        // Try primary path
        if (useHosted && canUseHosted)
        {
            var result = await UploadToHostedAsync(
                context,
                data,
                hostedFileClient!,
                mediaType,
                cancellationToken);

            if (result != null)
                return result;

            // Hosted failed in Auto mode — try fallback
            if (strategy == UploadStrategy.Auto && canUseLocal)
            {
                return await UploadToLocalAsync(
                    context,
                    data,
                    mediaType,
                    cancellationToken) ?? data;
            }

            // Hosted failed in Hosted mode — error already emitted
            return data;
        }

        // Use local path
        if (canUseLocal)
        {
            return await UploadToLocalAsync(
                context,
                data,
                mediaType,
                cancellationToken) ?? data;
        }

        // No upload path available
        return data;
    }

    private async Task<AIContent?> UploadToHostedAsync(
        BeforeMessageTurnContext context,
        DataContent data,
        IHostedFileClient hostedClient,
        string mediaType,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(data.Data.ToArray());
            var fileName = data.Name ?? ExtractFileName(data) ?? $"upload_{Guid.NewGuid():N}";

            var uploadOptions = HostedFileOperationOptionsCompiler.Compile(
                context.RunConfig,
                context.ClientSet,
                omittedPurposeFallback: "assistants");

            var hostedContent = await hostedClient.UploadAsync(
                stream,
                mediaType,
                fileName,
                uploadOptions,
                cancellationToken);

            await context.PublishAsync(new HostedFileUploadedEvent(
                FileId: hostedContent.FileId,
                MediaType: mediaType,
                SizeBytes: data.Data.Length));

            return hostedContent;
        }
        catch (Exception ex)
        {
            await context.PublishAsync(new HostedFileUploadFailedEvent(
                ErrorMessage: $"Hosted upload failed: {ex.Message}"));
            return null;
        }
    }

    private async Task<AIContent?> UploadToLocalAsync(
        BeforeMessageTurnContext context,
        DataContent data,
        string mediaType,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = await _contentStore!.WriteBytesAsync(
                scope: CreateScope(context),
                data: data.Data.ToArray(),
                metadata: new ContentMetadata
                {
                    ContentType = mediaType,
                    Name = ExtractFileName(data),
                    Origin = ContentSource.User,
                    Tags = new Dictionary<string, string>
                    {
                        ["kind"] = "upload"
                    }
                },
                options: new ContentWriteOptions { Mode = ContentWriteMode.Create },
                cancellationToken: cancellationToken);

            var uriContent = new UriContent(
                ContentReferenceResolverMiddleware.CreateContentUri(info.Address.ContentId),
                mediaType);

            await context.PublishAsync(new ContentUploadedEvent(
                ContentId: info.Address.ContentId,
                MediaType: mediaType,
                SizeBytes: data.Data.Length));

            return uriContent;
        }
        catch (Exception ex)
        {
            await context.PublishAsync(new ContentUploadFailedEvent(
                ErrorMessage: $"Local upload failed: {ex.Message}"));
            return null;
        }
    }

    private static async ValueTask<IHostedFileClient?> GetHostedFileClientAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        if (context.RunConfig.Clients.HostedFiles?.Override?.Client is { } runClient)
            return runClient;

        return context.ClientSet is null ? null :
            await context.ClientSet.GetHostedFilesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? ExtractFileName(AIContent content)
    {
        if (content.AdditionalProperties != null &&
            content.AdditionalProperties.TryGetValue("filename", out var fn))
            return fn?.ToString();
        return null;
    }

    private static ContentScope CreateScope(HookContext context)
    {
        if (context.SessionId is null || context.ThreadId is null)
            throw new InvalidOperationException("Content upload requires an active session and thread.");

        return ContentScope.Create(ContentStoreScopes.ForThread(context.SessionId, context.ThreadId));
    }
}
