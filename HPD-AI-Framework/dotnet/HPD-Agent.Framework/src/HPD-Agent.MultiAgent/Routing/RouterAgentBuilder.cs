using HPDAgent.Graph.Abstractions.Graph;

namespace HPD.MultiAgent.Routing;

/// <summary>
/// Builder for configuring a router agent with handoff targets.
/// </summary>
public class RouterAgentBuilder
{
    private readonly MultiAgent _workflowBuilder;
    private readonly string _routerNodeId;
    private readonly Dictionary<string, string> _handoffTargets = new();

    internal RouterAgentBuilder(MultiAgent workflowBuilder, string routerNodeId)
    {
        _workflowBuilder = workflowBuilder;
        _routerNodeId = routerNodeId;
    }

    /// <summary>
    /// Add a handoff target.
    /// Creates a handoff_to_{targetId}() tool for the router agent to call.
    /// </summary>
    /// <param name="targetId">The node ID to hand off to.</param>
    /// <param name="description">Description of when to use this handoff (for the LLM).</param>
    public RouterAgentBuilder WithHandoff(string targetId, string description)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            throw new ArgumentException("Target ID cannot be empty", nameof(targetId));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be empty", nameof(description));

        _handoffTargets[targetId] = description;

        // Update options
        var options = _workflowBuilder.GetOrCreateOptions(_routerNodeId);
        options.HandoffTargets ??= new Dictionary<string, string>();
        options.HandoffTargets[targetId] = description;

        // Add conditional edge based on handoff_target field
        _workflowBuilder.AddEdgeInternal(_routerNodeId, targetId, new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = "handoff_target",
            Value = targetId
        });

        return this;
    }

    /// <summary>
    /// Add a default handoff target for when no specific handoff is called.
    /// </summary>
    /// <param name="targetId">The default target node ID.</param>
    public RouterAgentBuilder WithDefaultHandoff(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            throw new ArgumentException("Target ID cannot be empty", nameof(targetId));

        // Add default edge
        _workflowBuilder.AddEdgeInternal(_routerNodeId, targetId, new EdgeCondition
        {
            Type = ConditionType.Default
        });

        return this;
    }

    /// <summary>
    /// Configure additional options for the router agent.
    /// </summary>
    public RouterAgentBuilder Configure(Action<AgentNodeOptions> configure)
    {
        if (configure == null)
            throw new ArgumentNullException(nameof(configure));

        var options = _workflowBuilder.GetOrCreateOptions(_routerNodeId);
        configure(options);

        // Ensure handoff mode is preserved
        options.OutputMode = AgentOutputMode.Handoff;

        return this;
    }

    /// <summary>
    /// Continue building the workflow by defining more edges.
    /// </summary>
    public EdgeBuilder From(params string[] sourceNodes)
    {
        return _workflowBuilder.From(sourceNodes);
    }

    /// <summary>
    /// Add another agent to the workflow.
    /// </summary>
    public MultiAgent AddAgent(
        string id,
        HPD.Agent.AgentConfig config,
        Action<AgentNodeOptions>? configure = null)
    {
        return _workflowBuilder.AddAgent(id, config, configure);
    }

    /// <summary>
    /// Add another router agent to the workflow.
    /// </summary>
    public RouterAgentBuilder AddRouterAgent(string id, HPD.Agent.AgentConfig config)
    {
        return _workflowBuilder.AddRouterAgent(id, config);
    }

    /// <summary>
    /// Build the workflow.
    /// </summary>
    public Task<AgentWorkflowInstance> BuildAsync(CancellationToken cancellationToken = default)
    {
        return _workflowBuilder.BuildAsync(cancellationToken);
    }
}
