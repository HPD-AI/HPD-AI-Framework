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
    /// Convert a Branch to a BranchDto.
    /// </summary>
    public static BranchDto ToDto(this Branch branch, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return new BranchDto(
            branch.Id,
            sessionId,
            branch.GetDisplayName(),
            branch.Description,
            branch.ForkedFrom,
            branch.ForkedAtMessageId,
            branch.ForkedAtMessageIndex,
            branch.CreatedAt,
            branch.LastActivity,
            branch.MessageCount,
            branch.Tags,
            branch.Ancestors,
            branch.SiblingIndex,
            branch.TotalSiblings,
            branch.IsOriginal,
            branch.OriginalBranchId,
            branch.PreviousSiblingId,
            branch.NextSiblingId,
            branch.TotalForks,
            branch.Metadata.Count > 0 ? branch.Metadata : null);
    }

    /// <summary>
    /// Convert WorkspaceContentInfo to ContentDto.
    /// </summary>
    public static ContentDto ToDto(this WorkspaceContentInfo content)
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
