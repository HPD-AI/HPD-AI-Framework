using HPD.AI.Platform.Studio;

namespace HPD.Graph.Studio;

/// <summary>Projects current Graph authority into bounded, read-only Studio observations.</summary>
public interface IGraphStudioInspectionAuthority
{
    /// <summary>Executes one registered bounded observation after Studio authorization succeeds.</summary>
    ValueTask<StudioFrameworkSurfaceResponse?> ObserveAsync(
        string operationId, string relativePath, string applicationId, CancellationToken cancellationToken);

    /// <summary>Confirms that an exact graph resource identity is current.</summary>
    ValueTask<bool> ExistsAsync(StudioResourceIdentity resource, CancellationToken cancellationToken);
}

/// <summary>
/// Fails closed when a host contributes Graph Studio presentation without installing a Graph inspection authority.
/// Hosts replace this registration with an adapter over their current definition, execution, and checkpoint stores.
/// </summary>
internal sealed class UnavailableGraphStudioInspectionAuthority : IGraphStudioInspectionAuthority
{
    public ValueTask<StudioFrameworkSurfaceResponse?> ObserveAsync(
        string operationId, string relativePath, string applicationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<StudioFrameworkSurfaceResponse?>(null);
    }

    public ValueTask<bool> ExistsAsync(StudioResourceIdentity resource, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}
