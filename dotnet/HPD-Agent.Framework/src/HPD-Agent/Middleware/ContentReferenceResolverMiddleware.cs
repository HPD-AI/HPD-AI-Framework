using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware;

/// <summary>
/// Resolves HPD internal content references into provider-facing MEAI content.
/// </summary>
/// <remarks>
/// <para>
/// `hpd-content://{contentId}` is an internal durable reference for session and thread history.
/// Providers should see a normal `UriContent`, `HostedFileContent`, or `DataContent`, not the
/// internal HPD reference.
/// </para>
/// <para>
/// Resolution order:
/// </para>
/// <list type="number">
/// <item>Temporary direct URI from the content store.</item>
/// <item>Provider hosted file upload from the content stream.</item>
/// <item>Buffered `DataContent` fallback.</item>
/// </list>
/// </remarks>
public class ContentReferenceResolverMiddleware : IAgentMiddleware
{
    public const string ContentUriScheme = "hpd-content";

    private readonly IContentStore? _contentStore;

    public ContentReferenceResolverMiddleware(IContentStore? contentStore = null)
    {
        _contentStore = contentStore;
    }

    public async Task BeforeIterationAsync(
        BeforeIterationContext context,
        CancellationToken cancellationToken)
    {
        if (_contentStore == null || context.Session == null)
            return;

        for (var i = 0; i < context.Messages.Count; i++)
        {
            var message = context.Messages[i];
            if (!message.Contents.Any(IsContentReference))
                continue;

            var resolvedContents = new List<AIContent>();
            foreach (var content in message.Contents)
            {
                AIContent resolvedContent = content;

                if (content is UriContent uri && IsContentReference(uri))
                {
                    var resolved = await ResolveContentReferenceAsync(
                        context,
                        uri,
                        cancellationToken);

                    resolvedContent = resolved ?? content;
                }

                resolvedContents.Add(resolvedContent);
            }

            context.Messages[i] = new ChatMessage(message.Role, resolvedContents)
            {
                AuthorName = message.AuthorName,
                AdditionalProperties = message.AdditionalProperties
            };
        }
    }

    public static Uri CreateContentUri(string contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            throw new ArgumentException("Content id cannot be empty.", nameof(contentId));

        return new Uri($"{ContentUriScheme}://{contentId}");
    }

    public static bool IsContentReference(AIContent content) =>
        content is UriContent uriContent && IsContentReference(uriContent);

    public static bool IsContentReference(UriContent content) =>
        content.Uri.Scheme.Equals(ContentUriScheme, StringComparison.OrdinalIgnoreCase);

    private async Task<AIContent?> ResolveContentReferenceAsync(
        BeforeIterationContext context,
        UriContent uriContent,
        CancellationToken cancellationToken)
    {
        var contentId = ExtractContentId(uriContent.Uri);
        if (string.IsNullOrWhiteSpace(contentId))
            return null;

        try
        {
            var scope = CreateScope(context);
            var address = new ContentAddress(scope, contentId);
            var info = await _contentStore!.StatAsync(
                address: address,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (info == null)
            {
                await context.PublishAsync(new ContentReferenceResolutionFailedEvent(
                    ContentUri: uriContent.Uri,
                    ErrorMessage: $"Content not found in store: {contentId}"));
                return null;
            }

            var directUri = await _contentStore.CreateReadUriAsync(
                address: info.Address,
                expiresIn: TimeSpan.FromMinutes(15),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (directUri != null)
            {
                await context.PublishAsync(new ContentReferenceResolvedEvent(
                    ContentUri: uriContent.Uri,
                    ResolutionKind: ContentReferenceResolutionKind.DirectUri,
                    MediaType: info.ContentType,
                    SizeBytes: info.SizeBytes));

                return new UriContent(directUri, info.ContentType);
            }

            var hostedFileClient = GetHostedFileClient(context);
            if (hostedFileClient != null)
            {
                var hosted = await UploadToHostedFileAsync(
                    context,
                    hostedFileClient,
                    scope,
                    contentId,
                    info,
                    uriContent.Uri,
                    cancellationToken).ConfigureAwait(false);

                if (hosted != null)
                    return hosted;
            }

            return await BufferAsDataContentAsync(
                context,
                scope,
                contentId,
                info,
                uriContent.Uri,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await context.PublishAsync(new ContentReferenceResolutionFailedEvent(
                ContentUri: uriContent.Uri,
                ErrorMessage: $"Resolution failed: {ex.Message}"));
            return null;
        }
    }

    private async Task<AIContent?> UploadToHostedFileAsync(
        BeforeIterationContext context,
        IHostedFileClient hostedFileClient,
        ContentScope scope,
        string contentId,
        ContentInfo info,
        Uri sourceUri,
        CancellationToken cancellationToken)
    {
        await using var result = await _contentStore!.OpenReadAsync(info.Address, cancellationToken)
            .ConfigureAwait(false);
        if (result == null)
            return null;

        try
        {
            var hosted = await hostedFileClient.UploadAsync(
                result.Content,
                info.ContentType,
                info.Name,
                HostedFileOperationOptionsCompiler.Compile(
                    context.RunConfig,
                    context.ClientSet,
                    omittedPurposeFallback: "assistants"),
                cancellationToken).ConfigureAwait(false);

            await context.PublishAsync(new ContentReferenceResolvedEvent(
                ContentUri: sourceUri,
                ResolutionKind: ContentReferenceResolutionKind.HostedFile,
                MediaType: info.ContentType,
                SizeBytes: info.SizeBytes));

            return hosted;
        }
        catch (Exception ex)
        {
            await context.PublishAsync(new HostedFileUploadFailedEvent(
                ErrorMessage: $"Hosted upload from content store failed: {ex.Message}"));
            return null;
        }
    }

    private async Task<AIContent?> BufferAsDataContentAsync(
        HookContext context,
        ContentScope scope,
        string contentId,
        ContentInfo info,
        Uri sourceUri,
        CancellationToken cancellationToken)
    {
        await using var result = await _contentStore!.OpenReadAsync(info.Address, cancellationToken)
            .ConfigureAwait(false);
        if (result == null)
            return null;

        using var memory = new MemoryStream();
        await result.Content.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);

        await context.PublishAsync(new ContentReferenceResolvedEvent(
            ContentUri: sourceUri,
            ResolutionKind: ContentReferenceResolutionKind.BufferedData,
            MediaType: info.ContentType,
            SizeBytes: memory.Length));

        return new DataContent(memory.ToArray(), info.ContentType)
        {
            Name = info.Name
        };
    }

    private IHostedFileClient? GetHostedFileClient(BeforeIterationContext context)
    {
        if (context.RunConfig.Clients.HostedFiles?.Override?.Client is { } runClient)
            return runClient;

        if (context.ClientSet?.HostedFiles is { } buildClient)
            return buildClient;

        return null;
    }

    private static string ExtractContentId(Uri uri)
    {
        var contentId = uri.Host;
        if (string.IsNullOrWhiteSpace(contentId))
            contentId = uri.AbsolutePath.TrimStart('/');
        return contentId;
    }

    private static ContentScope CreateScope(HookContext context)
    {
        if (context.SessionId is null || context.ThreadId is null)
            throw new InvalidOperationException("Content reference resolution requires an active session and thread.");

        return ContentScope.Create(ContentStoreScopes.ForThread(context.SessionId, context.ThreadId));
    }
}
