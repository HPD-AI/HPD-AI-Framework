namespace HPD.Agent;

/// <summary>
/// Typed facade for session and branch runtime records stored as workspace spaces and documents.
/// </summary>
public interface ISessionRepository
{
    Task<Session?> LoadSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task SaveSessionAsync(
        Session session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListSessionIdsAsync(
        CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<Branch?> LoadBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default);

    Task<BranchEventDocument?> LoadBranchDocumentAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default);

    Task SaveBranchDocumentAsync(
        BranchEventDocument document,
        long? expectedSequenceNumber = null,
        CancellationToken cancellationToken = default);

    Task AppendBranchEventAsync(
        string sessionId,
        string branchId,
        AgentEvent evt,
        long? expectedSequenceNumber = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentEvent> ReadBranchEventsAsync(
        string sessionId,
        string branchId,
        HPD.Events.ReplayReadOptions options,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListBranchIdsAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task DeleteBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default);

    Task<int> DeleteInactiveSessionsAsync(
        TimeSpan inactivityThreshold,
        bool dryRun = false,
        CancellationToken cancellationToken = default);
}
