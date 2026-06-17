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

        return SaveAsync();

        async Task SaveAsync()
        {
            var document = BranchEventDocumentBuilder.FromInitialBranch(sessionId, branch);
            foreach (var evt in document.Events)
            {
                await store.AppendBranchEventAsync(
                    sessionId,
                    branch.Id,
                    evt,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
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
