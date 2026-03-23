using HPDAgent.Graph.Abstractions.Execution;

namespace HPDAgent.Graph.Abstractions.Context;

/// <summary>
/// Extension methods for IGraphContext providing convenient state queries.
/// </summary>
public static class GraphContextExtensions
{
    /// <summary>
    /// Get current state of a node.
    /// Returns null if node has no state tag.
    /// </summary>
    /// <param name="context">Graph context</param>
    /// <param name="nodeId">Node identifier</param>
    /// <returns>Current node state, or null if not tracked</returns>
    /// <remarks>
    /// If multiple state values exist (due to tag accumulation without clearing),
    /// returns the first parseable state. In production, the orchestrator should clear
    /// old state tags before setting new ones to ensure single-value semantics.
    /// </remarks>
    public static NodeState? GetNodeState(this IGraphContext context, string nodeId)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        if (string.IsNullOrWhiteSpace(nodeId))
            return null;

        var tagKey = $"node_state:{nodeId}";
        if (context.Tags.TryGetValue(tagKey, out var values) && values.Count > 0)
        {
            // Get the first parseable value
            // Note: ConcurrentBag doesn't guarantee order, so this may not be deterministic
            // The orchestrator should ensure only one state value per node by clearing old tags
            var stateValue = values.FirstOrDefault();
            if (stateValue != null && Enum.TryParse<NodeState>(stateValue, out var state))
            {
                return state;
            }
        }

        return null;
    }

    /// <summary>
    /// Get all nodes currently in a specific state.
    /// </summary>
    /// <param name="context">Graph context</param>
    /// <param name="state">State to filter by</param>
    /// <returns>List of node IDs in the specified state</returns>
    public static IReadOnlyList<string> GetNodesInState(this IGraphContext context, NodeState state)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        return context.Tags.Keys
            .Where(k => k.StartsWith("node_state:"))
            .Select(k => k.Substring("node_state:".Length))
            .Where(nodeId => context.GetNodeState(nodeId) == state)
            .ToList();
    }

    /// <summary>
    /// Check if any nodes are currently polling (waiting for condition).
    /// </summary>
    /// <param name="context">Graph context</param>
    /// <returns>True if at least one node is in Polling state</returns>
    public static bool HasPollingNodes(this IGraphContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        return context.GetNodesInState(NodeState.Polling).Count > 0;
    }

    /// <summary>
    /// Get all nodes currently polling.
    /// Convenience method equivalent to GetNodesInState(NodeState.Polling).
    /// </summary>
    /// <param name="context">Graph context</param>
    /// <returns>List of node IDs currently polling</returns>
    public static IReadOnlyList<string> GetPollingNodes(this IGraphContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        return context.GetNodesInState(NodeState.Polling);
    }

    /// <summary>
    /// Check if any nodes are currently in active states (Running, Polling, or Suspended).
    /// </summary>
    /// <param name="context">Graph context</param>
    /// <returns>True if at least one node is active</returns>
    public static bool HasActiveNodes(this IGraphContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        return context.Tags.Keys
            .Where(k => k.StartsWith("node_state:"))
            .Select(k => k.Substring("node_state:".Length))
            .Any(nodeId =>
            {
                var state = context.GetNodeState(nodeId);
                return state?.IsActive() == true;
            });
    }

    /// <summary>
    /// Get count of nodes in each state.
    /// Useful for dashboard summaries.
    /// </summary>
    /// <param name="context">Graph context</param>
    /// <returns>Dictionary mapping NodeState to count</returns>
    public static IReadOnlyDictionary<NodeState, int> GetStateDistribution(this IGraphContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var distribution = new Dictionary<NodeState, int>();

        // Initialize all states to 0
        foreach (NodeState state in Enum.GetValues<NodeState>())
        {
            distribution[state] = 0;
        }

        // Count nodes in each state
        var nodeIds = context.Tags.Keys
            .Where(k => k.StartsWith("node_state:"))
            .Select(k => k.Substring("node_state:".Length))
            .ToList();

        foreach (var nodeId in nodeIds)
        {
            var state = context.GetNodeState(nodeId);
            if (state.HasValue)
            {
                distribution[state.Value]++;
            }
        }

        return distribution;
    }

    /// <summary>
    /// Check if a node is in a waiting state (Polling or Suspended).
    /// </summary>
    /// <param name="context">Graph context</param>
    /// <param name="nodeId">Node identifier</param>
    /// <returns>True if node is waiting</returns>
    public static bool IsNodeWaiting(this IGraphContext context, string nodeId)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var state = context.GetNodeState(nodeId);
        return state?.IsWaiting() == true;
    }

    /// <summary>
    /// Check if a node is in a terminal state (Succeeded, Failed, Skipped, or Cancelled).
    /// </summary>
    /// <param name="context">Graph context</param>
    /// <param name="nodeId">Node identifier</param>
    /// <returns>True if node is in terminal state</returns>
    public static bool IsNodeTerminal(this IGraphContext context, string nodeId)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var state = context.GetNodeState(nodeId);
        return state?.IsTerminal() == true;
    }
}
