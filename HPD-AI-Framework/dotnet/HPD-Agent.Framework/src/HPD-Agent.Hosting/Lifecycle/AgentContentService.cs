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
        Stream content,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(content);

        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<ContentDto>.NotFound;

        var stored = await _contentStore.WriteAsync(
            scope: sessionId,
            data: content,
            metadata: new ContentMetadata
            {
                ContentType = contentType ?? "application/octet-stream",
                Name = fileName,
                Origin = ContentSource.User,
                Tags = new Dictionary<string, string>
                {
                    ["folder"] = "/uploads"
                }
            },
            options: new ContentWriteOptions { Mode = ContentWriteMode.Create },
            cancellationToken: cancellationToken);

        var dto = new ContentDto(
            stored.Id,
            stored.Version,
            stored.ContentType,
            stored.SizeBytes,
            stored.CreatedAt.ToString("O"));

        return AgentServiceResult<ContentDto>.Success(dto);
    }

    public async Task<AgentServiceResult<IReadOnlyList<ContentDto>>> ListContentAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<IReadOnlyList<ContentDto>>.NotFound;

        var content = await _contentStore.QueryAsync(
            scope: sessionId,
            query: new ContentQuery { Tags = new Dictionary<string, string> { ["folder"] = "/uploads" } },
            cancellationToken: cancellationToken);

        var dtos = content.Select(a => new ContentDto(
            a.Id,
            a.Version,
            a.ContentType,
            a.SizeBytes,
            a.CreatedAt.ToString("O"))).ToList();

        return AgentServiceResult<IReadOnlyList<ContentDto>>.Success(dtos);
    }

    public async Task<AgentServiceResult<AgentContentDownload>> DownloadContentAsync(
        string sessionId,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);

        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<AgentContentDownload>.NotFound;

        var info = await _contentStore.StatAsync(sessionId, contentId, cancellationToken);

        if (info == null)
            return AgentServiceResult<AgentContentDownload>.NotFound;

        var data = await _contentStore.ReadBytesAsync(sessionId, contentId, cancellationToken);
        if (data == null)
            return AgentServiceResult<AgentContentDownload>.NotFound;

        return AgentServiceResult<AgentContentDownload>.Success(
            new AgentContentDownload(data, info.ContentType, info.Name));
    }

    public async Task<AgentServiceResult> DeleteContentAsync(
        string sessionId,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);

        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult.NotFound;

        var content = await _contentStore.StatAsync(sessionId, contentId, cancellationToken);

        if (content == null)
            return AgentServiceResult.NotFound;

        await _contentStore.DeleteAsync(
            sessionId,
            contentId,
            new ContentDeleteOptions { IfMatchVersion = content.Version },
            cancellationToken);
        return AgentServiceResult.Success;
    }
}
