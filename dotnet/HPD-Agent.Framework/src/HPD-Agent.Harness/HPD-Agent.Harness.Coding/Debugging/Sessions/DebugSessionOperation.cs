using HPD.Agent;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

/// <summary>Observes one debug tree through the unified operation lifecycle.</summary>
internal sealed class DebugSessionOperation(
    DebugSessionManager manager,
    DebugTreeLookupScope scope,
    string treeId)
{
    private int _publicationState;

    public IReadOnlyDictionary<string, string> Metadata =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["debugTreeId"] = treeId };

    public void CommitLive()
    {
        if (Interlocked.CompareExchange(ref _publicationState, 1, 0) != 0)
            throw new InvalidOperationException("The debug operation publication state is already settled.");
    }

    public void MarkPublicationFailed() => Interlocked.CompareExchange(ref _publicationState, 2, 0);

    public async ValueTask<AgentOperationCompletion> ObserveAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DebugSessionTree tree;
                try { tree = manager.ResolveTree(scope, treeId); }
                catch (KeyNotFoundException) { break; }
                var snapshot = DebugSnapshotProjector.Project(tree);
                if (string.Equals(snapshot.Status, "Terminated", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(snapshot.Status, "Faulted", StringComparison.OrdinalIgnoreCase))
                    break;
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (Interlocked.Exchange(ref _publicationState, 2) != 2)
                await manager.RemoveAndDisposeAsync(scope, treeId).ConfigureAwait(false);
            throw;
        }

        Interlocked.Exchange(ref _publicationState, 2);
        IReadOnlyList<string> artifacts = [];
        try
        {
            artifacts = manager.ResolveTree(scope, treeId).StoredArtifacts
                .Select(static artifact => artifact.ContentId)
                .Where(static contentId => !string.IsNullOrWhiteSpace(contentId))
                .Cast<string>()
                .ToArray();
        }
        catch (KeyNotFoundException) { }
        return new AgentOperationCompletion("Debug session terminated.", artifacts);
    }
}
