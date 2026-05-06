using HPD.MultiAgent.Config;
using HPDAgent.Graph.Core.Builders;

namespace HPD.MultiAgent;

/// <summary>
/// Graph builder extensions that embed MultiAgent workflows as HPD.Graph subgraphs.
/// </summary>
public static class MultiAgentGraphBuilderExtensions
{
    /// <summary>
    /// Adds a MultiAgent workflow configuration as a graph subgraph node.
    /// </summary>
    public static GraphBuilder AddMultiAgent(
        this GraphBuilder builder,
        string nodeId,
        MultiAgentWorkflowConfig config,
        Action<NodeBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(config);

        var workflow = AgentWorkflow
            .FromConfig(config)
            .BuildAsync()
            .GetAwaiter()
            .GetResult();

        return builder.AddMultiAgent(nodeId, workflow, configure);
    }

    /// <summary>
    /// Adds a built MultiAgent workflow instance as a graph subgraph node.
    /// </summary>
    public static GraphBuilder AddMultiAgent(
        this GraphBuilder builder,
        string nodeId,
        AgentWorkflowInstance workflow,
        Action<NodeBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(workflow);

        return builder.AddSubGraphNode(
            nodeId,
            string.IsNullOrWhiteSpace(workflow.WorkflowName) ? nodeId : workflow.WorkflowName,
            workflow.Graph,
            configure);
    }
}
