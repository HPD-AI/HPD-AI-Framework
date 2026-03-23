using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Validation;
using GraphDefinition = HPDAgent.Graph.Abstractions.Graph.Graph;

namespace HPDAgent.Graph.Core.Validation;

/// <summary>
/// Validates graph structure and configuration.
/// </summary>
public static class GraphValidator
{
    /// <summary>
    /// Validate a graph definition.
    /// </summary>
    public static GraphValidationResult Validate(GraphDefinition graph)
    {
        var errors = new List<GraphValidationError>();
        var warnings = new List<GraphValidationWarning>();

        // Check basic structure
        ValidateBasicStructure(graph, errors);

        // Check for cycles
        ValidateCycles(graph, errors, warnings);

        // Check for unreachable nodes
        ValidateReachability(graph, errors, warnings);

        // Check for orphaned nodes
        ValidateOrphanedNodes(graph, warnings);

        // Check handler names
        ValidateHandlerNames(graph, warnings);

        // Check default edges
        ValidateDefaultEdges(graph, errors);

        // Check map nodes
        ValidateMapNodes(graph, errors, warnings);

        // Check port-based routing
        ValidatePortRouting(graph, errors, warnings);

        if (errors.Count > 0)
        {
            return GraphValidationResult.Failure(errors, warnings);
        }

        return GraphValidationResult.Success(warnings);
    }

    private static void ValidateBasicStructure(GraphDefinition graph, List<GraphValidationError> errors)
    {
        // Check for START node
        var startNode = graph.GetNode(graph.EntryNodeId);
        if (startNode == null)
        {
            errors.Add(new GraphValidationError
            {
                Code = "MISSING_START",
                Message = $"Entry node '{graph.EntryNodeId}' not found in graph"
            });
        }
        else if (startNode.Type != NodeType.Start)
        {
            errors.Add(new GraphValidationError
            {
                Code = "INVALID_START",
                Message = $"Entry node '{graph.EntryNodeId}' is not of type Start",
                NodeId = startNode.Id
            });
        }

        // Check for END node
        var endNode = graph.GetNode(graph.ExitNodeId);
        if (endNode == null)
        {
            errors.Add(new GraphValidationError
            {
                Code = "MISSING_END",
                Message = $"Exit node '{graph.ExitNodeId}' not found in graph"
            });
        }
        else if (endNode.Type != NodeType.End)
        {
            errors.Add(new GraphValidationError
            {
                Code = "INVALID_END",
                Message = $"Exit node '{graph.ExitNodeId}' is not of type End",
                NodeId = endNode.Id
            });
        }

        // Check for duplicate node IDs
        var nodeIds = new HashSet<string>();
        foreach (var node in graph.Nodes)
        {
            if (!nodeIds.Add(node.Id))
            {
                errors.Add(new GraphValidationError
                {
                    Code = "DUPLICATE_NODE_ID",
                    Message = $"Duplicate node ID: {node.Id}",
                    NodeId = node.Id
                });
            }
        }

        // Check edge references
        foreach (var edge in graph.Edges)
        {
            if (graph.GetNode(edge.From) == null)
            {
                errors.Add(new GraphValidationError
                {
                    Code = "INVALID_EDGE_FROM",
                    Message = $"Edge references non-existent source node: {edge.From}"
                });
            }

            if (graph.GetNode(edge.To) == null)
            {
                errors.Add(new GraphValidationError
                {
                    Code = "INVALID_EDGE_TO",
                    Message = $"Edge references non-existent target node: {edge.To}"
                });
            }
        }
    }

    private static void ValidateCycles(GraphDefinition graph, List<GraphValidationError> errors, List<GraphValidationWarning> warnings)
    {
        // Simple cycle detection using DFS
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        foreach (var node in graph.Nodes.Where(n => n.Type != NodeType.Start && n.Type != NodeType.End))
        {
            if (HasCycle(graph, node.Id, visited, recursionStack))
            {
                warnings.Add(new GraphValidationWarning
                {
                    Code = "CYCLE_DETECTED",
                    Message = $"Cycle detected involving node: {node.Id}. Ensure MaxIterations is set appropriately.",
                    NodeId = node.Id
                });
            }
        }
    }

