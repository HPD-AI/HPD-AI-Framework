namespace HPDAgent.Graph.Abstractions.Graph;

/// <summary>
/// Directed graph definition.
/// Supports both DAG (acyclic) and cyclic graphs with iteration limits.
/// Immutable after construction.
/// </summary>
public sealed record Graph
{
    /// <summary>
    /// Unique graph identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable graph name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Graph version (semantic versioning recommended).
    /// </summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>
    /// All nodes in the graph.
    /// </summary>
    public required IReadOnlyList<Node> Nodes { get; init; }

    /// <summary>
    /// All edges in the graph.
    /// </summary>
    public required IReadOnlyList<Edge> Edges { get; init; }

    /// <summary>
    /// Entry node ID (typically "START").
    /// </summary>
    public required string EntryNodeId { get; init; }

    /// <summary>
    /// Exit node ID (typically "END").
    /// </summary>
    public required string ExitNodeId { get; init; }

    /// <summary>
    /// Additional metadata (author, description, tags, etc.).
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Maximum iterations for cyclic graphs (cycle protection).
    /// Note: When IterationOptions is set, IterationOptions.MaxIterations takes precedence.
    /// </summary>
    public int MaxIterations { get; init; } = 10;

    /// <summary>
    /// Advanced iteration options for change-aware execution.
    /// When null, uses default behavior (eager propagation).
    /// </summary>
    public IterationOptions? IterationOptions { get; init; }

    /// <summary>
    /// Global execution timeout for the entire graph.
    /// Null = no timeout.
    /// </summary>
    public TimeSpan? ExecutionTimeout { get; init; }

    /// <summary>
    /// Message cloning policy for output propagation.
    /// Default: LazyClone (optimal memory/performance trade-off).
    /// - LazyClone: First downstream edge gets original, subsequent get clones
    /// - AlwaysClone: All edges get clones (safest, highest memory)
    /// - NeverClone: All edges share references (fastest, requires immutable handlers)
    /// </summary>
    public Execution.CloningPolicy CloningPolicy { get; init; } = Execution.CloningPolicy.LazyClone;

    /// <summary>
    /// Get a node by ID.
    /// </summary>
    public Node? GetNode(string nodeId)
    {
        return Nodes.FirstOrDefault(n => n.Id == nodeId);
    }

    /// <summary>
    /// Get all incoming edges for a node.
    /// </summary>
    public IReadOnlyList<Edge> GetIncomingEdges(string nodeId)
    {
        return Edges.Where(e => e.To == nodeId).ToList();
    }

    /// <summary>
    /// Get all outgoing edges for a node.
    /// </summary>
    public IReadOnlyList<Edge> GetOutgoingEdges(string nodeId)
    {
        return Edges.Where(e => e.From == nodeId).ToList();
    }

    /// <summary>
    /// Get all edges in the graph.
    /// </summary>
    public IReadOnlyList<Edge> GetAllEdges()
    {
        return Edges;
    }

    /// <summary>
    /// Get execution layers using topological sorting (Kahn's algorithm).
    /// Nodes in the same layer can execute in parallel.
    /// </summary>
    /// <returns>List of execution layers in order</returns>
    public IReadOnlyList<Orchestration.ExecutionLayer> GetExecutionLayers()
    {
        var layers = new List<Orchestration.ExecutionLayer>();
        var inDegree = new Dictionary<string, int>();

        // Build adjacency list once for O(E) instead of O(V·E)
        var outgoingEdges = new Dictionary<string, List<Edge>>();

        // Build HashSet of excluded node IDs for O(1) lookup instead of O(V) FirstOrDefault
        var excludedNodes = new HashSet<string>(
            Nodes.Where(n => n.Type is NodeType.Start or NodeType.End)
                 .Select(n => n.Id)
        );

        // Initialize structures
        foreach (var node in Nodes.Where(n => n.Type != NodeType.Start && n.Type != NodeType.End))
        {
            inDegree[node.Id] = 0;
            outgoingEdges[node.Id] = new List<Edge>();
        }

        // Build adjacency list and count in-degrees in one pass O(E)
        foreach (var edge in Edges)
        {
            // Skip edges involving START/END nodes - O(1) HashSet lookup instead of O(V) FirstOrDefault
            if (excludedNodes.Contains(edge.From) || excludedNodes.Contains(edge.To))
                continue;

            if (inDegree.ContainsKey(edge.To))
            {
                inDegree[edge.To]++;
            }

            if (outgoingEdges.ContainsKey(edge.From))
            {
                outgoingEdges[edge.From].Add(edge);
            }
        }

        int level = 0;

        while (inDegree.Count > 0)
        {
            // Find all nodes with in-degree 0 (ready to execute)
            var ready = inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key).ToList();

            if (ready.Count == 0)
            {
                // Cycle detected or unreachable nodes
                break;
            }

            // Create layer for nodes that are ready
            layers.Add(new Orchestration.ExecutionLayer
            {
                Level = level,
                NodeIds = ready
            });

            // Remove processed nodes and update in-degrees
            foreach (var nodeId in ready)
            {
                inDegree.Remove(nodeId);

                // Decrease in-degree for downstream nodes using adjacency list
                if (outgoingEdges.TryGetValue(nodeId, out var edges))
                {
                    foreach (var edge in edges)
                    {
                        if (inDegree.ContainsKey(edge.To))
                        {
                            inDegree[edge.To]--;
                        }
                    }
                }
            }

            level++;
        }

