using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HPD.Events;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Caching;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Abstractions.Orchestration;
using HPDAgent.Graph.Core.Artifacts;
using HPDAgent.Graph.Core.Channels;
using HPDAgent.Graph.Core.State;
using HPDAgent.Graph.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace HPDAgent.Graph.Core.Orchestration;

/// <summary>
/// Core implementation of graph orchestrator.
/// Executes nodes in topological order with automatic parallelism.
/// Supports optional content-addressable caching for cost savings.
/// </summary>
public class GraphOrchestrator<TContext> : IGraphOrchestrator<TContext>
    where TContext : class, IGraphContext
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INodeCacheStore? _cacheStore;
    private readonly INodeFingerprintCalculator? _fingerprintCalculator;
    private readonly Abstractions.Checkpointing.IGraphCheckpointStore? _checkpointStore;
    private readonly IAffectedNodeDetector? _affectedNodeDetector;
    private readonly IGraphSnapshotStore? _snapshotStore;
    private readonly IArtifactRegistry? _artifactRegistry;

    // Default suspension options for nodes that don't specify their own
    private readonly Abstractions.Execution.SuspensionOptions _defaultSuspensionOptions;

    // Artifact index for O(1) producer lookups (built at graph initialization)
    private readonly ArtifactIndex? _artifactIndex;

    // Graph registry for multi-graph scenarios
    // Enables stateless orchestration with explicit graph references
    // Orchestrator is now TRULY STATELESS - all execution state lives in context!
    private readonly Abstractions.Registry.IGraphRegistry? _graphRegistry;

    // Partition snapshot cache: Cache snapshots per execution to avoid repeated resolution
    // Key = node ID + serialized partition definition, Value = resolved snapshot
    // Cleared per execution (not shared across executions to avoid stale data)
    private readonly ConcurrentDictionary<string, PartitionSnapshot> _partitionSnapshotCache = new();

    // Maximum concurrent nodes per layer (prevents thread pool starvation)
    // Defaults to 4x CPU cores, can be overridden via constructor
    private readonly int _maxLayerConcurrency;

    /// <summary>
    /// Initializes a new instance of the GraphOrchestrator.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency injection.</param>
    /// <param name="cacheStore">Optional cache store for content-addressable caching.</param>
    /// <param name="fingerprintCalculator">Optional fingerprint calculator for cache keys.</param>
    /// <param name="checkpointStore">Optional checkpoint store for durability.</param>
    /// <param name="defaultSuspensionOptions">Default suspension options for nodes.</param>
    /// <param name="affectedNodeDetector">Optional detector for incremental execution.</param>
    /// <param name="snapshotStore">Optional snapshot store for incremental execution.</param>
    /// <param name="artifactRegistry">Optional artifact registry for data orchestration (Phase 1).</param>
    /// <param name="graphRegistry">Optional graph registry for multi-graph scenarios.</param>
    /// <param name="maxLayerConcurrency">Maximum concurrent nodes per layer. Defaults to 4x CPU cores if not specified.</param>

    public GraphOrchestrator(
        IServiceProvider serviceProvider,
        INodeCacheStore? cacheStore = null,
        INodeFingerprintCalculator? fingerprintCalculator = null,
        Abstractions.Checkpointing.IGraphCheckpointStore? checkpointStore = null,
        Abstractions.Execution.SuspensionOptions? defaultSuspensionOptions = null,
        IAffectedNodeDetector? affectedNodeDetector = null,
        IGraphSnapshotStore? snapshotStore = null,
        IArtifactRegistry? artifactRegistry = null,
        Abstractions.Registry.IGraphRegistry? graphRegistry = null,
        int? maxLayerConcurrency = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _cacheStore = cacheStore;
        _fingerprintCalculator = fingerprintCalculator;
        _checkpointStore = checkpointStore;
        _affectedNodeDetector = affectedNodeDetector;
        _snapshotStore = snapshotStore;
        _artifactRegistry = artifactRegistry;
        _graphRegistry = graphRegistry;
        _defaultSuspensionOptions = defaultSuspensionOptions ?? Abstractions.Execution.SuspensionOptions.Default;
        _maxLayerConcurrency = maxLayerConcurrency ?? (Environment.ProcessorCount * 4);

        // Build artifact index if registry is provided
        _artifactIndex = artifactRegistry != null ? new ArtifactIndex() : null;
    }

    public async Task<TContext> ExecuteAsync(TContext context, CancellationToken cancellationToken = default)
    {
        context.Log("Orchestrator", $"Starting graph execution: {context.Graph.Name}", LogLevel.Information);

        var executionStartTime = DateTimeOffset.UtcNow;

        // Clear partition snapshot cache at the start of each execution to ensure isolation
        // This maintains statelessness across executions
        // Only clear if cache has entries (avoid overhead when no partitions are used)
        if (_partitionSnapshotCache.Count > 0)
        {
            _partitionSnapshotCache.Clear();
        }

        // Build artifact index if artifact registry is enabled
        if (_artifactIndex != null)
        {
            _artifactIndex.BuildIndex(context.Graph);
            context.Log("Orchestrator",
                $"Built artifact index: {_artifactIndex.ArtifactCount} artifacts declared",
                LogLevel.Debug);
        }

        try
        {
            // Detect back-edges to determine execution mode
            var backEdges = context.Graph.GetBackEdges();

            if (backEdges.Count > 0)
            {
                context.Log("Orchestrator",
                    $"Graph has {backEdges.Count} back-edge(s), enabling iteration mode",
                    LogLevel.Information);

                return await ExecuteIterativeAsync(context, backEdges, executionStartTime, cancellationToken);
            }
            else
            {
                // Fast path: no back-edges, use original single-pass execution
                return await ExecuteAcyclicAsync(context, executionStartTime, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            HandleCancellation(context, executionStartTime);
            throw;
        }
        catch (Exception ex)
        {
            HandleException(context, ex, executionStartTime);
            throw;
        }
    }

    /// <summary>
    /// Original single-pass execution for acyclic graphs (DAGs).
    /// Zero overhead - identical to previous implementation.
    /// </summary>
    private async Task<TContext> ExecuteAcyclicAsync(
        TContext context,
        DateTimeOffset executionStartTime,
        CancellationToken cancellationToken)
    {
        // Check for incremental execution support
        HashSet<string>? dirtyNodes = null;
        if (_affectedNodeDetector != null && _snapshotStore != null)
        {
            try
            {
                // Load previous snapshot
                var previousSnapshot = await _snapshotStore.GetLatestSnapshotAsync(
                    context.Graph.Id,
                    cancellationToken);

                // Compute affected nodes
                var affected = await _affectedNodeDetector.GetAffectedNodesAsync(
                    previousSnapshot,
                    context.Graph,
                    GetCurrentInputs(context),
                    _serviceProvider,
                    cancellationToken);

                if (previousSnapshot == null)
                {
                    context.Log("Orchestrator",
                        $"No previous snapshot found - executing all {affected.Count} nodes",
                        LogLevel.Information);
                }
                else
                {
                    var totalNodes = context.Graph.Nodes.Count;
                    var skippedCount = totalNodes - affected.Count;
                    var percentSkipped = totalNodes > 0 ? (skippedCount * 100.0 / totalNodes) : 0;
                    context.Log("Orchestrator",
                        $"Incremental execution: {affected.Count} affected nodes, {skippedCount} cached ({percentSkipped:F1}% skip rate)",
                        LogLevel.Information);
                }

                dirtyNodes = affected;
            }
            catch (Exception ex)
            {
                // If snapshot loading fails, fall back to full execution
                context.Log("Orchestrator",
                    $"Failed to load snapshot for incremental execution: {ex.Message}. Falling back to full execution.",
                    LogLevel.Warning,
                    exception: ex);
                // dirtyNodes remains null, so we'll execute all nodes below
            }
        }

        // Fast exit: if incremental execution determined no nodes are dirty, skip all work.
        if (dirtyNodes != null && dirtyNodes.Count == 0)
        {
            FinalizeExecution(context, executionStartTime, totalIterations: 1);
            return context;
        }

        // Get execution layers (topological sort)
        var layers = context.Graph.GetExecutionLayers();

        if (context is Context.GraphContext graphContext)
        {
            graphContext.SetTotalLayers(layers.Count);
        }

        context.Log("Orchestrator", $"Graph has {layers.Count} execution layers", LogLevel.Debug);

        // Emit graph started event
        EmitGraphStartedEvent(context, layers.Count);

        // Execute each layer (skip nodes not in dirtyNodes if incremental execution is active)
        for (int i = 0; i < layers.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var layer = layers[i];
            context.CurrentLayerIndex = i;

            // Filter layer nodes if using incremental execution
            var nodesToExecute = layer.NodeIds;
            if (dirtyNodes != null)
            {
                nodesToExecute = layer.NodeIds.Where(nodeId => dirtyNodes.Contains(nodeId)).ToList();
            }

            if (nodesToExecute.Count == 0)
            {
                // Skip empty layers
                continue;
            }

            context.Log("Orchestrator", $"Executing layer {i} with {nodesToExecute.Count} node(s)", LogLevel.Debug);

            var filteredLayer = new Abstractions.Orchestration.ExecutionLayer { Level = i, NodeIds = nodesToExecute };
            await ExecuteLayerAsync(context, filteredLayer, cancellationToken);

            // Update managed context
            if (context.Managed is ManagedContext managed)
            {
                managed.IncrementStep();
            }

            // Save checkpoint after layer completion (fire-and-forget, non-blocking)
            if (_checkpointStore != null && context is Context.GraphContext ctxGraph)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SaveCheckpointAsync(ctxGraph, i, Abstractions.Checkpointing.CheckpointTrigger.LayerCompleted, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        // Log but don't fail execution
                        context.Log("Orchestrator", $"Failed to save checkpoint: {ex.Message}", LogLevel.Warning, exception: ex);
                    }
                });
            }
        }

        // Save snapshot for next incremental execution.
        // Skip if no nodes ran (dirtyNodes was empty) — the existing snapshot is still valid
        // and saving an empty one would cause all nodes to be marked dirty on the next run.
        if (dirtyNodes == null || dirtyNodes.Count > 0)
        {
            await SaveSnapshotAsync(context, cancellationToken);
        }

        FinalizeExecution(context, executionStartTime, totalIterations: 1);
        return context;
    }

    /// <summary>
    /// Iterative execution for cyclic graphs.
    /// Evaluates back-edges after each pass and re-executes dirty nodes.
    /// Supports change-aware iteration with automatic convergence detection.
    /// </summary>
    private async Task<TContext> ExecuteIterativeAsync(
        TContext context,
        IReadOnlyList<BackEdge> backEdges,
        DateTimeOffset executionStartTime,
        CancellationToken cancellationToken)
    {
        // Use IterationOptions.MaxIterations if available, otherwise fall back to Graph.MaxIterations
        var options = context.Graph.IterationOptions;
        var maxIterations = options?.MaxIterations ?? context.Graph.MaxIterations;
        var iteration = 0;
        var converged = false;

        // Initial dirty set: Use incremental execution if available, otherwise all nodes
        HashSet<string> dirtyNodes;

        if (_affectedNodeDetector != null && _snapshotStore != null)
        {
            try
            {
                // Load previous snapshot for incremental execution
                var previousSnapshot = await _snapshotStore.GetLatestSnapshotAsync(
                    context.Graph.Id,
                    cancellationToken);

                // Compute affected nodes (reuses existing fingerprint calculator!)
                dirtyNodes = await _affectedNodeDetector.GetAffectedNodesAsync(
                    previousSnapshot,
                    context.Graph,
                    GetCurrentInputs(context),
                    _serviceProvider,
                    cancellationToken);

                if (previousSnapshot == null)
                {
                    context.Log("Orchestrator",
                        $"No previous snapshot found - executing all {dirtyNodes.Count} nodes",
                        LogLevel.Information);
                }
                else
                {
                    var totalNodes = GetExecutableNodeIds(context.Graph).Count;
                    var skippedCount = totalNodes - dirtyNodes.Count;
                    var percentSkipped = totalNodes > 0 ? (skippedCount * 100.0 / totalNodes) : 0;

                    context.Log("Orchestrator",
                        $"Incremental execution: {dirtyNodes.Count} affected nodes, {skippedCount} cached ({percentSkipped:F1}% skip rate)",
                        LogLevel.Information);
                }
            }
            catch (Exception ex)
            {
                // If snapshot loading fails, fall back to full execution
                context.Log("Orchestrator",
                    $"Failed to load snapshot for incremental execution: {ex.Message}. Falling back to full execution.",
                    LogLevel.Warning,
                    exception: ex);
                dirtyNodes = GetExecutableNodeIds(context.Graph);
            }
        }
        else
        {
            // Fallback to full execution (no incremental support)
            dirtyNodes = GetExecutableNodeIds(context.Graph);

            if (_affectedNodeDetector == null && _snapshotStore == null)
            {
                context.Log("Orchestrator",
                    "Incremental execution disabled (no AffectedNodeDetector or SnapshotStore configured)",
                    LogLevel.Debug);
            }
        }

        // Emit graph started event
        EmitGraphStartedEvent(context, layerCount: 0); // Updated per-iteration

        while (dirtyNodes.Count > 0 && iteration < maxIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Execute one iteration
            var iterationResult = await ExecuteIterationAsync(
                context,
                dirtyNodes,
                backEdges,
                iteration,
                cancellationToken);

            dirtyNodes = iterationResult.NextDirtyNodes;

            // Check if graph converged (all outputs unchanged)
            if (iterationResult.Converged)
            {
                converged = true;
                break;
            }

            if (dirtyNodes.Count > 0)
            {
                iteration++;

                if (context is Context.GraphContext gc)
                {
                    gc.IncrementIteration();
                }
            }
        }

        // Handle max iterations reached (only if not converged)
        if (!converged && iteration >= maxIterations && dirtyNodes.Count > 0)
        {
            HandleMaxIterationsReached(context, maxIterations, dirtyNodes, backEdges);
        }

        // Save snapshot for next incremental execution
        await SaveSnapshotAsync(context, cancellationToken);

        FinalizeExecution(context, executionStartTime, iteration + 1);
        return context;
    }

    /// <summary>
    /// Executes a single iteration of the graph.
    /// Returns information about which nodes need re-execution.
    /// </summary>
    private async Task<IterationResult> ExecuteIterationAsync(
        TContext context,
        HashSet<string> dirtyNodes,
        IReadOnlyList<BackEdge> backEdges,
        int iteration,
        CancellationToken cancellationToken)
    {
        var iterationStart = DateTimeOffset.UtcNow;
        var options = context.Graph.IterationOptions;

        // Snapshot output hashes before execution (for convergence detection)
        var preIterationHashes = context.InternalOutputHashes.ToDictionary(x => x.Key, x => x.Value);

        // Clear ephemeral channels between iterations (not on first)
        if (iteration > 0)
        {
            ClearEphemeralChannels(context);
        }

        // Invalidate output channels for dirty nodes
        InvalidateOutputChannels(context, dirtyNodes);

        // Compute layers from dirty nodes only
        var layers = ComputeLayersFromDirtyNodes(dirtyNodes, context.Graph, backEdges);

        if (context is Context.GraphContext gc)
        {
            gc.SetTotalLayers(layers.Count);
        }

        // Emit iteration started event
        context.EventCoordinator?.Emit(new IterationStartedEvent
        {
            IterationIndex = iteration,
            DirtyNodeCount = dirtyNodes.Count,
            DirtyNodeIds = dirtyNodes.ToList(),
            LayerCount = layers.Count,
            GraphContext = CreateGraphExecutionContext(context)
        });

        context.Log("Orchestrator",
            $"Iteration {iteration}: {layers.Count} layer(s), {dirtyNodes.Count} node(s)",
            LogLevel.Debug);

        // Execute all layers
        for (int i = 0; i < layers.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.CurrentLayerIndex = i;
            await ExecuteLayerAsync(context, layers[i], cancellationToken);

            // Update managed context
            if (context.Managed is ManagedContext managed)
            {
                managed.IncrementStep();
            }
        }

        //  CRITICAL SYNCHRONIZATION POINT
        // Wait for all polling nodes to reach terminal state before evaluating back-edges
        // This prevents premature convergence while nodes are still polling
        await WaitForPollingNodesToResolveAsync(context, cancellationToken);

        var executedNodes = new HashSet<string>(dirtyNodes);

        // Check for convergence (when enabled) - do this BEFORE evaluating back-edges
        // Now safe - all nodes in terminal state after polling synchronization
        if (options?.UseChangeAwareIteration == true &&
            options?.EnableAutoConvergence == true &&
            HasConverged(preIterationHashes, executedNodes, context))
        {
            context.Log("Orchestrator",
                $"Graph converged at iteration {iteration}: no output changes detected",
                LogLevel.Information);

            context.EventCoordinator?.Emit(new GraphConvergedEvent
            {
                IterationIndex = iteration,
                TotalIterations = iteration + 1,
                ConvergenceReason = "all_outputs_unchanged",
                GraphContext = CreateGraphExecutionContext(context)
            });

            // Emit iteration completed event (with convergence)
            context.EventCoordinator?.Emit(new IterationCompletedEvent
            {
                IterationIndex = iteration,
                Duration = DateTimeOffset.UtcNow - iterationStart,
                ExecutedNodes = executedNodes.ToList(),
                BackEdgesTriggered = 0,
                NodesToReExecute = new List<string>(),
                GraphContext = CreateGraphExecutionContext(context)
            });

            return new IterationResult
            {
                ExecutedNodes = executedNodes,
                TriggeredBackEdges = new List<BackEdge>(),
                NextDirtyNodes = new HashSet<string>(),
                Converged = true
            };
        }

        // Evaluate back-edges to find triggered nodes
        // Pass pre-iteration hashes for change detection
        var (triggeredNodes, triggeredBackEdges) =
            EvaluateBackEdges(context, backEdges, iteration, preIterationHashes);

        // Compute next dirty set
        HashSet<string> nextDirtyNodes;
        if (triggeredNodes.Count > 0)
        {
            // Use change-aware or eager propagation based on options
            if (options?.UseChangeAwareIteration == true)
            {
                nextDirtyNodes = ComputeNextDirtyNodes(context, triggeredNodes, backEdges, iteration);

                context.Log("Orchestrator",
                    $"Back-edges triggered: [{string.Join(", ", triggeredNodes)}] → " +
                    $"re-executing {nextDirtyNodes.Count} node(s) (change-aware)",
                    LogLevel.Information);
            }
            else
            {
                // Legacy behavior: eager propagation
                nextDirtyNodes = GetAllForwardDependents(triggeredNodes, context.Graph, backEdges);

                context.Log("Orchestrator",
                    $"Back-edges triggered: [{string.Join(", ", triggeredNodes)}] → " +
                    $"re-executing {nextDirtyNodes.Count} node(s)",
                    LogLevel.Information);
            }

            // Un-mark dirty nodes as complete so they can re-execute
            context.UnmarkNodesComplete(nextDirtyNodes);
        }
        else
        {
            nextDirtyNodes = new HashSet<string>();
        }

        // Emit iteration completed event
        context.EventCoordinator?.Emit(new IterationCompletedEvent
        {
            IterationIndex = iteration,
            Duration = DateTimeOffset.UtcNow - iterationStart,
            ExecutedNodes = executedNodes.ToList(),
            BackEdgesTriggered = triggeredBackEdges.Count,
            NodesToReExecute = nextDirtyNodes.ToList(),
            GraphContext = CreateGraphExecutionContext(context)
        });

        // Save checkpoint after iteration (fire-and-forget)
        if (_checkpointStore != null && context is Context.GraphContext ctxGraph)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await SaveIterationCheckpointAsync(ctxGraph, iteration, nextDirtyNodes, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    context.Log("Orchestrator", $"Failed to save iteration checkpoint: {ex.Message}", LogLevel.Warning, exception: ex);
                }
            });
        }

        return new IterationResult
        {
            ExecutedNodes = executedNodes,
            TriggeredBackEdges = triggeredBackEdges,
            NextDirtyNodes = nextDirtyNodes
        };
    }

    #region Back-Edge Evaluation

    /// <summary>
    /// Evaluates all back-edges and returns which target nodes should be re-executed.
    /// When change-aware iteration is enabled, skips back-edges whose source output hasn't changed.
    /// </summary>
    /// <param name="preIterationHashes">Hash snapshot from BEFORE the iteration executed (for change detection)</param>
    private (HashSet<string> TriggeredNodes, List<BackEdge> TriggeredEdges) EvaluateBackEdges(
        TContext context,
        IReadOnlyList<BackEdge> backEdges,
        int iteration,
        Dictionary<string, string>? preIterationHashes = null)
    {
        var triggeredNodes = new HashSet<string>();
        var triggeredEdges = new List<BackEdge>();
        var options = context.Graph.IterationOptions;
        var excludeFields = options?.IgnoreFieldsForChangeDetection;
        var algorithm = options?.HashAlgorithm ?? Abstractions.Graph.OutputHashAlgorithm.XxHash64;

        foreach (var backEdge in backEdges)
        {
            var channelName = $"node_output:{backEdge.SourceNodeId}";

            if (!context.Channels.Contains(channelName))
                continue;

            var outputs = context.Channels[channelName].Get<Dictionary<string, object>>();

            if (outputs == null)
                continue;

            // Check if source output actually changed (when change-aware iteration enabled)
            if (options?.UseChangeAwareIteration == true && preIterationHashes != null)
            {
                // Compare current output hash against PRE-iteration hash
                var currentHash = ComputeOutputHash(outputs, excludeFields, algorithm);
                preIterationHashes.TryGetValue(backEdge.SourceNodeId, out var previousHash);

                // If hash is same as before iteration, output didn't change
                if (currentHash == previousHash)
                {
                    context.Log("Orchestrator",
                        $"Back-edge {backEdge.SourceNodeId}→{backEdge.TargetNodeId} skipped: output unchanged",
                        LogLevel.Debug);

                    // Emit skip event for observability
                    context.EventCoordinator?.Emit(new BackEdgeSkippedEvent
                    {
                        SourceNodeId = backEdge.SourceNodeId,
                        TargetNodeId = backEdge.TargetNodeId,
                        Reason = "output_unchanged",
                        IterationIndex = iteration,
                        GraphContext = CreateGraphExecutionContext(context)
                    });

                    continue;
                }
            }

            // Evaluate condition (null condition = unconditional back-edge)
            var conditionMet = backEdge.Condition == null ||
                ConditionEvaluator.Evaluate(backEdge.Condition, outputs, context, backEdge.Edge);

            if (conditionMet)
            {
                triggeredNodes.Add(backEdge.TargetNodeId);
                triggeredEdges.Add(backEdge);

                // Emit back-edge triggered event
                context.EventCoordinator?.Emit(new BackEdgeTriggeredEvent
                {
                    SourceNodeId = backEdge.SourceNodeId,
                    TargetNodeId = backEdge.TargetNodeId,
                    ConditionDescription = backEdge.Condition?.GetDescription(),
                    TriggerValue = GetTriggerValue(backEdge.Condition, outputs),
                    IterationIndex = iteration,
                    GraphContext = CreateGraphExecutionContext(context)
                });

                context.Log("Orchestrator",
                    $"Back-edge triggered: {backEdge.SourceNodeId} → {backEdge.TargetNodeId}",
                    LogLevel.Debug);
            }
        }

        return (triggeredNodes, triggeredEdges);
    }

    private static object? GetTriggerValue(EdgeCondition? condition, Dictionary<string, object> outputs)
    {
        if (condition?.Field == null)
            return null;

        outputs.TryGetValue(condition.Field, out var value);
        return value;
    }

    #endregion

    #region Dependency Propagation

    /// <summary>
    /// Gets all nodes that depend on the triggered nodes (forward edges only).
    /// Back-edges are excluded to prevent infinite propagation.
    /// </summary>
    private static HashSet<string> GetAllForwardDependents(
        HashSet<string> triggeredNodes,
        Abstractions.Graph.Graph graph,
        IReadOnlyList<BackEdge> backEdges)
    {
        var backEdgeSet = backEdges
            .Select(b => (b.SourceNodeId, b.TargetNodeId))
            .ToHashSet();

        var allDirty = new HashSet<string>(triggeredNodes);
        var queue = new Queue<string>(triggeredNodes);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();

            foreach (var edge in graph.GetOutgoingEdges(nodeId))
            {
                // Skip back-edges (they point backwards, not forward)
                if (backEdgeSet.Contains((edge.From, edge.To)))
                    continue;

                if (allDirty.Add(edge.To))
                {
                    queue.Enqueue(edge.To);
                }
            }
        }

        return allDirty;
    }

    #endregion

    #region Layer Computation for Iteration

    /// <summary>
    /// Computes execution layers from dirty nodes only.
    /// Respects dependencies between dirty nodes, excludes back-edges.
    /// </summary>
    private static IReadOnlyList<ExecutionLayer> ComputeLayersFromDirtyNodes(
        HashSet<string> dirtyNodes,
        Abstractions.Graph.Graph graph,
        IReadOnlyList<BackEdge> backEdges)
    {
        if (dirtyNodes.Count == 0)
            return Array.Empty<ExecutionLayer>();

        var backEdgeSet = backEdges
            .Select(b => (b.SourceNodeId, b.TargetNodeId))
            .ToHashSet();

        // Initialize in-degree for dirty nodes only
        var inDegree = new Dictionary<string, int>();
        var outgoing = new Dictionary<string, List<string>>();

        foreach (var nodeId in dirtyNodes)
        {
            inDegree[nodeId] = 0;
            outgoing[nodeId] = new List<string>();
        }

        // Count in-degrees (forward edges only, between dirty nodes only)
        foreach (var edge in graph.Edges)
        {
            // Skip back-edges
            if (backEdgeSet.Contains((edge.From, edge.To)))
                continue;

            // Only consider edges between dirty nodes
            if (!dirtyNodes.Contains(edge.From) || !dirtyNodes.Contains(edge.To))
                continue;

            inDegree[edge.To]++;
            outgoing[edge.From].Add(edge.To);
        }

        // Kahn's algorithm
        var layers = new List<ExecutionLayer>();
        var level = 0;

        while (inDegree.Count > 0)
        {
            var ready = inDegree
                .Where(kvp => kvp.Value == 0)
                .Select(kvp => kvp.Key)
                .ToList();

            if (ready.Count == 0)
            {
                // Remaining nodes form a cycle among themselves
                // (shouldn't happen if back-edges correctly identified)
                break;
            }

            layers.Add(new ExecutionLayer
            {
                Level = level++,
                NodeIds = ready
            });

            foreach (var nodeId in ready)
            {
                inDegree.Remove(nodeId);

                foreach (var dep in outgoing[nodeId])
                {
                    if (inDegree.ContainsKey(dep))
                    {
                        inDegree[dep]--;
                    }
                }
            }
        }

        return layers;
    }

    #endregion

    #region Channel Invalidation

    /// <summary>
    /// Clears output channels for dirty nodes before re-execution.
    /// Ensures downstream nodes receive fresh outputs.
    /// </summary>
    private static void InvalidateOutputChannels(TContext context, HashSet<string> dirtyNodes)
    {
        foreach (var nodeId in dirtyNodes)
        {
            var channelName = $"node_output:{nodeId}";

            if (context.Channels.Contains(channelName))
            {
                context.Channels[channelName].Clear();
            }
        }
    }

    #endregion

    #region Output Hash Tracking (Change-Aware Iteration)

    /// <summary>
    /// Compute hash of node outputs for change detection.
    /// Uses fast hashing (XxHash64) for within-iteration comparison.
    /// </summary>
    /// <param name="outputs">Node outputs to hash</param>
    /// <param name="excludeFields">Fields to exclude (for non-deterministic values)</param>
    /// <param name="algorithm">Hashing algorithm to use</param>
    private static string ComputeOutputHash(
        Dictionary<string, object>? outputs,
        HashSet<string>? excludeFields = null,
        Abstractions.Graph.OutputHashAlgorithm algorithm = Abstractions.Graph.OutputHashAlgorithm.XxHash64)
    {
        if (outputs == null || outputs.Count == 0)
            return string.Empty;

        var builder = new System.Text.StringBuilder();
        foreach (var (key, value) in outputs.OrderBy(kv => kv.Key))
        {
            // Skip excluded fields (timestamps, request IDs, etc.)
            if (excludeFields?.Contains(key) == true)
                continue;

            builder.Append(key).Append('=');
            builder.Append(HashValue(value)).Append(';');
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(builder.ToString());

        return algorithm switch
        {
            Abstractions.Graph.OutputHashAlgorithm.SHA256 =>
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
            _ => // XxHash64 (default)
                System.IO.Hashing.XxHash64.HashToUInt64(bytes).ToString("x16")
        };
    }

    /// <summary>
    /// Hash a single value. Handles: primitives, collections, complex objects (via JSON).
    /// </summary>
    private static string HashValue(object? value)
    {
        if (value == null)
            return "null";

        // Primitive types - direct conversion
        if (value is string str)
            return str;

        if (value is int || value is long || value is double ||
            value is float || value is decimal || value is bool)
            return value.ToString() ?? "null";

        // Collections - hash each element recursively
        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            var sb = new System.Text.StringBuilder("[");
            bool first = true;
            foreach (var item in enumerable)
            {
                if (!first) sb.Append(',');
                sb.Append(HashValue(item));
                first = false;
            }
            sb.Append(']');
            return sb.ToString();
        }

        // Complex objects - JSON serialize using source-generated context
        // Note: May fail on circular references, falls back to ToString
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(
                value,
                HPDAgent.Graph.Abstractions.Serialization.GraphJsonSerializerContext.Default.Object
            );
            return json;
        }
        catch
        {
            return value.ToString() ?? "object";
        }
    }

    /// <summary>
    /// Check if node's output changed since last check.
    /// Updates stored hash and returns change status.
    /// </summary>
    private bool NodeOutputChanged(
        TContext context,
        string nodeId,
        Dictionary<string, object>? outputs,
        HashSet<string>? excludeFields = null,
        Abstractions.Graph.OutputHashAlgorithm algorithm = Abstractions.Graph.OutputHashAlgorithm.XxHash64)
    {
        var currentHash = ComputeOutputHash(outputs, excludeFields, algorithm);
        var changed = !context.InternalOutputHashes.TryGetValue(nodeId, out var previousHash)
                      || previousHash != currentHash;

        context.InternalOutputHashes[nodeId] = currentHash;
        return changed;
    }

    /// <summary>
    /// Check if any input to this node changed since last iteration.
    /// Compares current upstream output hashes with stored hashes.
    /// </summary>
    private bool HasChangedInputs(
        TContext context,
        string nodeId,
        HashSet<string>? excludeFields = null,
        Abstractions.Graph.OutputHashAlgorithm algorithm = Abstractions.Graph.OutputHashAlgorithm.XxHash64)
    {
        foreach (var edge in context.Graph.GetIncomingEdges(nodeId))
        {
            var sourceNodeId = edge.From;

            // Skip START node
            var sourceNode = context.Graph.GetNode(sourceNodeId);
            if (sourceNode?.Type == NodeType.Start)
                continue;

            // Get the hash we stored when this upstream node last executed
            if (!context.InternalOutputHashes.TryGetValue(sourceNodeId, out var storedHash))
                continue; // No previous execution, can't compare

            // Get current output from channel
            var channelName = $"node_output:{sourceNodeId}";
            if (!context.Channels.Contains(channelName))
                continue;

            var outputs = context.Channels[channelName].Get<Dictionary<string, object>>();
            var currentHash = ComputeOutputHash(outputs, excludeFields, algorithm);

            // Compare: if different, this node's input changed
            if (currentHash != storedHash)
                return true;
        }

        return false; // All inputs unchanged
    }

    /// <summary>
    /// Check if all node outputs are unchanged from previous iteration (convergence).
    /// </summary>
    private bool HasConverged(
        Dictionary<string, string> preIterationHashes,
        HashSet<string> executedNodes,
        TContext context)
    {
        var options = context.Graph.IterationOptions;
        var excludeFields = options?.IgnoreFieldsForChangeDetection;
        var algorithm = options?.HashAlgorithm ?? Abstractions.Graph.OutputHashAlgorithm.XxHash64;

        // Check all executed nodes in this iteration
        foreach (var nodeId in executedNodes)
        {
            var channelName = $"node_output:{nodeId}";
            if (!context.Channels.Contains(channelName))
                continue;

            var outputs = context.Channels[channelName].Get<Dictionary<string, object>>();
            var currentHash = ComputeOutputHash(outputs, excludeFields, algorithm);

            if (!preIterationHashes.TryGetValue(nodeId, out var previousHash)
                || previousHash != currentHash)
            {
                return false; // At least one output changed
            }
        }

        return true; // All outputs unchanged = converged
    }

    /// <summary>
    /// Compute next dirty set by checking which nodes have changed inputs.
    /// Only nodes whose upstream outputs changed are marked dirty.
    /// </summary>
    private HashSet<string> ComputeNextDirtyNodes(
        TContext context,
        HashSet<string> triggeredNodes,
        IReadOnlyList<BackEdge> backEdges,
        int iteration)
    {
        if (triggeredNodes.Count == 0)
            return new HashSet<string>();

        var options = context.Graph.IterationOptions;
        var excludeFields = options?.IgnoreFieldsForChangeDetection;
        var algorithm = options?.HashAlgorithm ?? Abstractions.Graph.OutputHashAlgorithm.XxHash64;
        var alwaysDirty = options?.AlwaysDirtyNodes ?? new HashSet<string>();

        var backEdgeSet = backEdges
            .Select(b => (b.SourceNodeId, b.TargetNodeId))
            .ToHashSet();

        var dirtyNodes = new HashSet<string>();
        var visited = new HashSet<string>();
        var queue = new Queue<string>(triggeredNodes);

        // BFS through potential candidates
        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!visited.Add(nodeId))
                continue;

            // Check if this node should be dirty
            bool shouldBeDirty =
                triggeredNodes.Contains(nodeId) ||      // Directly triggered
                alwaysDirty.Contains(nodeId) ||         // Forced dirty (escape hatch)
                HasChangedInputs(context, nodeId, excludeFields, algorithm);  // Inputs changed

            if (shouldBeDirty)
            {
                dirtyNodes.Add(nodeId);

                // Add downstream nodes as candidates (but don't mark dirty yet)
                foreach (var edge in context.Graph.GetOutgoingEdges(nodeId))
                {
                    // Skip back-edges
                    if (backEdgeSet.Contains((edge.From, edge.To)))
                        continue;

                    queue.Enqueue(edge.To);
                }
            }
            else
            {
                // Emit skip event
                context.EventCoordinator?.Emit(new NodeSkippedUnchangedEvent
                {
                    NodeId = nodeId,
                    IterationIndex = iteration,
                    Reason = "inputs_unchanged",
                    GraphContext = CreateGraphExecutionContext(context)
                });
            }
            // If not dirty, don't propagate - downstream can't be affected
        }

        return dirtyNodes;
    }

    #endregion

    #region Helper Methods

    private static HashSet<string> GetExecutableNodeIds(Abstractions.Graph.Graph graph)
    {
        return graph.Nodes
            .Where(n => n.Type is NodeType.Handler
                               or NodeType.Router
                               or NodeType.SubGraph
                               or NodeType.Map)
            .Select(n => n.Id)
            .ToHashSet();
    }

    /// <summary>
    /// Extracts current inputs from context for fingerprint calculation.
    /// Used by AffectedNodeDetector to detect input changes.
    /// </summary>
    private static HandlerInputs GetCurrentInputs(TContext context)
    {
        var inputs = new HandlerInputs();

        // Extract all channels that look like inputs (input:* pattern)
        foreach (var channelName in context.Channels.ChannelNames)
        {
            if (channelName.StartsWith("input:"))
            {
                var key = channelName.Substring("input:".Length);
                try
                {
                    var value = context.Channels[channelName].Get<object>();
                    if (value != null)
                    {
                        inputs.Add(key, value);
                    }
                }
                catch
                {
                    // Skip channels that can't be retrieved as objects
                    // (e.g., uninitialized channels)
                }
            }
        }

        return inputs;
    }

    /// <summary>
    /// Saves snapshot for incremental execution if snapshot store is configured.
    /// </summary>
    private async Task SaveSnapshotAsync(TContext context, CancellationToken cancellationToken)
    {
        if (_snapshotStore == null)
            return;

        try
        {
            // Extract partition snapshots from cache
            // Cache key format: "nodeId:TypeName" → extract nodeId
            var partitionSnapshots = _partitionSnapshotCache
                .ToDictionary(
                    kvp => kvp.Key.Split(':')[0], // Extract nodeId from "nodeId:TypeName"
                    kvp => kvp.Value
                );

            var snapshot = new GraphSnapshot
            {
                NodeFingerprints = context.InternalCurrentFingerprints.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                GraphHash = context.Graph.Id + context.Graph.Version,
                Timestamp = DateTimeOffset.UtcNow,
                ExecutionId = context.ExecutionId,
                PartitionSnapshots = partitionSnapshots
            };

            await _snapshotStore.SaveSnapshotAsync(context.Graph.Id, snapshot, cancellationToken);

            context.Log("Orchestrator",
                $"Snapshot saved: {snapshot.NodeFingerprints.Count} node fingerprints, {snapshot.PartitionSnapshots.Count} partition snapshots",
                LogLevel.Debug);
        }
        catch (Exception ex)
        {
            // Don't fail execution if snapshot save fails
            context.Log("Orchestrator",
                $"Failed to save execution snapshot: {ex.Message}",
                LogLevel.Warning,
                exception: ex);
        }
    }

    private void EmitGraphStartedEvent(TContext context, int layerCount)
    {
        context.EventCoordinator?.Emit(new GraphExecutionStartedEvent
        {
            NodeCount = context.Graph.Nodes.Count,
            LayerCount = layerCount,
            GraphContext = CreateGraphExecutionContext(context),
            Kind = EventKind.Lifecycle,
            Priority = EventPriority.Normal
        });
    }

    private void HandleMaxIterationsReached(
        TContext context,
        int maxIterations,
        HashSet<string> dirtyNodes,
        IReadOnlyList<BackEdge> backEdges)
    {
        var activeBackEdges = backEdges
            .Where(b => dirtyNodes.Contains(b.TargetNodeId))
            .Select(b => $"{b.SourceNodeId}->{b.TargetNodeId}")
            .ToList();

        context.Log("Orchestrator",
            $"Max iterations ({maxIterations}) reached. " +
            $"{dirtyNodes.Count} nodes still dirty: [{string.Join(", ", dirtyNodes)}]",
            LogLevel.Warning);

        context.EventCoordinator?.Emit(new MaxIterationsReachedEvent
        {
            MaxIterations = maxIterations,
            RemainingDirtyNodes = dirtyNodes.ToList(),
            ActiveBackEdges = activeBackEdges,
            GraphContext = CreateGraphExecutionContext(context)
        });
    }

    private void FinalizeExecution(
        TContext context,
        DateTimeOffset startTime,
        int totalIterations)
    {
        if (context is Context.GraphContext gc)
        {
            gc.MarkComplete();
        }

        context.Log("Orchestrator",
            $"Graph completed in {totalIterations} iteration(s)",
            LogLevel.Information);

        context.EventCoordinator?.Emit(new GraphExecutionCompletedEvent
        {
            Duration = DateTimeOffset.UtcNow - startTime,
            SuccessfulNodes = context.CompletedNodes.Count,
            FailedNodes = 0,
            SkippedNodes = 0,
            GraphContext = CreateGraphExecutionContext(context),
            Kind = EventKind.Lifecycle,
            Priority = EventPriority.Normal
        });
    }

    private void HandleCancellation(TContext context, DateTimeOffset executionStartTime)
    {
        if (context is Context.GraphContext gc)
        {
            gc.MarkCancelled();
        }
        context.Log("Orchestrator", "Graph execution cancelled", LogLevel.Warning);

        context.EventCoordinator?.Emit(new GraphExecutionCompletedEvent
        {
            Duration = DateTimeOffset.UtcNow - executionStartTime,
            SuccessfulNodes = context.CompletedNodes.Count,
            FailedNodes = 0,
            SkippedNodes = 0,
            GraphContext = CreateGraphExecutionContext(context),
            Kind = EventKind.Lifecycle,
            Priority = EventPriority.Normal
        });
    }

    private void HandleException(TContext context, Exception ex, DateTimeOffset executionStartTime)
    {
        context.Log("Orchestrator", $"Graph execution failed: {ex.Message}", LogLevel.Error, exception: ex);

        context.EventCoordinator?.Emit(new GraphExecutionCompletedEvent
        {
            Duration = DateTimeOffset.UtcNow - executionStartTime,
            SuccessfulNodes = context.CompletedNodes.Count,
            FailedNodes = 1,
            SkippedNodes = 0,
            GraphContext = CreateGraphExecutionContext(context),
            Kind = EventKind.Lifecycle,
            Priority = EventPriority.Normal
        });
    }

    private static GraphExecutionContext CreateGraphExecutionContext(TContext context)
    {
        return new GraphExecutionContext
        {
            GraphId = context.Graph.Id,
            TotalNodes = context.Graph.Nodes.Count,
            CompletedNodes = context.CompletedNodes.Count,
            CurrentLayer = context.CurrentLayerIndex
        };
    }

    /// <summary>
    /// Saves checkpoint with iteration state information.
    /// </summary>
    private async Task SaveIterationCheckpointAsync(
        Context.GraphContext context,
        int iteration,
        HashSet<string> pendingDirtyNodes,
        CancellationToken cancellationToken)
    {
        if (_checkpointStore == null)
            return;

        // Extract all node outputs from channels for checkpoint restoration
        var nodeOutputs = new Dictionary<string, object>();
        var nodeStateMetadata = new Dictionary<string, Abstractions.Checkpointing.NodeStateMetadata>();

        foreach (var channelName in context.Channels.ChannelNames)
        {
            if (channelName.StartsWith("node_output:"))
            {
                var nodeId = channelName.Substring("node_output:".Length);

                // Only include outputs from completed nodes
                if (context.CompletedNodes.Contains(nodeId))
                {
                    try
                    {
                        var output = context.Channels[channelName].Get<object>();
                        if (output != null)
                        {
                            nodeOutputs[channelName] = output;

                            // Capture node version for compatibility checking
                            var node = context.Graph.GetNode(nodeId);
                            if (node != null)
                            {
                                nodeStateMetadata[nodeId] = new Abstractions.Checkpointing.NodeStateMetadata
                                {
                                    NodeId = nodeId,
                                    Version = node.Version,
                                    StateJson = System.Text.Json.JsonSerializer.Serialize(
                                        output,
                                        HPDAgent.Graph.Abstractions.Serialization.GraphJsonSerializerContext.Default.DictionaryStringObject
                                    ),
                                    CapturedAt = DateTimeOffset.UtcNow
                                };
                            }
                        }
                    }
                    catch
                    {
                        // Skip channels that can't be retrieved as object
                    }
                }
            }
        }

        var checkpoint = new Abstractions.Checkpointing.GraphCheckpoint
        {
            CheckpointId = Guid.NewGuid().ToString("N"),
            ExecutionId = context.ExecutionId,
            GraphId = context.Graph.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedNodes = context.CompletedNodes,
            NodeOutputs = nodeOutputs,
            NodeStateMetadata = nodeStateMetadata,
            CurrentIteration = iteration,
            PendingDirtyNodes = pendingDirtyNodes,
            ContextJson = System.Text.Json.JsonSerializer.Serialize(
                new HPDAgent.Graph.Abstractions.Serialization.ContextMetadata
                {
                    ExecutionId = context.ExecutionId,
                    CompletedNodes = context.CompletedNodes.ToList(),
                    CurrentLayerIndex = context.CurrentLayerIndex,
                    CurrentIteration = iteration,
                    PendingDirtyNodes = pendingDirtyNodes.ToList()
                },
                HPDAgent.Graph.Abstractions.Serialization.GraphJsonSerializerContext.Default.ContextMetadata
            ),
            Metadata = new Abstractions.Checkpointing.CheckpointMetadata
            {
                Trigger = Abstractions.Checkpointing.CheckpointTrigger.IterationCompleted,
                CompletedLayer = context.CurrentLayerIndex,
                IterationIndex = iteration
            }
        };

        await _checkpointStore.SaveCheckpointAsync(checkpoint, cancellationToken);

        context.Log("Orchestrator", $"Iteration checkpoint saved after iteration {iteration}", LogLevel.Debug);
    }

    #endregion

    #region Supporting Types

    private sealed record IterationResult
    {
        public required HashSet<string> ExecutedNodes { get; init; }
        public required List<BackEdge> TriggeredBackEdges { get; init; }
        public required HashSet<string> NextDirtyNodes { get; init; }

        /// <summary>
        /// True if the graph converged (all outputs unchanged) during this iteration.
        /// Only set when change-aware iteration with auto-convergence is enabled.
        /// </summary>
        public bool Converged { get; init; } = false;
    }

    #endregion

    public async Task<TContext> ResumeAsync(TContext context, CancellationToken cancellationToken = default)
    {
        context.Log("Orchestrator", $"Resuming graph execution: {context.Graph.Name}", LogLevel.Information);

        // Load latest checkpoint if available
        if (_checkpointStore != null && context is Context.GraphContext graphContext)
        {
            var checkpoint = await _checkpointStore.LoadLatestCheckpointAsync(graphContext.ExecutionId, cancellationToken);

            if (checkpoint != null)
            {
                context.Log("Orchestrator", $"Loaded checkpoint from {checkpoint.CreatedAt} with {checkpoint.CompletedNodes.Count} completed nodes", LogLevel.Information);

                // Validate node versions and restore state
                var restoredNodes = 0;
                var discardedNodes = 0;

                foreach (var completedNodeId in checkpoint.CompletedNodes)
                {
                    var currentNode = context.Graph.GetNode(completedNodeId);
                    if (currentNode == null)
                    {
                        context.Log("Orchestrator",
                            $"Node '{completedNodeId}' from checkpoint not found in current graph, skipping",
                            LogLevel.Warning);
                        continue;
                    }

                    // Version validation
                    if (checkpoint.NodeStateMetadata.TryGetValue(completedNodeId, out var savedState))
                    {
                        if (savedState.Version != currentNode.Version)
                        {
                            context.Log("Orchestrator",
                                $"Version mismatch for node '{completedNodeId}': saved={savedState.Version}, current={currentNode.Version}. " +
                                $"State will be discarded. Node will re-execute with current version.",
                                LogLevel.Warning);
                            discardedNodes++;
                            continue; // Don't mark as complete, force re-execution
                        }
                    }

                    // Version matches or no version info (old checkpoint) - mark complete
                    if (!graphContext.IsNodeComplete(completedNodeId))
                    {
                        graphContext.MarkNodeComplete(completedNodeId);
                    }
                    restoredNodes++;
                }

                // Restore node outputs from checkpoint (only for version-compatible nodes)
                foreach (var kvp in checkpoint.NodeOutputs)
                {
                    if (kvp.Key.StartsWith("node_output:"))
                    {
                        var nodeId = kvp.Key.Substring("node_output:".Length);

                        // Only restore if node is marked as complete (passed version check)
                        if (!graphContext.IsNodeComplete(nodeId))
                        {
                            continue; // Skip outputs from version-incompatible nodes
                        }
                    }

                    try
                    {
                        graphContext.Channels[kvp.Key].Set(kvp.Value);
                    }
                    catch (Exception ex)
                    {
                        context.Log("Orchestrator", $"Failed to restore channel '{kvp.Key}': {ex.Message}", LogLevel.Warning);
                    }
                }

                context.Log("Orchestrator",
                    $"Restored {restoredNodes} node(s) from checkpoint. Discarded {discardedNodes} node(s) due to version mismatch.",
                    LogLevel.Information);
            }
        }

        context.Log("Orchestrator", $"Already completed: {context.CompletedNodes.Count} node(s)", LogLevel.Debug);

        // ResumeAsync uses the same logic as ExecuteAsync
        // Nodes that are already complete will be skipped
        return await ExecuteAsync(context, cancellationToken);
    }

    private async Task ExecuteLayerAsync(TContext context, ExecutionLayer layer, CancellationToken cancellationToken)
    {
        var layerStartTime = DateTimeOffset.UtcNow;

        // Get nodes to execute (filter out already-completed nodes and nodes with failed dependencies)
        var nodesToExecute = layer.NodeIds
            .Where(nodeId => !context.IsNodeComplete(nodeId))
            .Select(nodeId => context.Graph.GetNode(nodeId))
            .Where(node => node != null && (node.Type == NodeType.Handler || node.Type == NodeType.Router || node.Type == NodeType.SubGraph || node.Type == NodeType.Map))
            .Where(node => !ShouldSkipDueToFailedDependency(context, node!))
            .ToList();


        if (nodesToExecute.Count == 0)
        {
            context.Log("Orchestrator", $"Layer {layer.Level}: All nodes already complete or skipped, skipping", LogLevel.Debug);
            return;
        }

        // Emit layer started event
        context.EventCoordinator?.Emit(new Abstractions.Events.LayerExecutionStartedEvent
        {
            LayerIndex = layer.Level,
            NodeCount = nodesToExecute.Count,
            GraphContext = new Abstractions.Events.GraphExecutionContext
            {
                GraphId = context.Graph.Id,
                TotalNodes = context.Graph.Nodes.Count,
                CompletedNodes = context.CompletedNodes.Count,
                CurrentLayer = layer.Level
            },
            Kind = HPD.Events.EventKind.Lifecycle,
            Priority = HPD.Events.EventPriority.Normal
        });

        if (nodesToExecute.Count == 1)
        {
            // Single node - execute sequentially
            await ExecuteNodeAsync(context, nodesToExecute[0]!, cancellationToken);
        }
        else
        {
            // Multiple nodes - execute in parallel
            context.Log("Orchestrator", $"Layer {layer.Level}: Executing {nodesToExecute.Count} nodes in parallel", LogLevel.Debug);
            await ExecuteNodesInParallelAsync(context, nodesToExecute!, cancellationToken);
        }

        // Emit layer completed event
        context.EventCoordinator?.Emit(new Abstractions.Events.LayerExecutionCompletedEvent
        {
            LayerIndex = layer.Level,
            Duration = DateTimeOffset.UtcNow - layerStartTime,
            SuccessfulNodes = nodesToExecute.Count(n => context.IsNodeComplete(n!.Id)),
            GraphContext = new Abstractions.Events.GraphExecutionContext
            {
                GraphId = context.Graph.Id,
                TotalNodes = context.Graph.Nodes.Count,
                CompletedNodes = context.CompletedNodes.Count,
                CurrentLayer = layer.Level
            },
            Kind = HPD.Events.EventKind.Lifecycle,
            Priority = HPD.Events.EventPriority.Normal
        });

        // Clear ephemeral channels after layer execution (prevents state leakage in loops)
        ClearEphemeralChannels(context);
    }

    private async Task ExecuteNodesInParallelAsync(TContext context, List<Node> nodes, CancellationToken cancellationToken)
    {
        // Check if any nodes have MaxParallelExecutions set (parallelism limiting)
        var nodeParallelismLimits = new Dictionary<string, SemaphoreSlim>();
        foreach (var node in nodes)
        {
            if (node.MaxParallelExecutions.HasValue)
            {
                // Create semaphore with initial count = max parallel executions
                nodeParallelismLimits[node.Id] = new SemaphoreSlim(node.MaxParallelExecutions.Value, node.MaxParallelExecutions.Value);
            }
        }

        try
        {
            // Use Parallel.ForEachAsync with bounded concurrency to prevent thread pool starvation
            var results = new ConcurrentBag<TContext>();

            await Parallel.ForEachAsync(nodes,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _maxLayerConcurrency,
                    CancellationToken = cancellationToken
                },
                async (node, ct) =>
                {
                    // Apply per-node parallelism limit if this node has one
                    SemaphoreSlim? parallelismSemaphore = null;
                    if (nodeParallelismLimits.TryGetValue(node.Id, out parallelismSemaphore))
                    {
                        // Wait for available execution slot (throttling)
                        await parallelismSemaphore.WaitAsync(ct);
                    }

                    try
                    {
                        var isolatedContext = (TContext)context.CreateIsolatedCopy();
                        await ExecuteNodeAsync(isolatedContext, node, ct);
                        results.Add(isolatedContext);
                    }
                    finally
                    {
                        // Release execution slot after completion
                        parallelismSemaphore?.Release();
                    }
                });

            // Merge results back into parent context
            foreach (var isolatedContext in results)
            {
                context.MergeFrom(isolatedContext);
            }
        }
        finally
        {
            // Dispose semaphores
            foreach (var semaphore in nodeParallelismLimits.Values)
            {
                semaphore.Dispose();
            }
        }
    }

    private async Task ExecuteNodeAsync(TContext context, Node node, CancellationToken cancellationToken)
    {
        // Check if already completed (idempotency)
        if (context.IsNodeComplete(node.Id))
        {
            context.Log("Orchestrator", $"Node {node.Id} already complete, skipping", LogLevel.Debug, nodeId: node.Id);
            return;
        }

        // Check max executions (loop/cycle protection)
        // Use per-node MaxExecutions if set, otherwise fall back to graph-level MaxIterations
        var maxExecutions = node.MaxExecutions ?? context.Graph.MaxIterations;
        var executionCount = context.GetNodeExecutionCount(node.Id);
        if (executionCount >= maxExecutions)
        {
            context.Log("Orchestrator",
                $"Node {node.Id} exceeded max executions ({maxExecutions})",
                LogLevel.Warning, nodeId: node.Id);
            return;
        }

        // NEW: Pre-execution condition check - skip if no incoming edge conditions are met
        // PHASE 4: Now async to support temporal operators (delay, schedule, retry)
        var (shouldSkip, skipDetails) = await EvaluateSkipConditions(context, node, cancellationToken);
        if (shouldSkip)
        {
            context.Log("Orchestrator",
                $"Skipping {node.Id}: conditions not met. Evaluated edges: {skipDetails}",
                LogLevel.Information,
                nodeId: node.Id);

            HandleSkipped(context, node, new NodeExecutionResult.Skipped(
                Reason: SkipReason.ConditionNotMet,
                Message: $"All incoming edge conditions evaluated to false. {skipDetails}"
            ));

            // Record skip reason for checkpoint/resume
            context.Channels[$"skip_reason:{node.Id}"].Set(SkipReason.ConditionNotMet);
            return;
        }

        context.SetCurrentNode(node.Id);
        context.IncrementNodeExecutionCount(node.Id);

        // Phase 5: Set current namespace if node has ArtifactNamespace
        if (node.ArtifactNamespace != null && context is Context.GraphContext graphContext)
        {
            // Combine with parent namespace if already in a namespace
            var combinedNamespace = context.CurrentNamespace != null
                ? context.CurrentNamespace.Concat(node.ArtifactNamespace).ToList()
                : node.ArtifactNamespace;

            graphContext.SetCurrentNamespace(combinedNamespace);

            context.Log("Orchestrator",
                $"Set artifact namespace: {string.Join("/", combinedNamespace)}",
                LogLevel.Debug, nodeId: node.Id);
        }

        // Mark as Running
        context.AddTag($"node_state:{node.Id}", Abstractions.Execution.NodeState.Running.ToString());

        context.Log("Orchestrator", $"Executing node: {node.Name} (ID: {node.Id}, Type: {node.Type})", LogLevel.Information, nodeId: node.Id);

        var nodeStartTime = DateTimeOffset.UtcNow;

        // Emit node started event
        context.EventCoordinator?.Emit(new Abstractions.Events.NodeExecutionStartedEvent
        {
            NodeId = node.Id,
            HandlerName = node.HandlerName ?? node.Type.ToString(),
            LayerIndex = context.CurrentLayerIndex,
            GraphContext = new Abstractions.Events.GraphExecutionContext
            {
                GraphId = context.Graph.Id,
                TotalNodes = context.Graph.Nodes.Count,
                CompletedNodes = context.CompletedNodes.Count,
                CurrentLayer = context.CurrentLayerIndex
            },
            Kind = HPD.Events.EventKind.Lifecycle,
            Priority = HPD.Events.EventPriority.Normal
        });

        try
        {
            // Handle partitioned nodes by converting partitions to Map items (Phase 2)
            if (node.Partitions != null)
            {
                await ExecutePartitionedNodeAsync(context, node, cancellationToken);
                return;
            }

            // Handle Map nodes by iterating over collection
            if (node.Type == NodeType.Map)
            {
                await ExecuteMapAsync(context, node, cancellationToken);
                return;
            }

            // Handle SubGraph nodes by recursively executing the sub-graph
            if (node.Type == NodeType.SubGraph)
            {
                await ExecuteSubGraphAsync(context, node, cancellationToken);
                return;
            }

            // Get handler for Handler/Router nodes
            var handler = ResolveHandler(node);
            if (handler == null)
            {
                throw new InvalidOperationException($"No handler found for node '{node.Id}' with handler name '{node.HandlerName}'");
            }

            // Prepare inputs from upstream nodes
            var inputs = PrepareInputs(context, node);

            // Compute fingerprint if fingerprint calculator is available (for caching OR artifacts)
            string? fingerprint = null;
            if (_fingerprintCalculator != null)
            {
                // Compute fingerprint for this node
                var upstreamHashes = GetUpstreamFingerprints(context, node);
                var globalHash = context.Graph.Id + context.Graph.Version; // Simple global hash
                fingerprint = _fingerprintCalculator.Compute(node.Id, inputs, upstreamHashes, globalHash);

                // Store for downstream nodes and artifact versioning
                context.InternalCurrentFingerprints[node.Id] = fingerprint;
                // Try to get from cache if cache store is available
                if (_cacheStore != null)
                {
                    var cached = await _cacheStore.GetAsync(fingerprint, cancellationToken);
                    if (cached != null)
                    {
                        context.Log("Orchestrator", $"Cache HIT for node {node.Id} (fingerprint: {fingerprint[..8]}...)",
                            LogLevel.Debug, nodeId: node.Id);

                        // Create success result from cache
                        var cachedResult = NodeExecutionResult.Success.Single(
                            output: cached.Outputs,
                            duration: cached.Duration,
                            metadata: new NodeExecutionMetadata
                            {
                                AttemptNumber = 1,
                                CustomMetrics = new Dictionary<string, object>
                                {
                                    ["CacheHit"] = true,
                                    ["Fingerprint"] = fingerprint
                                }
                            }
                        );

                        HandleSuccess(context, node, cachedResult);
                        return; // Skip execution entirely
                    }

                    context.Log("Orchestrator", $"Cache MISS for node {node.Id} (fingerprint: {fingerprint[..8]}...)",
                        LogLevel.Debug, nodeId: node.Id);
                }
            }

            // Execute with timeout if specified
            NodeExecutionResult result;
            if (node.Timeout.HasValue)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(node.Timeout.Value);
                result = await handler.ExecuteAsync(context, inputs, cts.Token);
            }
            else
            {
                result = await handler.ExecuteAsync(context, inputs, cancellationToken);
            }

            // Handle result using pattern matching
            switch (result)
            {
                case NodeExecutionResult.Success success:
                    HandleSuccess(context, node, success);
                    break;

                case NodeExecutionResult.Failure failure:
                    await HandleFailureAsync(context, node, failure, cancellationToken);
                    break;

                case NodeExecutionResult.Skipped skipped:
                    HandleSkipped(context, node, skipped);
                    break;

                case NodeExecutionResult.Suspended suspended:
                    // New: async handling with potential continuation
                    var shouldContinue = await HandleSuspendedAsync(context, node, suspended, cancellationToken);
                    if (!shouldContinue)
                    {
                        // Halt execution - throw exception so caller knows graph is suspended
                        throw new GraphSuspendedException(node.Id, suspended.SuspendToken, suspended.Message);
                    }
                    // If shouldContinue is true, node is marked complete and execution continues to next node
                    break;

                case NodeExecutionResult.Cancelled cancelled:
                    HandleCancelled(context, node, cancelled);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown result type: {result.GetType().Name}");
            }
        }
        catch (OperationCanceledException)
        {
            context.Log("Orchestrator", $"Node {node.Id} cancelled", LogLevel.Warning, nodeId: node.Id);
            throw;
        }
        catch (Exception ex)
        {
            context.Log("Orchestrator", $"Node {node.Id} failed: {ex.Message}", LogLevel.Error, nodeId: node.Id, exception: ex);
            throw;
        }
        finally
        {
            context.SetCurrentNode(null);
        }
    }

    private IGraphNodeHandler<TContext>? ResolveHandler(Node node)
    {
        if (string.IsNullOrWhiteSpace(node.HandlerName))
        {
            return null;
        }

        // Get all registered handlers
        var handlers = _serviceProvider.GetServices<IGraphNodeHandler<TContext>>();
        return handlers.FirstOrDefault(h => h.HandlerName == node.HandlerName);
    }

    private Abstractions.Routing.IMapRouter? ResolveRouter(Node node)
    {
        if (string.IsNullOrWhiteSpace(node.MapRouterName))
        {
            return null;
        }

        // Get all registered routers from DI (same pattern as handlers)
        var routers = _serviceProvider.GetServices<Abstractions.Routing.IMapRouter>();
        return routers.FirstOrDefault(r => r.RouterName == node.MapRouterName);
    }

    private void ValidateMapNode(Node node)
    {
        // Validate mutually exclusive properties
        if (node.MapProcessorGraph != null && node.MapProcessorGraphs != null)
        {
            throw new InvalidOperationException(
                $"Map node '{node.Id}' cannot have both MapProcessorGraph and MapProcessorGraphs. " +
                "Use MapProcessorGraph for homogeneous mapping or MapProcessorGraphs for heterogeneous mapping.");
        }

        // Validate MapProcessorGraphs requires MapRouterName
        if (node.MapProcessorGraphs != null && string.IsNullOrWhiteSpace(node.MapRouterName))
        {
            throw new InvalidOperationException(
                $"Map node '{node.Id}' has MapProcessorGraphs but no MapRouterName. " +
                "MapRouterName is required for heterogeneous mapping.");
        }

        // Validate at least one processor is specified
        if (node.MapProcessorGraph == null &&
            node.MapProcessorGraphRef == null &&
            node.MapProcessorGraphs == null)
        {
            throw new InvalidOperationException(
                $"Map node '{node.Id}' must have MapProcessorGraph, MapProcessorGraphRef, or MapProcessorGraphs.");
        }
    }

    private Abstractions.Graph.Graph SelectProcessorGraph(Node node, object item, TContext context)
    {
        // Homogeneous map - same graph for all items
        if (node.MapProcessorGraph != null)
        {
            return node.MapProcessorGraph;
        }

        // Heterogeneous map - route using DI-resolved router (same as handlers!)
        if (node.MapProcessorGraphs != null && node.MapRouterName != null)
        {
            // Resolve router from DI (identical to handler resolution)
            var router = ResolveRouter(node);
            if (router == null)
            {
                throw new InvalidOperationException(
                    $"Map router '{node.MapRouterName}' not found. " +
                    "Ensure router is registered in DI (use services.AddGeneratedMapRouters()).");
            }

            var routingValue = router.Route(item);

            if (node.MapProcessorGraphs.TryGetValue(routingValue, out var routedGraph))
            {
                context.Log("Orchestrator",
                    $"Map node '{node.Id}' routed item to processor '{routingValue}'",
                    LogLevel.Debug, nodeId: node.Id);
                return routedGraph;
            }

            // No match - use default if available
            if (node.MapDefaultGraph != null)
            {
                context.Log("Orchestrator",
                    $"Map routing value '{routingValue}' not found, using default graph",
                    LogLevel.Debug, nodeId: node.Id);
                return node.MapDefaultGraph;
            }

            // No match and no default - throw error
            throw new InvalidOperationException(
                $"Map node '{node.Id}' routing value '{routingValue}' not found in MapProcessorGraphs and no MapDefaultGraph specified. " +
                $"Available keys: {string.Join(", ", node.MapProcessorGraphs.Keys)}");
        }

        throw new InvalidOperationException(
            $"Map node '{node.Id}' has no valid processor graph configuration.");
    }

    private HandlerInputs PrepareInputs(TContext context, Node node)
    {
        var inputs = new HandlerInputs();

        // Get incoming edges
        var incomingEdges = context.Graph.GetIncomingEdges(node.Id);

        // Group edges by source node for default edge handling
        var edgesBySource = incomingEdges.GroupBy(e => e.From);

        // Track which sources contributed to each non-prefixed key (for ambiguity warnings)
        var keySourceMap = new Dictionary<string, List<string>>();

        foreach (var sourceGroup in edgesBySource)
        {
            var sourceNodeId = sourceGroup.Key;
            var sourceNode = context.Graph.GetNode(sourceNodeId);

            if (sourceNode != null && context.IsNodeComplete(sourceNodeId))
            {
                // Get outputs from the appropriate port channel
                // Group edges by FromPort to handle multi-port routing
                var edgesByPort = sourceGroup.GroupBy(e => e.FromPort ?? 0);

                foreach (var portGroup in edgesByPort)
                {
                    var fromPort = portGroup.Key;
                    var channelName = fromPort == 0
                        ? $"node_output:{sourceNodeId}" // Legacy channel for port 0
                        : $"node_output:{sourceNodeId}:port:{fromPort}"; // Port-specific channel

                    if (!context.Channels.Contains(channelName))
                    {
                        // Port has no outputs - this is valid (node may not have output on this port)
                        context.Log("Orchestrator",
                            $"No outputs found on port {fromPort} of node {sourceNodeId}",
                            LogLevel.Debug, nodeId: node.Id);
                        continue;
                    }

                    var rawOutputs = context.Channels[channelName].Get<Dictionary<string, object>>();
                    if (rawOutputs != null)
                    {
                        // Two-pass evaluation for default edges (within this port group)
                        var regularEdges = portGroup.Where(e => e.Condition?.Type != ConditionType.Default).ToList();
                        var defaultEdge = portGroup.FirstOrDefault(e => e.Condition?.Type == ConditionType.Default);
                        bool anyRegularEdgeMatched = false;

                        // Pass 1: Evaluate regular conditions
                        foreach (var edge in regularEdges)
                        {
                            if (ConditionEvaluator.Evaluate(edge.Condition, rawOutputs, context, edge))
                            {
                                // Apply cloning policy for this edge
                                var outputs = ApplyCloningPolicy(context, rawOutputs, sourceNodeId, fromPort, edge);

                                // Condition met - include these outputs with namespace
                                // Namespace format: {sourceNodeId}.{key} or {sourceNodeId}:port{N}.{key} for non-zero ports
                                var portSuffix = fromPort == 0 ? "" : $":port{fromPort}";
                                AddOutputsWithNamespace(inputs, sourceNodeId + portSuffix, outputs, keySourceMap);
                                anyRegularEdgeMatched = true;
                                break; // Only use outputs from first matching edge per port
                            }
                            else
                            {
                                // Condition not met - skip this edge
                                context.Log("Orchestrator",
                                    $"Edge condition not met: {edge.From}:port{fromPort} -> {edge.To} ({edge.Condition?.GetDescription() ?? "N/A"})",
                                    LogLevel.Debug, nodeId: node.Id);
                            }
                        }

                        // Pass 2: If no regular edges matched, try default edge
                        if (!anyRegularEdgeMatched && defaultEdge != null)
                        {
                            context.Log("Orchestrator",
                                $"Using default edge: {defaultEdge.From}:port{fromPort} -> {defaultEdge.To}",
                                LogLevel.Debug, nodeId: node.Id);

                            // Apply cloning policy for this edge
                            var outputs = ApplyCloningPolicy(context, rawOutputs, sourceNodeId, fromPort, defaultEdge);

                            // Include outputs from default edge with namespace
                            var portSuffix = fromPort == 0 ? "" : $":port{fromPort}";
                            AddOutputsWithNamespace(inputs, sourceNodeId + portSuffix, outputs, keySourceMap);
                        }
                    }
                }
            }
        }

        // Log warnings for ambiguous non-prefixed keys (multiple sources for same key)
        foreach (var kvp in keySourceMap.Where(k => k.Value.Count > 1))
        {
            context.Log("Orchestrator",
                $"Warning: Key '{kvp.Key}' has multiple sources: [{string.Join(", ", kvp.Value)}]. " +
                $"Using value from '{kvp.Value.First()}'. Consider using namespaced access: '{kvp.Value.First()}.{kvp.Key}'",
                LogLevel.Warning,
                nodeId: node.Id);
        }

        // Include SharedData if available (prefixed with "shared.")
        if (context.SharedData != null)
        {
            foreach (var kvp in context.SharedData)
            {
                // Add with "shared." prefix for clear namespacing
                inputs.Add($"shared.{kvp.Key}", kvp.Value);

                // Also add without prefix if not already present (convenience)
                if (!inputs.Contains(kvp.Key))
                {
                    inputs.Add(kvp.Key, kvp.Value);
                }
            }
        }

        // Validate inputs against schemas if configured
        if (node.InputSchemas != null)
        {
            var validationErrors = ValidateInputs(inputs, node.InputSchemas);
            if (validationErrors.Count > 0)
            {
                var message = string.Join("; ", validationErrors);
                context.Log("Orchestrator",
                    $"Input validation failed for node {node.Id}: {message}",
                    LogLevel.Error, nodeId: node.Id);

                throw new InvalidOperationException(
                    $"Input validation failed for node {node.Id}: {message}");
            }
        }

        return inputs;
    }

    /// <summary>
    /// Applies cloning policy to outputs based on graph configuration and edge settings.
    /// Implements lazy cloning: first edge gets original, subsequent edges get clones.
    /// </summary>
    private Dictionary<string, object> ApplyCloningPolicy(
        TContext context,
        Dictionary<string, object> outputs,
        string sourceNodeId,
        int fromPort,
        Edge edge)
    {
        // Determine cloning policy (edge-specific or graph-level)
        var policy = edge.CloningPolicy ?? context.Graph.CloningPolicy;

        // Track consumption for lazy cloning
        var consumptionKey = $"{sourceNodeId}:{fromPort}";
        var edgeId = $"{edge.From}:{edge.FromPort ?? 0}->{edge.To}:{edge.ToPort ?? 0}";

        // Apply cloning strategy
        return policy switch
        {
            CloningPolicy.AlwaysClone => CloneOutputs(outputs),
            CloningPolicy.NeverClone => outputs, // Share reference (requires immutable handlers)
            CloningPolicy.LazyClone => IsFirstConsumer(context, consumptionKey, edgeId)
                ? outputs // First gets original (zero copy)
                : CloneOutputs(outputs), // Subsequent get clones
            _ => CloneOutputs(outputs) // Default to safe
        };
    }

    /// <summary>
    /// Checks if this edge is the first consumer of the output and marks it as consumed.
    /// Thread-safe for parallel execution.
    /// </summary>
    private bool IsFirstConsumer(TContext context, string consumptionKey, string edgeId)
    {
        var consumers = context.InternalConsumedOutputs.GetOrAdd(consumptionKey, _ => new HashSet<string>());

        lock (consumers)
        {
            var isFirst = consumers.Count == 0;
            consumers.Add(edgeId);
            return isFirst;
        }
    }

    /// <summary>
    /// Deep clones outputs using source-generated JSON serialization.
    /// Handles circular references and preserves type information.
    /// </summary>
    private Dictionary<string, object> CloneOutputs(Dictionary<string, object> outputs)
    {
        return Abstractions.Serialization.OutputCloner.DeepClone(outputs);
    }

    /// <summary>
    /// Adds outputs to inputs with namespace prefix for source attribution.
    /// Also adds non-prefixed keys for backward compatibility (first source wins).
    /// </summary>
    private void AddOutputsWithNamespace(
        HandlerInputs inputs,
        string sourceNodeId,
        Dictionary<string, object> outputs,
        Dictionary<string, List<string>> keySourceMap)
    {
        foreach (var kvp in outputs)
        {
            // Always add with namespace prefix (e.g., "solver1.answer")
            inputs.Add($"{sourceNodeId}.{kvp.Key}", kvp.Value);

            // Track sources for non-prefixed key (for ambiguity detection)
            if (!keySourceMap.TryGetValue(kvp.Key, out var sources))
            {
                sources = new List<string>();
                keySourceMap[kvp.Key] = sources;
            }
            sources.Add(sourceNodeId);

            // Add non-prefixed for backward compatibility (first source wins)
            if (!inputs.Contains(kvp.Key))
            {
                inputs.Add(kvp.Key, kvp.Value);
            }
        }
    }

    /// <summary>
    /// Validates inputs against declared schemas.
    /// Returns list of validation error messages (empty if valid).
    /// </summary>
    private List<string> ValidateInputs(
        HandlerInputs inputs,
        IReadOnlyDictionary<string, Abstractions.Validation.InputSchema> schemas)
    {
        var errors = new List<string>();

        foreach (var (inputName, schema) in schemas)
        {
            var hasValue = inputs.TryGet<object>(inputName, out var value);

            // Check required
            if (!hasValue && schema.Required)
            {
                errors.Add($"Required input '{inputName}' is missing");
                continue;
            }

            // Apply default if missing and optional
            if (!hasValue && schema.DefaultValue != null)
            {
                inputs.Add(inputName, schema.DefaultValue);
                continue;
            }

            if (!hasValue)
                continue; // Optional and no default

            // Type check
            if (value != null && !schema.Type.IsInstanceOfType(value))
            {
                errors.Add($"Input '{inputName}' has type {value.GetType().Name}, " +
                          $"expected {schema.Type.Name}");
                continue;
            }

            // Custom validation
            if (schema.Validator != null)
            {
                var result = schema.Validator.Validate(inputName, value);
                if (!result.IsValid)
                {
                    errors.AddRange(result.Errors);
                }
            }
        }

        return errors;
    }

    private void HandleSuccess(TContext context, Node node, NodeExecutionResult.Success success)
    {
        var isCacheHit = success.Metadata?.CustomMetrics?.ContainsKey("CacheHit") == true;

        context.Log("Orchestrator",
            $"Node {node.Id} completed successfully in {success.Duration.TotalMilliseconds:F2}ms{(isCacheHit ? " (from cache)" : "")}",
            LogLevel.Information, nodeId: node.Id);

        // Validate that all outputs are serializable (for cloning)
        foreach (var portOutput in success.PortOutputs)
        {
            try
            {
                Abstractions.Serialization.OutputCloner.ValidateSerializable(portOutput.Value);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"Node {node.Id} produced non-serializable outputs on port {portOutput.Key}: {ex.Message}",
                    ex
                );
            }
        }

        // Store outputs in channels for downstream nodes
        // Port 0 is stored in the legacy channel for backward compatibility
        var port0ChannelName = $"node_output:{node.Id}";
        var port0Outputs = success.PortOutputs.TryGetValue(0, out var outputs) ? outputs : new Dictionary<string, object>();
        context.Channels[port0ChannelName].Set(port0Outputs);

        // Store ALL port outputs for multi-port routing
        // Format: "node_output:{nodeId}:port:{portNumber}"
        foreach (var portOutput in success.PortOutputs)
        {
            var portChannelName = $"node_output:{node.Id}:port:{portOutput.Key}";
            context.Channels[portChannelName].Set(portOutput.Value);
        }

        // Store result for upstream condition evaluation
        context.Channels[$"node_result:{node.Id}"].Set(success);

        // Track output hash for change-aware iteration (always track, even if not enabled)
        // This ensures hashes are available if the feature is enabled mid-execution
        var options = context.Graph.IterationOptions;
        var excludeFields = options?.IgnoreFieldsForChangeDetection;
        var algorithm = options?.HashAlgorithm ?? Abstractions.Graph.OutputHashAlgorithm.XxHash64;
        var outputHash = ComputeOutputHash(port0Outputs, excludeFields, algorithm);
        context.InternalOutputHashes[node.Id] = outputHash;

        // Cache the result if caching is enabled and this wasn't a cache hit
        if (!isCacheHit && _cacheStore != null && context.InternalCurrentFingerprints.TryGetValue(node.Id, out var fingerprint))
        {
            var cachedResult = new CachedNodeResult
            {
                Outputs = port0Outputs,
                CachedAt = DateTimeOffset.UtcNow,
                Duration = success.Duration,
                Metadata = success.Metadata.CustomMetrics != null
                    ? new Dictionary<string, object>(success.Metadata.CustomMetrics)
                    : null
            };

            // Fire and forget - don't await (caching shouldn't slow down execution)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _cacheStore.SetAsync(fingerprint, cachedResult);
                }
                catch (Exception ex)
                {
                    context.Log("Orchestrator", $"Failed to cache result for node {node.Id}: {ex.Message}",
                        LogLevel.Warning, nodeId: node.Id);
                }
            });
        }

        // Register artifact if node produces one (Phase 1: Data Orchestration Primitives)
        if (node.ProducesArtifact != null && _artifactRegistry != null)
        {
            var version = context.InternalCurrentFingerprints.TryGetValue(node.Id, out var fp) ? fp : Guid.NewGuid().ToString();

            // Collect input artifact versions for lineage tracking
            var inputVersions = GetInputArtifactVersions(context, node);

            // Phase 5: Prefix artifact key with current namespace (if any)
            var qualifiedPath = context.CurrentNamespace != null && context.CurrentNamespace.Count > 0
                ? context.CurrentNamespace.Concat(node.ProducesArtifact.Path).ToList()
                : node.ProducesArtifact.Path;

            // Determine artifact key with partition (if current context has partition)
            var artifactKey = context.CurrentPartition != null
                ? new ArtifactKey { Path = qualifiedPath, Partition = context.CurrentPartition }
                : new ArtifactKey { Path = qualifiedPath, Partition = node.ProducesArtifact.Partition };

            var metadata = new ArtifactMetadata
            {
                CreatedAt = DateTimeOffset.UtcNow,
                InputVersions = inputVersions,
                ProducedByNodeId = node.Id,
                ExecutionId = context.ExecutionId,
                CustomMetadata = success.Metadata?.CustomMetrics != null
                    ? new Dictionary<string, object>(success.Metadata.CustomMetrics)
                    : null
            };

            // Register artifact synchronously to ensure it's available for MaterializeAsync
            // (MaterializeAsync depends on artifacts being registered to resolve producers)
            try
            {
                _artifactRegistry.RegisterAsync(artifactKey, version, metadata).GetAwaiter().GetResult();

                context.Log("Orchestrator",
                    $"Registered artifact {artifactKey} version {version.Substring(0, Math.Min(8, version.Length))}",
                    LogLevel.Debug, nodeId: node.Id);
            }
            catch (Exception ex)
            {
                context.Log("Orchestrator",
                    $"Failed to register artifact {artifactKey} for node {node.Id}: {ex.Message}",
                    LogLevel.Warning, nodeId: node.Id);
            }
        }

        // Mark node as complete
        context.MarkNodeComplete(node.Id);

        // Update state to Succeeded
        context.AddTag($"node_state:{node.Id}", Abstractions.Execution.NodeState.Succeeded.ToString());

        // Emit node completed event
        context.EventCoordinator?.Emit(new Abstractions.Events.NodeExecutionCompletedEvent
        {
            NodeId = node.Id,
            HandlerName = node.HandlerName ?? node.Type.ToString(),
            LayerIndex = context.CurrentLayerIndex,
            Progress = (float)context.CompletedNodes.Count / context.Graph.Nodes.Count,
            Outputs = port0Outputs, // Include port 0 outputs for downstream consumers
            Duration = success.Duration,
            Result = success,
            GraphContext = new Abstractions.Events.GraphExecutionContext
            {
                GraphId = context.Graph.Id,
                TotalNodes = context.Graph.Nodes.Count,
                CompletedNodes = context.CompletedNodes.Count,
                CurrentLayer = context.CurrentLayerIndex
            },
            Kind = HPD.Events.EventKind.Lifecycle,
            Priority = HPD.Events.EventPriority.Normal
        });
    }

    private async Task HandleFailureAsync(TContext context, Node node, NodeExecutionResult.Failure failure, CancellationToken cancellationToken)
    {
        context.Log("Orchestrator",
            $"Node {node.Id} failed: {failure.Exception.Message} (Severity: {failure.Severity})",
            LogLevel.Error, nodeId: node.Id, exception: failure.Exception);

        // Check if we should retry
        if (node.RetryPolicy != null && failure.IsTransient && node.RetryPolicy.ShouldRetry(failure.Exception))
        {
            var executionCount = context.GetNodeExecutionCount(node.Id);
            if (executionCount < node.RetryPolicy.MaxAttempts)
            {
                var delay = node.RetryPolicy.GetDelay(executionCount);
                context.Log("Orchestrator",
                    $"Retrying node {node.Id} after {delay.TotalMilliseconds}ms (attempt {executionCount + 1}/{node.RetryPolicy.MaxAttempts})",
                    LogLevel.Warning, nodeId: node.Id);

                await Task.Delay(delay, cancellationToken);
                await ExecuteNodeAsync(context, node, cancellationToken);
                return;
            }
        }

        // Apply error propagation policy
        var policy = node.ErrorPolicy ?? ErrorPropagationPolicy.StopGraph(); // Default: StopGraph

        // Check if error should propagate based on custom predicate
        if (policy.ShouldPropagate != null && !policy.ShouldPropagate(failure.Exception))
        {
            context.Log("Orchestrator",
                $"Error in node {node.Id} filtered by ShouldPropagate predicate - not propagating",
                LogLevel.Warning, nodeId: node.Id);
            return; // Don't propagate this error
        }

        switch (policy.Mode)
        {
            case PropagationMode.StopGraph:
                // Immediately stop entire graph execution
                context.Log("Orchestrator",
                    $"Stopping graph execution due to failure in node {node.Id} (Mode: StopGraph)",
                    LogLevel.Error, nodeId: node.Id);
                throw new GraphExecutionException($"Node {node.Id} failed: {failure.Exception.Message}", failure.Exception);

            case PropagationMode.SkipDependents:
                // Mark this node as failed, downstream dependents will be skipped
                context.InternalFailedNodes.TryAdd(node.Id, failure.Exception);
                context.AddTag($"node_state:{node.Id}", Abstractions.Execution.NodeState.Failed.ToString());
                context.Log("Orchestrator",
                    $"Node {node.Id} failed - downstream dependents will be skipped (Mode: SkipDependents)",
                    LogLevel.Warning, nodeId: node.Id);
                break;

            case PropagationMode.ExecuteFallback:
                // Execute fallback node
                if (string.IsNullOrEmpty(policy.FallbackNodeId))
                {
                    throw new InvalidOperationException($"Node {node.Id} has ExecuteFallback mode but no FallbackNodeId specified");
                }

                var fallbackNode = context.Graph.GetNode(policy.FallbackNodeId);
                if (fallbackNode == null)
                {
                    throw new InvalidOperationException($"Fallback node {policy.FallbackNodeId} not found for node {node.Id}");
                }

                context.Log("Orchestrator",
                    $"Node {node.Id} failed - executing fallback node {fallbackNode.Id} (Mode: ExecuteFallback)",
                    LogLevel.Warning, nodeId: node.Id);

                // Store error context for fallback node
                context.Channels[$"error_context:{node.Id}"].Set(new Dictionary<string, object>
                {
                    ["original_node_id"] = node.Id,
                    ["exception"] = failure.Exception,
                    ["severity"] = failure.Severity.ToString()
                });

                await ExecuteNodeAsync(context, fallbackNode, cancellationToken);
                break;

            case PropagationMode.Isolate:
                // Isolate error - don't affect downstream nodes
                // Note: Still mark as Failed for observability, just don't propagate
                context.InternalFailedNodes.TryAdd(node.Id, failure.Exception);
                context.AddTag($"node_state:{node.Id}", Abstractions.Execution.NodeState.Failed.ToString());
                context.Log("Orchestrator",
                    $"Node {node.Id} failed but error is isolated - downstream nodes will continue (Mode: Isolate)",
                    LogLevel.Warning, nodeId: node.Id);
                // Store result for upstream condition evaluation
                context.Channels[$"node_result:{node.Id}"].Set(failure);
                // Mark as complete so downstream nodes can execute
                context.MarkNodeComplete(node.Id);
                break;

            default:
                throw new NotSupportedException($"Propagation mode {policy.Mode} is not supported");
        }
    }

    private void HandleSkipped(TContext context, Node node, NodeExecutionResult.Skipped skipped)
    {
        // Update state to Skipped
        context.AddTag($"node_state:{node.Id}", Abstractions.Execution.NodeState.Skipped.ToString());

        context.Log("Orchestrator",
            $"Node {node.Id} skipped: {skipped.Reason} - {skipped.Message}",
            LogLevel.Information, nodeId: node.Id);

        // Store result for upstream condition evaluation
        context.Channels[$"node_result:{node.Id}"].Set(skipped);

        // Mark node as complete so it doesn't block downstream execution
        context.MarkNodeComplete(node.Id);

        // Emit node skipped event
        context.EventCoordinator?.Emit(new Abstractions.Events.NodeExecutionCompletedEvent
        {
            NodeId = node.Id,
            HandlerName = node.HandlerName ?? node.Type.ToString(),
            LayerIndex = context.CurrentLayerIndex,
            Progress = (float)context.CompletedNodes.Count / context.Graph.Nodes.Count,
            Duration = TimeSpan.Zero,
            Result = skipped,
            GraphContext = new Abstractions.Events.GraphExecutionContext
            {
                GraphId = context.Graph.Id,
                TotalNodes = context.Graph.Nodes.Count,
                CompletedNodes = context.CompletedNodes.Count,
                CurrentLayer = context.CurrentLayerIndex
            },
            Kind = HPD.Events.EventKind.Lifecycle,
            Priority = HPD.Events.EventPriority.Normal
        });
    }

    /// <summary>
    /// Handles node suspension with layered suspension pattern:
    /// 1. Durability: Save checkpoint (if store available)
    /// 2. Reactivity: Emit bidirectional event (if coordinator available)
    /// 3. Waiting: Await response with timeout
    /// 4. Fallback: On timeout/denial, return false to halt cleanly
    /// </summary>
    /// <returns>True if approved and should continue, false if should halt</returns>
    private async Task<bool> HandleSuspendedAsync(
        TContext context,
        Node node,
        NodeExecutionResult.Suspended suspended,
        CancellationToken ct)
    {
        // Check if sensor polling pattern
        if (suspended.Reason == Abstractions.Execution.SuspendReason.PollingCondition ||
            suspended.Reason == Abstractions.Execution.SuspendReason.ResourceWait)
        {
            // Update state to Polling
            context.AddTag($"node_state:{node.Id}", Abstractions.Execution.NodeState.Polling.ToString());
            return await HandleSensorPollingAsync(context, node, suspended, ct);
        }

        // HITL suspension or external task wait
        context.AddTag($"node_state:{node.Id}", Abstractions.Execution.NodeState.Suspended.ToString());

        var options = node.SuspensionOptions ?? _defaultSuspensionOptions;

        context.Log("Orchestrator",
            $"Node {node.Id} suspended for external input: {suspended.Message}",
            LogLevel.Information, nodeId: node.Id);

        // Store suspension info in context (always, for resume capability)
        context.AddTag("suspended_nodes", node.Id);
        context.AddTag($"suspend_token:{node.Id}", suspended.SuspendToken);
        context.AddTag($"suspend_outcome:{node.Id}", Abstractions.Execution.SuspensionOutcome.Pending.ToString());

        if (suspended.ResumeValue != null)
        {
            context.Channels[$"suspend_resume:{node.Id}"].Set(suspended.ResumeValue);
        }

        // LAYER 1: Durability - Save checkpoint first (if store available)
        if (options.SaveCheckpointFirst && _checkpointStore != null)
        {
            try
            {
                await SaveSuspensionCheckpointAsync(context, node, suspended, ct);
                context.Log("Orchestrator",
                    $"Checkpoint saved for suspended node {node.Id}",
                    LogLevel.Information, nodeId: node.Id);
            }
            catch (Exception ex)
            {
                // Log but don't fail - suspension should still work
                context.Log("Orchestrator",
                    $"Failed to save checkpoint for suspended node {node.Id}: {ex.Message}",
                    LogLevel.Warning, nodeId: node.Id, exception: ex);
            }
        }
        else if (options.SaveCheckpointFirst && _checkpointStore == null)
        {
            context.Log("Orchestrator",
                $"No checkpoint store configured; skipping checkpoint for suspended node {node.Id}",
                LogLevel.Debug, nodeId: node.Id);
        }

        // LAYER 2 & 3: Reactivity and Waiting (if coordinator available)
        if (options.EmitEvents && context.EventCoordinator != null)
        {
            // REUSE EXISTING EVENT TYPE - map SuspendToken to RequestId
            var requestEvent = new Abstractions.Events.NodeApprovalRequestEvent
            {
                RequestId = suspended.SuspendToken,
                SourceName = $"GraphOrchestrator:{context.ExecutionId}",
                NodeId = node.Id,
                Message = suspended.Message ?? $"Node {node.Id} requires approval",
                Description = $"Suspended at {DateTimeOffset.UtcNow:O}",
                Metadata = suspended.ResumeValue != null
                    ? new Dictionary<string, object?> { ["ResumeValue"] = suspended.ResumeValue }
                    : null
            };

            if (options.ActiveWaitTimeout > TimeSpan.Zero)
            {
                // CRITICAL: Register waiter BEFORE emitting to avoid race condition
                // WaitForResponseAsync registers immediately when called, not when awaited
                var waitTask = context.EventCoordinator.WaitForResponseAsync<Abstractions.Events.NodeApprovalResponseEvent>(
                    suspended.SuspendToken,
                    options.ActiveWaitTimeout,
                    ct
                );

                // Now emit the event (waiter is already registered)
                context.EventCoordinator.Emit(requestEvent);

                try
                {
                    var response = await waitTask;

                    if (response.Approved)
                    {
                        context.Log("Orchestrator",
                            $"Node {node.Id} suspension approved",
                            LogLevel.Information, nodeId: node.Id);

                        // Update outcome
                        context.AddTag($"suspend_outcome:{node.Id}", Abstractions.Execution.SuspensionOutcome.Approved.ToString());

                        // Store resume data if provided
                        if (response.ResumeData != null)
                        {
                            context.Channels[$"suspend_response:{node.Id}"].Set(response.ResumeData);
                        }

                        // Clear suspension token (use empty string since RemoveTag may not exist)
                        context.AddTag($"suspend_token:{node.Id}", "");

                        // Mark node complete so execution continues to NEXT node
                        context.MarkNodeComplete(node.Id);

                        return true; // Continue to next node
                    }
                    else
                    {
                        context.Log("Orchestrator",
                            $"Node {node.Id} suspension denied: {response.Reason}",
                            LogLevel.Warning, nodeId: node.Id);

                        // Update outcome
                        context.AddTag($"suspend_outcome:{node.Id}", Abstractions.Execution.SuspensionOutcome.Denied.ToString());

                        return false; // Halt execution
                    }
                }
                catch (TimeoutException)
                {
                    context.Log("Orchestrator",
                        $"Node {node.Id} suspension timed out after {options.ActiveWaitTimeout}",
                        LogLevel.Information, nodeId: node.Id);

                    // Update outcome
                    context.AddTag($"suspend_outcome:{node.Id}", Abstractions.Execution.SuspensionOutcome.TimedOut.ToString());

                    // Emit timeout event for observability
                    context.EventCoordinator.Emit(new Abstractions.Events.NodeApprovalTimeoutEvent
                    {
                        RequestId = suspended.SuspendToken,
                        SourceName = $"GraphOrchestrator:{context.ExecutionId}",
                        NodeId = node.Id,
                        WaitedFor = options.ActiveWaitTimeout
                    });

                    // LAYER 4: Fallback - checkpoint already saved, halt cleanly
                    return false;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // External cancellation - different from timeout
                    context.Log("Orchestrator",
                        $"Node {node.Id} suspension wait cancelled",
                        LogLevel.Information, nodeId: node.Id);

                    // Keep as Pending - can be resumed
                    throw; // Re-throw cancellation
                }
            }
            else
            {
                // No active wait - just emit and halt immediately
                context.EventCoordinator.Emit(requestEvent);
            }
        }
        else if (options.EmitEvents && context.EventCoordinator == null)
        {
            context.Log("Orchestrator",
                $"No EventCoordinator available; cannot emit suspension event for node {node.Id}",
                LogLevel.Warning, nodeId: node.Id);
        }

        // Halt execution
        return false;
    }

    /// <summary>
    /// Handles sensor polling with iterative active waiting (stack-safe, no recursion).
    /// Retries in-place until condition met or timeout.
    /// </summary>
    private async Task<bool> HandleSensorPollingAsync(
        TContext context,
        Node node,
        NodeExecutionResult.Suspended suspended,
        CancellationToken ct)
    {
        // Try to restore polling state from previous checkpoint
        var pollingState = TryRestorePollingState(context, node.Id);

        var startTime = pollingState?.StartTime ?? DateTimeOffset.UtcNow;
        var currentAttempt = pollingState?.AttemptNumber ?? 0;
        var maxWaitTime = suspended.MaxWaitTime ?? TimeSpan.FromHours(1);
        var maxRetries = suspended.MaxRetries ?? int.MaxValue;

        context.Log("Orchestrator",
            $"Node {node.Id} polling (attempt {currentAttempt}, retry after {suspended.RetryAfter})",
            LogLevel.Debug, nodeId: node.Id);

        // Emit initial polling event
        context.EventCoordinator?.Emit(new Abstractions.Events.NodePollingEvent
        {
            NodeId = node.Id,
            ExecutionId = context.ExecutionId,
            SuspendToken = suspended.SuspendToken,
            AttemptNumber = currentAttempt,
            RetryAfter = suspended.RetryAfter!.Value,
            MaxWaitTime = maxWaitTime
        });

        // ITERATIVE POLLING LOOP (stack-safe)
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

            // Update polling state in context tags (for checkpoint resume)
            var currentPollingState = new HPDAgent.Graph.Abstractions.Serialization.PollingState
            {
                StartTime = startTime,
                AttemptNumber = currentAttempt,
                SuspendToken = suspended.SuspendToken,
                RetryAfter = suspended.RetryAfter!.Value,
                MaxWaitTime = maxWaitTime
            };
            context.AddTag($"polling_info:{node.Id}", System.Text.Json.JsonSerializer.Serialize(
                currentPollingState,
                HPDAgent.Graph.Abstractions.Serialization.GraphJsonSerializerContext.Default.PollingState
            ));

            // CHECKPOINT FIRST (durability)
            if (_checkpointStore != null && context is Context.GraphContext ctxGraph)
            {
                try
                {
                    await SaveCheckpointAsync(ctxGraph, context.CurrentLayerIndex,
                        Abstractions.Checkpointing.CheckpointTrigger.Suspension, ct);
                }
                catch (Exception ex)
                {
                    context.Log("Orchestrator",
                        $"Failed to save checkpoint during polling for node {node.Id}: {ex.Message}",
                        LogLevel.Warning, nodeId: node.Id, exception: ex);
                }
            }

            // Check timeout (important after resume or after delays)
            var elapsed = DateTimeOffset.UtcNow - startTime;
            if (elapsed >= maxWaitTime)
            {
                context.Log("Orchestrator",
                    $"Node {node.Id} polling timed out after {elapsed}",
                    LogLevel.Warning, nodeId: node.Id);

                // Clear polling state
                context.RemoveTag($"polling_info:{node.Id}");

                // Create failure result and handle it
                var timeoutFailure = new NodeExecutionResult.Failure(
                    Exception: new TimeoutException($"Sensor polling timeout after {elapsed}"),
                    Severity: Abstractions.Execution.ErrorSeverity.Fatal,
                    IsTransient: false,
                    Duration: elapsed
                );

                // Emit timeout event
                context.EventCoordinator?.Emit(new Abstractions.Events.NodePollingTimeoutEvent
                {
                    NodeId = node.Id,
                    ExecutionId = context.ExecutionId,
                    SuspendToken = suspended.SuspendToken,
                    Elapsed = elapsed
                });

                // Clear polling state
                context.RemoveTag($"polling_info:{node.Id}");

                // Store result BEFORE calling HandleFailureAsync (in case it throws)
                context.Channels[$"node_result:{node.Id}"].Set(timeoutFailure);

                // Handle failure through normal error propagation system
                // This applies the node's error policy (Isolate, StopGraph, etc.)
                await HandleFailureAsync(context, node, timeoutFailure, ct);

                return true; // Continue execution if error policy allows it
            }

            // Wait before retry (always wait in polling loop since we already executed once to get here)
            await Task.Delay(suspended.RetryAfter!.Value, ct);

            // Increment attempt count BEFORE executing (so we can check if we've exceeded maxRetries)
            currentAttempt++;

            // Check max retries BEFORE executing the next attempt
            // currentAttempt now represents the NEXT execution number (including the initial one)
            // So if maxRetries=3, we allow attempts 0, 1, 2 (3 total calls)
            // currentAttempt=3 means we're about to do the 4th call, which exceeds maxRetries
            if (currentAttempt >= maxRetries)
            {
                context.Log("Orchestrator",
                    $"Node {node.Id} exceeded max retries ({maxRetries})",
                    LogLevel.Warning, nodeId: node.Id);

                // Clear polling state
                context.RemoveTag($"polling_info:{node.Id}");

                // Create failure result and handle it
                var maxRetriesFailure = new NodeExecutionResult.Failure(
                    Exception: new InvalidOperationException($"Max polling retries exceeded ({currentAttempt})"),
                    Severity: Abstractions.Execution.ErrorSeverity.Fatal,
                    IsTransient: false,
                    Duration: TimeSpan.Zero
                );

                // Emit max retries event
                context.EventCoordinator?.Emit(new Abstractions.Events.NodePollingMaxRetriesEvent
                {
                    NodeId = node.Id,
                    ExecutionId = context.ExecutionId,
                    SuspendToken = suspended.SuspendToken,
                    Attempts = currentAttempt
                });

                // Store result BEFORE calling HandleFailureAsync (in case it throws)
                context.Channels[$"node_result:{node.Id}"].Set(maxRetriesFailure);

                // Handle failure through normal error propagation system
                // This applies the node's error policy (Isolate, StopGraph, etc.)
                await HandleFailureAsync(context, node, maxRetriesFailure, ct);

                return true; // Continue execution if error policy allows it
            }

            // Re-execute node (polling check)
            context.Log("Orchestrator",
                $"Node {node.Id} retrying polling check (attempt {currentAttempt})",
                LogLevel.Debug, nodeId: node.Id);

            // State back to Running for retry
            context.AddTag($"node_state:{node.Id}", Abstractions.Execution.NodeState.Running.ToString());

            // Re-execute handler directly
            var handler = ResolveHandler(node);
            if (handler == null)
            {
                throw new InvalidOperationException($"No handler found for node '{node.Id}' with handler name '{node.HandlerName}'");
            }

            var inputs = PrepareInputs(context, node);
            var retryResult = await handler.ExecuteAsync(context, inputs, ct);

            // Check result - loop continues if still suspended, exits otherwise
            if (retryResult is NodeExecutionResult.Suspended retrySuspended &&
                (retrySuspended.Reason == Abstractions.Execution.SuspendReason.PollingCondition ||
                 retrySuspended.Reason == Abstractions.Execution.SuspendReason.ResourceWait))
            {
                // Still suspended - continue loop
                continue;
            }

            // Not suspended anymore (success, failure, or different suspension type)
            // Clear polling state BEFORE handling result (ensures cleanup happens before any checkpoint)
            context.RemoveTag($"polling_info:{node.Id}");

            // Handle the final result
            return await HandleNodeResultAsync(context, node, retryResult, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Clean up polling state on cancellation
            context.RemoveTag($"polling_info:{node.Id}");

            // Create cancellation result
            var cancelResult = new NodeExecutionResult.Cancelled(
                Reason: Abstractions.Execution.CancellationReason.UserRequested,
                Message: "Polling cancelled by user"
            );

            // Handle cancellation
            HandleCancelled(context, node, cancelResult);

            // Re-throw to propagate cancellation
            throw;
        }
    }

    /// <summary>
    /// Try to restore polling state from checkpoint tags.
    /// Returns null if no polling state found.
    /// </summary>
    private Abstractions.Serialization.PollingState? TryRestorePollingState(TContext context, string nodeId)
    {
        var tagKey = $"polling_info:{nodeId}";
        if (context.Tags.TryGetValue(tagKey, out var values) && values.Count > 0)
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize(
                    values.First(),
                    HPDAgent.Graph.Abstractions.Serialization.GraphJsonSerializerContext.Default.PollingState);
            }
            catch (System.Text.Json.JsonException ex)
            {
                context.Log("Orchestrator",
                    $"Failed to deserialize polling state for node {nodeId}: {ex.Message}",
                    LogLevel.Warning, nodeId: nodeId, exception: ex);
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// Wait for all polling nodes to reach terminal state before iteration can complete.
    /// This prevents premature back-edge evaluation while nodes are still polling.
    /// Uses intelligent delay to avoid busy-waiting.
    /// </summary>
    private async Task WaitForPollingNodesToResolveAsync(TContext context, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Get all nodes currently in polling state
            var pollingNodes = context.GetNodesInState(NodeState.Polling);

            if (pollingNodes.Count == 0)
            {
                // No nodes polling - iteration can complete
                return;
            }

            context.Log("Orchestrator",
                $"Iteration waiting for {pollingNodes.Count} polling node(s) to resolve: {string.Join(", ", pollingNodes)}",
                LogLevel.Debug);

            // Intelligent delay: Calculate next retry time across all polling nodes
            var nextRetryTime = GetEarliestPollingNodeRetryTime(context, pollingNodes);
            var delay = nextRetryTime - DateTimeOffset.UtcNow;

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, ct);
            }
            else
            {
                // Small delay to avoid tight loop
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            }

            // NOTE: Polling nodes will transition to terminal states via HandleSensorPollingAsync
            // (running in parallel). This loop just waits for those transitions.
        }
    }

    /// <summary>
    /// Get earliest retry time across all polling nodes (intelligent delay optimization).
    /// </summary>
    private DateTimeOffset GetEarliestPollingNodeRetryTime(TContext context, IReadOnlyList<string> pollingNodeIds)
    {
        var earliestRetryTime = DateTimeOffset.MaxValue;

        foreach (var nodeId in pollingNodeIds)
        {
            // Try to get polling info from tags
            var tagKey = $"polling_info:{nodeId}";
            if (context.Tags.TryGetValue(tagKey, out var values) && values.Count > 0)
            {
                try
                {
                    var pollingState = System.Text.Json.JsonSerializer.Deserialize(
                        values.First(),
                        HPDAgent.Graph.Abstractions.Serialization.GraphJsonSerializerContext.Default.PollingState);
                    if (pollingState != null)
                    {
                        // Calculate next retry time for this node
                        // Note: We don't have LastAttempt in PollingState, so use conservative estimate
                        var nextRetry = DateTimeOffset.UtcNow + pollingState.RetryAfter;
                        if (nextRetry < earliestRetryTime)
                        {
                            earliestRetryTime = nextRetry;
                        }
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // Failed to deserialize - ignore this node
                }
            }
        }

        // If no valid polling states found, return current time (will trigger small delay)
        return earliestRetryTime == DateTimeOffset.MaxValue
            ? DateTimeOffset.UtcNow
            : earliestRetryTime;
    }

    /// <summary>
    /// Handle node result after polling completes or fails.
    /// </summary>
    private async Task<bool> HandleNodeResultAsync(
        TContext context,
        Node node,
        NodeExecutionResult result,
        CancellationToken ct)
    {
        switch (result)
        {
            case NodeExecutionResult.Success success:
                HandleSuccess(context, node, success);
                return true; // Continue to next node

            case NodeExecutionResult.Failure failure:
                await HandleFailureAsync(context, node, failure, ct);
                return false; // Halt execution

            case NodeExecutionResult.Skipped skipped:
                HandleSkipped(context, node, skipped);
                return false; // Halt execution

            case NodeExecutionResult.Suspended s:
                // Different type of suspension (e.g., HITL)
                return await HandleSuspendedAsync(context, node, s, ct);

            case NodeExecutionResult.Cancelled cancelled:
                HandleCancelled(context, node, cancelled);
                return false; // Halt execution

            default:
                context.MarkNodeComplete(node.Id);
                return true; // Continue for other result types
        }
    }

    /// <summary>
    /// Polling state stored in context tags for checkpoint resume.
    /// </summary>

    /// <summary>
    /// Saves checkpoint specifically for suspension with suspension-related metadata.
    /// </summary>
    private async Task SaveSuspensionCheckpointAsync(
        TContext context,
        Node node,
        NodeExecutionResult.Suspended suspended,
        CancellationToken cancellationToken)
    {
        if (_checkpointStore == null || context is not Context.GraphContext graphContext)
            return;

        // Extract all node outputs from channels for checkpoint restoration
        var nodeOutputs = new Dictionary<string, object>();
        var nodeStateMetadata = new Dictionary<string, Abstractions.Checkpointing.NodeStateMetadata>();

        foreach (var channelName in context.Channels.ChannelNames)
        {
            if (channelName.StartsWith("node_output:"))
            {
                var nodeId = channelName.Substring("node_output:".Length);

                // Only include outputs from completed nodes
                if (context.CompletedNodes.Contains(nodeId))
                {
                    try
                    {
                        var output = context.Channels[channelName].Get<object>();
                        if (output != null)
                        {
                            nodeOutputs[channelName] = output;

                            // Capture node version for compatibility checking
                            var completedNode = context.Graph.GetNode(nodeId);
                            if (completedNode != null)
                            {
                                nodeStateMetadata[nodeId] = new Abstractions.Checkpointing.NodeStateMetadata
                                {
                                    NodeId = nodeId,
                                    Version = completedNode.Version,
                                    StateJson = System.Text.Json.JsonSerializer.Serialize(
                                        output,
                                        HPDAgent.Graph.Abstractions.Serialization.GraphJsonSerializerContext.Default.DictionaryStringObject
                                    ),
                                    CapturedAt = DateTimeOffset.UtcNow
                                };
                            }
                        }
                    }
                    catch
                    {
                        // Skip channels that can't be retrieved as object
                    }
                }
            }
        }

        var checkpoint = new Abstractions.Checkpointing.GraphCheckpoint
        {
            CheckpointId = Guid.NewGuid().ToString("N"),
            ExecutionId = context.ExecutionId,
            GraphId = context.Graph.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedNodes = context.CompletedNodes,
            NodeOutputs = nodeOutputs,
            NodeStateMetadata = nodeStateMetadata,
            ContextJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                ExecutionId = context.ExecutionId,
                CompletedNodes = context.CompletedNodes,
                CurrentLayerIndex = graphContext.CurrentLayerIndex,
                SuspendedNodeId = node.Id,
                SuspendToken = suspended.SuspendToken
            }),
            Metadata = new Abstractions.Checkpointing.CheckpointMetadata
            {
                Trigger = Abstractions.Checkpointing.CheckpointTrigger.Suspension,
                CompletedLayer = graphContext.CurrentLayerIndex,
                SuspendedNodeId = node.Id,
                SuspendToken = suspended.SuspendToken,
                SuspensionOutcome = Abstractions.Execution.SuspensionOutcome.Pending
            }
        };

        await _checkpointStore.SaveCheckpointAsync(checkpoint, cancellationToken);
    }

    private void HandleCancelled(TContext context, Node node, NodeExecutionResult.Cancelled cancelled)
    {
        // Update state to Cancelled
        context.AddTag($"node_state:{node.Id}", Abstractions.Execution.NodeState.Cancelled.ToString());

        context.Log("Orchestrator",
            $"Node {node.Id} cancelled: {cancelled.Reason} - {cancelled.Message}",
            LogLevel.Warning, nodeId: node.Id);

        throw new OperationCanceledException($"Node {node.Id} was cancelled: {cancelled.Reason}");
    }

    /// <summary>
    /// Checks if a node should be skipped due to a failed dependency.
    /// Used for SkipDependents error propagation mode.
    /// </summary>
    private bool ShouldSkipDueToFailedDependency(TContext context, Node node)
    {
        // Get all incoming edges for this node
        var incomingEdges = context.Graph.GetIncomingEdges(node.Id);

        foreach (var edge in incomingEdges)
        {
            // Check if the source node failed
            if (context.InternalFailedNodes.ContainsKey(edge.From))
            {
                var failedNode = context.Graph.GetNode(edge.From);
                if (failedNode == null) continue;

                // Check the error policy of the failed node
                var policy = failedNode.ErrorPolicy;
                if (policy == null || policy.Mode != PropagationMode.SkipDependents)
                    continue;

                // Check if this node is in the affected list (or if no list specified, all dependents affected)
                if (policy.AffectedNodes != null && policy.AffectedNodes.Count > 0)
                {
                    if (!policy.AffectedNodes.Contains(node.Id))
                        continue; // This node is not in the affected list
                }

                // This node should be skipped
                context.Log("Orchestrator",
                    $"Skipping node {node.Id} due to failed dependency {edge.From}",
                    LogLevel.Warning, nodeId: node.Id);

                // Mark as skipped in context
                context.Channels[$"skip_reason:{node.Id}"].Set(SkipReason.DependencyFailed.ToString());
                context.MarkNodeComplete(node.Id); // Mark as "complete" so it doesn't block further processing

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Evaluates whether a node should be skipped based on incoming edge conditions.
    /// Returns (shouldSkip, detailsForLogging).
    /// </summary>
    /// <remarks>
    /// A node should be skipped if ALL of the following are true:
    /// - It has incoming edges (not a START node)
    /// - All source nodes have completed
    /// - No incoming edge conditions evaluate to true (including unconditional and default edges)
    /// </remarks>
    private async Task<(bool ShouldSkip, string Details)> EvaluateSkipConditions(TContext context, Node node, CancellationToken cancellationToken)
    {
        // Get all incoming edges
        var incomingEdges = context.Graph.GetIncomingEdges(node.Id);

        // Emit diagnostic for skip condition evaluation
        context.EmitDebug("EvaluateSkipConditions",
            $"Evaluating {node.Id}: CompletedNodes=[{string.Join(",", context.CompletedNodes)}], Channels=[{string.Join(",", context.Channels.ChannelNames)}]",
            nodeId: node.Id);

        // No incoming edges = always execute (START node or explicit entry point)
        if (incomingEdges.Count == 0)
            return (false, "No incoming edges");

        var evaluatedEdges = new List<string>();
        var hasAnyCompletedSource = false;
        var failedEdgeSources = new List<string>();

        foreach (var edge in incomingEdges)
        {
            // Skip edges from incomplete nodes (they haven't run yet - topology will handle scheduling)
            if (!context.IsNodeComplete(edge.From))
            {
                evaluatedEdges.Add($"{edge.From}→{edge.To}: source incomplete");
                context.EmitTrace("EvaluateSkipConditions", $"Edge {edge.From}→{edge.To}: source NOT complete", nodeId: node.Id);
                continue;
            }

            hasAnyCompletedSource = true;

            // ========== PHASE 4: TEMPORAL OPERATORS - Edge-level temporal checks ==========

            //  NEW: Edge DELAY evaluation
            if (edge.Delay.HasValue)
            {
                var delayDuration = edge.Delay.Value;

                // Smart threshold: < 30s = synchronous Task.Delay, >= 30s = checkpoint and suspend
                if (delayDuration < TimeSpan.FromSeconds(30))
                {
                    // Short delay - execute synchronously
                    context.Log("Orchestrator",
                        $"Edge {edge.From}→{edge.To}: short delay {delayDuration.TotalSeconds}s (synchronous)",
                        LogLevel.Debug, nodeId: node.Id);

                    await Task.Delay(delayDuration, cancellationToken);
                }
                else
                {
                    // Long delay - checkpoint and suspend
                    context.Log("Orchestrator",
                        $"Edge {edge.From}→{edge.To}: long delay {delayDuration.TotalSeconds}s (suspending)",
                        LogLevel.Information, nodeId: node.Id);

                    throw new GraphSuspendedException(
                        node.Id,
                        Guid.NewGuid().ToString(),
                        $"Edge {edge.From}→{edge.To} delay: {delayDuration}");
                }
            }

            //  NEW: Edge SCHEDULE evaluation (cron-based)
            if (edge.Schedule != null)
            {
                var schedule = edge.Schedule;
                var now = DateTimeOffset.UtcNow;

                // Parse cron expression and get next occurrence
                // Note: Requires Cronos NuGet package
                var cronExpr = Cronos.CronExpression.Parse(schedule.CronExpression);
                var timeZone = schedule.TimeZone ?? TimeZoneInfo.Utc;

                // Cronos requires DateTime.Kind = Utc, so pass now.UtcDateTime
                var nextOccurrence = cronExpr.GetNextOccurrence(now.UtcDateTime, timeZone);

                if (nextOccurrence == null)
                {
                    throw new InvalidOperationException(
                        $"Edge {edge.From}→{edge.To}: cron expression '{schedule.CronExpression}' has no next occurrence");
                }

                var nextTime = new DateTimeOffset(nextOccurrence.Value, timeZone.GetUtcOffset(nextOccurrence.Value));
                var timeUntilNext = nextTime - now;

                // Check if we're within tolerance window
                var isWithinWindow = Math.Abs(timeUntilNext.TotalMilliseconds) <= schedule.Tolerance.TotalMilliseconds;

                if (!isWithinWindow)
                {
                    // Not within schedule window - suspend until next occurrence
                    context.Log("Orchestrator",
                        $"Edge {edge.From}→{edge.To}: schedule not satisfied (next: {nextTime:yyyy-MM-dd HH:mm:ss zzz})",
                        LogLevel.Information, nodeId: node.Id);

                    throw new GraphSuspendedException(
                        node.Id,
                        Guid.NewGuid().ToString(),
                        $"Edge {edge.From}→{edge.To} schedule: waiting until {nextTime}");
                }

                // Within window - check additional condition if present
                if (schedule.AdditionalCondition != null)
                {
                    var additionalConditionMet = await schedule.AdditionalCondition(context);
                    if (!additionalConditionMet)
                    {
                        context.Log("Orchestrator",
                            $"Edge {edge.From}→{edge.To}: schedule satisfied but additional condition not met",
                            LogLevel.Information, nodeId: node.Id);

                        // Retry after tolerance period
                        throw new GraphSuspendedException(
                            node.Id,
                            Guid.NewGuid().ToString(),
                            $"Edge {edge.From}→{edge.To}: schedule additional condition not met");
                    }
                }

                context.Log("Orchestrator",
                    $"Edge {edge.From}→{edge.To}: schedule satisfied at {now:yyyy-MM-dd HH:mm:ss zzz}",
                    LogLevel.Debug, nodeId: node.Id);
            }

            //  NEW: Edge RETRY POLICY evaluation (polling)
            if (edge.RetryPolicy != null)
            {
                var policy = edge.RetryPolicy;

                var retryConditionMet = policy.RetryCondition != null
                    ? await policy.RetryCondition(context)
                    : true;

                if (!retryConditionMet)
                {
                    // Condition not met -> suspend and retry later
                    context.Log("Orchestrator",
                        $"Edge {edge.From}→{edge.To}: retry condition not met, suspending for {policy.RetryInterval.TotalSeconds}s",
                        LogLevel.Information, nodeId: node.Id);

                    throw new GraphSuspendedException(
                        node.Id,
                        Guid.NewGuid().ToString(),
                        $"Edge {edge.From}→{edge.To}: retry condition not met");
                }

                context.Log("Orchestrator",
                    $"Edge {edge.From}→{edge.To}: retry condition satisfied",
                    LogLevel.Debug, nodeId: node.Id);
            }

            // Get source node outputs
            var channelName = $"node_output:{edge.From}";
            if (!context.Channels.Contains(channelName))
            {
                evaluatedEdges.Add($"{edge.From}→{edge.To}: no outputs");
                failedEdgeSources.Add(edge.From);
                context.EmitDebug("EvaluateSkipConditions", $"Edge {edge.From}→{edge.To}: channel {channelName} NOT found", nodeId: node.Id);
                continue;
            }

            var outputs = context.Channels[channelName].Get<Dictionary<string, object>>();
            context.EmitDebug("EvaluateSkipConditions",
                $"Edge {edge.From}→{edge.To}: outputs={outputs?.Count ?? -1} keys=[{(outputs != null ? string.Join(",", outputs.Keys) : "null")}]",
                nodeId: node.Id);

            // Unconditional edge (no condition) = active path exists
            if (edge.Condition == null)
            {
                evaluatedEdges.Add($"{edge.From}→{edge.To}: unconditional (ACTIVE)");

                // Emit edge traversed event
                context.EventCoordinator?.Emit(new Abstractions.Events.EdgeTraversedEvent
                {
                    FromNodeId = edge.From,
                    ToNodeId = edge.To,
                    HasCondition = false,
                    GraphContext = CreateGraphExecutionContext(context)
                });

                return (false, string.Join("; ", evaluatedEdges));
            }

            // Default edge = active (fallback path)
            if (edge.Condition.Type == ConditionType.Default)
            {
                evaluatedEdges.Add($"{edge.From}→{edge.To}: default edge (ACTIVE)");

                // Emit edge traversed event
                context.EventCoordinator?.Emit(new Abstractions.Events.EdgeTraversedEvent
                {
                    FromNodeId = edge.From,
                    ToNodeId = edge.To,
                    HasCondition = true,
                    ConditionDescription = "default",
                    GraphContext = CreateGraphExecutionContext(context)
                });

                return (false, string.Join("; ", evaluatedEdges));
            }

            // Evaluate the condition (use new overload that supports upstream conditions)
            var conditionMet = ConditionEvaluator.Evaluate(edge.Condition, outputs, context, edge);
            var conditionDesc = edge.Condition.GetDescription() ?? edge.Condition.Type.ToString();

            // Emit condition evaluation diagnostic
            var actualVal = outputs != null && edge.Condition?.Field != null && outputs.TryGetValue(edge.Condition.Field, out var fv) ? fv?.ToString() : "(not found)";
            context.EmitDebug("EvaluateSkipConditions",
                $"Condition {conditionDesc}: field={edge.Condition?.Field}, actual='{actualVal}', expected='{edge.Condition?.Value}', result={conditionMet}",
                nodeId: node.Id);

            if (conditionMet)
            {
                evaluatedEdges.Add($"{edge.From}→{edge.To}: {conditionDesc} (ACTIVE)");

                // Emit edge traversed event
                context.EventCoordinator?.Emit(new Abstractions.Events.EdgeTraversedEvent
                {
                    FromNodeId = edge.From,
                    ToNodeId = edge.To,
                    HasCondition = true,
                    ConditionDescription = conditionDesc,
                    GraphContext = CreateGraphExecutionContext(context)
                });

                return (false, string.Join("; ", evaluatedEdges));
            }
            else
            {
                evaluatedEdges.Add($"{edge.From}→{edge.To}: {conditionDesc} (not met)");
                failedEdgeSources.Add(edge.From);

                // Get actual value for debugging
                string? actualValue = null;
                if (edge.Condition.Field != null && outputs != null &&
                    outputs.TryGetValue(edge.Condition.Field, out var fieldValue))
                {
                    actualValue = fieldValue?.ToString();
                }

                // Emit edge condition failed event
                context.EventCoordinator?.Emit(new Abstractions.Events.EdgeConditionFailedEvent
                {
                    FromNodeId = edge.From,
                    ToNodeId = edge.To,
                    ConditionDescription = conditionDesc,
                    ActualValue = actualValue,
                    ExpectedValue = edge.Condition.Value?.ToString(),
                    GraphContext = CreateGraphExecutionContext(context)
                });
            }
        }

        // If no source nodes have completed yet, don't skip - wait for topology
        if (!hasAnyCompletedSource)
        {
            return (false, "No completed source nodes yet");
        }

        // No active paths found from any completed source → skip this node
        // Emit node skipped event
        context.EventCoordinator?.Emit(new Abstractions.Events.NodeSkippedEvent
        {
            NodeId = node.Id,
            Reason = "All incoming edge conditions evaluated to false",
            PotentialSourceNodes = failedEdgeSources.Distinct().ToList(),
            GraphContext = CreateGraphExecutionContext(context)
        });

        return (true, string.Join("; ", evaluatedEdges));
    }

    /// <summary>
    /// Execute a sub-graph node by recursively running the nested graph.
    /// </summary>
    private async Task ExecuteSubGraphAsync(TContext context, Node node, CancellationToken cancellationToken)
    {
        if (node.SubGraph == null)
        {
            throw new InvalidOperationException($"SubGraph node '{node.Id}' has no SubGraph definition");
        }

        context.Log("Orchestrator", $"Entering sub-graph: {node.SubGraph.Name}", LogLevel.Information, nodeId: node.Id);

        // Prepare inputs for sub-graph
        var inputs = PrepareInputs(context, node);

        // Create isolated context for sub-graph execution, preserving TContext type
        var subGraphContext = (TContext)context.CreateIsolatedCopy();

        // For GraphContext, create new instance with replaced graph while preserving TContext type
        // This works because CreateIsolatedCopy() returns the same type as the source
        if (subGraphContext is Context.GraphContext baseContext)
        {
            // Create a new GraphContext-based instance with the sub-graph
            // If TContext is a derived type, this will preserve the derived type through CreateIsolatedCopy
            var subGraphContextWithGraph = new Context.GraphContext(
                executionId: context.ExecutionId + $":{node.Id}",
                graph: node.SubGraph,
                services: context.Services,
                channels: baseContext.Channels,
                managed: baseContext.Managed
            );

            // Pass inputs to sub-graph via channels
            foreach (var input in inputs.GetAll())
            {
                subGraphContextWithGraph.Channels[$"input:{input.Key}"].Set(input.Value);
            }

            // If TContext is exactly GraphContext, use it directly
            // If TContext is a derived type, we need to preserve custom properties
            // For now, we execute with GraphContext - derived types should override ExecuteSubGraphAsync if needed
            var orchestrator = new GraphOrchestrator<Context.GraphContext>(
                _serviceProvider,
                _cacheStore,
                _fingerprintCalculator,
                _checkpointStore,
                _defaultSuspensionOptions,
                _affectedNodeDetector,
                _snapshotStore,
                _artifactRegistry,
                _graphRegistry,
                _maxLayerConcurrency
            );
            await orchestrator.ExecuteAsync(subGraphContextWithGraph, cancellationToken);

            // Collect outputs from sub-graph
            var outputs = new Dictionary<string, object>();
            foreach (var chName in subGraphContextWithGraph.Channels.ChannelNames)
            {
                if (chName.StartsWith("output:"))
                {
                    var outputKey = chName.Substring("output:".Length);
                    var value = subGraphContextWithGraph.Channels[chName].Get<object>();
                    if (value != null)
                    {
                        outputs[outputKey] = value;
                    }
                }
            }

            // Store sub-graph outputs in parent context
            var outputChannelName = $"node_output:{node.Id}";
            context.Channels[outputChannelName].Set(outputs);

            // Create and store Success result (for upstream condition evaluation)
            var duration = DateTimeOffset.UtcNow - (context.Tags.TryGetValue($"node_start_time:{node.Id}", out var startTimes) && startTimes.Count > 0
                ? DateTimeOffset.Parse(startTimes.First())
                : DateTimeOffset.UtcNow);
            var successResult = NodeExecutionResult.Success.Single(
                output: outputs,
                duration: duration > TimeSpan.Zero ? duration : TimeSpan.FromMilliseconds(1),
                metadata: new NodeExecutionMetadata()
            );
            context.Channels[$"node_result:{node.Id}"].Set(successResult);

            // Update state to Succeeded
            context.AddTag($"node_state:{node.Id}", Abstractions.Execution.NodeState.Succeeded.ToString());

            // Mark node as complete
            context.MarkNodeComplete(node.Id);

            context.Log("Orchestrator", $"Exiting sub-graph: {node.SubGraph.Name} with {outputs.Count} outputs",
                LogLevel.Information, nodeId: node.Id);
        }
        else
        {
            throw new InvalidOperationException($"SubGraph execution requires TContext to be or derive from GraphContext. Actual type: {typeof(TContext).Name}");
        }
    }

    /// <summary>
    /// Execute a partitioned node by converting partitions to Map items.
    /// Delegates to existing ExecuteMapAsync infrastructure (Phase 2: Data Orchestration Primitives).
    /// </summary>
    private async Task ExecutePartitionedNodeAsync(TContext context, Node node, CancellationToken cancellationToken)
    {
        if (node.Partitions == null)
        {
            // Not partitioned -> normal execution
            await ExecuteNodeAsync(context, node, cancellationToken);
            return;
        }

        // If context already has CurrentPartition set (e.g., from MaterializeAsync),
        // execute only for that partition instead of all partitions
        if (context.CurrentPartition != null)
        {
            context.Log("Orchestrator",
                $"Node {node.Id} executing for single partition {context.CurrentPartition.Dimensions.FirstOrDefault()} (MaterializeAsync mode)",
                LogLevel.Debug, nodeId: node.Id);

            // Execute the node as a regular handler node (remove Partitions to avoid recursion)
            // The CurrentPartition is already set in context for artifact registration
            var singlePartitionNode = node with { Partitions = null };
            await ExecuteNodeAsync(context, singlePartitionNode, cancellationToken);

            // Mark original node as complete
            context.MarkNodeComplete(node.Id);
            return;
        }

        // 1. Resolve partition snapshot (with caching)
        var snapshot = await ResolvePartitionsAsync(node, cancellationToken);

        if (snapshot.KeyCount == 0)
        {
            context.Log("Orchestrator",
                $"Node {node.Id} has no partitions to process (empty partition definition)",
                LogLevel.Warning, nodeId: node.Id);
            return;
        }

        context.Log("Orchestrator",
            $"Node {node.Id} will execute for {snapshot.KeyCount} partitions (snapshot: {snapshot.SnapshotHash[..8]}...)",
            LogLevel.Information, nodeId: node.Id);

        var partitionKeys = snapshot.Keys.ToList();

        // 2. Convert to Map-compatible items (with partition marker interface)
        var mapItems = partitionKeys
            .Select((pk, idx) => new PartitionMapItem(idx, pk) as IPartitionMapItem)
            .ToList();

        // 3. Create synthetic Map node wrapping the partitioned node
        var mapNode = new Node
        {
            Id = $"{node.Id}:__partition_map",
            Name = $"{node.Name} (Partitioned)",
            Type = NodeType.Map,
            MapProcessorGraph = CreateSingleNodeGraph(node),
            MaxParallelMapTasks = node.MaxParallelExecutions ?? Environment.ProcessorCount,
            MapErrorMode = MapErrorMode.FailFast
        };

        // 4. Pre-populate input channel with partition items
        var inputChannelName = $"__partition_input:{node.Id}";
        context.Channels[inputChannelName].Set(mapItems);

        // Temporarily override MapInputChannel
        mapNode = mapNode with { MapInputChannel = inputChannelName };

        // 5. Delegate to EXISTING ExecuteMapAsync (which sets CurrentPartition via IPartitionMapItem detection!)
        await ExecuteMapAsync(context, mapNode, cancellationToken);

        // Mark original node as complete (synthetic map node completion doesn't count)
        context.MarkNodeComplete(node.Id);
    }

    /// <summary>
    /// Resolve partitions for a node with caching.
    /// Uses partition snapshot cache to avoid repeated resolution within the same execution.
    /// </summary>
    private async Task<PartitionSnapshot> ResolvePartitionsAsync(
        Node node,
        CancellationToken cancellationToken)
    {
        if (node.Partitions == null)
        {
            // Non-partitioned node: return default snapshot with single default partition
            return new PartitionSnapshot
            {
                Keys = Array.Empty<PartitionKey>(),
                SnapshotHash = "Default"
            };
        }

        // Use node ID + partition definition type as cache key
        // This ensures different nodes with same partition definition share cache
        var cacheKey = $"{node.Id}:{node.Partitions.GetType().Name}";

        if (_partitionSnapshotCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        // Resolve partitions asynchronously (may involve I/O for dynamic partitions)
        var snapshot = await node.Partitions.ResolveAsync(_serviceProvider, cancellationToken);

        // Cache for this execution
        _partitionSnapshotCache[cacheKey] = snapshot;

        return snapshot;
    }

    /// <summary>
    /// Wrap partitioned node in minimal processor graph.
    /// </summary>
    private Abstractions.Graph.Graph CreateSingleNodeGraph(Node node)
    {
        // Preserve the original node ID so artifacts are registered correctly
        var handlerId = node.Id;

        return new Abstractions.Graph.Graph
        {
            Id = $"{node.Id}:__processor",
            Name = $"{node.Name} Processor",
            Version = "1.0",
            Nodes =
            [
                new Node { Id = "start", Name = "Start", Type = NodeType.Start },
                node with { Partitions = null },  // Keep original ID, clear Partitions to prevent recursion
                new Node { Id = "end", Name = "End", Type = NodeType.End }
            ],
            Edges =
            [
                new Edge { From = "start", To = handlerId },
                new Edge { From = handlerId, To = "end" }
            ],
            EntryNodeId = "start",
            ExitNodeId = "end"
        };
    }

    /// <summary>
    /// Execute a map node by iterating over input collection and running processor graph for each item.
    /// </summary>
    private async Task ExecuteMapAsync(TContext context, Node node, CancellationToken cancellationToken)
    {
        // VALIDATION
        ValidateMapNode(node);

        context.Log("Orchestrator", $"Entering map: {node.Name}", LogLevel.Information, nodeId: node.Id);

        // Determine input channel
        var inputChannelName = node.MapInputChannel;
        if (string.IsNullOrEmpty(inputChannelName))
        {
            // Default: read from previous node's output
            var incomingEdges = context.Graph.Edges.Where(e => e.To == node.Id).ToList();
            if (incomingEdges.Count > 0)
            {
                inputChannelName = $"node_output:{incomingEdges[0].From}";
            }
            else
            {
                throw new InvalidOperationException($"Map node '{node.Id}' has no MapInputChannel and no incoming edges");
            }
        }

        // Read input items
        var inputData = context.Channels[inputChannelName].Get<object>();
        if (inputData == null)
        {
            throw new InvalidOperationException($"Map node '{node.Id}' input channel '{inputChannelName}' is null");
        }

        // If the input is a Dictionary (handler output format), extract the "output" key
        object? actualInputData = inputData;
        if (inputData is Dictionary<string, object> handlerOutput)
        {
            if (handlerOutput.TryGetValue("output", out var outputValue))
            {
                actualInputData = outputValue;
            }
            else if (handlerOutput.Count == 1)
            {
                // If there's only one key, use its value
                actualInputData = handlerOutput.Values.First();
            }
            else
            {
                throw new InvalidOperationException($"Map node '{node.Id}' input channel '{inputChannelName}' contains a dictionary but no 'output' key. Available keys: {string.Join(", ", handlerOutput.Keys)}");
            }
        }

        if (actualInputData is not System.Collections.IEnumerable inputItems)
        {
            throw new InvalidOperationException($"Map node '{node.Id}' input channel '{inputChannelName}' must be IEnumerable, got {actualInputData?.GetType().FullName ?? "null"}");
        }

        var itemList = inputItems.Cast<object>().ToList();

        // Handle empty input
        if (itemList.Count == 0)
        {
            context.Log("Orchestrator", $"Map node '{node.Id}' has empty input, returning empty results", LogLevel.Information, nodeId: node.Id);
            var emptyOutputChannelName = node.MapOutputChannel ?? $"node_output:{node.Id}";
            context.Channels[emptyOutputChannelName].Set(new List<object>());
            context.MarkNodeComplete(node.Id);
            return;
        }

        // Note: Optional type validation removed for Native AOT compatibility
        // Type.GetType() requires runtime reflection which is not AOT-compatible
        // Type safety is enforced at handler level instead

        // Determine concurrency
        var maxConcurrency = node.MaxParallelMapTasks ?? 0;
        if (maxConcurrency == 0)
        {
            maxConcurrency = Environment.ProcessorCount;
        }

        context.Log("Orchestrator",
            $"Map node '{node.Id}' processing {itemList.Count} items with concurrency={maxConcurrency}",
            LogLevel.Information, nodeId: node.Id);

        // Error mode
        var errorMode = node.MapErrorMode ?? Abstractions.Graph.MapErrorMode.FailFast;

        // Initialize item states for polling support
        var itemStates = new System.Collections.Concurrent.ConcurrentDictionary<int, MapItemState>();
        var startTime = DateTimeOffset.UtcNow;

        for (int i = 0; i < itemList.Count; i++)
        {
            itemStates[i] = new MapItemState
            {
                Status = MapItemStatus.Pending,
                StartTime = DateTimeOffset.UtcNow
            };
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Exception? firstException = null; // Track first exception for FailFast mode

        try
        {
            // OUTER POLLING LOOP: Continue until all items reach terminal state
            while (itemStates.Values.Any(s => !s.IsTerminal()))
            {
                cts.Token.ThrowIfCancellationRequested();

                // Get items ready to execute (pending or ready to retry)
                var itemsToExecute = itemStates
                    .Where(kvp => ShouldExecuteMapItem(kvp.Value))
                    .Select(kvp => kvp.Key)
                    .ToList();

                if (itemsToExecute.Count == 0)
                {
                    // No items ready - calculate next wake-up time (intelligent delay)
                    var nextRetryTime = GetEarliestRetryTime(itemStates);
                    var delay = nextRetryTime - DateTimeOffset.UtcNow;

                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cts.Token);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token); // Avoid tight loop
                    }
                    continue;
                }

                // Execute ready items in parallel (with dynamic parallelism)
                var effectiveParallelism = Math.Min(itemsToExecute.Count, maxConcurrency);

                await Parallel.ForEachAsync(itemsToExecute,
                    new ParallelOptions { MaxDegreeOfParallelism = effectiveParallelism, CancellationToken = cts.Token },
                    async (index, ct) =>
                    {
                        var item = itemList[index];
                        var itemState = itemStates[index];

                        // Update state to Running
                        context.AddTag($"node_state:map:{node.Id}[{index}]", NodeState.Running.ToString());
                        itemState.LastAttempt = DateTimeOffset.UtcNow;

                        try
                        {
                            // Create isolated context for this item
                            var mapContext = (TContext)context.CreateIsolatedCopy();

                            if (mapContext is Context.GraphContext baseContext)
                            {
                                // Select processor graph based on item
                                Abstractions.Graph.Graph processorGraph = SelectProcessorGraph(node, item, context);

                                // Create context with processor graph
                                var mapContextWithGraph = new Context.GraphContext(
                                    executionId: context.ExecutionId + $":{node.Id}[{index}]",
                                    graph: processorGraph,
                                    services: context.Services,
                                    channels: baseContext.Channels,
                                    managed: baseContext.Managed
                                );

                                // Write item to input channel
                                mapContextWithGraph.Channels["input:item"].Set(item);
                                mapContextWithGraph.Channels["input:index"].Set(index);

                                //  NEW (3 lines): Set partition on context if item is partition-aware (Phase 2)
                                if (item is IPartitionMapItem partitionItem)
                                {
                                    mapContextWithGraph.SetCurrentPartition(partitionItem.PartitionKey);
                                }

                                // Execute processor graph
                                var orchestrator = new GraphOrchestrator<Context.GraphContext>(
                                    _serviceProvider,
                                    _cacheStore,
                                    _fingerprintCalculator,
                                    _checkpointStore,
                                    _defaultSuspensionOptions,
                                    _affectedNodeDetector,
                                    _snapshotStore,
                                    _artifactRegistry,
                                    _graphRegistry,
                                    _maxLayerConcurrency
                                );
                                await orchestrator.ExecuteAsync(mapContextWithGraph, ct);

                                // Check if any nodes failed in the processor graph
                                // A graph with failures will have at least one handler node with a Failure result
                                var hasFailures = false;
                                NodeExecutionResult.Failure? failureResult = null;

                                // Check if the processor graph execution was successful
                                // We consider it successful if the graph completed without errors
                                // The nested orchestrator handles polling internally, so when ExecuteAsync completes,
                                // all polling has resolved and we have final results

                                // Check graph completion status
                                if (!mapContextWithGraph.IsComplete)
                                {
                                    // Graph didn't complete - this is an error
                                    hasFailures = true;
                                    failureResult = new NodeExecutionResult.Failure(
                                        Exception: new InvalidOperationException($"Processor graph for item {index} did not complete"),
                                        Severity: ErrorSeverity.Fatal,
                                        IsTransient: false,
                                        Duration: DateTimeOffset.UtcNow - itemState.StartTime
                                    );
                                }
                                else
                                {
                                    // Graph completed - check if any handler nodes failed
                                    var handlerNodes = processorGraph.Nodes.Where(n => n.Type == Abstractions.Graph.NodeType.Handler).ToList();

                                    foreach (var graphNode in handlerNodes)
                                    {
                                        var resultChannelName = $"node_result:{graphNode.Id}";

                                        // Try to get the result
                                        try
                                        {
                                            if (mapContextWithGraph.Channels.Contains(resultChannelName))
                                            {
                                                var result = mapContextWithGraph.Channels[resultChannelName].Get<NodeExecutionResult>();

                                                // If we find a Failure, the graph failed
                                                if (result is NodeExecutionResult.Failure failure)
                                                {
                                                    hasFailures = true;
                                                    failureResult = failure;
                                                    break;
                                                }
                                            }
                                        }
                                        catch
                                        {
                                            // Channel doesn't exist or can't be read - skip this node
                                            continue;
                                        }
                                    }
                                }

                                if (hasFailures && failureResult != null)
                                {
                                    // Graph completed but with failures - mark item as failed
                                    itemState.Status = MapItemStatus.Failed;
                                    itemState.Result = failureResult;
                                    context.AddTag($"node_state:map:{node.Id}[{index}]", NodeState.Failed.ToString());

                                    context.Log("Orchestrator", $"Map node '{node.Id}' item {index + 1}/{itemList.Count} failed: {failureResult.Exception.Message}",
                                        LogLevel.Warning, nodeId: node.Id);
                                }
                                else
                                {
                                    // Graph succeeded - read final result
                                    // Priority: 1) output: channels (explicit output), 2) node_output: from last handler
                                    object? result = null;

                                    // First, try to find explicit output: channels
                                    if (mapContextWithGraph.Channels.ChannelNames.Any(c => c.StartsWith("output:")))
                                    {
                                        var outputChannel = mapContextWithGraph.Channels.ChannelNames.First(c => c.StartsWith("output:"));
                                        result = mapContextWithGraph.Channels[outputChannel].Get<object>();
                                    }
                                    else
                                    {
                                        // No explicit output channel - try to read from handler nodes
                                        // Find the last handler node in execution order
                                        var handlerNodes = processorGraph.Nodes
                                            .Where(n => n.Type == Abstractions.Graph.NodeType.Handler)
                                            .ToList();

                                        if (handlerNodes.Count > 0)
                                        {
                                            // For simple linear graphs, use the only/last handler
                                            var lastHandler = handlerNodes.Last();
                                            var outputChannelName = $"node_output:{lastHandler.Id}";

                                            if (mapContextWithGraph.Channels.Contains(outputChannelName))
                                            {
                                                var handlerOutput = mapContextWithGraph.Channels[outputChannelName].Get<Dictionary<string, object>>();
                                                // Handler outputs are stored as Dictionary<string, object>
                                                // Extract the actual result value
                                                if (handlerOutput != null)
                                                {
                                                    result = handlerOutput;
                                                }
                                            }
                                        }
                                    }

                                    // Store result as Success
                                    itemState.Status = MapItemStatus.Succeeded;
                                    var outputs = new Dictionary<string, object>();
                                    if (result != null)
                                    {
                                        outputs["output"] = result;
                                    }
                                    itemState.Result = NodeExecutionResult.Success.Single(
                                        output: outputs,
                                        duration: DateTimeOffset.UtcNow - itemState.StartTime,
                                        metadata: new NodeExecutionMetadata()
                                    );
                                    itemState.Context = mapContext;
                                    context.AddTag($"node_state:map:{node.Id}[{index}]", NodeState.Succeeded.ToString());

                                    context.Log("Orchestrator", $"Map node '{node.Id}' completed item {index + 1}/{itemList.Count}",
                                        LogLevel.Debug, nodeId: node.Id);
                                }
                            }
                            else
                            {
                                throw new InvalidOperationException($"Map execution requires TContext to be or derive from GraphContext. Actual type: {typeof(TContext).Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            // Item execution failed
                            itemState.Status = MapItemStatus.Failed;
                            itemState.Result = new NodeExecutionResult.Failure(
                                Exception: ex,
                                Severity: ErrorSeverity.Fatal,
                                IsTransient: false,
                                Duration: DateTimeOffset.UtcNow - itemState.StartTime
                            );
                            context.AddTag($"node_state:map:{node.Id}[{index}]", NodeState.Failed.ToString());

                            context.Log("Orchestrator",
                                $"Map node '{node.Id}' failed processing item {index}. Mode: {errorMode}. Error: {ex.Message}",
                                LogLevel.Error, nodeId: node.Id, exception: ex);

                            // In FailFast mode, capture first exception and cancel remaining tasks
                            if (errorMode == Abstractions.Graph.MapErrorMode.FailFast)
                            {
                                // Capture first exception atomically
                                System.Threading.Interlocked.CompareExchange(ref firstException, ex, null);
                                cts.Cancel();
                            }
                        }
                    });
            }
        }
        catch (OperationCanceledException) when (firstException != null)
        {
            // In FailFast mode, if we cancelled due to an exception, rethrow the original exception
            // instead of the TaskCanceledException
            throw firstException;
        }
        finally
        {
            cts.Dispose();
        }

        var duration = DateTimeOffset.UtcNow - startTime;

        // All items resolved - aggregate results based on error mode
        var finalResults = new List<object?>();
        var successCount = 0;
        var failureCount = 0;

        for (int i = 0; i < itemList.Count; i++)
        {
            var itemState = itemStates[i];

            // Merge context back to parent
            if (itemState.Context != null)
            {
                context.MergeFrom(itemState.Context);
            }

            if (itemState.Status == MapItemStatus.Succeeded)
            {
                // Extract result from Success output (port 0)
                if (itemState.Result is NodeExecutionResult.Success success &&
                    success.PortOutputs.TryGetValue(0, out var port0Outputs) &&
                    port0Outputs.TryGetValue("output", out var output))
                {
                    finalResults.Add(output);
                    successCount++;
                }
                else
                {
                    finalResults.Add(null);
                    successCount++;
                }
            }
            else if (itemState.Status == MapItemStatus.Failed)
            {
                failureCount++;

                switch (errorMode)
                {
                    case Abstractions.Graph.MapErrorMode.ContinueWithNulls:
                        finalResults.Add(null);
                        break;

                    case Abstractions.Graph.MapErrorMode.ContinueOmitFailures:
                        // Skip failed item
                        break;

                    case Abstractions.Graph.MapErrorMode.FailFast:
                        // Throw first failure
                        if (itemState.Result is NodeExecutionResult.Failure failure)
                        {
                            throw failure.Exception;
                        }
                        break;
                }
            }
        }

        // Write results to output channel
        var outputChannelName = node.MapOutputChannel ?? $"node_output:{node.Id}";
        context.Channels[outputChannelName].Set(finalResults);

        // Mark node as complete
        context.MarkNodeComplete(node.Id);

        // Log statistics
        context.Log("Orchestrator",
            $"Map node '{node.Id}' processed {itemList.Count} items: {successCount} succeeded, {failureCount} failed, " +
            $"{duration.TotalMilliseconds}ms (avg: {duration.TotalMilliseconds / (double)itemList.Count:F2}ms/item, concurrency: {maxConcurrency})",
            LogLevel.Information, nodeId: node.Id);

        context.Log("Orchestrator", $"Exiting map: {node.Name} with {finalResults.Count} results",
            LogLevel.Information, nodeId: node.Id);
    }

    /// <summary>
    /// Check if map item should execute (pending or ready to retry).
    /// </summary>
    private bool ShouldExecuteMapItem(MapItemState itemState)
    {
        if (itemState.Status == MapItemStatus.Pending)
            return true;

        if (itemState.Status == MapItemStatus.Polling)
        {
            var elapsed = DateTimeOffset.UtcNow - itemState.LastAttempt;
            return elapsed >= itemState.RetryAfter;
        }

        return false;
    }

    /// <summary>
    /// Get earliest retry time across all polling items (intelligent delay).
    /// </summary>
    private DateTimeOffset GetEarliestRetryTime(System.Collections.Concurrent.ConcurrentDictionary<int, MapItemState> itemStates)
    {
        var pollingItems = itemStates.Values
            .Where(s => s.Status == MapItemStatus.Polling)
            .ToList();

        if (pollingItems.Count == 0)
            return DateTimeOffset.UtcNow;

        return pollingItems.Min(s => s.LastAttempt + s.RetryAfter);
    }

    /// <summary>
    /// Get fingerprints from all upstream nodes (for hierarchical hashing).
    /// </summary>
    private Dictionary<string, string> GetUpstreamFingerprints(TContext context, Node node)
    {
        var upstreamHashes = new Dictionary<string, string>();

        foreach (var edge in context.Graph.GetIncomingEdges(node.Id))
        {
            if (context.InternalCurrentFingerprints.TryGetValue(edge.From, out var upstreamHash))
            {
                upstreamHashes[edge.From] = upstreamHash;
            }
        }

        return upstreamHashes;
    }

    /// <summary>
    /// Get input artifact versions for lineage tracking (Phase 1: Data Orchestration Primitives).
    /// Collects artifact versions from upstream nodes that declare ProducesArtifact.
    /// Used to build lineage: what artifacts were inputs to produce this artifact?
    /// </summary>
    private Dictionary<ArtifactKey, string> GetInputArtifactVersions(TContext context, Node node)
    {
        var inputVersions = new Dictionary<ArtifactKey, string>();

        if (_artifactIndex == null)
            return inputVersions;

        // Strategy 1: Collect from explicit RequiresArtifacts declarations
        if (node.RequiresArtifacts != null)
        {
            foreach (var requiredArtifact in node.RequiresArtifacts)
            {
                // Find which node produced this artifact
                var producers = _artifactIndex.GetProducers(requiredArtifact);
                foreach (var producerNodeId in producers)
                {
                    // Get the version (fingerprint) for this producer
                    if (context.InternalCurrentFingerprints.TryGetValue(producerNodeId, out var version))
                    {
                        inputVersions[requiredArtifact] = version;
                        break; // Take first producer (Phase 1: single producer only)
                    }
                }
            }
        }

        // Strategy 2: Collect from upstream nodes with ProducesArtifact
        foreach (var edge in context.Graph.GetIncomingEdges(node.Id))
        {
            var upstreamNode = context.Graph.Nodes.FirstOrDefault(n => n.Id == edge.From);
            if (upstreamNode?.ProducesArtifact != null)
            {
                if (context.InternalCurrentFingerprints.TryGetValue(edge.From, out var version))
                {
                    // Only add if not already present (RequiresArtifacts takes precedence)
                    if (!inputVersions.ContainsKey(upstreamNode.ProducesArtifact))
                    {
                        inputVersions[upstreamNode.ProducesArtifact] = version;
                    }
                }
            }
        }

        return inputVersions;
    }

    /// <summary>
    /// Clear all ephemeral channels to prevent state leakage across loop iterations.
    /// Ephemeral channels are designed for temporary routing decisions and should be
    /// cleared after each execution step to avoid stale data affecting future iterations.
    /// </summary>
    private void ClearEphemeralChannels(TContext context)
    {
        foreach (var channelName in context.Channels.ChannelNames.ToList())
        {
            var channel = context.Channels[channelName];
            if (channel is EphemeralChannel ephemeral)
            {
                ephemeral.Clear();
            }
        }
    }

    public async IAsyncEnumerable<Event> ExecuteStreamingAsync(
        TContext context,
        StreamingOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new StreamingOptions();

        context.Log("Orchestrator", $"Starting streaming graph execution: {context.Graph.Name}", LogLevel.Information);

        Exception? errorToYield = null;

        // Get execution layers (topological sort)
        IReadOnlyList<ExecutionLayer> layers;
        try
        {
            layers = context.Graph.GetExecutionLayers();

            if (context is Context.GraphContext graphContext)
            {
                graphContext.SetTotalLayers(layers.Count);
            }

            context.Log("Orchestrator", $"Graph has {layers.Count} execution layers", LogLevel.Debug);

        }
        catch (Exception ex)
        {
            errorToYield = ex;
            yield break;
        }

        // Calculate total nodes for progress tracking
        var totalNodes = layers.SelectMany(l => l.NodeIds).Count();
        var completedNodes = 0;

        // Execute each layer
        for (int i = 0; i < layers.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var layer = layers[i];
            context.CurrentLayerIndex = i;

            context.Log("Orchestrator", $"Executing layer {i} with {layer.NodeIds.Count} node(s)", LogLevel.Debug);

            // Get nodes to execute in this layer
            var nodesToExecute = layer.NodeIds
                .Where(nodeId => !context.IsNodeComplete(nodeId))
                .Select(nodeId => context.Graph.GetNode(nodeId))
                .Where(node => node != null && (node.Type == NodeType.Handler || node.Type == NodeType.Router || node.Type == NodeType.SubGraph))
                .ToList();

            if (nodesToExecute.Count == 0)
            {
                continue;
            }

            // Execute nodes and yield results
            if (options.EmissionMode == PartialResultEmissionMode.EveryNode)
            {
                // Emit result after each node
                foreach (var node in nodesToExecute)
                {
                    var startTime = DateTimeOffset.UtcNow;

                    try
                    {
                        await ExecuteNodeAsync(context, node!, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        errorToYield = ex;
                        break;
                    }

                    var duration = DateTimeOffset.UtcNow - startTime;
                    completedNodes++;
                    var progress = (float)completedNodes / totalNodes;

                    yield return new NodeExecutionCompletedEvent
                    {
                        NodeId = node!.Id,
                        HandlerName = node.HandlerName ?? node.Type.ToString(),
                        LayerIndex = i,
                        Progress = progress,
                        Outputs = options.IncludeOutputs
                            ? context.Channels[$"node_output:{node!.Id}"].Get<Dictionary<string, object>>()
                            : null,
                        Duration = duration,
                        Result = NodeExecutionResult.Success.Single(
                            output: new Dictionary<string, object>(),
                            duration: duration,
                            metadata: new NodeExecutionMetadata()
                        )
                    };
                }

                if (errorToYield != null)
                    break;
            }
            else
            {
                // Execute layer and emit result at end
                var layerStartTime = DateTimeOffset.UtcNow;

                try
                {
                    if (nodesToExecute.Count == 1)
                    {
                        await ExecuteNodeAsync(context, nodesToExecute[0]!, cancellationToken);
                    }
                    else
                    {
                        await ExecuteNodesInParallelAsync(context, nodesToExecute!, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    errorToYield = ex;
                    break;
                }

                var layerDuration = DateTimeOffset.UtcNow - layerStartTime;
                completedNodes += nodesToExecute.Count;

                // Emit layer completion event
                yield return new LayerExecutionCompletedEvent
                {
                    LayerIndex = i,
                    Duration = layerDuration,
                    SuccessfulNodes = nodesToExecute.Count
                };
            }

            // Clear ephemeral channels after layer execution
            ClearEphemeralChannels(context);

            // Update managed context
            if (context.Managed is ManagedContext managed)
            {
                managed.IncrementStep();
            }
        }

        if (errorToYield == null && context is Context.GraphContext gc)
        {
            gc.MarkComplete();
            context.Log("Orchestrator", "Streaming graph execution completed successfully", LogLevel.Information);
        }
        else if (errorToYield is OperationCanceledException)
        {
            if (context is Context.GraphContext gc2)
            {
                gc2.MarkCancelled();
            }
            context.Log("Orchestrator", "Streaming graph execution cancelled", LogLevel.Warning);
        }
        else if (errorToYield != null)
        {
            context.Log("Orchestrator", $"Streaming graph execution failed: {errorToYield.Message}", LogLevel.Error, exception: errorToYield);
        }

        // Throw error after iterator completes
        if (errorToYield != null)
        {
            throw errorToYield;
        }
    }

    /// <summary>
    /// Saves checkpoint for the current execution state.
    /// Called after each layer completion (fire-and-forget pattern).
    /// </summary>
    private async Task SaveCheckpointAsync(Context.GraphContext context, int completedLayerIndex, Abstractions.Checkpointing.CheckpointTrigger trigger, CancellationToken cancellationToken)
    {
        if (_checkpointStore == null)
            return;

        // Extract all node outputs from channels for checkpoint restoration
        var nodeOutputs = new Dictionary<string, object>();
        var nodeStateMetadata = new Dictionary<string, Abstractions.Checkpointing.NodeStateMetadata>();

        foreach (var channelName in context.Channels.ChannelNames)
        {
            if (channelName.StartsWith("node_output:"))
            {
                var nodeId = channelName.Substring("node_output:".Length);

                // Only include outputs from completed nodes
                if (context.CompletedNodes.Contains(nodeId))
                {
                    try
                    {
                        var output = context.Channels[channelName].Get<object>();
                        if (output != null)
                        {
                            nodeOutputs[channelName] = output;

                            // Capture node version for compatibility checking
                            var node = context.Graph.GetNode(nodeId);
                            if (node != null)
                            {
                                nodeStateMetadata[nodeId] = new Abstractions.Checkpointing.NodeStateMetadata
                                {
                                    NodeId = nodeId,
                                    Version = node.Version,
                                    StateJson = System.Text.Json.JsonSerializer.Serialize(
                                        output,
                                        HPDAgent.Graph.Abstractions.Serialization.GraphJsonSerializerContext.Default.DictionaryStringObject
                                    ),
                                    CapturedAt = DateTimeOffset.UtcNow
                                };
                            }
                        }
                    }
                    catch
                    {
                        // Skip channels that can't be retrieved as object
                        // (may have specific type requirements)
                    }
                }
            }
        }

        var checkpoint = new Abstractions.Checkpointing.GraphCheckpoint
        {
            CheckpointId = Guid.NewGuid().ToString("N"),
            ExecutionId = context.ExecutionId,
            GraphId = context.Graph.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedNodes = context.CompletedNodes,
            NodeOutputs = nodeOutputs,
            NodeStateMetadata = nodeStateMetadata,
            ContextJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                ExecutionId = context.ExecutionId,
                CompletedNodes = context.CompletedNodes,
                CurrentLayerIndex = completedLayerIndex,
            }),
            Metadata = new Abstractions.Checkpointing.CheckpointMetadata
            {
                Trigger = trigger,
                CompletedLayer = completedLayerIndex
            }
        };

        await _checkpointStore.SaveCheckpointAsync(checkpoint, cancellationToken);

        context.Log("Orchestrator", $"Checkpoint saved after layer {completedLayerIndex} with {nodeOutputs.Count} node outputs", LogLevel.Debug);
    }

    // ========== PHASE 3: DEMAND-DRIVEN EXECUTION API ==========

    /// <summary>
    /// Materialize an artifact by executing only the necessary nodes.
    /// Thin wrapper around existing ExecuteAsync() infrastructure.
    /// Requires graph registry to be configured and graph to be registered.
    /// </summary>
    /// <param name="graphId">The ID of the graph to use for materialization (registered in graph registry)</param>
    /// <param name="artifactKey">The artifact to materialize</param>
    /// <param name="partition">Optional partition key</param>
    /// <param name="options">Materialization options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public Task<Artifact<T>> MaterializeAsync<T>(
        string graphId,
        ArtifactKey artifactKey,
        PartitionKey? partition = null,
        MaterializationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_graphRegistry == null)
        {
            throw new InvalidOperationException(
                "Graph registry is not configured. Provide IGraphRegistry in constructor to use graph-ID-based materialization.");
        }

        var graph = _graphRegistry.GetGraph(graphId);
        if (graph == null)
        {
            throw new InvalidOperationException(
                $"Graph with ID '{graphId}' is not registered. Available graphs: {string.Join(", ", _graphRegistry.GetGraphIds())}");
        }

        // Delegate to internal implementation with the resolved graph
        return MaterializeAsync<T>(graph, artifactKey, partition, options, cancellationToken);
    }

    /// <summary>
    /// Materialize an artifact by executing only the necessary nodes.
    /// This is the core implementation that accepts an explicit graph.
    /// </summary>
    internal async Task<Artifact<T>> MaterializeAsync<T>(
        Abstractions.Graph.Graph graph,
        ArtifactKey artifactKey,
        PartitionKey? partition = null,
        MaterializationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_artifactRegistry == null)
            throw new InvalidOperationException("Artifact registry is required for demand-driven execution. Provide IArtifactRegistry in constructor.");

        if (_artifactIndex == null)
            throw new InvalidOperationException("Artifact index is required for demand-driven execution.");

        options ??= new MaterializationOptions();

        // Apply partition to artifact key if provided
        var fullArtifactKey = partition != null
            ? new ArtifactKey { Path = artifactKey.Path, Partition = partition }
            : artifactKey;

        // Build artifact index from graph
        _artifactIndex.BuildIndex(graph);

        // 1. Acquire distributed lock (prevent concurrent materialization)
        await using var lockHandle = options.WaitForLock
            ? await _artifactRegistry.TryAcquireMaterializationLockAsync(
                fullArtifactKey, partition, options.LockTimeout, cancellationToken)
            : await _artifactRegistry.TryAcquireMaterializationLockAsync(
                fullArtifactKey, partition, TimeSpan.Zero, cancellationToken);

        if (lockHandle == null)
            throw new InvalidOperationException(
                $"Failed to acquire lock for artifact {fullArtifactKey}. " +
                $"Another process may be materializing this artifact.");

        // 2. Resolve producing node via artifact index
        var producingNodeIds = _artifactIndex.GetProducers(artifactKey);
        if (producingNodeIds.Count == 0)
            throw new InvalidOperationException(
                $"No node produces artifact {artifactKey}. " +
                $"Ensure the graph contains a node with ProducesArtifact = {artifactKey}.");

        // For now, use first producer (multi-producer resolution will be added later)
        var producingNodeId = producingNodeIds.First();

        // 3. Check if artifact exists with current version
        if (!options.ForceRecompute)
        {
            var latestVersion = await _artifactRegistry.GetLatestVersionAsync(fullArtifactKey, partition, cancellationToken);

            if (latestVersion != null && _fingerprintCalculator != null && _cacheStore != null)
            {
                // Try to load from cache
                var cacheResult = await _cacheStore.GetAsync(latestVersion, cancellationToken);
                if (cacheResult != null)
                {
                    // Found cached artifact
                    var metadata = await _artifactRegistry.GetMetadataAsync(fullArtifactKey, latestVersion, cancellationToken);
                    if (metadata != null)
                    {
                        // Extract value from cache result
                        var value = cacheResult.Outputs.Values.FirstOrDefault();
                        if (value is T typedValue)
                        {
                            return new Artifact<T>
                            {
                                Key = fullArtifactKey,
                                Version = latestVersion,
                                Value = typedValue,
                                CreatedAt = metadata.CreatedAt,
                                InputVersions = metadata.InputVersions,
                                ProducedByNodeId = metadata.ProducedByNodeId,
                                ExecutionId = metadata.ExecutionId
                            };
                        }
                    }
                }
            }
        }

        // 4. Build minimal subgraph using AffectedNodeDetector (if available)
        Abstractions.Graph.Graph minimalGraph;
        HashSet<string>? affectedNodes = null;

        if (_affectedNodeDetector != null && _snapshotStore != null && !options.ForceRecompute)
        {
            // Load previous snapshot to compute affected nodes
            var previousSnapshot = await _snapshotStore.GetLatestSnapshotAsync(graph.Id, cancellationToken);

            // Create empty inputs for now (TODO: Extract from context if needed)
            var currentInputs = new Abstractions.Handlers.HandlerInputs();

            // Compute which nodes are affected (need re-execution)
            affectedNodes = await _affectedNodeDetector.GetAffectedNodesAsync(
                previousSnapshot, graph, currentInputs, _serviceProvider, cancellationToken);

            // Build minimal subgraph containing only affected nodes + the producing node
            if (!affectedNodes.Contains(producingNodeId))
            {
                affectedNodes.Add(producingNodeId);
            }

            minimalGraph = BuildMinimalSubgraph(graph, affectedNodes, producingNodeId);
            // Use a unique ID so ExecuteAcyclicAsync's own incremental detection
            // doesn't find the existing snapshot and fast-exit (we already handled incrementality above).
            minimalGraph = minimalGraph with { Id = $"{graph.Id}-mat-{Guid.NewGuid():N}" };
        }
        else
        {
            // No incremental execution (ForceRecompute or no detector/store).
            // Use a snapshot-exempt copy so ExecuteAcyclicAsync's incremental detection
            // doesn't fire and skip nodes that must be re-executed.
            minimalGraph = graph with { Id = $"{graph.Id}-force-{Guid.NewGuid():N}" };
        }

        // 5. Create context and execute
        var executionId = $"materialize-{fullArtifactKey.Path.Last()}-{Guid.NewGuid():N}";

        // We need to create a GraphContext to execute - use the GraphContext constructor
        var context = new Context.GraphContext(executionId, minimalGraph, _serviceProvider);

        // Set CurrentPartition if a specific partition was requested
        // This ensures partitioned nodes only execute for the requested partition
        if (partition != null)
        {
            context.SetCurrentPartition(partition);
        }

        // Execute the graph
        await ExecuteAsync(context as TContext ?? throw new InvalidOperationException("Failed to cast context"), cancellationToken);

        // 6. Get version from fingerprint (computed during execution)
        var version = _fingerprintCalculator != null
            ? context.InternalCurrentFingerprints.GetValueOrDefault(producingNodeId, Guid.NewGuid().ToString("N"))
            : Guid.NewGuid().ToString("N");

        // 7. Extract artifact value directly from context channels (not cache, to avoid race conditions)
        // The cache write is fire-and-forget, so we retrieve from the execution context instead
        var port0ChannelName = $"node_output:{producingNodeId}";
        T typedArtifactValue;

        if (context.Channels.ChannelNames.Contains(port0ChannelName))
        {
            var channel = context.Channels[port0ChannelName];
            var port0Outputs = channel.Get<Dictionary<string, object>>();
            if (port0Outputs != null && port0Outputs.Count > 0)
            {
                var artifactValue = port0Outputs.Values.FirstOrDefault();
                if (artifactValue is not T value)
                {
                    throw new InvalidOperationException(
                        $"Node {producingNodeId} produced value of type {artifactValue?.GetType().Name ?? "null"}, expected {typeof(T).Name}");
                }
                typedArtifactValue = value;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Node {producingNodeId} did not produce any output.");
            }
        }
        else
        {
            throw new InvalidOperationException(
                $"Node {producingNodeId} did not execute or produce output. Channel {port0ChannelName} not found.");
        }

        // 8. Extract input artifact versions for lineage tracking
        var producingNode = graph.Nodes.FirstOrDefault(n => n.Id == producingNodeId);
        var inputVersions = producingNode != null && context is TContext typedContext
            ? GetInputArtifactVersions(typedContext, producingNode)
            : new Dictionary<ArtifactKey, string>();

        // 9. Register artifact in registry
        var artifactMetadata = new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = inputVersions,
            ProducedByNodeId = producingNodeId,
            ExecutionId = executionId
        };

        await _artifactRegistry.RegisterAsync(fullArtifactKey, version, artifactMetadata, cancellationToken);

        // 10. Save snapshot for future incremental execution (if snapshot store is available)
        if (_snapshotStore != null)
        {
            var snapshot = new Abstractions.Caching.GraphSnapshot
            {
                ExecutionId = executionId,
                Timestamp = DateTimeOffset.UtcNow,
                NodeFingerprints = context.InternalCurrentFingerprints.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                GraphHash = ComputeGraphHash(graph)
            };
            await _snapshotStore.SaveSnapshotAsync(graph.Id, snapshot, cancellationToken);
        }

        return new Artifact<T>
        {
            Key = fullArtifactKey,
            Version = version,
            Value = typedArtifactValue,
            CreatedAt = artifactMetadata.CreatedAt,
            InputVersions = artifactMetadata.InputVersions,
            ProducedByNodeId = producingNodeId,
            ExecutionId = executionId
        };
    }

    /// <summary>
    /// Compute a hash of the graph structure for snapshot validation.
    /// Uses the built-in ComputeStructureHash() method from Graph class.
    /// </summary>
    private string ComputeGraphHash(Abstractions.Graph.Graph graph)
    {
        return graph.Id + graph.Version;
    }

    /// <summary>
    /// Build a minimal subgraph containing only the necessary nodes to materialize a target artifact.
    /// Includes the target node and all its transitive dependencies (backward traversal).
    /// </summary>
    /// <param name="graph">Full graph</param>
    /// <param name="affectedNodes">Set of nodes that need re-execution (from AffectedNodeDetector)</param>
    /// <param name="targetNodeId">Target node that produces the artifact</param>
    /// <returns>Minimal subgraph</returns>
    private Abstractions.Graph.Graph BuildMinimalSubgraph(
        Abstractions.Graph.Graph graph,
        HashSet<string> affectedNodes,
        string targetNodeId)
    {
        // Build dependency graph (reverse edges for backward traversal)
        var dependencies = new Dictionary<string, HashSet<string>>();
        foreach (var edge in graph.Edges)
        {
            if (!dependencies.ContainsKey(edge.To))
            {
                dependencies[edge.To] = new HashSet<string>();
            }
            dependencies[edge.To].Add(edge.From);
        }

        // Backward traversal from target node to collect all necessary nodes
        var necessaryNodes = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(targetNodeId);
        necessaryNodes.Add(targetNodeId);

        // Always include start and end nodes
        necessaryNodes.Add(graph.EntryNodeId);
        necessaryNodes.Add(graph.ExitNodeId);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();

            // Add all dependencies of this node
            if (dependencies.TryGetValue(nodeId, out var deps))
            {
                foreach (var depNodeId in deps)
                {
                    // Only include if it's affected or if we haven't seen it yet
                    if (necessaryNodes.Add(depNodeId))
                    {
                        queue.Enqueue(depNodeId);
                    }
                }
            }
        }

        // Filter nodes and edges to only include necessary ones
        var filteredNodes = graph.Nodes
            .Where(n => necessaryNodes.Contains(n.Id))
            .ToList();

        var filteredEdges = graph.Edges
            .Where(e => necessaryNodes.Contains(e.From) && necessaryNodes.Contains(e.To))
            .ToList();

        // Create minimal subgraph
        return new Abstractions.Graph.Graph
        {
            Id = $"{graph.Id}-minimal-{targetNodeId}",
            Name = $"{graph.Name} (Minimal for {targetNodeId})",
            Version = graph.Version,
            Nodes = filteredNodes,
            Edges = filteredEdges,
            EntryNodeId = graph.EntryNodeId,
            ExitNodeId = graph.ExitNodeId,
            Metadata = graph.Metadata,
            MaxIterations = graph.MaxIterations,
            IterationOptions = graph.IterationOptions,
            ExecutionTimeout = graph.ExecutionTimeout,
            CloningPolicy = graph.CloningPolicy
        };
    }

    /// <summary>
    /// Materialize multiple artifacts in parallel.
    /// Requires graph registry to be configured and graph to be registered.
    /// </summary>
    /// <param name="graphId">The ID of the graph to use for materialization (registered in graph registry)</param>
    /// <param name="artifactKeys">The artifacts to materialize</param>
    /// <param name="partition">Optional partition key</param>
    /// <param name="options">Materialization options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<IReadOnlyDictionary<ArtifactKey, object>> MaterializeManyAsync(
        string graphId,
        IEnumerable<ArtifactKey> artifactKeys,
        PartitionKey? partition = null,
        MaterializationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_artifactRegistry == null)
            throw new InvalidOperationException("Artifact registry is required for demand-driven execution. Provide IArtifactRegistry in constructor.");

        if (_graphRegistry == null)
        {
            throw new InvalidOperationException(
                "Graph registry is not configured. Provide IGraphRegistry in constructor to use graph-ID-based materialization.");
        }

        var artifactList = artifactKeys.ToList();
        if (artifactList.Count == 0)
            return new Dictionary<ArtifactKey, object>();

        options ??= new MaterializationOptions();

        // Resolve graph from registry
        if (_graphRegistry == null)
        {
            throw new InvalidOperationException(
                "Graph registry is not configured. Provide IGraphRegistry in constructor to use graph-ID-based materialization.");
        }

        var graph = _graphRegistry.GetGraph(graphId);
        if (graph == null)
        {
            throw new InvalidOperationException(
                $"Graph with ID '{graphId}' is not registered. Available graphs: {string.Join(", ", _graphRegistry.GetGraphIds())}");
        }

        // Delegate to internal implementation (which handles lock acquisition)
        return await MaterializeManyAsync(graph, artifactList, partition, options, cancellationToken);
    }

    /// <summary>
    /// Internal overload: Materialize multiple artifacts in parallel (with graph parameter).
    /// Automatically deduplicates shared upstream dependencies by building a unified minimal subgraph.
    /// </summary>
    internal async Task<IReadOnlyDictionary<ArtifactKey, object>> MaterializeManyAsync(
        Abstractions.Graph.Graph graph,
        IEnumerable<ArtifactKey> artifactKeys,
        PartitionKey? partition = null,
        MaterializationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_artifactRegistry == null)
            throw new InvalidOperationException("Artifact registry is required for demand-driven execution. Provide IArtifactRegistry in constructor.");

        if (_artifactIndex == null)
            throw new InvalidOperationException("Artifact index is required for demand-driven execution.");

        var artifactList = artifactKeys.ToList();
        if (artifactList.Count == 0)
            return new Dictionary<ArtifactKey, object>();

        options ??= new MaterializationOptions();

        // Build artifact index from graph
        _artifactIndex.BuildIndex(graph);

        // Strategy: Build a unified minimal subgraph that produces ALL requested artifacts,
        // then execute once. This automatically deduplicates shared upstream dependencies.

        // 1. Resolve producing nodes for all artifacts
        var artifactToProducerMap = new Dictionary<ArtifactKey, string>();
        var allProducingNodeIds = new HashSet<string>();

        foreach (var artifactKey in artifactList)
        {
            var producingNodeIds = _artifactIndex.GetProducers(artifactKey);
            if (producingNodeIds.Count == 0)
                throw new InvalidOperationException($"No node produces artifact {artifactKey}");

            // Use first producer (multi-producer resolution can be enhanced later)
            var producingNodeId = producingNodeIds.First();
            artifactToProducerMap[artifactKey] = producingNodeId;
            allProducingNodeIds.Add(producingNodeId);
        }

        // 2. Acquire locks for all artifacts (prevents concurrent materialization)
        // CRITICAL: Sort artifacts to ensure deterministic lock ordering and prevent deadlocks
        // When multiple concurrent requests need overlapping artifacts, they must acquire locks
        // in the same order to avoid circular wait conditions.
        var sortedArtifactList = artifactList
            .OrderBy(a => a.ToString(), StringComparer.Ordinal)
            .ToList();

        var lockHandles = new List<IAsyncDisposable>();
        try
        {
            foreach (var artifactKey in sortedArtifactList)
            {
                var fullArtifactKey = partition != null
                    ? new ArtifactKey { Path = artifactKey.Path, Partition = partition }
                    : artifactKey;

                var lockHandle = options.WaitForLock
                    ? await _artifactRegistry.TryAcquireMaterializationLockAsync(
                        fullArtifactKey, partition, options.LockTimeout, cancellationToken)
                    : await _artifactRegistry.TryAcquireMaterializationLockAsync(
                        fullArtifactKey, partition, TimeSpan.Zero, cancellationToken);

                if (lockHandle == null)
                    throw new InvalidOperationException(
                        $"Failed to acquire lock for artifact {fullArtifactKey}. " +
                        $"Another process may be materializing this artifact.");

                lockHandles.Add(lockHandle);
            }

            // 3. Build unified minimal subgraph containing all necessary producing nodes
            // Use backward traversal to collect all dependencies for ALL target nodes
            var allNecessaryNodes = new HashSet<string>();

            // Build dependency graph (reverse edges for backward traversal)
            var dependencies = new Dictionary<string, HashSet<string>>();
            foreach (var edge in graph.Edges)
            {
                if (!dependencies.ContainsKey(edge.To))
                {
                    dependencies[edge.To] = new HashSet<string>();
                }
                dependencies[edge.To].Add(edge.From);
            }

            // Always include start and end nodes
            allNecessaryNodes.Add(graph.EntryNodeId);
            allNecessaryNodes.Add(graph.ExitNodeId);

            // Backward traversal from ALL producing nodes (deduplication happens naturally)
            var queue = new Queue<string>();
            foreach (var producingNodeId in allProducingNodeIds)
            {
                if (allNecessaryNodes.Add(producingNodeId))
                {
                    queue.Enqueue(producingNodeId);
                }
            }

            while (queue.Count > 0)
            {
                var nodeId = queue.Dequeue();

                // Add all dependencies of this node
                if (dependencies.TryGetValue(nodeId, out var deps))
                {
                    foreach (var depNodeId in deps)
                    {
                        if (allNecessaryNodes.Add(depNodeId))
                        {
                            queue.Enqueue(depNodeId);
                        }
                    }
                }
            }

            // 4. Build minimal subgraph with affected nodes (incremental execution)
            HashSet<string>? affectedNodes = null;

            if (_affectedNodeDetector != null && _snapshotStore != null && !options.ForceRecompute)
            {
                // Load previous snapshot to compute affected nodes
                var previousSnapshot = await _snapshotStore.GetLatestSnapshotAsync(graph.Id, cancellationToken);

                // Create empty inputs for now
                var currentInputs = new Abstractions.Handlers.HandlerInputs();

                // Compute which nodes are affected (need re-execution)
                affectedNodes = await _affectedNodeDetector.GetAffectedNodesAsync(
                    previousSnapshot, graph, currentInputs, _serviceProvider, cancellationToken);

                // Only prune if there are actually dirty nodes to filter by.
                // When affectedNodes is non-empty, intersect to limit execution scope.
                // When empty (nothing changed), keep the full dependency graph — producing nodes
                // and their upstream dependencies must still execute to emit output channels.
                if (affectedNodes.Count > 0)
                {
                    // Always keep producing nodes and start/end
                    foreach (var pid in allProducingNodeIds) affectedNodes.Add(pid);
                    affectedNodes.Add(graph.EntryNodeId);
                    affectedNodes.Add(graph.ExitNodeId);
                    allNecessaryNodes.IntersectWith(affectedNodes);
                }
            }

            // Filter nodes and edges to only include necessary ones
            var filteredNodes = graph.Nodes
                .Where(n => allNecessaryNodes.Contains(n.Id))
                .ToList();

            var filteredEdges = graph.Edges
                .Where(e => allNecessaryNodes.Contains(e.From) && allNecessaryNodes.Contains(e.To))
                .ToList();

            // Create minimal subgraph
            var minimalGraph = new Abstractions.Graph.Graph
            {
                Id = $"{graph.Id}-mat-many-{Guid.NewGuid():N}",
                Name = $"{graph.Name} (Minimal for {artifactList.Count} artifacts)",
                Version = graph.Version,
                Nodes = filteredNodes,
                Edges = filteredEdges,
                EntryNodeId = graph.EntryNodeId,
                ExitNodeId = graph.ExitNodeId,
                Metadata = graph.Metadata,
                MaxIterations = graph.MaxIterations,
                IterationOptions = graph.IterationOptions,
                ExecutionTimeout = graph.ExecutionTimeout,
                CloningPolicy = graph.CloningPolicy
            };

            // 5. Execute minimal subgraph
            var executionId = $"materialize-many-{Guid.NewGuid():N}";
            var context = new Context.GraphContext(executionId, minimalGraph, _serviceProvider);

            // Cast to TContext and execute
            if (context is not TContext executionContext)
                throw new InvalidOperationException($"Context must be of type {typeof(TContext).Name}");

            await ExecuteAsync(executionContext, cancellationToken);

            // 6. Extract artifacts from execution results and register them
            var results = new Dictionary<ArtifactKey, object>();

            foreach (var artifactKey in artifactList)
            {
                var producingNodeId = artifactToProducerMap[artifactKey];
                var producingNode = graph.Nodes.FirstOrDefault(n => n.Id == producingNodeId);

                if (producingNode == null)
                    throw new InvalidOperationException($"Producing node {producingNodeId} not found in graph");

                // Extract value from context outputs
                var outputKey = $"node_output:{producingNodeId}";
                if (!executionContext.Channels.Contains(outputKey))
                    throw new InvalidOperationException($"Node {producingNodeId} did not produce output");

                var outputChannel = executionContext.Channels[outputKey];
                var outputs = outputChannel.Get<Dictionary<string, object>>();
                var value = outputs.Values.FirstOrDefault()
                    ?? throw new InvalidOperationException($"Node {producingNodeId} produced empty output");

                results[artifactKey] = value;

                // 7. Get fingerprint from execution (populated during ExecuteAsync)
                var fingerprint = context.InternalCurrentFingerprints.TryGetValue(producingNodeId, out var fp)
                    ? fp
                    : Guid.NewGuid().ToString("N");

                // 8. Extract input artifact versions for lineage tracking
                var inputVersions = GetInputArtifactVersions(executionContext, producingNode);

                // 9. Register artifact in registry
                var metadata = new ArtifactMetadata
                {
                    CreatedAt = DateTimeOffset.UtcNow,
                    InputVersions = inputVersions,
                    ProducedByNodeId = producingNodeId,
                    ExecutionId = executionId
                };

                var fullArtifactKey = partition != null
                    ? new ArtifactKey { Path = artifactKey.Path, Partition = partition }
                    : artifactKey;

                await _artifactRegistry.RegisterAsync(fullArtifactKey, fingerprint, metadata, cancellationToken);
            }

            // 10. Save snapshot for future incremental execution
            if (_snapshotStore != null)
            {
                // Use fingerprints from the execution context (same object, but accessed via TContext)
                var fingerprints = executionContext.InternalCurrentFingerprints.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                var snapshot = new Abstractions.Caching.GraphSnapshot
                {
                    ExecutionId = executionId,
                    Timestamp = DateTimeOffset.UtcNow,
                    GraphHash = graph.Id + graph.Version,
                    NodeFingerprints = fingerprints
                };

                await _snapshotStore.SaveSnapshotAsync(graph.Id, snapshot, cancellationToken);
            }

            return results;
        }
        finally
        {
            // Release all locks
            foreach (var lockHandle in lockHandles)
            {
                await lockHandle.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Backfill: materialize an artifact for a range of partitions.
    /// Requires graph registry to be configured and graph to be registered.
    /// </summary>
    /// <param name="graphId">ID of the graph to use (registered in graph registry)</param>
    /// <param name="artifactKey">Artifact to backfill</param>
    /// <param name="partitions">Partitions to materialize</param>
    /// <param name="options">Backfill options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async IAsyncEnumerable<Event> BackfillAsync<T>(
        string graphId,
        ArtifactKey artifactKey,
        IEnumerable<PartitionKey> partitions,
        BackfillOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_artifactRegistry == null)
            throw new InvalidOperationException("Artifact registry is required for demand-driven execution. Provide IArtifactRegistry in constructor.");

        if (_graphRegistry == null)
        {
            throw new InvalidOperationException(
                "Graph registry is not configured. Provide IGraphRegistry in constructor to use graph-ID-based materialization.");
        }

        var graph = _graphRegistry.GetGraph(graphId);
        if (graph == null)
        {
            throw new InvalidOperationException(
                $"Graph with ID '{graphId}' is not registered. Available graphs: {string.Join(", ", _graphRegistry.GetGraphIds())}");
        }

        // Delegate to internal implementation with the resolved graph
        await foreach (var evt in BackfillAsync<T>(graph, artifactKey, partitions, options, cancellationToken))
        {
            yield return evt;
        }
    }

    /// <summary>
    /// Internal overload: Backfill an artifact for a range of partitions (with graph parameter).
    /// Processes partitions in parallel and streams events as they complete.
    /// </summary>
    internal async IAsyncEnumerable<Event> BackfillAsync<T>(
        Abstractions.Graph.Graph graph,
        ArtifactKey artifactKey,
        IEnumerable<PartitionKey> partitions,
        BackfillOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_artifactRegistry == null)
            throw new InvalidOperationException("Artifact registry is required for demand-driven execution. Provide IArtifactRegistry in constructor.");

        if (_artifactIndex == null)
            throw new InvalidOperationException("Artifact index is required for demand-driven execution.");

        options ??= new BackfillOptions();

        var partitionList = partitions.ToList();
        if (partitionList.Count == 0)
            yield break;

        var startTime = DateTimeOffset.UtcNow;

        // Build artifact index from graph
        _artifactIndex.BuildIndex(graph);

        // Filter partitions based on SkipExisting option
        var partitionsToProcess = new List<PartitionKey>();
        var partitionsSkipped = 0;

        if (options.SkipExisting)
        {
            foreach (var partition in partitionList)
            {
                var version = await _artifactRegistry.GetLatestVersionAsync(artifactKey, partition, cancellationToken);
                if (version == null)
                {
                    partitionsToProcess.Add(partition);
                }
                else
                {
                    partitionsSkipped++;
                }
            }
        }
        else
        {
            partitionsToProcess.AddRange(partitionList);
        }

        // Emit BackfillStartedEvent
        if (options.EmitProgressEvents)
        {
            yield return new Abstractions.Events.BackfillStartedEvent
            {
                ArtifactKey = artifactKey,
                TotalPartitions = partitionList.Count,
                PartitionsToProcess = partitionsToProcess.Count,
                PartitionsSkipped = partitionsSkipped,
                MaxParallelPartitions = options.MaxParallelPartitions
            };
        }

        if (partitionsToProcess.Count == 0)
        {
            // All partitions already exist and SkipExisting is true - emit completion
            if (options.EmitProgressEvents)
            {
                yield return new Abstractions.Events.BackfillCompletedEvent
                {
                    ArtifactKey = artifactKey,
                    Duration = DateTimeOffset.UtcNow - startTime,
                    SuccessfulPartitions = 0,
                    FailedPartitions = 0,
                    SkippedPartitions = partitionsSkipped
                };
            }
            yield break;
        }

        // Use SemaphoreSlim to limit concurrent partition processing
        using var semaphore = new SemaphoreSlim(options.MaxParallelPartitions, options.MaxParallelPartitions);

        // Create a channel for streaming events
        var eventChannel = Channel.CreateUnbounded<Event>();
        var processingTasks = new List<Task>();

        var completedCount = 0;
        var totalCount = partitionsToProcess.Count;
        var successfulCount = 0;
        var failedPartitions = new List<(PartitionKey Partition, Exception Error)>();

        // Start processing partitions in parallel
        foreach (var partition in partitionsToProcess)
        {
            var task = Task.Run(async () =>
            {
                await semaphore.WaitAsync(cancellationToken);
                var partitionStartTime = DateTimeOffset.UtcNow;
                try
                {
                    // Materialize the artifact for this partition
                    var artifact = await MaterializeAsync<T>(graph, artifactKey, partition,
                        new MaterializationOptions { ForceRecompute = !options.SkipExisting },
                        cancellationToken);

                    // Track progress
                    var completed = Interlocked.Increment(ref completedCount);
                    Interlocked.Increment(ref successfulCount);

                    // Emit completion event with artifact
                    if (options.EmitProgressEvents)
                    {
                        var completionEvent = new Abstractions.Events.BackfillPartitionCompletedEvent
                        {
                            ArtifactKey = artifactKey,
                            Partition = partition,
                            Version = artifact.Version,
                            CompletedCount = completed,
                            TotalCount = totalCount,
                            Duration = DateTimeOffset.UtcNow - partitionStartTime,
                            Success = true,
                            Artifact = artifact // Include the artifact in the event
                        };
                        await eventChannel.Writer.WriteAsync(completionEvent, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    failedPartitions.Add((partition, ex));

                    // Track progress even on failure
                    var completed = Interlocked.Increment(ref completedCount);

                    // Emit failure event
                    if (options.EmitProgressEvents)
                    {
                        var failureEvent = new Abstractions.Events.BackfillPartitionCompletedEvent
                        {
                            ArtifactKey = artifactKey,
                            Partition = partition,
                            Version = string.Empty,
                            CompletedCount = completed,
                            TotalCount = totalCount,
                            Duration = DateTimeOffset.UtcNow - partitionStartTime,
                            Success = false,
                            ErrorMessage = ex.Message,
                            Artifact = null
                        };
                        await eventChannel.Writer.WriteAsync(failureEvent, cancellationToken);
                    }

                    if (options.FailFast)
                    {
                        // Close the channel and stop processing
                        eventChannel.Writer.Complete(ex);
                        throw;
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken);

            processingTasks.Add(task);
        }

        // Complete the channel when all tasks finish
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(processingTasks);
                eventChannel.Writer.Complete();
            }
            catch (Exception ex)
            {
                eventChannel.Writer.Complete(ex);
            }
        }, cancellationToken);

        // Stream events as they become available
        await foreach (var evt in eventChannel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }

        // Emit final BackfillCompletedEvent
        if (options.EmitProgressEvents)
        {
            yield return new Abstractions.Events.BackfillCompletedEvent
            {
                ArtifactKey = artifactKey,
                Duration = DateTimeOffset.UtcNow - startTime,
                SuccessfulPartitions = successfulCount,
                FailedPartitions = failedPartitions.Count,
                SkippedPartitions = partitionsSkipped
            };
        }

        // Check for failures after all processing is complete
        if (failedPartitions.Count > 0 && !options.FailFast)
        {
            // Aggregate all failures into a single exception
            var failureMessages = string.Join(", ", failedPartitions.Select(f => $"{f.Partition}: {f.Error.Message}"));
            throw new AggregateException(
                $"Backfill completed with {failedPartitions.Count} failures out of {totalCount} partitions: {failureMessages}",
                failedPartitions.Select(f => f.Error));
        }
    }
}