    private static bool HasCycle(GraphDefinition graph, string nodeId, HashSet<string> visited, HashSet<string> recursionStack)
    {
        if (!visited.Contains(nodeId))
        {
            visited.Add(nodeId);
            recursionStack.Add(nodeId);

            foreach (var edge in graph.GetOutgoingEdges(nodeId))
            {
                if (!visited.Contains(edge.To))
                {
                    if (HasCycle(graph, edge.To, visited, recursionStack))
                    {
                        return true;
                    }
                }
                else if (recursionStack.Contains(edge.To))
                {
                    return true; // Cycle detected
                }
            }
        }

        recursionStack.Remove(nodeId);
        return false;
    }

    private static void ValidateReachability(GraphDefinition graph, List<GraphValidationError> errors, List<GraphValidationWarning> warnings)
    {
        // BFS from START to find all reachable nodes
        var reachable = new HashSet<string>();
        var queue = new Queue<string>();

        queue.Enqueue(graph.EntryNodeId);
        reachable.Add(graph.EntryNodeId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var edge in graph.GetOutgoingEdges(current))
            {
                if (reachable.Add(edge.To))
                {
                    queue.Enqueue(edge.To);
                }
            }
        }

        // Check if END is reachable
        if (!reachable.Contains(graph.ExitNodeId))
        {
            errors.Add(new GraphValidationError
            {
                Code = "UNREACHABLE_END",
                Message = "End node is not reachable from Start node"
            });
        }

