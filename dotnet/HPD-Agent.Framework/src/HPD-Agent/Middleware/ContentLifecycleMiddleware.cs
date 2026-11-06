#pragma warning disable MEAI001
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware;

/// <summary>Runtime transport for a stored attachment before provider materialization.</summary>
public sealed class StoredContentReferenceContent : AIContent
{
    /// <summary>Creates a stored-content transport part.</summary>
    public StoredContentReferenceContent(ContentAddress address, string mediaType, string? name)
    {
        Address = address;
        MediaType = mediaType;
        Name = name;
    }

    /// <summary>Gets the exact stored address.</summary>
    public ContentAddress Address { get; }
    /// <summary>Gets the media type.</summary>
    public string MediaType { get; }
    /// <summary>Gets the optional filename.</summary>
    public string? Name { get; }
}

/// <summary>Owns attachment upload and provider-boundary content materialization.</summary>
public sealed class ContentLifecycleMiddleware : IAgentMiddleware
{
    private readonly IContentStore? _store;

    /// <summary>Creates the content lifecycle middleware.</summary>
    public ContentLifecycleMiddleware(IContentStore? store = null) => _store = store;

    /// <inheritdoc />
    public async Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
    {
        if (context.Session is null ||
            _store is null &&
            context.ClientSet is null &&
            context.RunConfig.Clients.HostedFiles?.Override?.Client is null)
            return;
        var strategy = context.RunConfig.UploadStrategy;
        for (var messageIndex = 0; messageIndex < context.UserInputMessages.Count; messageIndex++)
        {
            var message = context.UserInputMessages[messageIndex];
            if (!message.Contents.Any(static content => content is DataContent { Data.Length: > 0 })) continue;
            var contents = new List<AIContent>(message.Contents.Count);
            foreach (var content in message.Contents)
                contents.Add(content is DataContent { Data.Length: > 0 } data
                    ? await UploadAsync(context, data, strategy, cancellationToken).ConfigureAwait(false)
                    : content);
            context.UserInputMessages[messageIndex] = Copy(message, contents);
        }
    }

    /// <inheritdoc />
    public async Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
    {
        if (_store is null || context.Session is null) return;
        for (var messageIndex = 0; messageIndex < context.Messages.Count; messageIndex++)
        {
            var message = context.Messages[messageIndex];
            if (!message.Contents.Any(static content => content is StoredContentReferenceContent)) continue;
            var contents = new List<AIContent>(message.Contents.Count);
            foreach (var content in message.Contents)
                contents.Add(content is StoredContentReferenceContent stored
                    ? await MaterializeAsync(context, stored, cancellationToken).ConfigureAwait(false)
                    : content);
            context.Messages[messageIndex] = Copy(message, contents);
        }
    }

    private async Task<AIContent> UploadAsync(
        BeforeMessageTurnContext context,
        DataContent data,
        UploadStrategy strategy,
        CancellationToken cancellationToken)
    {
        var mediaType = data.MediaType ?? "application/octet-stream";
        var hosted = await ResolveHostedClientAsync(context.RunConfig, context.ClientSet, cancellationToken).ConfigureAwait(false);
        var useHosted = strategy == UploadStrategy.Hosted || strategy == UploadStrategy.Auto && hosted is not null;
        if (strategy == UploadStrategy.Hosted && hosted is null)
        {
            await context.PublishAsync(new HostedFileUploadFailedEvent(
                "UploadStrategy.Hosted requested but no HostedFileClient is available.")).ConfigureAwait(false);
            return data;
        }
        if (strategy == UploadStrategy.Local && _store is null)
        {
            await context.PublishAsync(new ContentUploadFailedEvent(
                "UploadStrategy.Local requested but no content store is configured.")).ConfigureAwait(false);
            return data;
        }
        if (useHosted && hosted is not null)
        {
            try
            {
                await using var source = new MemoryStream(data.Data.ToArray());
                var uploaded = await hosted.UploadAsync(
                    source,
                    mediaType,
                    data.Name ?? $"upload_{Guid.NewGuid():N}",
                    HostedFileOperationOptionsCompiler.Compile(context.RunConfig, context.ClientSet, "assistants"),
                    cancellationToken).ConfigureAwait(false);
                await context.PublishAsync(new HostedFileUploadedEvent(
                    uploaded.FileId, mediaType, data.Data.Length)).ConfigureAwait(false);
                return uploaded;
            }
            catch (Exception exception) when (strategy == UploadStrategy.Auto && _store is not null)
            {
                await context.PublishAsync(new HostedFileUploadFailedEvent(
                    $"Hosted upload failed: {exception.Message}")).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await context.PublishAsync(new HostedFileUploadFailedEvent(
                    $"Hosted upload failed: {exception.Message}")).ConfigureAwait(false);
                return data;
            }
        }
        if (_store is null) return data;
        try
        {
            var info = await _store.WriteBytesAsync(
                ContentScope.Create(context.SessionId ?? throw new InvalidOperationException("content_session_required")),
                data.Data.ToArray(),
                new ContentMetadata
                {
                    ContentType = mediaType,
                    Name = data.Name,
                    Origin = ContentSource.User,
                    Tags = new Dictionary<string, string>
                    {
                        ["kind"] = "upload",
                        ["thread-id"] = context.ThreadId ?? "main"
                    }
                },
                new ContentWriteOptions { Mode = ContentWriteMode.Create },
                cancellationToken).ConfigureAwait(false);
            await context.PublishAsync(new ContentUploadedEvent(
                info.Address.ContentId, mediaType, checked((int)info.SizeBytes))).ConfigureAwait(false);
            return new StoredContentReferenceContent(info.Address, mediaType, data.Name);
        }
        catch (Exception exception)
        {
            await context.PublishAsync(new ContentUploadFailedEvent(
                $"Local upload failed: {exception.Message}")).ConfigureAwait(false);
            return data;
        }
    }

