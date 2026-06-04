using HPD.Agent;

namespace HPD.Agent.Audio.V2.Tests;

internal static class WorkspaceTestExtensions
{
    public static async Task<IReadOnlyList<WorkspaceVisibleContentResult>> QueryAsync(
        this IWorkspaceStore workspace,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var sessionSpace = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = sessionId
            },
            cancellationToken).ConfigureAwait(false);

        if (sessionSpace is null)
            return [];

        return await workspace.SearchContentAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceVisibleContentQuery
            {
                SpaceId = sessionSpace.Id,
                TraversalMode = WorkspaceContentTraversalMode.SpaceDescendants
            },
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<WorkspaceStoredContentInfo?> StatAsync(
        this IWorkspaceStore workspace,
        string sessionId,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        var matches = await workspace.QueryAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var match = matches.FirstOrDefault(item => item.ContentId == contentId);
        return match is null
            ? null
            : new WorkspaceStoredContentInfo(
                match.Content.ContentType,
                match.Content.SizeBytes,
                ParseContentSource(match.Content.Metadata),
                match.Attachment.Role,
                match.Attachment.PathHint,
                match.Content.Metadata);
    }

    public static async Task<byte[]> ReadBytesAsync(
        this IWorkspaceStore workspace,
        string sessionId,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        await using var stream = await workspace.OpenContentAsync(
            WorkspacePrincipalRef.System,
            contentId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (stream is null)
            return [];

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static ContentSource? ParseContentSource(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is not null &&
            metadata.TryGetValue("origin", out var origin) &&
            Enum.TryParse<ContentSource>(origin, out var source))
        {
            return source;
        }

        return null;
    }
}

internal sealed record WorkspaceStoredContentInfo(
    string ContentType,
    long SizeBytes,
    ContentSource? Origin,
    string Role,
    string? PathHint,
    IReadOnlyDictionary<string, string>? Tags);
