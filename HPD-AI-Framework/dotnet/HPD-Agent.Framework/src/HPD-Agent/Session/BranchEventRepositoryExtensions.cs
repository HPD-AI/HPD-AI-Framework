namespace HPD.Agent;

public static class BranchEventRepositoryExtensions
{
    public static Task SaveInitialBranchAsync(
        this ISessionRepository repository,
        string sessionId,
        Branch branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(branch);

        var document = BranchEventDocumentBuilder.FromBranchSnapshot(sessionId, branch);
        return repository.SaveBranchDocumentAsync(document, cancellationToken: cancellationToken);
    }

    public static Task AppendBranchMetadataUpdatedAsync(
        this ISessionRepository repository,
        Branch branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(branch);

        return repository.AppendBranchEventAsync(
            branch.SessionId,
            branch.Id,
            BranchEventFactory.BranchMetadataUpdated(branch),
            cancellationToken: cancellationToken);
    }

    public static Task AppendBranchTreeUpdatedAsync(
        this ISessionRepository repository,
        Branch branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(branch);

        return repository.AppendBranchEventAsync(
            branch.SessionId,
            branch.Id,
            BranchEventFactory.BranchTreeUpdated(branch),
            cancellationToken: cancellationToken);
    }
}
