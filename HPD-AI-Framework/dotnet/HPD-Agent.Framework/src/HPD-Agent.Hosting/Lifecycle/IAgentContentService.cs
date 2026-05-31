using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentContentService
{
    Task<AgentServiceResult<ContentDto>> UploadContentAsync(
        string sessionId,
        Stream content,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<IReadOnlyList<ContentDto>>> ListContentAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<AgentContentDownload>> DownloadContentAsync(
        string sessionId,
        string contentId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult> DeleteContentAsync(
        string sessionId,
        string contentId,
        CancellationToken cancellationToken = default);
}

public sealed record AgentContentDownload(
    byte[] Data,
    string ContentType,
    string? FileName);
