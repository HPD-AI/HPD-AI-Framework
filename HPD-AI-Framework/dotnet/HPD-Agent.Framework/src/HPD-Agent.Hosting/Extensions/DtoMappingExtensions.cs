using HPD.Agent;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Extensions;

/// <summary>
/// Extension methods for converting between domain objects and DTOs.
/// </summary>
public static class DtoMappingExtensions
{
    /// <summary>
    /// Convert a Session to a SessionDto.
    /// </summary>
    public static SessionDto ToDto(this Session session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new SessionDto(
            session.Id,
            session.CreatedAt,
            session.LastActivity,
            session.Metadata.Count > 0 ? session.Metadata : null);
    }

    /// <summary>
    /// Convert a Thread to a ThreadDto.
    /// </summary>
    public static ThreadDto ToDto(this Thread thread, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return new ThreadDto(
            thread.Id,
            sessionId,
            thread.GetDisplayName(),
            thread.Description,
            thread.ForkedFrom,
            thread.ForkedAtMessageId,
            thread.ForkedAtMessageIndex,
            thread.CreatedAt,
            thread.LastActivity,
            thread.MessageCount,
            thread.Tags,
            thread.Ancestors,
            thread.TotalForks,
            thread.Metadata.Count > 0 ? thread.Metadata : null,
            thread.Kind,
            thread.Visibility,
            thread.ParentSessionId,
            thread.ParentThreadId,
            thread.SubAgentName,
            thread.SubAgentRunId,
            thread.SubAgentSourceKind,
            thread.ParentToolCallId,
            thread.SessionPolicy,
            thread.ThreadPolicy);
    }

    /// <summary>
    /// Convert ContentInfo to ContentDto.
    /// </summary>
    public static ContentDto ToDto(this ContentInfo content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new ContentDto(
            content.Id,
            content.Version,
            content.ContentType,
            content.SizeBytes,
            content.CreatedAt.ToString("O"));
    }

}
