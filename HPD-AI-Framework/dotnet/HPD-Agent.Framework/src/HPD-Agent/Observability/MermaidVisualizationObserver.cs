using System.Text;

namespace HPD.Agent;

/// <summary>
/// Generates Mermaid flowchart diagrams showing the actual execution path taken by the agent.
/// Unlike static graphs, this shows WHAT ACTUALLY HAPPENED during runtime.
/// </summary>
/// <remarks>
/// <para><b>What This Visualizes:</b></para>
/// <list type="bullet">
/// <item>Actual execution flow (iterations, decisions, tool calls)</item>
/// <item>Timing information (how long operations took)</item>
/// <item>Error conditions (circuit breakers, permission denials)</item>
/// <item>Parallel tool execution patterns</item>
/// </list>
///
/// <para><b>Usage:</b></para>
/// <code>
/// var vizObserver = new MermaidVisualizationObserver();
///
/// var agent = new AgentBuilder()
///     .WithModel(model)
///     .WithObserver(vizObserver)
///     .Build();
///
/// await foreach (var evt in agent.RunAsync(messages, thread))
/// {
///     // Process events...
/// }
///
/// // Generate Mermaid diagram
/// var diagram = vizObserver.GenerateMermaid();
/// File.WriteAllText("execution-flow.mmd", diagram);
///
/// // Or get ASCII timeline
/// Console.WriteLine(vizObserver.GenerateTimeline());
/// </code>
///
/// <para><b>Output Formats:</b></para>
/// <list type="bullet">
/// <item><see cref="GenerateMermaid"/> - Flowchart diagram (renders in GitHub, VS Code, etc.)</item>
/// <item><see cref="GenerateTimeline"/> - ASCII timeline for console output</item>
/// </list>
/// </remarks>
public class MermaidVisualizationObserver : IAgentEventObserver
{
    private readonly List<GraphNode> _nodes = [];
    private readonly List<GraphEdge> _edges = [];
    private readonly List<ExecutionEvent> _trace = [];
    private readonly object _lock = new();

    private int _nodeCounter = 0;
    private string? _currentNode = null;
    private DateTimeOffset? _startTime = null;

    /// <summary>
    /// Gets the number of iterations executed.
    /// </summary>
    public int IterationCount { get; private set; }

    /// <summary>
    /// Gets the total execution duration.
    /// </summary>
    public TimeSpan TotalDuration => _trace.LastOrDefault()?.Timestamp - (_startTime ?? DateTimeOffset.UtcNow) ?? TimeSpan.Zero;

