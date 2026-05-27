namespace HPD.Agent;

public static class BranchEventStoreExtensions
{
    public static Task SaveInitialBranchAsync(
        this ISessionStore store,
        string sessionId,
        Branch branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(branch);

        var document = BranchEventDocumentBuilder.FromBranchSnapshot(sessionId, branch);
        return store.SaveBranchDocumentAsync(document, cancellationToken: cancellationToken);
    }

    public static Task AppendBranchMetadataUpdatedAsync(
        this ISessionStore store,
        Branch branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(branch);

        return store.AppendBranchEventAsync(
            branch.SessionId,
            branch.Id,
            BranchEventFactory.BranchMetadataUpdated(branch),
            cancellationToken: cancellationToken);
    }

    public static Task AppendBranchTreeUpdatedAsync(
        this ISessionStore store,
        Branch branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(branch);

        return store.AppendBranchEventAsync(
            branch.SessionId,
            branch.Id,
            BranchEventFactory.BranchTreeUpdated(branch),
            cancellationToken: cancellationToken);
    }
}
