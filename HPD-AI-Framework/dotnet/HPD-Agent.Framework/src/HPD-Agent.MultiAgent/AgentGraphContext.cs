using HPD.Agent;
using HPD.MultiAgent.Routing;
using HPDAgent.Graph.Abstractions.Channels;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.State;
using HPDAgent.Graph.Core.Context;
using Microsoft.Extensions.AI;
using GraphDefinition = HPDAgent.Graph.Abstractions.Graph.Graph;

namespace HPD.MultiAgent;

/// <summary>
/// Extended graph context for multi-agent workflows.
/// Provides access to agents and workflow-specific state.
/// Uses SharedData to make the original input available to all nodes.
/// </summary>
public class AgentGraphContext : GraphContext
{
    private readonly Dictionary<string, Agent.Agent> _agents;
    private readonly Dictionary<string, AgentNodeOptions> _agentOptions;
    private readonly Dictionary<string, Func<EdgeConditionContext, bool>> _predicateEdges;

    /// <summary>
    /// The original user input that started the workflow.
    /// Also available via SharedData["input"] for all handlers.
    /// </summary>
    public string? OriginalInput => SharedData?.TryGetValue("input", out var input) == true
        ? input as string
        : null;

    /// <summary>
    /// Fallback chat client for agents that don't have their own provider configured.
    /// Inherited from parent agent when workflow is invoked as a tool.
    /// </summary>
    public IChatClient? FallbackChatClient { get; set; }

    /// <summary>
    /// Additional context data available to all agents in the workflow.
    /// This is a convenience view of SharedData (excluding "input").
    /// </summary>
    public IReadOnlyDictionary<string, object> WorkflowData =>
        SharedData?.Where(kvp => kvp.Key != "input")
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        ?? new Dictionary<string, object>();

    /// <summary>
    /// Creates a new agent graph context.
    /// </summary>
    public AgentGraphContext(
        string executionId,
        GraphDefinition graph,
        IServiceProvider services,
        Dictionary<string, Agent.Agent> agents,
        Dictionary<string, AgentNodeOptions> agentOptions,
        IGraphChannelSet? channels = null,
        IManagedContext? managed = null,
        string? originalInput = null,
        Dictionary<string, Func<EdgeConditionContext, bool>>? predicateEdges = null)
        : base(executionId, graph, services, channels, managed, enableSharedData: true)
    {
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _agentOptions = agentOptions ?? throw new ArgumentNullException(nameof(agentOptions));
        _predicateEdges = predicateEdges ?? new Dictionary<string, Func<EdgeConditionContext, bool>>();

        // Store original input in SharedData so it's available to all nodes
        if (!string.IsNullOrEmpty(originalInput) && SharedData != null)
        {
            SharedData["input"] = originalInput;
        }
    }

    /// <summary>
    /// Gets an agent by node ID.
    /// </summary>
    public Agent.Agent? GetAgent(string nodeId)
    {
        return _agents.TryGetValue(nodeId, out var agent) ? agent : null;
    }

    /// <summary>
    /// Gets the options for an agent node.
    /// </summary>
    public AgentNodeOptions? GetAgentOptions(string nodeId)
    {
        return _agentOptions.TryGetValue(nodeId, out var options) ? options : null;
    }

    /// <summary>
    /// Gets all registered agent node IDs.
    /// </summary>
    public IReadOnlyCollection<string> AgentNodeIds => _agents.Keys;

    /// <summary>
    /// Checks if a node is an agent node.
    /// </summary>
    public bool IsAgentNode(string nodeId)
    {
        return _agents.ContainsKey(nodeId);
    }

    /// <summary>
    /// Evaluates all predicate edges originating from the given node and writes
    /// synthetic boolean output keys (e.g. "__predicate_from_to") into the outputs
    /// dictionary so the graph's FieldEquals conditions can route correctly.
    /// </summary>
    public void EvaluatePredicateEdges(string fromNodeId, Dictionary<string, object> outputs)
    {
        foreach (var (key, predicate) in _predicateEdges)
        {
            // Key format is "from->to"
            if (!key.StartsWith($"{fromNodeId}->")) continue;

            var toNodeId = key.Substring(fromNodeId.Length + 2);
            var syntheticKey = $"__predicate_{fromNodeId}_{toNodeId}";
            var ctx = new EdgeConditionContext(outputs);

            try
            {
                outputs[syntheticKey] = predicate(ctx);
            }
            catch
            {
                // Predicate threw — treat as false (edge not traversed)
                outputs[syntheticKey] = false;
            }
        }
    }

    /// <inheritdoc/>
    public override IGraphContext CreateIsolatedCopy()
    {
        var copy = new AgentGraphContext(
            ExecutionId,
            Graph,
            Services,
            _agents, // Agents are shared (immutable once built)
            _agentOptions, // Options are shared (immutable)
            CloneChannelsInternal(),
            Managed,
            OriginalInput, // Will be copied to SharedData in constructor
            _predicateEdges) // Predicates are shared (immutable)
        {
            CurrentLayerIndex = CurrentLayerIndex,
            // CRITICAL: Share the event coordinator so events from parallel nodes are streamed
            EventCoordinator = EventCoordinator
        };

        // Copy completed nodes
        foreach (var nodeId in CompletedNodes)
        {
            copy.MarkNodeComplete(nodeId);
        }

        // Copy SharedData (includes workflow data and original input)
        if (SharedData != null && copy.SharedData != null)
        {
            foreach (var kvp in SharedData)
            {
                copy.SharedData[kvp.Key] = kvp.Value;
            }
        }

        return copy;
    }

    private IGraphChannelSet CloneChannelsInternal()
    {
        var clonedChannels = new HPDAgent.Graph.Core.Channels.GraphChannelSet();

        foreach (var channelName in Channels.ChannelNames)
        {
            if (channelName.StartsWith("node_output:"))
            {
                var outputs = Channels[channelName].Get<Dictionary<string, object>>();
                if (outputs != null)
                {
                    clonedChannels[channelName].Set(outputs);
                }
            }
        }

        return clonedChannels;
    }
}
