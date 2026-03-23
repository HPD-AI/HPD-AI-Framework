using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Events;
using HPD.Events.Core;
using HPD.MultiAgent.Config;
using HPD.MultiAgent.Internal;
using HPD.MultiAgent.Routing;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using GraphDefinition = HPDAgent.Graph.Abstractions.Graph.Graph;

namespace HPD.MultiAgent;

/// <summary>
/// Result of a workflow execution.
/// </summary>
public sealed record WorkflowResult
{
    /// <summary>
    /// The final answer/output from the workflow.
    /// </summary>
    public string? FinalAnswer { get; init; }

    /// <summary>
    /// All outputs from the final node(s).
    /// </summary>
    public Dictionary<string, object> Outputs { get; init; } = new();

    /// <summary>
    /// Total execution duration.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Whether the workflow completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if the workflow failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Exception if the workflow failed.
    /// </summary>
    public Exception? Exception { get; init; }
}

/// <summary>
/// Factory for creating agents lazily with optional chat client inheritance.
/// </summary>
public abstract class AgentFactory
{
    /// <summary>
    /// Build the agent, optionally with a fallback chat client.
    /// </summary>
    public abstract Task<Agent.Agent> BuildAsync(IChatClient? fallbackChatClient, CancellationToken cancellationToken);

    /// <summary>
    /// Return the AgentConfig backing this factory, if available.
    /// Used by ExportConfigJson to reconstruct serializable config.
    /// </summary>
    internal virtual AgentConfig? GetConfig() => null;
}

/// <summary>
/// Factory that wraps a pre-built agent.
/// </summary>
internal sealed class PrebuiltAgentFactory : AgentFactory
{
    private readonly Agent.Agent _agent;

    public PrebuiltAgentFactory(Agent.Agent agent) => _agent = agent;

    public override Task<Agent.Agent> BuildAsync(IChatClient? fallbackChatClient, CancellationToken cancellationToken)
        => Task.FromResult(_agent);

    internal override AgentConfig? GetConfig() => _agent.Config;
}

/// <summary>
/// Factory that builds an agent from config with chat client inheritance.
/// </summary>
internal sealed class ConfigAgentFactory : AgentFactory
{
    private readonly AgentConfig _config;
    private readonly Action<AgentBuilder>? _builderAction;

    public ConfigAgentFactory(AgentConfig config, Action<AgentBuilder>? builderAction = null)
    {
        _config = config;
        _builderAction = builderAction;
    }

    public override async Task<Agent.Agent> BuildAsync(IChatClient? fallbackChatClient, CancellationToken cancellationToken)
    {
        var builder = new AgentBuilder(_config);

        // Apply custom builder action if provided
        _builderAction?.Invoke(builder);

        // If no provider configured and we have a fallback, use it
        if (_config.Provider == null && fallbackChatClient != null)
        {
            builder.WithChatClient(fallbackChatClient);
        }

        return await builder.BuildAsync(cancellationToken);
    }

    internal override AgentConfig? GetConfig() => _config;
}

/// <summary>
/// A built multi-agent workflow ready for execution.
/// </summary>
public sealed class AgentWorkflowInstance
{
    private readonly GraphDefinition _graph;
    private readonly Dictionary<string, AgentFactory> _agentFactories;
    private readonly Dictionary<string, AgentNodeOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _workflowName;
    private readonly WorkflowSettingsConfig _settings;
    private readonly Dictionary<string, Func<EdgeConditionContext, bool>> _predicateEdges;

    // Cache of built agents (built lazily on first execution)
    private Dictionary<string, Agent.Agent>? _builtAgents;

    internal AgentWorkflowInstance(
        GraphDefinition graph,
        Dictionary<string, AgentFactory> agentFactories,
        Dictionary<string, AgentNodeOptions> options,
        IServiceProvider serviceProvider,
        string? workflowName = null,
        WorkflowSettingsConfig? settings = null,
        Dictionary<string, Func<EdgeConditionContext, bool>>? predicateEdges = null)
    {
        _graph = graph;
        _agentFactories = agentFactories;
        _options = options;
        _serviceProvider = serviceProvider;
        _workflowName = workflowName ?? graph.Name ?? "Workflow";
        _settings = settings ?? new WorkflowSettingsConfig();
        _predicateEdges = predicateEdges ?? new Dictionary<string, Func<EdgeConditionContext, bool>>();
    }

