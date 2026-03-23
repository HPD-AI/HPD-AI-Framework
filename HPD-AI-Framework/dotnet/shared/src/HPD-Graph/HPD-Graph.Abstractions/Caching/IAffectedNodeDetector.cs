using HPDAgent.Graph.Abstractions.Handlers;
using GraphDefinition = HPDAgent.Graph.Abstractions.Graph.Graph;

namespace HPDAgent.Graph.Abstractions.Caching;

/// <summary>
/// Detects which nodes are affected by input/config changes.
/// Enables incremental execution (only run changed nodes).
/// </summary>
public interface IAffectedNodeDetector
{
    /// <summary>
    /// Compute affected nodes based on changes since previous execution.
    /// </summary>
    /// <param name="previousSnapshot">Snapshot from previous execution (fingerprints + partition snapshots)</param>
    /// <param name="currentGraph">Current graph definition</param>
    /// <param name="currentInputs">Current execution inputs</param>
    /// <param name="services">Service provider for resolving partition snapshots</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Set of node IDs that need re-execution</returns>
    Task<HashSet<string>> GetAffectedNodesAsync(
        GraphSnapshot? previousSnapshot,
        GraphDefinition currentGraph,
        HandlerInputs currentInputs,
        IServiceProvider services,
        CancellationToken ct = default);
}
