using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentContentService : IAgentContentService
{
    private readonly SessionManager _sessionManager;
    private readonly IContentStore _contentStore;

    public AgentContentService(SessionManager sessionManager, IContentStore? contentStore = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _contentStore = contentStore ?? new InMemoryContentStore();
    }

    public async Task<AgentServiceResult<ContentDto>> UploadContentAsync(
        string sessionId,
        string threadId,
        Stream content,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(content);

        if (await _sessionManager.Store.GetThreadAsync(new ThreadKey(sessionId, threadId), cancellationToken) == null)
            return AgentServiceResult<ContentDto>.NotFound;

        var scope = ContentScope.Create(ContentStoreScopes.ForThread(sessionId, threadId));
        var stored = await _contentStore.WriteAsync(
            scope: scope,
            data: content,
            metadata: new ContentMetadata
            {
                ContentType = contentType ?? "application/octet-stream",
                Name = fileName,
                Origin = ContentSource.User,
                Tags = new Dictionary<string, string>
                {
                    ["kind"] = "upload"
                }
            },
            options: new ContentWriteOptions { Mode = ContentWriteMode.Create },
            cancellationToken: cancellationToken);

        var dto = new ContentDto(
            stored.Address.ContentId,
            stored.Address.Version!,
            stored.ContentType,
            stored.SizeBytes,
            stored.CreatedAt.ToString("O"));

        return AgentServiceResult<ContentDto>.Success(dto);
    }

    public async Task<AgentServiceResult<IReadOnlyList<ContentDto>>> ListContentAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        if (await _sessionManager.Store.GetThreadAsync(new ThreadKey(sessionId, threadId), cancellationToken) == null)
            return AgentServiceResult<IReadOnlyList<ContentDto>>.NotFound;

        var content = await _contentStore.QueryAsync(
            scope: ContentScope.Create(ContentStoreScopes.ForThread(sessionId, threadId)),
            query: new ContentQuery { Tags = new Dictionary<string, string> { ["kind"] = "upload" } },
            cancellationToken: cancellationToken);

        var dtos = content.Select(a => new ContentDto(
            a.Address.ContentId,
            a.Address.Version!,
            a.ContentType,
            a.SizeBytes,
            a.CreatedAt.ToString("O"))).ToList();

        return AgentServiceResult<IReadOnlyList<ContentDto>>.Success(dtos);
    }

    public async Task<AgentServiceResult<AgentContentDownload>> DownloadContentAsync(
        string sessionId,
        string threadId,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);

        if (await _sessionManager.Store.GetThreadAsync(new ThreadKey(sessionId, threadId), cancellationToken) == null)
            return AgentServiceResult<AgentContentDownload>.NotFound;

        var scope = ContentScope.Create(ContentStoreScopes.ForThread(sessionId, threadId));
        var info = await _contentStore.StatAsync(new ContentAddress(scope, contentId), cancellationToken);

        if (info == null)
            return AgentServiceResult<AgentContentDownload>.NotFound;

        var data = await _contentStore.ReadBytesAsync(info.Address, cancellationToken);
        if (data == null)
            return AgentServiceResult<AgentContentDownload>.NotFound;

        return AgentServiceResult<AgentContentDownload>.Success(
            new AgentContentDownload(data, info.ContentType, info.Name));
    }

    public async Task<AgentServiceResult> DeleteContentAsync(
        string sessionId,
        string threadId,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);

        if (await _sessionManager.Store.GetThreadAsync(new ThreadKey(sessionId, threadId), cancellationToken) == null)
            return AgentServiceResult.NotFound;

        var scope = ContentScope.Create(ContentStoreScopes.ForThread(sessionId, threadId));
        var content = await _contentStore.StatAsync(new ContentAddress(scope, contentId), cancellationToken);

        if (content == null)
            return AgentServiceResult.NotFound;

        await _contentStore.DeleteAsync(content.Address, cancellationToken);
        return AgentServiceResult.Success;
    }
}