        return layers;
    }

    /// <summary>
    /// Gets back-edges in this graph (edges pointing backwards in topological order).
    /// Back-edges create cycles and enable iterative execution patterns.
    /// Note: Not cached internally - the orchestrator caches the result at execution start.
    /// </summary>
    /// <returns>List of back-edges sorted by jump distance (largest first)</returns>
    public IReadOnlyList<BackEdge> GetBackEdges()
    {
        return ComputeBackEdges();
    }

    /// <summary>
    /// True if this graph contains cycles (has back-edges).
    /// Cyclic graphs require iterative execution mode.
    /// </summary>
    public bool HasCycles => GetBackEdges().Count > 0;

    /// <summary>
    /// Computes back-edges using DFS traversal.
    /// A back-edge points from a node to an ancestor in the DFS tree.
    /// </summary>
    private IReadOnlyList<BackEdge> ComputeBackEdges()
    {
        var backEdges = new List<BackEdge>();

        // Build adjacency list for non-START/END nodes
        var excludedNodes = new HashSet<string>(
            Nodes.Where(n => n.Type is NodeType.Start or NodeType.End)
                 .Select(n => n.Id)
        );

        var adjacency = new Dictionary<string, List<Edge>>();
        foreach (var node in Nodes.Where(n => !excludedNodes.Contains(n.Id)))
        {
            adjacency[node.Id] = new List<Edge>();
        }

        foreach (var edge in Edges)
        {
            if (excludedNodes.Contains(edge.From) || excludedNodes.Contains(edge.To))
                continue;

            if (adjacency.ContainsKey(edge.From))
            {
                adjacency[edge.From].Add(edge);
            }
        }

        // DFS state: 0 = unvisited, 1 = in current path (gray), 2 = finished (black)
        var state = new Dictionary<string, int>();
        var discoveryOrder = new Dictionary<string, int>();
        var currentOrder = 0;

        foreach (var nodeId in adjacency.Keys)
        {
            state[nodeId] = 0;
        }

        void Dfs(string nodeId)
        {
            state[nodeId] = 1; // Mark as being visited
            discoveryOrder[nodeId] = currentOrder++;

            if (adjacency.TryGetValue(nodeId, out var edges))
            {
                foreach (var edge in edges)
                {
                    if (!state.ContainsKey(edge.To))
                        continue;

                    if (state[edge.To] == 1) // Back-edge: points to an ancestor in current path
                    {
                        backEdges.Add(new BackEdge
                        {
                            Edge = edge,
                            SourceOrder = discoveryOrder[nodeId],
                            TargetOrder = discoveryOrder[edge.To],
                            JumpDistance = discoveryOrder[nodeId] - discoveryOrder[edge.To]
                        });
                    }
                    else if (state[edge.To] == 0) // Unvisited
                    {
                        Dfs(edge.To);
                    }
                    // state[edge.To] == 2 means cross/forward edge, not a back-edge
                }
            }

            state[nodeId] = 2; // Mark as finished
        }

        // Find entry points (nodes reachable from START)
        var entryPoints = Edges
            .Where(e => e.From == EntryNodeId && adjacency.ContainsKey(e.To))
            .Select(e => e.To)
            .ToList();

        // Start DFS from entry points
        foreach (var entry in entryPoints)
        {
            if (state.TryGetValue(entry, out var s) && s == 0)
            {
                Dfs(entry);
            }
        }

        // Also process any unvisited nodes (disconnected components)
        foreach (var nodeId in adjacency.Keys)
        {
            if (state[nodeId] == 0)
            {
                Dfs(nodeId);
            }
        }

        // Sort by jump distance descending: evaluate long-range back-edges first
        // This provides deterministic behavior when multiple back-edges share nodes
        return backEdges
            .OrderByDescending(b => b.JumpDistance)
            .ToList();
    }

    /// <summary>
    /// Generate Mermaid flowchart diagram for visualization.
    /// Output can be rendered at https://mermaid.live or in Markdown.
    /// </summary>
    /// <returns>Mermaid flowchart syntax</returns>
    public string ToMermaid()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("flowchart TD");

        // Add nodes with appropriate shapes
        foreach (var node in Nodes)
        {
            var nodeLabel = EscapeMermaidLabel(node.Name);
            var shape = node.Type switch
            {
                NodeType.Start => $"{node.Id}([{nodeLabel}])",      // Stadium shape for Start
                NodeType.End => $"{node.Id}([{nodeLabel}])",        // Stadium shape for End
                NodeType.Router => $"{node.Id}{{{{{nodeLabel}}}}}",  // Diamond for Router
                NodeType.SubGraph => $"{node.Id}[/{nodeLabel}/]",   // Trapezoid for SubGraph
                NodeType.Handler => $"{node.Id}[{nodeLabel}]",      // Rectangle for Handler
                _ => $"{node.Id}[{nodeLabel}]"
            };
            sb.AppendLine($"    {shape}");
        }

        sb.AppendLine();

        // Add edges with labels for conditions
        foreach (var edge in Edges)
        {
            if (edge.Condition != null)
            {
                var conditionLabel = EscapeMermaidLabel(edge.Condition.GetDescription() ?? "condition");
                sb.AppendLine($"    {edge.From} -->|{conditionLabel}| {edge.To}");
            }
            else
            {
                sb.AppendLine($"    {edge.From} --> {edge.To}");
            }
        }

        // Add styling
        sb.AppendLine();
        sb.AppendLine("    classDef startEnd fill:#e1f5e1,stroke:#4caf50,stroke-width:2px");
        sb.AppendLine("    classDef handler fill:#e3f2fd,stroke:#2196f3,stroke-width:2px");
        sb.AppendLine("    classDef router fill:#fff3e0,stroke:#ff9800,stroke-width:2px");
        sb.AppendLine("    classDef subgraph fill:#f3e5f5,stroke:#9c27b0,stroke-width:2px");

        // Apply styles to nodes
        var startEndNodes = Nodes.Where(n => n.Type == NodeType.Start || n.Type == NodeType.End)
            .Select(n => n.Id);
        var handlerNodes = Nodes.Where(n => n.Type == NodeType.Handler).Select(n => n.Id);
        var routerNodes = Nodes.Where(n => n.Type == NodeType.Router).Select(n => n.Id);
        var subGraphNodes = Nodes.Where(n => n.Type == NodeType.SubGraph).Select(n => n.Id);

        if (startEndNodes.Any())
            sb.AppendLine($"    class {string.Join(",", startEndNodes)} startEnd");
        if (handlerNodes.Any())
            sb.AppendLine($"    class {string.Join(",", handlerNodes)} handler");
        if (routerNodes.Any())
            sb.AppendLine($"    class {string.Join(",", routerNodes)} router");
        if (subGraphNodes.Any())
            sb.AppendLine($"    class {string.Join(",", subGraphNodes)} subgraph");

        return sb.ToString();
    }

    /// <summary>
    /// Escape special characters in Mermaid labels.
    /// </summary>
    private static string EscapeMermaidLabel(string label)
    {
        return label
            .Replace("\"", "&quot;")
            .Replace("[", "&#91;")
            .Replace("]", "&#93;")
            .Replace("{", "&#123;")
            .Replace("}", "&#125;")
            .Replace("|", "&#124;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    /// <summary>
    /// Computes a structural hash of the graph for change detection.
    /// Hash includes nodes, edges, and critical configuration.
    /// Used by incremental execution to detect graph structure changes.
    /// If the structure hash changes, the entire graph must re-execute.
    /// </summary>
    public string ComputeStructureHash()
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();

        // Build deterministic string representation of graph structure
        var structureBuilder = new System.Text.StringBuilder();

        // Include graph-level properties
        structureBuilder.AppendLine($"Version:{Version}");
        structureBuilder.AppendLine($"MaxIterations:{MaxIterations}");
        structureBuilder.AppendLine($"CloningPolicy:{CloningPolicy}");

        // Include iteration options if present
        if (IterationOptions != null)
        {
            structureBuilder.AppendLine($"IterationOptions.MaxIterations:{IterationOptions.MaxIterations}");
            structureBuilder.AppendLine($"IterationOptions.UseChangeAwareIteration:{IterationOptions.UseChangeAwareIteration}");
            structureBuilder.AppendLine($"IterationOptions.EnableAutoConvergence:{IterationOptions.EnableAutoConvergence}");
        }

        // Include nodes (sorted by ID for determinism)
        foreach (var node in Nodes.OrderBy(n => n.Id))
        {
            structureBuilder.AppendLine($"Node:{node.Id}|{node.Type}|{node.HandlerName}");

            // Include critical node properties that affect execution
            if (node.Timeout.HasValue)
                structureBuilder.AppendLine($"  Timeout:{node.Timeout}");
            if (node.MaxExecutions.HasValue)
                structureBuilder.AppendLine($"  MaxExecutions:{node.MaxExecutions}");
            if (node.OutputPortCount > 1)
                structureBuilder.AppendLine($"  OutputPortCount:{node.OutputPortCount}");
        }

        // Include edges (sorted by From → To for determinism)
        foreach (var edge in Edges.OrderBy(e => e.From).ThenBy(e => e.To))
        {
            structureBuilder.AppendLine($"Edge:{edge.From}→{edge.To}");

            // Include port information if multi-port
            if (edge.FromPort.HasValue || edge.ToPort.HasValue)
                structureBuilder.AppendLine($"  Ports:{edge.FromPort}→{edge.ToPort}");

            // Include cloning policy override if present
            if (edge.CloningPolicy.HasValue)
                structureBuilder.AppendLine($"  CloningPolicy:{edge.CloningPolicy}");
        }

        // Compute hash
        var bytes = System.Text.Encoding.UTF8.GetBytes(structureBuilder.ToString());
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes);
    }
}
