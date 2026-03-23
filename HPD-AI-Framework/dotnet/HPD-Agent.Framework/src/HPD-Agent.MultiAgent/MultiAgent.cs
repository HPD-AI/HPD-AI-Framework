using System.Text.Json;
using HPD.Agent;
using HPD.MultiAgent.Config;
using HPD.MultiAgent.Internal;
using HPD.MultiAgent.Routing;
using HPDAgent.Graph.Abstractions;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Builders;
using MultiAgentEdgeBuilder = HPD.MultiAgent.Routing.EdgeBuilder;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.MultiAgent;

/// <summary>
/// Fluent builder for creating multi-agent workflows.
/// </summary>
public class MultiAgent
{
    private readonly GraphBuilder _graphBuilder;
    private readonly Dictionary<string, Agent.Agent> _agents = new();
    private readonly Dictionary<string, AgentConfig> _agentConfigs = new();
    private readonly Dictionary<string, AgentNodeOptions> _options = new();
    private readonly List<(string From, string To, EdgeCondition? Condition)> _edges = new();

    // Stores predicate-based edge conditions keyed by "from->to".
    // Predicates are evaluated by AgentNodeHandler after the source node runs and
    // write a synthetic boolean output key that the graph routes on.
    internal readonly Dictionary<string, Func<EdgeConditionContext, bool>> PredicateEdges = new();
    private readonly WorkflowSettingsConfig _settings;
    private string? _workflowName;

    /// <summary>
    /// Creates a new workflow builder.
    /// </summary>
    public MultiAgent()
    {
        _graphBuilder = new GraphBuilder();
        _settings = new WorkflowSettingsConfig();
    }

    /// <summary>
    /// Creates a workflow builder from a configuration.
    /// </summary>
    public MultiAgent(MultiAgentWorkflowConfig config)
    {
        _graphBuilder = new GraphBuilder();
        _settings = config.Settings;
        _workflowName = config.Name;

        // Add agents from config
        foreach (var (nodeId, nodeConfig) in config.Agents)
        {
            _agentConfigs[nodeId] = nodeConfig.Agent;
            _options[nodeId] = ConvertToOptions(nodeConfig);
        }

        // Add edges from config
        foreach (var edge in config.Edges)
        {
            var condition = edge.When != null ? MapCondition(edge.When) : null;
            _edges.Add((edge.From, edge.To, condition));
        }
    }

    /// <summary>
    /// Set the workflow name.
    /// </summary>
    public MultiAgent WithName(string name)
    {
        _workflowName = name;
        return this;
    }

    /// <summary>
    /// Add a pre-built agent to the workflow.
    /// </summary>
    public MultiAgent AddAgent(
        string id,
        Agent.Agent agent,
        Action<AgentNodeOptions>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Agent ID cannot be empty", nameof(id));

        _agents[id] = agent ?? throw new ArgumentNullException(nameof(agent));

        var options = new AgentNodeOptions();
        configure?.Invoke(options);
        _options[id] = options;

        return this;
    }

    /// <summary>
    /// Add an agent via AgentConfig for deferred building.
    /// </summary>
    public MultiAgent AddAgent(
        string id,
        AgentConfig config,
        Action<AgentNodeOptions>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Agent ID cannot be empty", nameof(id));

        _agentConfigs[id] = config ?? throw new ArgumentNullException(nameof(config));

        var options = new AgentNodeOptions();
        configure?.Invoke(options);
        _options[id] = options;

        return this;
    }

    /// <summary>
    /// Add an agent via inline builder configuration.
    /// </summary>
    public MultiAgent AddAgent(
        string id,
        Action<AgentBuilder> configureAgent,
        Action<AgentNodeOptions>? configureNode = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Agent ID cannot be empty", nameof(id));
        if (configureAgent == null)
            throw new ArgumentNullException(nameof(configureAgent));

        // Store the builder action for deferred building at execution time.
        // The agent is built when the workflow runs so it can inherit the
        // parent's chat client if no Provider is configured.
        _agentConfigs[id] = new AgentConfig(); // placeholder — factory uses the builder action
        _builderActions[id] = configureAgent;

        var options = new AgentNodeOptions();
        configureNode?.Invoke(options);
        _options[id] = options;

        return this;
    }

    private readonly Dictionary<string, Action<AgentBuilder>> _builderActions = new();

    /// <summary>
    /// Add a router agent that uses handoffs to decide routing.
    /// </summary>
    /// <param name="id">The node ID for this router agent.</param>
    /// <param name="config">The agent configuration.</param>
    /// <returns>A builder for configuring handoff targets.</returns>
    public RouterAgentBuilder AddRouterAgent(string id, AgentConfig config)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Agent ID cannot be empty", nameof(id));