    // Legacy constructor for backward compatibility
    internal AgentWorkflowInstance(
        GraphDefinition graph,
        Dictionary<string, Agent.Agent> agents,
        Dictionary<string, AgentNodeOptions> options,
        IServiceProvider serviceProvider,
        string? workflowName = null)
        : this(graph, agents.ToDictionary(kvp => kvp.Key, kvp => (AgentFactory)new PrebuiltAgentFactory(kvp.Value)), options, serviceProvider, workflowName)
    {
    }

    /// <summary>
    /// The workflow name for identification in execution context.
    /// </summary>
    public string WorkflowName => _workflowName;

    /// <summary>
    /// Build agents lazily, caching the result for subsequent executions.
    /// If a fallback chat client is provided, agents without their own provider will use it.
    /// </summary>
    private async Task<Dictionary<string, Agent.Agent>> BuildAgentsAsync(
        IChatClient? fallbackChatClient,
        CancellationToken cancellationToken)
    {
        // Return cached agents if already built (for workflows used standalone without parent)
        if (_builtAgents != null)
            return _builtAgents;

        var agents = new Dictionary<string, Agent.Agent>();
        foreach (var (id, factory) in _agentFactories)
        {
            agents[id] = await factory.BuildAsync(fallbackChatClient, cancellationToken);
        }

        // Cache for subsequent executions (only if no fallback was used, as different parents may have different clients)
        if (fallbackChatClient == null)
        {
            _builtAgents = agents;
        }

        return agents;
    }

    /// <summary>
    /// Execute the workflow and return the final result.
    /// </summary>
    public async Task<WorkflowResult> RunAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var outputs = new Dictionary<string, object>();
        string? finalAnswer = null;
        string? error = null;
        Exception? exception = null;
        var success = true;