        // Warn about unreachable nodes
        foreach (var node in graph.Nodes.Where(n => n.Type != NodeType.Start && n.Type != NodeType.End))
        {
            if (!reachable.Contains(node.Id))
            {
                warnings.Add(new GraphValidationWarning
                {
                    Code = "UNREACHABLE_NODE",
                    Message = $"Node '{node.Id}' is not reachable from Start",
                    NodeId = node.Id
                });
            }
        }
    }

    private static void ValidateOrphanedNodes(GraphDefinition graph, List<GraphValidationWarning> warnings)
    {
        foreach (var node in graph.Nodes.Where(n => n.Type != NodeType.Start && n.Type != NodeType.End))
        {
            var incomingCount = graph.GetIncomingEdges(node.Id).Count;
            var outgoingCount = graph.GetOutgoingEdges(node.Id).Count;

            if (incomingCount == 0 && outgoingCount == 0)
            {
                warnings.Add(new GraphValidationWarning
                {
                    Code = "ORPHANED_NODE",
                    Message = $"Node '{node.Id}' has no incoming or outgoing edges",
                    NodeId = node.Id
                });
            }
        }
    }

    private static void ValidateHandlerNames(GraphDefinition graph, List<GraphValidationWarning> warnings)
    {
        foreach (var node in graph.Nodes.Where(n => n.Type == NodeType.Handler || n.Type == NodeType.Router))
        {
            if (string.IsNullOrWhiteSpace(node.HandlerName))
            {
                warnings.Add(new GraphValidationWarning
                {
                    Code = "MISSING_HANDLER_NAME",
                    Message = $"Node '{node.Id}' is type {node.Type} but has no handler name",
                    NodeId = node.Id
                });
            }
        }
    }

    private static void ValidateDefaultEdges(GraphDefinition graph, List<GraphValidationError> errors)
    {
        // Ensure only one default edge per source node
        var defaultEdgesBySource = graph.Edges
            .Where(e => e.Condition?.Type == ConditionType.Default)
            .GroupBy(e => e.From);

        foreach (var group in defaultEdgesBySource)
        {
            if (group.Count() > 1)
            {
                errors.Add(new GraphValidationError
                {
                    Code = "MULTIPLE_DEFAULT_EDGES",
                    Message = $"Node '{group.Key}' has {group.Count()} default edges. Only one default edge per source node is allowed.",
                    NodeId = group.Key
                });
            }
        }
    }

    private static void ValidateMapNodes(GraphDefinition graph, List<GraphValidationError> errors, List<GraphValidationWarning> warnings)
    {
        foreach (var node in graph.Nodes.Where(n => n.Type == NodeType.Map))
        {
            // Rule 1: MapProcessorGraph and MapProcessorGraphs are mutually exclusive
            if (node.MapProcessorGraph != null && node.MapProcessorGraphs != null)
            {
                errors.Add(new GraphValidationError
                {
                    Code = "MAP_CONFLICTING_PROCESSORS",
                    Message = $"Map node '{node.Id}' cannot have both MapProcessorGraph and MapProcessorGraphs. Use MapProcessorGraph for homogeneous or MapProcessorGraphs for heterogeneous mapping.",
                    NodeId = node.Id
                });
            }

            // Rule 2: MapProcessorGraphs requires MapRouterName
            if (node.MapProcessorGraphs != null && string.IsNullOrWhiteSpace(node.MapRouterName))
            {
                errors.Add(new GraphValidationError
                {
                    Code = "MAP_MISSING_ROUTER",
                    Message = $"Map node '{node.Id}' has MapProcessorGraphs but no MapRouterName. MapRouterName is required for heterogeneous mapping.",
                    NodeId = node.Id
                });
            }

            // Rule 3: At least one processor must be specified
            if (node.MapProcessorGraph == null &&
                node.MapProcessorGraphRef == null &&
                node.MapProcessorGraphs == null)
            {
                errors.Add(new GraphValidationError
                {
                    Code = "MAP_NO_PROCESSOR",
                    Message = $"Map node '{node.Id}' must have MapProcessorGraph, MapProcessorGraphRef, or MapProcessorGraphs",
                    NodeId = node.Id
                });
            }

            // Rule 4: Validate single processor graph if provided
            if (node.MapProcessorGraph != null)
            {
                var processorValidation = Validate(node.MapProcessorGraph);
                if (!processorValidation.IsValid)
                {
                    foreach (var error in processorValidation.Errors)
                    {
                        errors.Add(new GraphValidationError
                        {
                            Code = "MAP_INVALID_PROCESSOR",
                            Message = $"Map node '{node.Id}' has invalid processor graph: {error.Message}",
                            NodeId = node.Id
                        });
                    }
                }
            }

            // Rule 5: Validate all processor graphs in dictionary
            if (node.MapProcessorGraphs != null)
            {
                foreach (var kvp in node.MapProcessorGraphs)
                {
                    var processorValidation = Validate(kvp.Value);
                    if (!processorValidation.IsValid)
                    {
                        foreach (var error in processorValidation.Errors)
                        {
                            errors.Add(new GraphValidationError
                            {
                                Code = "MAP_INVALID_PROCESSOR_GRAPH",
                                Message = $"Map node '{node.Id}' processor graph '{kvp.Key}' is invalid: {error.Message}",
                                NodeId = node.Id
                            });
                        }
                    }
                }

                // Warn if dictionary is empty
                if (node.MapProcessorGraphs.Count == 0)
                {
                    warnings.Add(new GraphValidationWarning
                    {
                        Code = "MAP_EMPTY_PROCESSORS",
                        Message = $"Map node '{node.Id}' has empty MapProcessorGraphs dictionary",
                        NodeId = node.Id
                    });
                }
            }

            // Rule 6: Validate default graph if specified
            if (node.MapDefaultGraph != null)
            {
                var defaultValidation = Validate(node.MapDefaultGraph);
                if (!defaultValidation.IsValid)
                {
                    foreach (var error in defaultValidation.Errors)
                    {
                        errors.Add(new GraphValidationError
                        {
                            Code = "MAP_INVALID_DEFAULT_GRAPH",
                            Message = $"Map node '{node.Id}' default graph is invalid: {error.Message}",
                            NodeId = node.Id
                        });
                    }
                }
            }

            // Rule 7: MaxParallelMapTasks validation
            if (node.MaxParallelMapTasks.HasValue)
            {
                if (node.MaxParallelMapTasks < 0)
                {
                    errors.Add(new GraphValidationError
                    {
                        Code = "MAP_INVALID_CONCURRENCY",
                        Message = $"Map node '{node.Id}' MaxParallelMapTasks cannot be negative (value: {node.MaxParallelMapTasks})",
                        NodeId = node.Id
                    });
                }
                else if (node.MaxParallelMapTasks > 1000)
                {
                    warnings.Add(new GraphValidationWarning
                    {
                        Code = "MAP_HIGH_CONCURRENCY",
                        Message = $"Map node '{node.Id}' has very high MaxParallelMapTasks ({node.MaxParallelMapTasks}). Consider using a lower value to prevent resource exhaustion.",
                        NodeId = node.Id
                    });
                }
            }

            // Warning: MapRouterName without MapProcessorGraphs
            if (!string.IsNullOrWhiteSpace(node.MapRouterName) && node.MapProcessorGraphs == null)
            {
                warnings.Add(new GraphValidationWarning
                {
                    Code = "MAP_ROUTER_IGNORED",
                    Message = $"Map node '{node.Id}' has MapRouterName '{node.MapRouterName}' but no MapProcessorGraphs. MapRouterName is ignored in homogeneous maps.",
                    NodeId = node.Id
                });
            }

            // Warning: MapDefaultGraph without MapProcessorGraphs
            if (node.MapDefaultGraph != null && node.MapProcessorGraphs == null)
            {
                warnings.Add(new GraphValidationWarning
                {
                    Code = "MAP_DEFAULT_IGNORED",
                    Message = $"Map node '{node.Id}' has MapDefaultGraph but no MapProcessorGraphs. MapDefaultGraph is ignored in homogeneous maps.",
                    NodeId = node.Id
                });
            }

            // Warning: Map node with HandlerName (should be null)
            if (!string.IsNullOrEmpty(node.HandlerName))
            {
                warnings.Add(new GraphValidationWarning
                {
                    Code = "MAP_HAS_HANDLER",
                    Message = $"Map node '{node.Id}' has HandlerName '{node.HandlerName}' which will be ignored. Map nodes use MapProcessorGraph/MapProcessorGraphs instead.",
                    NodeId = node.Id
                });
            }
        }
    }

    private static void ValidatePortRouting(GraphDefinition graph, List<GraphValidationError> errors, List<GraphValidationWarning> warnings)
    {
        foreach (var node in graph.Nodes)
        {
            // Rule 1: OutputPortCount must be positive
            if (node.OutputPortCount < 1)
            {
                errors.Add(new GraphValidationError
                {
                    Code = "INVALID_PORT_COUNT",
                    Message = $"Node '{node.Id}' has OutputPortCount={node.OutputPortCount}. Must be at least 1.",
                    NodeId = node.Id
                });
            }

            // Rule 2: OutputPortCount > 100 warning (likely misconfiguration)
            if (node.OutputPortCount > 100)
            {
                warnings.Add(new GraphValidationWarning
                {
                    Code = "HIGH_PORT_COUNT",
                    Message = $"Node '{node.Id}' has OutputPortCount={node.OutputPortCount}. This is unusually high - verify this is intentional.",
                    NodeId = node.Id
                });
            }

            // Rule 3: Validate outgoing edges reference valid ports
            foreach (var edge in graph.GetOutgoingEdges(node.Id))
            {
                var fromPort = edge.FromPort ?? 0; // Default to port 0

                if (fromPort < 0)
                {
                    errors.Add(new GraphValidationError
                    {
                        Code = "NEGATIVE_FROM_PORT",
                        Message = $"Edge from '{edge.From}' to '{edge.To}' has negative FromPort={fromPort}. Port numbers must be non-negative.",
                        NodeId = edge.From,
                        EdgeId = $"{edge.From}->{edge.To}"
                    });
                }
                else if (fromPort >= node.OutputPortCount)
                {
                    errors.Add(new GraphValidationError
                    {
                        Code = "INVALID_FROM_PORT",
                        Message = $"Edge from '{edge.From}' to '{edge.To}' references FromPort={fromPort}, but node only has {node.OutputPortCount} output port(s) (0-{node.OutputPortCount - 1}).",
                        NodeId = edge.From,
                        EdgeId = $"{edge.From}->{edge.To}"
                    });
                }
            }

            // Rule 4: Warn if multi-output node has no edges on some ports
            if (node.OutputPortCount > 1)
            {
                var usedPorts = new HashSet<int>();
                foreach (var edge in graph.GetOutgoingEdges(node.Id))
                {
                    usedPorts.Add(edge.FromPort ?? 0);
                }

                var unusedPorts = Enumerable.Range(0, node.OutputPortCount)
                    .Where(p => !usedPorts.Contains(p))
                    .ToList();

                if (unusedPorts.Any())
                {
                    warnings.Add(new GraphValidationWarning
                    {
                        Code = "UNUSED_OUTPUT_PORTS",
                        Message = $"Node '{node.Id}' has {node.OutputPortCount} output ports but port(s) [{string.Join(", ", unusedPorts)}] have no outgoing edges. Data sent to these ports will be dropped.",
                        NodeId = node.Id
                    });
                }
            }

            // Rule 5: Validate ToPort is non-negative (if specified)
            foreach (var edge in graph.GetIncomingEdges(node.Id))
            {
                if (edge.ToPort.HasValue && edge.ToPort.Value < 0)
                {
                    errors.Add(new GraphValidationError
                    {
                        Code = "NEGATIVE_TO_PORT",
                        Message = $"Edge from '{edge.From}' to '{edge.To}' has negative ToPort={edge.ToPort.Value}. Port numbers must be non-negative.",
                        NodeId = edge.To,
                        EdgeId = $"{edge.From}->{edge.To}"
                    });
                }

                // Note: We don't validate ToPort against a max since multi-input isn't implemented yet.
                // When implemented, we'd check against Node.InputPortCount (future property).
            }

            // Rule 6: Warn about explicit port on single-output nodes (redundant)
            if (node.OutputPortCount == 1)
            {
                var explicitPort0Edges = graph.GetOutgoingEdges(node.Id)
                    .Where(e => e.FromPort.HasValue && e.FromPort.Value == 0)
                    .ToList();

                if (explicitPort0Edges.Any())
                {
                    warnings.Add(new GraphValidationWarning
                    {
                        Code = "REDUNDANT_PORT_0",
                        Message = $"Node '{node.Id}' has OutputPortCount=1 (single output), but {explicitPort0Edges.Count} edge(s) explicitly specify FromPort=0. This is redundant - omit FromPort for single-output nodes.",
                        NodeId = node.Id
                    });
                }
            }
        }

        // Rule 7: Validate Priority is non-negative
        foreach (var edge in graph.Edges)
        {
            if (edge.Priority.HasValue && edge.Priority.Value < 0)
            {
                errors.Add(new GraphValidationError
                {
                    Code = "NEGATIVE_PRIORITY",
                    Message = $"Edge from '{edge.From}' to '{edge.To}' has negative Priority={edge.Priority.Value}. Priority must be non-negative (lower = higher priority).",
                    EdgeId = $"{edge.From}->{edge.To}"
                });
            }
        }
    }
}