        _agentConfigs[id] = config ?? throw new ArgumentNullException(nameof(config));
        _options[id] = new AgentNodeOptions { OutputMode = AgentOutputMode.Handoff };

        return new RouterAgentBuilder(this, id);
    }

    /// <summary>
    /// Add a router agent that uses handoffs to decide routing.
    /// </summary>
    /// <param name="id">The node ID for this router agent.</param>
    /// <param name="agent">The pre-built agent.</param>
    /// <returns>A builder for configuring handoff targets.</returns>
    public RouterAgentBuilder AddRouterAgent(string id, Agent.Agent agent)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Agent ID cannot be empty", nameof(id));

        _agents[id] = agent ?? throw new ArgumentNullException(nameof(agent));
        _options[id] = new AgentNodeOptions { OutputMode = AgentOutputMode.Handoff };

        return new RouterAgentBuilder(this, id);
    }

    /// <summary>
    /// Start defining edges from source nodes.
    /// </summary>
    public MultiAgentEdgeBuilder From(params string[] sourceNodes)
    {
        if (sourceNodes == null || sourceNodes.Length == 0)
            throw new ArgumentException("At least one source node is required", nameof(sourceNodes));

        return new Routing.EdgeBuilder(this, sourceNodes);
    }

    /// <summary>
    /// Set maximum iterations for cyclic graphs.
    /// </summary>
    public MultiAgent WithMaxIterations(int maxIterations)
    {
        _graphBuilder.WithMaxIterations(maxIterations);
        return this;
    }

    /// <summary>
    /// Set execution timeout.
    /// </summary>
    public MultiAgent WithTimeout(TimeSpan timeout)
    {
        _graphBuilder.WithExecutionTimeout(timeout);
        return this;
    }

    /// <summary>
    /// Build the workflow.
    /// </summary>
    public Task<AgentWorkflowInstance> BuildAsync(CancellationToken cancellationToken = default)
    {
        // Create agent factories for deferred building (agents are built at execution time
        // so they can inherit the parent's chat client when no provider is configured)
        var factories = new Dictionary<string, AgentFactory>();

        // Wrap pre-built agents
        foreach (var (id, agent) in _agents)
        {
            factories[id] = new PrebuiltAgentFactory(agent);
        }

        // Create factories for agents from configs (not yet in _agents)
        foreach (var (id, config) in _agentConfigs)
        {
            if (factories.ContainsKey(id))
                continue;

            if (_builderActions.TryGetValue(id, out var builderAction))
            {
                // Agent with custom builder action
                factories[id] = new ConfigAgentFactory(config, builderAction);
            }
            else
            {
                // Agent from config only
                factories[id] = new ConfigAgentFactory(config);
            }
        }

        // Configure graph
        _graphBuilder.WithName(_workflowName ?? "MultiAgentWorkflow");

        // Wire iteration options if configured
        if (_settings.IterationOptions != null)
        {
            _graphBuilder.WithIterationOptions(new HPDAgent.Graph.Abstractions.Graph.IterationOptions
            {
                MaxIterations = _settings.IterationOptions.MaxIterations,
                UseChangeAwareIteration = _settings.IterationOptions.UseChangeAwareIteration,
                EnableAutoConvergence = _settings.IterationOptions.EnableAutoConvergence,
                IgnoreFieldsForChangeDetection = _settings.IterationOptions.IgnoreFieldsForChangeDetection != null
                    ? new HashSet<string>(_settings.IterationOptions.IgnoreFieldsForChangeDetection)
                    : null,
                AlwaysDirtyNodes = _settings.IterationOptions.AlwaysDirtyNodes != null
                    ? new HashSet<string>(_settings.IterationOptions.AlwaysDirtyNodes)
                    : null
            });
        }

        // Add START and END nodes
        _graphBuilder.AddStartNode();
        _graphBuilder.AddEndNode();

        // Add agent nodes
        foreach (var id in GetAllAgentIds())
        {
            var handlerName = $"{id}Handler";
            _graphBuilder.AddHandlerNode(id, id, handlerName, node =>
            {
                var opts = _options.TryGetValue(id, out var o) ? o : new AgentNodeOptions();

                if (opts.Timeout.HasValue)
                    node.WithTimeout(opts.Timeout.Value);

                if (opts.RetryPolicy != null)
                    node.WithRetryPolicy(opts.RetryPolicy);

                if (opts.MaxConcurrentExecutions.HasValue)
                    node.WithMaxParallelExecutions(opts.MaxConcurrentExecutions.Value);
            });
        }

        // Add edges
        foreach (var (from, to, condition) in _edges)
        {
            if (condition != null)
            {
                _graphBuilder.AddEdge(from, to, e => e.WithCondition(condition));
            }
            else
            {
                _graphBuilder.AddEdge(from, to);
            }
        }

        // Build graph
        var graph = _graphBuilder.Build();

        // Create service provider with handlers
        var services = new ServiceCollection();

        foreach (var id in GetAllAgentIds())
        {
            var handler = new AgentNodeHandler(id);
            services.AddSingleton<HPDAgent.Graph.Abstractions.Handlers.IGraphNodeHandler<AgentGraphContext>>(handler);
        }

        return Task.FromResult(new AgentWorkflowInstance(graph, factories, _options, services.BuildServiceProvider(), _workflowName, _settings, PredicateEdges));
    }

    /// <summary>
    /// Load workflow from JSON configuration.
    /// </summary>
    public static MultiAgent FromConfig(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        var config = JsonSerializer.Deserialize<MultiAgentWorkflowConfig>(json)
            ?? throw new InvalidOperationException("Failed to deserialize workflow config");

        return new MultiAgent(config);
    }

    /// <summary>
    /// Load workflow from configuration object.
    /// </summary>
    public static MultiAgent FromConfig(MultiAgentWorkflowConfig config)
    {
        return new MultiAgent(config);
    }

    // Internal methods for EdgeBuilder
    internal void AddEdgeInternal(string from, string to, EdgeCondition? condition)
    {
        // Check if edge already exists
        var existingIndex = _edges.FindIndex(e => e.From == from && e.To == to);
        if (existingIndex >= 0)
        {
            _edges[existingIndex] = (from, to, condition);
        }
        else
        {
            _edges.Add((from, to, condition));
        }
    }

    internal void UpdateEdgeCondition(string from, string to, EdgeCondition? condition)
    {
        var index = _edges.FindIndex(e => e.From == from && e.To == to);
        if (index >= 0)
        {
            _edges[index] = (from, to, condition);
        }
    }

    /// <summary>
    /// Registers a predicate-based edge. The predicate is evaluated by AgentNodeHandler
    /// after the source node runs and writes a synthetic boolean key that the graph routes on.
    /// The synthetic key is "__predicate_{from}_{to}".
    /// </summary>
    internal void AddPredicateEdge(string from, string to, Func<EdgeConditionContext, bool> predicate)
    {
        var syntheticKey = $"__predicate_{from}_{to}";
        PredicateEdges[$"{from}->{to}"] = predicate;

        // Register the edge with a FieldEquals condition on the synthetic key
        AddEdgeInternal(from, to, new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = syntheticKey,
            Value = true
        });
    }

    internal AgentNodeOptions GetOrCreateOptions(string nodeId)
    {
        if (!_options.TryGetValue(nodeId, out var options))
        {
            options = new AgentNodeOptions();
            _options[nodeId] = options;
        }
        return options;
    }

    private IEnumerable<string> GetAllAgentIds()
    {
        return _agents.Keys.Union(_agentConfigs.Keys).Distinct();
    }

    private static EdgeCondition MapCondition(Config.ConditionConfig c) => new EdgeCondition
    {
        Type = c.Type,
        Field = c.Field,
        Value = c.Value,
        RegexOptions = c.RegexOptions,
        Conditions = c.Conditions?.Select(MapCondition).ToList()
    };

    private static AgentNodeOptions ConvertToOptions(AgentNodeConfig config)
    {
        var options = new AgentNodeOptions
        {
            OutputMode = config.OutputMode,
            Timeout = config.Timeout,
            MaxConcurrentExecutions = config.MaxConcurrent,
            InputKey = config.InputKey,
            OutputKey = config.OutputKey,
            InputTemplate = config.InputTemplate,
            AdditionalSystemInstructions = config.AdditionalInstructions
        };

        if (config.Retry != null)
        {
            options.RetryPolicy = new RetryPolicy
            {
                MaxAttempts = config.Retry.MaxAttempts,
                InitialDelay = config.Retry.InitialDelay,
                Strategy = config.Retry.Strategy,
                MaxDelay = config.Retry.MaxDelay
            };
        }

        return options;
    }
}

/// <summary>
/// Static factory for creating workflows.
/// </summary>
public static class AgentWorkflow
{
    /// <summary>
    /// Create a new workflow builder.
    /// </summary>
    public static MultiAgent Create() => new();

    /// <summary>
    /// Create a workflow builder from configuration.
    /// </summary>
    public static MultiAgent FromConfig(MultiAgentWorkflowConfig config)
        => MultiAgent.FromConfig(config);

    /// <summary>
    /// Create a workflow builder from JSON file.
    /// </summary>
    public static MultiAgent FromJson(string jsonPath)
        => MultiAgent.FromConfig(jsonPath);
}
