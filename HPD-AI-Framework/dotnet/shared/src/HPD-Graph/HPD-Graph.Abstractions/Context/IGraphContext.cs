using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Channels;
using HPDAgent.Graph.Abstractions.State;

namespace HPDAgent.Graph.Abstractions.Context;

/// <summary>
/// Execution context for graph execution.
/// Carries state, data, and execution progress through the graph.
/// Generic base for domain-specific contexts.
///
/// DESIGN: Slim context - only cross-cutting concerns.
/// Data flows through HandlerInputs/NodeResult, NOT context properties.
/// </summary>
public interface IGraphContext
{
    // ========================================
    // Execution Identity
    // ========================================

    /// <summary>
    /// Unique execution identifier.
    /// </summary>
    string ExecutionId { get; }

    /// <summary>
    /// The graph being executed.
    /// </summary>
    Graph.Graph Graph { get; }

    // ========================================
    // Execution State (Mutable)
    // ========================================

    /// <summary>
    /// Current node being executed (null if not executing).
    /// </summary>
    string? CurrentNodeId { get; }

    /// <summary>
    /// Nodes that have successfully completed.
    /// </summary>
    IReadOnlySet<string> CompletedNodes { get; }

    /// <summary>
    /// When execution started.
    /// </summary>
    DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    DateTimeOffset LastUpdatedAt { get; }

    /// <summary>
    /// Whether execution is complete.
    /// </summary>
    bool IsComplete { get; }

    /// <summary>
    /// Whether execution was cancelled.
    /// </summary>
    bool IsCancelled { get; }

    // ========================================
    // State Management (Unified System)
    // ========================================

    /// <summary>
    /// Channel set for managing state with explicit update semantics.
    /// </summary>
    IGraphChannelSet Channels { get; }

    /// <summary>
    /// Managed context for execution metadata (ephemeral).
    /// </summary>
    IManagedContext Managed { get; }

    /// <summary>
    /// Get a state scope for hierarchical namespace-based state management.
    /// </summary>
    /// <param name="scopeName">Scope name (null for root scope)</param>
    IGraphStateScope GetScope(string? scopeName = null);

    // ========================================
    // Node Execution Tracking
    // ========================================

    /// <summary>
    /// Set the current node being executed.
    /// </summary>
    void SetCurrentNode(string? nodeId);

    /// <summary>
    /// Mark a node as completed.
    /// </summary>
    void MarkNodeComplete(string nodeId);

    /// <summary>
    /// Unmark a node as complete, allowing re-execution.
    /// Used during iteration when back-edge conditions trigger.
    /// </summary>
    void UnmarkNodeComplete(string nodeId);

    /// <summary>
    /// Unmark multiple nodes as complete.
    /// More efficient than calling UnmarkNodeComplete repeatedly.
    /// </summary>
    void UnmarkNodesComplete(IEnumerable<string> nodeIds);

    /// <summary>
    /// Check if a node is complete.
    /// </summary>
    bool IsNodeComplete(string nodeId);

    /// <summary>
    /// Get execution count for a node (for loop detection).
    /// </summary>
    int GetNodeExecutionCount(string nodeId);

    /// <summary>
    /// Increment execution count for a node.
    /// </summary>
    void IncrementNodeExecutionCount(string nodeId);

    // ========================================
    // Logging & Observability
    // ========================================

    /// <summary>
    /// Log entries for this execution.
    /// </summary>
    IReadOnlyList<GraphLogEntry> LogEntries { get; }

    /// <summary>
    /// Add a log entry.
    /// </summary>
    void Log(string source, string message, LogLevel level = LogLevel.Information,
             string? nodeId = null, Exception? exception = null);

    // ========================================
    // Service Resolution (DI)
    // ========================================

    /// <summary>
    /// Service provider for dependency injection.
    /// </summary>
    IServiceProvider Services { get; }

    // ========================================
    // Event Coordination
    // ========================================

    /// <summary>
    /// Event coordinator for emitting graph lifecycle events.
    /// Integrates with HPD-Events for hierarchical event streaming.
    /// Null = no event emission (default).
    /// When set, events are emitted at key lifecycle points:
    /// - Graph start/completion
    /// - Layer start/completion
    /// - Node start/completion
    /// </summary>
    HPD.Events.IEventCoordinator? EventCoordinator { get; }