    private async Task<AIContent> MaterializeAsync(
        BeforeIterationContext context,
        StoredContentReferenceContent stored,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(context.SessionId) ||
                !string.Equals(stored.Address.Scope.Value, context.SessionId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Content is outside the current session.");
            var info = await _store!.StatAsync(stored.Address, cancellationToken).ConfigureAwait(false)
                ?? throw new FileNotFoundException($"Content '{stored.Address.ContentId}' was not found.");
            var ownerThreadId = info.Tags?.GetValueOrDefault("thread-id");
            if (ownerThreadId is null ||
                !string.Equals(ownerThreadId, context.ThreadId, StringComparison.Ordinal) &&
                !string.Equals(ownerThreadId, context.Thread?.ForkedFrom, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Content is outside the current thread.");
            var direct = await _store.CreateReadUriAsync(info.Address, TimeSpan.FromMinutes(15), cancellationToken)
                .ConfigureAwait(false);
            if (direct is not null)
            {
                await context.PublishAsync(new ContentReferenceResolvedEvent(
                    new Uri($"stored-content://{stored.Address.ContentId}"),
                    ContentReferenceResolutionKind.DirectUri,
                    info.ContentType,
                    info.SizeBytes)).ConfigureAwait(false);
                return new UriContent(direct, info.ContentType);
            }
            var hosted = await ResolveHostedClientAsync(context.RunConfig, context.ClientSet, cancellationToken).ConfigureAwait(false);
            if (hosted is not null)
            {
                await using var opened = await _store.OpenReadAsync(info.Address, cancellationToken).ConfigureAwait(false)
                    ?? throw new FileNotFoundException();
                var hostedContent = await hosted.UploadAsync(
                    opened.Content,
                    info.ContentType,
                    info.Name,
                    HostedFileOperationOptionsCompiler.Compile(context.RunConfig, context.ClientSet, "assistants"),
                    cancellationToken).ConfigureAwait(false);
                await context.PublishAsync(new ContentReferenceResolvedEvent(
                    new Uri($"stored-content://{stored.Address.ContentId}"),
                    ContentReferenceResolutionKind.HostedFile,
                    info.ContentType,
                    info.SizeBytes)).ConfigureAwait(false);
                return hostedContent;
            }
            await using var result = await _store.OpenReadAsync(info.Address, cancellationToken).ConfigureAwait(false)
                ?? throw new FileNotFoundException();
            using var memory = new MemoryStream();
            await result.Content.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            await context.PublishAsync(new ContentReferenceResolvedEvent(
                new Uri($"stored-content://{stored.Address.ContentId}"),
                ContentReferenceResolutionKind.BufferedData,
                info.ContentType,
                info.SizeBytes)).ConfigureAwait(false);
            return new DataContent(memory.ToArray(), info.ContentType) { Name = info.Name };
        }
        catch (Exception exception)
        {
            await context.PublishAsync(new ContentReferenceResolutionFailedEvent(
                new Uri($"stored-content://{stored.Address.ContentId}"),
                $"Resolution failed: {exception.Message}")).ConfigureAwait(false);
            return new TextContent($"[content unavailable: {stored.Address.ContentId}]");
        }
    }

    private static async ValueTask<IHostedFileClient?> ResolveHostedClientAsync(
        AgentRunConfig runConfig,
        AgentClientSet? clientSet,
        CancellationToken cancellationToken)
    {
        if (runConfig.Clients.HostedFiles?.Override?.Client is { } client) return client;
        return clientSet is null ? null : await clientSet.GetHostedFilesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ChatMessage Copy(ChatMessage source, IReadOnlyList<AIContent> contents) => new(source.Role, contents.ToList())
    {
        AuthorName = source.AuthorName,
        AdditionalProperties = source.AdditionalProperties,
        CreatedAt = source.CreatedAt,
        MessageId = source.MessageId,
        RawRepresentation = source.RawRepresentation
    };
}
