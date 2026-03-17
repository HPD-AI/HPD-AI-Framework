using HPDAgent.Graph.Abstractions.Caching;
using HPDAgent.Graph.Abstractions.Handlers;
using GraphDefinition = HPDAgent.Graph.Abstractions.Graph.Graph;

namespace HPDAgent.Graph.Core.Caching;

/// <summary>
/// Detects which nodes need re-execution based on fingerprint changes.
/// Implements incremental execution for massive cost savings.
/// </summary>
public class AffectedNodeDetector : IAffectedNodeDetector
{
    private readonly INodeFingerprintCalculator _fingerprintCalculator;

    public AffectedNodeDetector(INodeFingerprintCalculator fingerprintCalculator)
    {
        _fingerprintCalculator = fingerprintCalculator ?? throw new ArgumentNullException(nameof(fingerprintCalculator));
    }

    public async Task<HashSet<string>> GetAffectedNodesAsync(
        GraphSnapshot? previousSnapshot,
        GraphDefinition currentGraph,
        HandlerInputs currentInputs,
        IServiceProvider services,
        CancellationToken ct = default)
    {
        var affectedNodes = new HashSet<string>();

        // If no previous snapshot, all nodes are affected
        if (previousSnapshot == null)
        {
            foreach (var node in currentGraph.Nodes)
            {
                if (node.Type != Graph.Abstractions.Graph.NodeType.Start &&
                    node.Type != Graph.Abstractions.Graph.NodeType.End)
                {
                    affectedNodes.Add(node.Id);
                }
            }
            return affectedNodes;
        }

        // Fast path: if the graph structure hasn't changed, inputs are empty, and no nodes
        // have partitions (which can change independently), all fingerprints are deterministic
        // from graph identity alone — skip per-node recomputation entirely.
        var currentGraphHash = currentGraph.Id + currentGraph.Version;
        if (previousSnapshot.GraphHash == currentGraphHash
            && currentInputs.GetAll().Count == 0
            && !currentGraph.Nodes.Any(n => n.Partitions != null)
            && currentGraph.Nodes
                .Where(n => n.Type != Graph.Abstractions.Graph.NodeType.Start
                         && n.Type != Graph.Abstractions.Graph.NodeType.End)
                .All(n => previousSnapshot.NodeFingerprints.ContainsKey(n.Id)))
        {
            return new HashSet<string>();
        }

        // Compute current fingerprints and compare with previous
        var currentFingerprints = new Dictionary<string, string>();
        var upstreamHashes = new Dictionary<string, string>();

        // Use topological ordering to process nodes in dependency order
        var layers = currentGraph.GetExecutionLayers();

        foreach (var layer in layers)
        {
            foreach (var nodeId in layer.NodeIds)
            {
                var node = currentGraph.GetNode(nodeId);
                if (node == null)
                    continue;

                // Collect upstream fingerprints for this node
                var nodeUpstreamHashes = new Dictionary<string, string>();
                foreach (var edge in currentGraph.GetIncomingEdges(nodeId))
                {
                    if (currentFingerprints.TryGetValue(edge.From, out var upstreamHash))
                    {
                        nodeUpstreamHashes[edge.From] = upstreamHash;
                    }
                }

                // Resolve partition snapshot if node is partitioned
                string? currentPartitionHash = null;
                if (node.Partitions != null)
                {
                    var currentPartitionSnapshot = await node.Partitions.ResolveAsync(services, ct);
                    currentPartitionHash = currentPartitionSnapshot.SnapshotHash;
                }

                // Compute fingerprint for this node (including partition hash if applicable)
                var fingerprint = _fingerprintCalculator.Compute(
                    nodeId,
                    currentInputs, // In real implementation, would get node-specific inputs
                    nodeUpstreamHashes,
                    previousSnapshot.GraphHash,
                    currentPartitionHash);

                currentFingerprints[nodeId] = fingerprint;

                // Check if fingerprint changed
                if (!previousSnapshot.NodeFingerprints.TryGetValue(nodeId, out var previousFingerprint) ||
                    previousFingerprint != fingerprint)
                {
                    // Fingerprint changed - this node is affected
                    affectedNodes.Add(nodeId);

                    // Mark all downstream nodes as affected too
                    MarkDownstreamAsAffected(currentGraph, nodeId, affectedNodes);
                }
            }
        }

        return affectedNodes;
    }

    /// <summary>
    /// Recursively mark all downstream nodes as affected.
    /// </summary>
    private void MarkDownstreamAsAffected(
        GraphDefinition graph,
        string nodeId,
        HashSet<string> affectedNodes)
    {
        var outgoingEdges = graph.GetOutgoingEdges(nodeId);

        foreach (var edge in outgoingEdges)
        {
            if (affectedNodes.Add(edge.To))
            {
                // Newly added - continue propagating
                MarkDownstreamAsAffected(graph, edge.To, affectedNodes);
            }
        }
    }
}
