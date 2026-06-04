using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware;

/// <summary>
/// Resolves HPD internal content references into provider-facing MEAI content.
/// </summary>
/// <remarks>
/// <para>
/// `hpd-content://{contentId}` is an internal durable reference for session and branch history.
/// Providers should see a normal `UriContent`, `HostedFileContent`, or `DataContent`, not the
/// internal HPD reference.
/// </para>
/// <para>
/// Resolution order:
/// </para>
/// <list type="number">
/// <item>Provider hosted file upload from the workspace content stream.</item>
/// <item>Buffered `DataContent` fallback.</item>
/// </list>
/// </remarks>
public class ContentReferenceResolverMiddleware : IAgentMiddleware
{
    public const string ContentUriScheme = "hpd-content";

    private readonly IWorkspaceStore? _workspaceStore;

    public ContentReferenceResolverMiddleware(IWorkspaceStore? workspaceStore = null)
    {
        _workspaceStore = workspaceStore;
    }

    public async Task BeforeIterationAsync(
        BeforeIterationContext context,
        CancellationToken cancellationToken)
    {
        if (_workspaceStore == null || context.Session == null)
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
                        context.Session,
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
        Session session,
        CancellationToken cancellationToken)
    {
        var contentId = ExtractContentId(uriContent.Uri);
        if (string.IsNullOrWhiteSpace(contentId))
            return null;

        try
        {
            var info = await _workspaceStore!.StatContentAsync(
                WorkspacePrincipalRef.System,
                contentId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (info == null)
            {
                context.Emit(new ContentReferenceResolutionFailedEvent(
                    ContentUri: uriContent.Uri,
                    Error: $"Content not found in workspace: {contentId}"));
                return null;
            }

            var hostedFileClient = GetHostedFileClient(context);
            if (hostedFileClient != null)
            {
                var hosted = await UploadToHostedFileAsync(
                    context,
                    hostedFileClient,
                    contentId,
                    info,
                    uriContent.Uri,
                    cancellationToken).ConfigureAwait(false);

                if (hosted != null)
                    return hosted;
            }

            return await BufferAsDataContentAsync(
                context,
                contentId,
                info,
                uriContent.Uri,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.Emit(new ContentReferenceResolutionFailedEvent(
                ContentUri: uriContent.Uri,
                Error: $"Resolution failed: {ex.Message}"));
            return null;
        }
    }

    private async Task<AIContent?> UploadToHostedFileAsync(
        BeforeIterationContext context,
        IHostedFileClient hostedFileClient,
        string contentId,
        WorkspaceContentInfo info,
        Uri sourceUri,
        CancellationToken cancellationToken)
    {
        await using var stream = await _workspaceStore!.OpenContentAsync(
                WorkspacePrincipalRef.System,
                contentId,
                info.Version,
                cancellationToken)
            .ConfigureAwait(false);
        if (stream == null)
            return null;

        try
        {
            var hosted = await hostedFileClient.UploadAsync(
                stream,
                info.ContentType,
                info.Name,
                new HostedFileClientOptions { Purpose = "assistants" },
                cancellationToken).ConfigureAwait(false);

            context.Emit(new ContentReferenceResolvedEvent(
                ContentUri: sourceUri,
                ResolutionKind: ContentReferenceResolutionKind.HostedFile,
                MediaType: info.ContentType,
                SizeBytes: info.SizeBytes));

            return hosted;
        }
        catch (Exception ex)
        {
            context.Emit(new HostedFileUploadFailedEvent(
                Error: $"Hosted upload from workspace content failed: {ex.Message}"));
            return null;
        }
    }

    private async Task<AIContent?> BufferAsDataContentAsync(
        HookContext context,
        string contentId,
        WorkspaceContentInfo info,
        Uri sourceUri,
        CancellationToken cancellationToken)
    {
        await using var stream = await _workspaceStore!.OpenContentAsync(
                WorkspacePrincipalRef.System,
                contentId,
                info.Version,
                cancellationToken)
            .ConfigureAwait(false);
        if (stream == null)
            return null;

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);

        context.Emit(new ContentReferenceResolvedEvent(
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
        if (context.RunConfig.OverrideHostedFileClient is { } runClient)
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
}
