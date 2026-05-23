using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentAssetService : IAgentAssetService
{
    private readonly SessionManager _sessionManager;

    public AgentAssetService(SessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public async Task<AgentServiceResult<AssetDto>> UploadAssetAsync(
        string sessionId,
        Stream content,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(content);

        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<AssetDto>.NotFound;

        var contentStore = _sessionManager.Store.GetContentStore(sessionId);
        if (contentStore == null)
            return AgentServiceResult<AssetDto>.Validation(
                "AssetStoreNotAvailable",
                "Content storage is not available for this session store.");

        using var memoryStream = new MemoryStream();
        await content.CopyToAsync(memoryStream, cancellationToken);

        var assetId = await contentStore.PutAsync(
            scope: sessionId,
            data: memoryStream.ToArray(),
            contentType: contentType ?? "application/octet-stream",
            metadata: new ContentMetadata
            {
                Name = fileName,
                Origin = ContentSource.User,
                Tags = new Dictionary<string, string>
                {
                    ["folder"] = "/uploads",
                    ["session"] = sessionId
                }
            },
            cancellationToken: cancellationToken);

        var stored = await contentStore.GetAsync(sessionId, assetId, cancellationToken);
        if (stored == null)
            return AgentServiceResult<AssetDto>.Validation(
                "UploadFailed",
                "Asset was uploaded but could not be retrieved.");

        var dto = new AssetDto(
            assetId,
            stored.ContentType,
            stored.Data.Length,
            stored.Info.CreatedAt.ToString("O"));

        return AgentServiceResult<AssetDto>.Success(dto);
    }

    public async Task<AgentServiceResult<IReadOnlyList<AssetDto>>> ListAssetsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<IReadOnlyList<AssetDto>>.NotFound;

        var contentStore = _sessionManager.Store.GetContentStore(sessionId);
        if (contentStore == null)
            return AgentServiceResult<IReadOnlyList<AssetDto>>.Success([]);

        var assets = await contentStore.QueryAsync(
            scope: sessionId,
            query: new ContentQuery { Tags = new Dictionary<string, string> { ["folder"] = "/uploads" } },
            cancellationToken: cancellationToken);

        var dtos = assets.Select(a => new AssetDto(
            a.Id,
            a.ContentType,
            a.SizeBytes,
            a.CreatedAt.ToString("O"))).ToList();

        return AgentServiceResult<IReadOnlyList<AssetDto>>.Success(dtos);
    }

    public async Task<AgentServiceResult<AgentAssetDownload>> DownloadAssetAsync(
        string sessionId,
        string assetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<AgentAssetDownload>.NotFound;

        var contentStore = _sessionManager.Store.GetContentStore(sessionId);
        var content = contentStore == null
            ? null
            : await contentStore.GetAsync(sessionId, assetId, cancellationToken);

        if (content == null)
            return AgentServiceResult<AgentAssetDownload>.NotFound;

        return AgentServiceResult<AgentAssetDownload>.Success(
            new AgentAssetDownload(content.Data, content.ContentType, content.Info.Name));
    }

    public async Task<AgentServiceResult> DeleteAssetAsync(
        string sessionId,
        string assetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult.NotFound;

        var contentStore = _sessionManager.Store.GetContentStore(sessionId);
        var content = contentStore == null
            ? null
            : await contentStore.GetAsync(sessionId, assetId, cancellationToken);

        if (content == null)
            return AgentServiceResult.NotFound;

        await contentStore!.DeleteAsync(sessionId, assetId, cancellationToken);
        return AgentServiceResult.Success;
    }
}