/// <summary>
/// Exception thrown when graph execution fails.
/// </summary>
public class GraphExecutionException : Exception
{
    public GraphExecutionException(string message) : base(message) { }
    public GraphExecutionException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a node suspends execution (human-in-the-loop).
/// Caller should save checkpoint and wait for external input.
/// </summary>
public class GraphSuspendedException : Exception
{
    public string NodeId { get; }
    public string SuspendToken { get; }

    public GraphSuspendedException(string nodeId, string suspendToken, string? message = null)
        : base(message ?? $"Graph suspended at node {nodeId}")
    {
        NodeId = nodeId;
        SuspendToken = suspendToken;
    }
}

/// <summary>
/// Map item execution state for polling support.
/// </summary>
internal enum MapItemStatus
{
    Pending,
    Polling,
    Succeeded,
    Failed
}

/// <summary>
/// Per-item state tracking for Map node polling.
/// Tracks the execution state, timing, and results for each mapped item.
/// </summary>
internal class MapItemState
{
    public MapItemStatus Status { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset LastAttempt { get; set; }
    public TimeSpan RetryAfter { get; set; }
    public int AttemptNumber { get; set; }
    public NodeExecutionResult? Result { get; set; }
    public Abstractions.Context.IGraphContext? Context { get; set; }

    /// <summary>
    /// Check if this item has reached a terminal state (succeeded or failed).
    /// </summary>
    public bool IsTerminal() => Status == MapItemStatus.Succeeded || Status == MapItemStatus.Failed;
}
