using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentSessionService
{
    Task<SessionDto> CreateSessionAsync(
        CreateSessionRequest? request = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionDto>> SearchSessionsAsync(
        SearchSessionsRequest? request = null,
        CancellationToken cancellationToken = default);

    Task<SessionDto?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<SessionDto?> UpdateSessionAsync(
        string sessionId,
        UpdateSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}