    public Task OnEventAsync(AgentEvent evt, CancellationToken ct = default)
    {
        lock (_lock)
        {
            // Extract timestamp from event (all events have it as a parameter)
            var timestamp = GetEventTimestamp(evt);

            // Track execution trace for timeline
            _trace.Add(new ExecutionEvent
            {
                Timestamp = timestamp,
                Type = evt.GetType().Name,
                Data = evt
            });

            if (_startTime == null)
            {
                _startTime = timestamp;
            }

            // Build graph nodes and edges
            switch (evt)
            {
                case MessageTurnStartedEvent e:
                    var startNode = AddNode("START", "Message Turn Start", shape: "circle");
                    _currentNode = startNode;
                    break;

                case IterationStartEvent e:
                    IterationCount = Math.Max(IterationCount, e.Iteration + 1);
                    var iterNode = AddNode($"ITER_{e.Iteration}",
                        $"Iteration {e.Iteration}\\n({e.CurrentMessageCount} msgs)");

                    if (_currentNode != null)
                    {
                        AddEdge(_currentNode, iterNode);
                    }
                    _currentNode = iterNode;
                    break;

                case AgentDecisionEvent e:
                    var decisionNode = AddNode($"DEC_{e.Iteration}",
                        $"Decision:\\n{e.DecisionType}",
                        shape: "diamond");

                    if (_currentNode != null)
                    {
                        AddEdge(_currentNode, decisionNode);
                    }
                    _currentNode = decisionNode;
                    break;

                case IterationMessagesEvent e:
                    var llmNode = AddNode($"LLM_{e.Iteration}",
                        $"LLM Call\\n{e.MessageCount} messages");

                    if (_currentNode != null)
                    {
                        AddEdge(_currentNode, llmNode);
                    }
                    _currentNode = llmNode;
                    break;

                case InternalParallelToolExecutionEvent e:
                    var toolNode = AddNode($"TOOL_{e.Iteration}",
                        $"Tools: {e.ToolCount}\\n{e.Duration.TotalMilliseconds:F0}ms" +
                        (e.IsParallel ? "\\n(parallel)" : ""));

                    if (_currentNode != null)
                    {
                        AddEdge(_currentNode, toolNode);
                    }
                    _currentNode = toolNode;
                    break;

                case CircuitBreakerTriggeredEvent e:
                    var cbNode = AddNode($"CB_{_nodeCounter++}",
                        $"⚠ Circuit Breaker\\n{e.FunctionName}",
                        style: "error");

                    if (_currentNode != null)
                    {
                        AddEdge(_currentNode, cbNode, "blocked");
                    }
                    _currentNode = cbNode;
                    break;

                case PermissionCheckEvent e when !e.IsApproved:
                    var deniedNode = AddNode($"PERM_{_nodeCounter++}",
                        $"🚫 Permission Denied\\n{e.FunctionName}",
                        style: "warning");

                    if (_currentNode != null)
                    {
                        AddEdge(_currentNode, deniedNode, "denied");
                    }
                    break;

                case AgentCompletionEvent e:
                    var endNode = AddNode("END",
                        $"Complete\\n{e.TotalIterations} iterations\\n{e.Duration.TotalSeconds:F1}s",
                        shape: "circle",
                        style: "success");

                    if (_currentNode != null)
                    {
                        AddEdge(_currentNode, endNode);
                    }
                    _currentNode = endNode;
                    break;
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Generates a Mermaid flowchart diagram of the execution.
    /// Output can be rendered in GitHub, VS Code, or any Mermaid-compatible viewer.
    /// </summary>
    public string GenerateMermaid()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            sb.AppendLine("```mermaid");
            sb.AppendLine("graph TD");
            sb.AppendLine();

            // Add nodes
            foreach (var node in _nodes)
            {
                var shape = node.Shape switch
                {
                    "diamond" => $"{{{node.Label}}}",
                    "circle" => $"(({node.Label}))",
                    _ => $"[{node.Label}]"
                };

                sb.Append($"    {node.Id}{shape}");

                if (node.Style != null)
                {
                    sb.Append($":::{node.Style}");
                }

                sb.AppendLine();
            }

            sb.AppendLine();

            // Add edges
            foreach (var edge in _edges)
            {
                var arrow = edge.Label != null
                    ? $"-->|{edge.Label}|"
                    : "-->";
                sb.AppendLine($"    {edge.From} {arrow} {edge.To}");
            }

            // Add styling classes
            sb.AppendLine();
            sb.AppendLine("    classDef error fill:#f96,stroke:#333,stroke-width:2px");
            sb.AppendLine("    classDef warning fill:#fa3,stroke:#333,stroke-width:2px");
            sb.AppendLine("    classDef success fill:#6f6,stroke:#333,stroke-width:2px");
            sb.AppendLine("```");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Generates an ASCII timeline of execution events.
    /// Useful for console output and debugging.
    /// </summary>
    public string GenerateTimeline()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Agent Execution Timeline");
            sb.AppendLine("     ");

            var startTime = _startTime ?? DateTimeOffset.UtcNow;

            foreach (var evt in _trace)
            {
                var elapsed = (evt.Timestamp - startTime).TotalSeconds;
                var indent = GetIndent(evt.Data);

                sb.AppendLine($"[{elapsed,6:F2}s] {indent}{FormatEvent(evt.Data)}");
            }

            sb.AppendLine("     ");
            sb.AppendLine($"Total: {TotalDuration.TotalSeconds:F2}s, {IterationCount} iterations");

            return sb.ToString();
        }
    }

    private string FormatEvent(AgentEvent evt) => evt switch
    {
        MessageTurnStartedEvent e => $"▶ Message Turn Started: {e.MessageTurnId}",
        IterationStartEvent e => $"├─ Iteration {e.Iteration}/{e.MaxIterations}",
        AgentDecisionEvent e => $"  ├─ Decision: {e.DecisionType}",
        IterationMessagesEvent e => $"  ├─ LLM Call ({e.MessageCount} messages)",
        InternalParallelToolExecutionEvent e =>
            $"  ├─ Tools: {e.ToolCount} executed ({e.Duration.TotalMilliseconds:F0}ms)" +
            (e.IsParallel ? " [PARALLEL]" : ""),
        CircuitBreakerTriggeredEvent e =>
            $"  └─ ⚠ Circuit Breaker: {e.FunctionName} ({e.ConsecutiveCount} consecutive errors)",
        PermissionCheckEvent e when !e.IsApproved =>
            $"  └─ 🚫 Permission Denied: {e.FunctionName} - {e.DenialReason}",
        AgentCompletionEvent e =>
            $"✓ Completed: {e.TotalIterations} iterations in {e.Duration.TotalSeconds:F1}s",
        MessageTurnFinishedEvent e =>
            $"◀ Message Turn Finished: {e.Duration.TotalSeconds:F2}s",
        _ => evt.GetType().Name
    };

    private string GetIndent(AgentEvent evt) => evt switch
    {
        MessageTurnStartedEvent => "",
        MessageTurnFinishedEvent => "",
        IterationStartEvent => "  ",
        AgentTurnStartedEvent => "  ",
        _ => "    "
    };

    private string AddNode(string id, string label,
        string shape = "rectangle", string? style = null)
    {
        _nodes.Add(new GraphNode
        {
            Id = id,
            Label = label,
            Shape = shape,
            Style = style
        });
        return id;
    }

    private void AddEdge(string from, string to, string? label = null)
    {
        _edges.Add(new GraphEdge { From = from, To = to, Label = label });
    }

    private static DateTimeOffset GetEventTimestamp(AgentEvent evt) => evt switch
    {
        MessageTurnStartedEvent e => e.Timestamp,
        MessageTurnFinishedEvent e => e.Timestamp,
        IterationStartEvent e => e.Timestamp,
        AgentDecisionEvent e => e.Timestamp,
        IterationMessagesEvent e => e.Timestamp,
        InternalParallelToolExecutionEvent e => e.Timestamp,
        CircuitBreakerTriggeredEvent e => e.Timestamp,
        PermissionCheckEvent e => e.Timestamp,
        AgentCompletionEvent e => e.Timestamp,
        ContainerExpandedEvent e => e.Timestamp,
        _ => DateTimeOffset.UtcNow
    };

    private record ExecutionEvent
    {
        public required DateTimeOffset Timestamp { get; init; }
        public required string Type { get; init; }
        public required AgentEvent Data { get; init; }
    }

    private record GraphNode
    {
        public required string Id { get; init; }
        public required string Label { get; init; }
        public string Shape { get; init; } = "rectangle";
        public string? Style { get; init; }
    }

    private record GraphEdge
    {
        public required string From { get; init; }
        public required string To { get; init; }
        public string? Label { get; init; }
    }
}