        try
        {
            await foreach (var evt in ExecuteStreamingAsync(input, cancellationToken))
            {
                // Capture the last TextDeltaEvent content for final answer
                if (evt is TextDeltaEvent textDelta)
                {
                    // This will be overwritten by the last agent's output
                    // For a proper implementation, we'd track the final node
                }

                // Capture node completion outputs (now using wrapped WorkflowNodeCompletedEvent)
                if (evt is WorkflowNodeCompletedEvent nodeComplete)
                {
                    if (nodeComplete.Outputs != null)
                    {
                        foreach (var kvp in nodeComplete.Outputs)
                        {
                            outputs[$"{nodeComplete.NodeId}.{kvp.Key}"] = kvp.Value;
                        }

                        // Check for answer in the outputs
                        if (nodeComplete.Outputs.TryGetValue("answer", out var answer))
                        {
                            finalAnswer = answer?.ToString();
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            success = false;
            error = "Workflow was cancelled";
        }
        catch (Exception ex)
        {
            success = false;
            error = ex.Message;
            exception = ex;
        }

        return new WorkflowResult
        {
            FinalAnswer = finalAnswer,
            Outputs = outputs,
            Duration = DateTimeOffset.UtcNow - startTime,
            Success = success,
            Error = error,
            Exception = exception
        };
    }

    /// <summary>
    /// Execute the workflow with streaming events.
    /// Returns unified stream of graph and agent events.
    /// </summary>
    public IAsyncEnumerable<Event> ExecuteStreamingAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        return ExecuteStreamingAsync(input, parentCoordinator: null, parentExecutionContext: null, parentChatClient: null, cancellationToken);
    }

    /// <summary>
    /// Execute the workflow with streaming events, using a <see cref="WorkflowEventCoordinator"/>
    /// for bidirectional event patterns (e.g. approval responses) and observer dispatch.
    /// This overload avoids any direct dependency on HPD.Events.
    /// </summary>
    /// <param name="input">The input to the workflow.</param>
    /// <param name="coordinator">
    /// A <see cref="WorkflowEventCoordinator"/> used to send approval responses and receive observer callbacks.
    /// Call <see cref="WorkflowEventCoordinator.Approve"/> or <see cref="WorkflowEventCoordinator.Deny"/>
    /// while iterating the returned stream.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Unified stream of graph and agent events.</returns>
    public async IAsyncEnumerable<Event> ExecuteStreamingAsync(
        string input,
        WorkflowEventCoordinator coordinator,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var evt in ExecuteStreamingAsync(input, coordinator.Inner, parentExecutionContext: null, parentChatClient: null, cancellationToken))
        {
            if (coordinator.HasObservers)
                await coordinator.DispatchToObserversAsync(evt, cancellationToken);
            yield return evt;
        }
    }

    /// <summary>
    /// Execute the workflow with streaming events, with optional parent coordinator for event bubbling.
    /// When a parent coordinator is provided, events will automatically bubble up to it.
    /// This enables nested workflows where events from inner workflows appear in the parent's event stream.
    /// </summary>
    /// <param name="input">The input to the workflow.</param>
    /// <param name="parentCoordinator">Optional parent event coordinator for hierarchical event bubbling.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Unified stream of graph and agent events.</returns>
    public IAsyncEnumerable<Event> ExecuteStreamingAsync(
        string input,
        HPD.Events.IEventCoordinator? parentCoordinator,
        CancellationToken cancellationToken = default)
    {
        return ExecuteStreamingAsync(input, parentCoordinator, parentExecutionContext: null, parentChatClient: null, cancellationToken);
    }

    /// <summary>
    /// Execute the workflow with streaming events, with full hierarchical context support.
    /// </summary>
    /// <param name="input">The input to the workflow.</param>
    /// <param name="parentCoordinator">Optional parent event coordinator for hierarchical event bubbling.</param>
    /// <param name="parentExecutionContext">Optional parent execution context for agent hierarchy tracking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Unified stream of graph and agent events.</returns>
    public IAsyncEnumerable<Event> ExecuteStreamingAsync(
        string input,
        HPD.Events.IEventCoordinator? parentCoordinator,
        AgentExecutionContext? parentExecutionContext,
        CancellationToken cancellationToken = default)
    {
        return ExecuteStreamingAsync(input, parentCoordinator, parentExecutionContext, parentChatClient: null, cancellationToken);
    }

    /// <summary>
    /// Execute the workflow with streaming events, with full hierarchical context support including chat client inheritance.
    /// </summary>
    /// <param name="input">The input to the workflow.</param>
    /// <param name="parentCoordinator">Optional parent event coordinator for hierarchical event bubbling.</param>
    /// <param name="parentExecutionContext">Optional parent execution context for agent hierarchy tracking.</param>
    /// <param name="parentChatClient">Optional parent chat client for agents that don't have their own provider configured.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Unified stream of graph and agent events.</returns>
    public async IAsyncEnumerable<Event> ExecuteStreamingAsync(
        string input,
        HPD.Events.IEventCoordinator? parentCoordinator,
        AgentExecutionContext? parentExecutionContext,
        IChatClient? parentChatClient,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Build agents lazily (with chat client inheritance if no provider configured)
        var agents = await BuildAgentsAsync(parentChatClient, cancellationToken);

        // Create event coordinator for unified streaming
        var eventCoordinator = new EventCoordinator();

        // Set up parent for hierarchical event bubbling if provided
        if (parentCoordinator != null)
        {
            eventCoordinator.SetParent(parentCoordinator);
        }

        // Build workflow-level execution context
        var randomId = Guid.NewGuid().ToString("N")[..8];
        var sanitizedWorkflowName = System.Text.RegularExpressions.Regex.Replace(
            _workflowName, @"[^a-zA-Z0-9]", "_");

        var workflowContext = new AgentExecutionContext
        {
            AgentName = _workflowName,
            AgentId = parentExecutionContext != null
                ? $"{parentExecutionContext.AgentId}-{sanitizedWorkflowName}-{randomId}"
                : $"{sanitizedWorkflowName}-{randomId}",
            ParentAgentId = parentExecutionContext?.AgentId,
            AgentChain = parentExecutionContext != null
                ? new List<string>(parentExecutionContext.AgentChain) { _workflowName }
                : new List<string> { _workflowName },
            Depth = (parentExecutionContext?.Depth ?? -1) + 1
        };

        // Set ExecutionContext on each agent in the workflow for proper event attribution
        foreach (var (agentName, agent) in agents)
        {
            var agentRandomId = Guid.NewGuid().ToString("N")[..8];
            var sanitizedAgentName = System.Text.RegularExpressions.Regex.Replace(
                agentName, @"[^a-zA-Z0-9]", "_");

            agent.ExecutionContext = new AgentExecutionContext
            {
                AgentName = agentName,
                AgentId = $"{workflowContext.AgentId}-{sanitizedAgentName}-{agentRandomId}",
                ParentAgentId = workflowContext.AgentId,
                AgentChain = new List<string>(workflowContext.AgentChain) { agentName },
                Depth = workflowContext.Depth + 1
            };
        }

        // Create context
        var context = new AgentGraphContext(
            executionId: Guid.NewGuid().ToString(),
            graph: _graph,
            services: _serviceProvider,
            agents: agents,
            agentOptions: _options,
            originalInput: input,
            predicateEdges: _predicateEdges)
        {
            EventCoordinator = eventCoordinator,
            FallbackChatClient = parentChatClient
        };

        // Set initial input in channels
        context.Channels["input"].Set(input);

        // Create orchestrator — pass checkpoint store when checkpointing is enabled
        var checkpointStore = _settings.EnableCheckpointing
            ? _serviceProvider.GetService<HPDAgent.Graph.Abstractions.Checkpointing.IGraphCheckpointStore>()
            : null;

        var orchestrator = new GraphOrchestrator<AgentGraphContext>(_serviceProvider, checkpointStore: checkpointStore);

        // Start execution in background task
        var executionTask = Task.Run(async () =>
        {
            try
            {
                await orchestrator.ExecuteAsync(context, cancellationToken);
            }
            catch (Exception ex)
            {
                // Emit error event
                eventCoordinator.Emit(new MessageTurnErrorEvent(
                    Message: ex.Message,
                    Exception: ex));
            }
        }, cancellationToken);

        // Stream events from coordinator, wrapping graph events into agent-idiomatic workflow events
        await foreach (var evt in eventCoordinator.ReadAllAsync(cancellationToken))
        {
            // Wrap graph events into AgentEvent-derived workflow events
            var wrappedEvent = WrapGraphEvent(evt, workflowContext);
            if (wrappedEvent != null)
            {
                yield return wrappedEvent;
            }

            // Check if execution is complete
            if (context.IsComplete || context.IsCancelled)
            {
                break;
            }
        }

        // Wait for execution to complete
        await executionTask;
    }

    /// <summary>
    /// Wraps internal graph events into public AgentEvent-derived workflow events.
    /// This allows consumers to use only HPD.Agent + HPD.MultiAgent without depending on HPD.Graph.
    /// </summary>
    private Event? WrapGraphEvent(Event evt, AgentExecutionContext workflowContext)
    {
        return evt switch
        {
            // Graph lifecycle events → Workflow events
            GraphExecutionStartedEvent g => new WorkflowStartedEvent
            {
                WorkflowName = _workflowName,
                NodeCount = g.NodeCount,
                LayerCount = g.LayerCount,
                ExecutionContext = workflowContext
            },

            GraphExecutionCompletedEvent g => new WorkflowCompletedEvent
            {
                WorkflowName = _workflowName,
                Duration = g.Duration,
                SuccessfulNodes = g.SuccessfulNodes,
                FailedNodes = g.FailedNodes,
                SkippedNodes = g.SkippedNodes,
                ExecutionContext = workflowContext
            },

            // Node events → WorkflowNode events
            NodeExecutionStartedEvent n => new WorkflowNodeStartedEvent
            {
                WorkflowName = _workflowName,
                NodeId = n.NodeId,
                AgentName = n.HandlerName,
                LayerIndex = n.LayerIndex,
                ExecutionContext = workflowContext
            },

            NodeExecutionCompletedEvent n => new WorkflowNodeCompletedEvent
            {
                WorkflowName = _workflowName,
                NodeId = n.NodeId,
                AgentName = n.HandlerName,
                Success = n.Result is NodeExecutionResult.Success,
                Duration = n.Duration,
                Progress = n.Progress,
                Outputs = n.Outputs,
                ErrorMessage = n.Result is NodeExecutionResult.Failure f ? f.Exception.Message : null,
                ExecutionContext = workflowContext
            },

            NodeSkippedEvent n => new WorkflowNodeSkippedEvent
            {
                WorkflowName = _workflowName,
                NodeId = n.NodeId,
                Reason = n.Reason,
                ExecutionContext = workflowContext
            },

            // Layer events → WorkflowLayer events
            LayerExecutionStartedEvent l => new WorkflowLayerStartedEvent
            {
                WorkflowName = _workflowName,
                LayerIndex = l.LayerIndex,
                NodeCount = l.NodeCount,
                ExecutionContext = workflowContext
            },

            LayerExecutionCompletedEvent l => new WorkflowLayerCompletedEvent
            {
                WorkflowName = _workflowName,
                LayerIndex = l.LayerIndex,
                Duration = l.Duration,
                SuccessfulNodes = l.SuccessfulNodes,
                ExecutionContext = workflowContext
            },

            // Edge events → WorkflowEdge events (diagnostic)
            EdgeTraversedEvent e => new WorkflowEdgeTraversedEvent
            {
                WorkflowName = _workflowName,
                FromNodeId = e.FromNodeId,
                ToNodeId = e.ToNodeId,
                HasCondition = e.HasCondition,
                ConditionDescription = e.ConditionDescription,
                ExecutionContext = workflowContext
            },

            // Diagnostic events
            GraphDiagnosticEvent d => new WorkflowDiagnosticEvent
            {
                WorkflowName = _workflowName,
                Level = (LogLevel)(int)d.Level,  // Cast from HPDAgent.Graph LogLevel
                Source = d.Source,
                Message = d.Message,
                NodeId = d.NodeId,
                ExecutionContext = workflowContext
            },

            // Pass through AgentEvents unchanged (they're already in the right format)
            AgentEvent ae => ae,

            // Skip other graph events (EdgeConditionFailedEvent, iteration events, HITL events, etc.)
            // These are internal implementation details
            _ => null
        };
    }

    /// <summary>
    /// Get the underlying graph definition.
    /// </summary>
    public GraphDefinition Graph => _graph;

    /// <summary>
    /// Get Mermaid diagram of the workflow.
    /// </summary>
    public string ToDiagram()
    {
        return _graph.ToMermaid();
    }

    /// <summary>
    /// Export the workflow configuration as JSON.
    /// Reconstructs a <see cref="MultiAgentWorkflowConfig"/> from the runtime graph and options,
    /// then serializes it to indented JSON.
    /// Note: Agents added as pre-built instances will only export config if Agent.Config is set.
    /// </summary>
    public string ExportConfigJson()
    {
        // --- Build Agents dictionary ---
        var agents = new Dictionary<string, AgentNodeConfig>();

        foreach (var (nodeId, factory) in _agentFactories)
        {
            var agentConfig = factory.GetConfig() ?? new AgentConfig();
            var nodeOptions = _options.TryGetValue(nodeId, out var opts) ? opts : new AgentNodeOptions();

            RetryConfig? retryConfig = null;
            if (nodeOptions.RetryPolicy is { } rp)
            {
                retryConfig = new RetryConfig
                {
                    MaxAttempts = rp.MaxAttempts,
                    InitialDelay = rp.InitialDelay,
                    Strategy = rp.Strategy,
                    MaxDelay = rp.MaxDelay,
                    OnlyTransient = rp.RetryableExceptions?.Count > 0
                };
            }

            ErrorConfig? errorConfig = null;
            if (nodeOptions.ErrorMode != ErrorMode.Stop || nodeOptions.FallbackAgentId != null)
            {
                errorConfig = new ErrorConfig
                {
                    Mode = nodeOptions.ErrorMode,
                    FallbackAgent = nodeOptions.FallbackAgentId
                };
            }

            agents[nodeId] = new AgentNodeConfig
            {
                Agent = agentConfig,
                OutputMode = nodeOptions.OutputMode,
                StructuredOutputType = nodeOptions.StructuredType?.AssemblyQualifiedName,
                UnionTypeNames = nodeOptions.UnionTypes?.Select(t => t.AssemblyQualifiedName!).ToList(),
                Timeout = nodeOptions.Timeout,
                MaxConcurrent = nodeOptions.MaxConcurrentExecutions,
                Retry = retryConfig,
                OnError = errorConfig,
                InputKey = nodeOptions.InputKey,
                OutputKey = nodeOptions.OutputKey,
                InputTemplate = nodeOptions.InputTemplate,
                AdditionalInstructions = nodeOptions.AdditionalSystemInstructions
            };
        }

        // --- Build Edges list (skip START/END infrastructure edges) ---
        var entryId = _graph.EntryNodeId;
        var exitId = _graph.ExitNodeId;

        var edges = _graph.Edges
            .Where(e => e.From != entryId && e.To != exitId)
            .Select(e =>
            {
                ConditionConfig? when = null;
                if (e.Condition is { } c && c.Type != HPDAgent.Graph.Abstractions.Graph.ConditionType.Always)
                {
                    when = MapEdgeConditionToConfig(c);
                }
                return new EdgeConfig { From = e.From, To = e.To, When = when };
            })
            .ToList();

        // --- Build Settings ---
        var settings = new WorkflowSettingsConfig
        {
            MaxIterations = _graph.MaxIterations,
            DefaultTimeout = _graph.ExecutionTimeout
        };

        // --- Assemble and serialize ---
        var config = new MultiAgentWorkflowConfig
        {
            Name = _workflowName,
            Version = _graph.Version,
            Agents = agents,
            Edges = edges,
            Settings = settings
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        return JsonSerializer.Serialize(config, jsonOptions);
    }

    /// <summary>
    /// Recursively maps an <see cref="EdgeCondition"/> to a serializable <see cref="ConditionConfig"/>.
    /// Preserves nested <c>Conditions</c> for compound types and <c>RegexOptions</c> for regex conditions.
    /// </summary>
    private static ConditionConfig MapEdgeConditionToConfig(HPDAgent.Graph.Abstractions.Graph.EdgeCondition c) =>
        new ConditionConfig
        {
            Type = c.Type,
            Field = c.Field,
            Value = c.Value is null ? null
                  : c.Value is JsonElement je ? je
                  : JsonSerializer.SerializeToElement(c.Value),
            RegexOptions = c.RegexOptions,
            Conditions = c.Conditions?.Select(MapEdgeConditionToConfig).ToList()
        };
}

/// <summary>
/// Extension methods for handling approval workflow events.
/// </summary>
public static class ApprovalWorkflowExtensions
{
    /// <summary>
    /// Respond to a node approval request (approve).
    /// </summary>
    /// <param name="coordinator">The event coordinator.</param>
    /// <param name="requestId">The request ID from NodeApprovalRequestEvent.</param>
    /// <param name="reason">Optional reason for approval.</param>
    /// <param name="resumeData">Optional data to pass back to the node.</param>
    public static void Approve(
        this HPD.Events.IEventCoordinator coordinator,
        string requestId,
        string? reason = null,
        object? resumeData = null)
    {
        coordinator.SendResponse(requestId, new NodeApprovalResponseEvent
        {
            RequestId = requestId,
            SourceName = "User",
            Approved = true,
            Reason = reason,
            ResumeData = resumeData
        });
    }

    /// <summary>
    /// Respond to a node approval request (deny).
    /// </summary>
    /// <param name="coordinator">The event coordinator.</param>
    /// <param name="requestId">The request ID from NodeApprovalRequestEvent.</param>
    /// <param name="reason">Reason for denial.</param>
    public static void Deny(
        this HPD.Events.IEventCoordinator coordinator,
        string requestId,
        string reason = "Denied by user")
    {
        coordinator.SendResponse(requestId, new NodeApprovalResponseEvent
        {
            RequestId = requestId,
            SourceName = "User",
            Approved = false,
            Reason = reason
        });
    }

    /// <summary>
    /// Create an approval response event.
    /// </summary>
    public static NodeApprovalResponseEvent CreateApprovalResponse(
        string requestId,
        bool approved,
        string? reason = null,
        object? resumeData = null)
    {
        return new NodeApprovalResponseEvent
        {
            RequestId = requestId,
            SourceName = "User",
            Approved = approved,
            Reason = reason,
            ResumeData = resumeData
        };
    }
}
