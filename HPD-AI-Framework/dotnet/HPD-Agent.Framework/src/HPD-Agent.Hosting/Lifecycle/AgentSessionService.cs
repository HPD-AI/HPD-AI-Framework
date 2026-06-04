using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentSessionService : IAgentSessionService
{
    private readonly SessionManager _sessionManager;

    public AgentSessionService(SessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public async Task<SessionDto> CreateSessionAsync(
        CreateSessionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var (sessionId, _) = await _sessionManager.CreateSessionAsync(
            request?.SessionId,
            request?.Metadata,
            cancellationToken);

        var session = await _sessionManager.Repository.LoadSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session '{sessionId}' not found after creation.");

        return ToDto(session);
    }

    public async Task<IReadOnlyList<SessionDto>> SearchSessionsAsync(
        SearchSessionsRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var sessionIds = await _sessionManager.Repository.ListSessionIdsAsync(cancellationToken);
        var sessions = new List<SessionDto>();

        foreach (var sessionId in sessionIds)
        {
            var session = await _sessionManager.Repository.LoadSessionAsync(sessionId, cancellationToken);
            if (session == null || !MatchesMetadata(session.Metadata, request?.Metadata))
                continue;

            sessions.Add(ToDto(session));
        }

        var offset = request?.Offset ?? 0;
        var limit = request?.Limit ?? 50;

        return sessions
            .OrderByDescending(s => s.LastActivity)
            .Skip(offset)
            .Take(limit)
            .ToList();
    }

    public async Task<SessionDto?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var session = await _sessionManager.Repository.LoadSessionAsync(sessionId, cancellationToken);
        return session == null ? null : ToDto(session);
    }

    public Task<SessionDto?> UpdateSessionAsync(
        string sessionId,
        UpdateSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(request);

        return _sessionManager.WithSessionLockAsync(
            sessionId,
            () => UpdateSessionCoreAsync(sessionId, request, cancellationToken),
            cancellationToken);
    }

    public async Task<bool> DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var session = await _sessionManager.Repository.LoadSessionAsync(sessionId, cancellationToken);
        if (session == null)
            return false;

        await _sessionManager.Repository.DeleteSessionAsync(sessionId, cancellationToken);
        _sessionManager.RemoveSession(sessionId);
        return true;
    }

    private async Task<SessionDto?> UpdateSessionCoreAsync(
        string sessionId,
        UpdateSessionRequest request,
        CancellationToken cancellationToken)
    {
        var session = await _sessionManager.Repository.LoadSessionAsync(sessionId, cancellationToken);
        if (session == null)
            return null;

        if (request.Metadata != null)
        {
            foreach (var kvp in request.Metadata)
            {
                if (IsNullValue(kvp.Value))
                    session.Metadata.Remove(kvp.Key);
                else
                    session.Metadata[kvp.Key] = kvp.Value!;
            }
        }

        session.LastActivity = DateTime.UtcNow;
        await _sessionManager.Repository.SaveSessionAsync(session, cancellationToken);

        return ToDto(session);
    }

    private static SessionDto ToDto(Session session)
    {
        var cleanedMetadata = session.Metadata
            .Where(kvp => kvp.Value != null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return new SessionDto(
            session.Id,
            session.CreatedAt,
            session.LastActivity,
            cleanedMetadata);
    }

    private static bool MatchesMetadata(
        Dictionary<string, object> metadata,
        Dictionary<string, object>? filter)
    {
        if (filter == null || filter.Count == 0)
            return true;

        foreach (var kvp in filter)
        {
            if (!metadata.TryGetValue(kvp.Key, out var value))
                return false;

            if ((value?.ToString() ?? "") != (kvp.Value?.ToString() ?? ""))
                return false;
        }

        return true;
    }

    private static bool IsNullValue(object? value) =>
        value == null ||
        value is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined };
}
