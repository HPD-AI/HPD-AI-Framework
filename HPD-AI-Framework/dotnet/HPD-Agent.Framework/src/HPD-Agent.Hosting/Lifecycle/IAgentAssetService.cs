using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentAssetService
{
    Task<AgentServiceResult<AssetDto>> UploadAssetAsync(
        string sessionId,
        Stream content,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<IReadOnlyList<AssetDto>>> ListAssetsAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<AgentAssetDownload>> DownloadAssetAsync(
        string sessionId,
        string assetId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult> DeleteAssetAsync(
        string sessionId,
        string assetId,
        CancellationToken cancellationToken = default);
}

public sealed record AgentAssetDownload(
    byte[] Data,
    string ContentType,
    string? FileName);