    /// <summary>
    /// Waits for a response to a bidirectional event (approvals, permissions).
    /// Convenience wrapper around EventCoordinator.WaitForResponseAsync.
    /// </summary>
    /// <typeparam name="TResponse">Expected response event type (must inherit from GraphEvent)</typeparam>
    /// <param name="requestId">Unique identifier matching the request event</param>
    /// <param name="timeout">Maximum time to wait for response</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The typed response event</returns>
    /// <exception cref="InvalidOperationException">EventCoordinator is null</exception>
    /// <exception cref="TimeoutException">No response received within timeout</exception>
    /// <exception cref="OperationCanceledException">Operation was cancelled</exception>
    /// <remarks>
    /// <para>
    /// This method is used by node handlers that need bidirectional communication:
    /// </para>
    /// <list type="number">
    /// <item>Handler emits request event (e.g., NodeApprovalRequestEvent)</item>
    /// <item>Handler calls WaitForResponseAsync() - BLOCKS HERE</item>
    /// <item>External handler receives request event (via EventCoordinator.ReadAllAsync)</item>
    /// <item>User provides input</item>
    /// <item>External handler calls EventCoordinator.SendResponse()</item>
    /// <item>Handler receives response and continues</item>
    /// </list>
    /// <para><b>Timeout vs. Cancellation:</b></para>
    /// <para>
    /// - TimeoutException: No response received within the specified timeout
    /// </para>
    /// <para>
    /// - OperationCanceledException: External cancellation (e.g., user stopped graph)
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // In node handler
    /// var requestId = Guid.NewGuid().ToString();
    ///
    /// context.EventCoordinator?.Emit(new NodeApprovalRequestEvent
    /// {
    ///     RequestId = requestId,
    ///     NodeId = context.CurrentNodeId,
    ///     Message = "Approve deletion of 1000 records?"
    /// });
    ///
    /// var response = await context.WaitForResponseAsync&lt;NodeApprovalResponseEvent&gt;(
    ///     requestId,
    ///     timeout: TimeSpan.FromMinutes(5),
    ///     cancellationToken
    /// );
    ///
    /// if (!response.Approved)
    ///     return new NodeExecutionResult.Skipped(SkipReason.UserCancelled);
    /// </code>
    /// </example>
    Task<TResponse> WaitForResponseAsync<TResponse>(
        string requestId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TResponse : HPD.Events.Event
    {
        if (EventCoordinator == null)
            throw new InvalidOperationException("EventCoordinator is not configured for this context");

        return EventCoordinator.WaitForResponseAsync<TResponse>(requestId, timeout, cancellationToken);
    }

    // ========================================
    // Context Isolation (For Parallel Execution)
    // ========================================

    /// <summary>
    /// Create an isolated copy of this context for parallel execution.
    /// Shared: Immutable state (ExecutionId, Graph, Services).
    /// Cloned: Mutable collections (CompletedNodes, Channels, Logs).
    /// Reset: Current execution position (CurrentNodeId = null).
    /// </summary>
    IGraphContext CreateIsolatedCopy();

    /// <summary>
    /// Merge state from an isolated context back into this context.
    /// Uses channel semantics (Append = accumulate, LastValue = overwrite).
    /// </summary>
    void MergeFrom(IGraphContext isolatedContext);

    // ========================================
    // Shared Data (Available to All Nodes)
    // ========================================

    /// <summary>
    /// Optional shared data available to all nodes in the graph.
    /// Unlike node outputs (which flow through edges), SharedData is globally accessible.
    ///
    /// Use cases:
    /// - Original workflow input that should be available to all nodes (not just the first)
    /// - Configuration or context that multiple nodes need
    /// - Routing patterns where upstream output is for branching, not for downstream processing
    ///
    /// When SharedData is set, the orchestrator includes it in HandlerInputs for every node.
    /// Keys are prefixed with "shared." to distinguish from upstream node outputs.
    /// </summary>
    /// <example>
    /// // Set shared data at workflow start
    /// context.SharedData["input"] = userQuestion;
    /// context.SharedData["config"] = pipelineConfig;
    ///
    /// // In any handler, access via inputs
    /// var question = inputs.Get&lt;string&gt;("shared.input");
    /// </example>
    IDictionary<string, object>? SharedData { get; }

    // ========================================
    // Metadata & Tags
    // ========================================

    /// <summary>
    /// Tags for categorization and filtering.
    /// Format: key → list of values.
    /// </summary>
    IDictionary<string, List<string>> Tags { get; }

    /// <summary>
    /// Add a tag.
    /// </summary>
    void AddTag(string key, string value);

    /// <summary>
    /// Remove a tag key and all its values.
    /// </summary>
    void RemoveTag(string key);

    // ========================================
    // Progress Tracking
    // ========================================

    /// <summary>
    /// Current layer index being executed (0-based).
    /// </summary>
    int CurrentLayerIndex { get; set; }

    /// <summary>
    /// Total number of layers in the graph.
    /// </summary>
    int TotalLayers { get; }

    /// <summary>
    /// Overall progress (0.0 to 1.0).
    /// </summary>
    float Progress { get; }

    // ========================================
    // Iteration Tracking (Cyclic Graphs)
    // ========================================

    /// <summary>
    /// Current iteration index for cyclic graph execution (0-based).
    /// Returns 0 for acyclic graphs or first iteration.
    /// Increments when back-edge conditions trigger re-execution.
    /// </summary>
    int CurrentIteration { get; }

    // ========================================
    // Partitioning (Phase 2: Data Orchestration Primitives)
    // ========================================

    /// <summary>
    /// The partition key for the current execution (null if not partitioned).
    /// Set by Map execution loop when processing partition-aware items.
    /// Available to handlers via context.CurrentPartition.
    ///
    /// Example:
    ///   // In a partitioned node handler
    ///   var partition = context.CurrentPartition; // e.g., "2025-01-15"
    ///   // Process data for this specific partition
    /// </summary>
    PartitionKey? CurrentPartition { get; }

    // ========================================
    // Hierarchical Namespaces (Phase 5: Universal Workflow Primitives)
    // ========================================

    /// <summary>
    /// The current artifact namespace for the execution (null if not in a namespaced subgraph).
    /// Set by SubGraph execution when entering a node with ArtifactNamespace property.
    /// Used for automatic artifact key prefixing to prevent naming collisions.
    ///
    /// Example:
    ///   // SubGraph with namespace ["pipeline", "stage1"]
    ///   var ns = context.CurrentNamespace; // ["pipeline", "stage1"]
    ///   // Child node produces artifact "users"
    ///   // Actual registered key: ["pipeline", "stage1", "users"]
    /// </summary>
    IReadOnlyList<string>? CurrentNamespace { get; }

    // ========================================
    // Internal Execution State (Orchestrator Usage Only)
    // ========================================
    // These properties are internal to the orchestrator and should NOT be accessed by handlers.
    // They track execution-scoped state like fingerprints, failed nodes, and output hashes.
    // By storing these in the context (not the orchestrator), we achieve true statelessness.

    /// <summary>
    /// Internal: Fingerprints for nodes in the current execution.
    /// Used by incremental execution to detect changes.
    /// ORCHESTRATOR USE ONLY - Not intended for handler access.
    /// </summary>
    System.Collections.Concurrent.ConcurrentDictionary<string, string> InternalCurrentFingerprints { get; }

    /// <summary>
    /// Internal: Failed nodes in the current execution with their exceptions.
    /// Used for error propagation and diagnostics.
    /// ORCHESTRATOR USE ONLY - Not intended for handler access.
    /// </summary>
    System.Collections.Concurrent.ConcurrentDictionary<string, Exception> InternalFailedNodes { get; }

    /// <summary>
    /// Internal: Output hashes for change-aware iteration.
    /// Stores hash of each node's outputs to detect changes between iterations.
    /// ORCHESTRATOR USE ONLY - Not intended for handler access.
    /// </summary>
    System.Collections.Concurrent.ConcurrentDictionary<string, string> InternalOutputHashes { get; }

    /// <summary>
    /// Internal: Track which edges have consumed outputs (for lazy cloning).
    /// Key: "nodeId:port", Value: HashSet of edge IDs that consumed this output.
    /// ORCHESTRATOR USE ONLY - Not intended for handler access.
    /// </summary>
    System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>> InternalConsumedOutputs { get; }
}

/// <summary>
/// Log entry for graph execution.
/// </summary>
public sealed record GraphLogEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Source { get; init; }
    public required LogLevel Level { get; init; }
    public required string Message { get; init; }
    public string? NodeId { get; init; }
    public Exception? Exception { get; init; }
}

/// <summary>
/// Log level.
/// </summary>
public enum LogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}
