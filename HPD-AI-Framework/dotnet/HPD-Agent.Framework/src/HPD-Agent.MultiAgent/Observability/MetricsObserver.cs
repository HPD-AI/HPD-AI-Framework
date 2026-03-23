using System.Collections.Concurrent;
using HPD.Agent;
using HPD.Events;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;

namespace HPD.MultiAgent.Observability;

/// <summary>
/// Observer that collects metrics from workflow execution events.
/// Implements IEventObserver for fire-and-forget metric collection.
/// </summary>
public class MetricsObserver : IEventObserver<Event>
{
    private readonly ConcurrentDictionary<string, WorkflowMetrics> _activeWorkflows = new();
    private readonly ConcurrentQueue<WorkflowMetrics> _completedWorkflows = new();
    private readonly int _maxCompletedWorkflows;

    // Tracks which node is currently executing per workflow execution ID.
    // Used to associate agent events (token usage, tool calls) with the right node.
    private readonly ConcurrentDictionary<string, string> _activeNodePerExecution = new();

    /// <summary>
    /// Creates a new metrics observer.
    /// </summary>
    /// <param name="maxCompletedWorkflows">Maximum completed workflows to retain. Default: 100.</param>
    public MetricsObserver(int maxCompletedWorkflows = 100)
    {
        _maxCompletedWorkflows = maxCompletedWorkflows;
    }

    /// <summary>
    /// Event raised when workflow metrics are updated.
    /// </summary>
    public event Action<WorkflowMetrics>? OnMetricsUpdated;

    /// <summary>
    /// Event raised when a workflow completes.
    /// </summary>
    public event Action<WorkflowMetrics>? OnWorkflowCompleted;

    /// <summary>
    /// Get metrics for an active workflow.
    /// </summary>
    public WorkflowMetrics? GetActiveWorkflow(string executionId)
    {
        return _activeWorkflows.TryGetValue(executionId, out var metrics) ? metrics : null;
    }

    /// <summary>
    /// Get all active workflow metrics.
    /// </summary>
    public IReadOnlyCollection<WorkflowMetrics> ActiveWorkflows => _activeWorkflows.Values.ToList();

    /// <summary>
    /// Get recently completed workflow metrics.
    /// </summary>
    public IReadOnlyCollection<WorkflowMetrics> CompletedWorkflows => _completedWorkflows.ToList();

    /// <inheritdoc/>
    public bool ShouldProcess(Event evt)
    {
        // Process graph events and agent events we care about
        return evt is GraphEvent
            or TextDeltaEvent
            or ToolCallStartEvent
            or ToolCallEndEvent
            or MessageTurnFinishedEvent
            or NodeApprovalRequestEvent
            or NodeApprovalResponseEvent;
    }

    /// <inheritdoc/>
    public Task OnEventAsync(Event evt, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (evt)
            {
                // Graph lifecycle events
                case GraphExecutionStartedEvent started:
                    HandleGraphStarted(started);
                    break;

                case GraphExecutionCompletedEvent completed:
                    HandleGraphCompleted(completed);
                    break;

                // Node execution events
                case NodeExecutionStartedEvent nodeStarted:
                    HandleNodeStarted(nodeStarted);
                    break;

                case NodeExecutionCompletedEvent nodeCompleted:
                    HandleNodeCompleted(nodeCompleted);
                    break;

                // Iteration events
                case IterationStartedEvent iterationStarted:
                    HandleIterationStarted(iterationStarted);
                    break;

                // Tool events (from agent)
                case ToolCallStartEvent toolStart:
                    HandleToolCallStart(toolStart);
                    break;

                // Token usage (from agent turn completion)
                case MessageTurnFinishedEvent turnFinished:
                    HandleTurnFinished(turnFinished);
                    break;

                // Approval events
                case NodeApprovalRequestEvent approvalRequest:
                    HandleApprovalRequest(approvalRequest);
                    break;

                case NodeApprovalResponseEvent approvalResponse:
                    HandleApprovalResponse(approvalResponse);
                    break;
            }
        }
        catch
        {
            // Observers should never crash the system
            // In production, log this error
        }

        return Task.CompletedTask;
    }

    private void HandleGraphStarted(GraphExecutionStartedEvent evt)
    {
        var executionId = evt.GraphContext?.GraphId ?? Guid.NewGuid().ToString();

        var metrics = new WorkflowMetrics
        {
            ExecutionId = executionId,
            WorkflowName = null, // Graph name not directly on event
            StartedAt = DateTimeOffset.UtcNow
        };

        _activeWorkflows[executionId] = metrics;
        OnMetricsUpdated?.Invoke(metrics);
    }

    private void HandleGraphCompleted(GraphExecutionCompletedEvent evt)
    {
        var executionId = evt.GraphContext?.GraphId;
        if (string.IsNullOrEmpty(executionId)) return;

        if (_activeWorkflows.TryRemove(executionId, out var metrics))
        {
            metrics.CompletedAt = DateTimeOffset.UtcNow;
            metrics.Success = evt.FailedNodes == 0;

            // Move to completed queue
            _completedWorkflows.Enqueue(metrics);

            // Trim if over limit
            while (_completedWorkflows.Count > _maxCompletedWorkflows)
            {
                _completedWorkflows.TryDequeue(out _);
            }

            OnWorkflowCompleted?.Invoke(metrics);
        }
    }

    private void HandleNodeStarted(NodeExecutionStartedEvent evt)
    {
        var executionId = evt.GraphContext?.GraphId;
        var metrics = GetOrCreateWorkflowMetrics(executionId);
        if (metrics == null) return;

        var nodeMetrics = metrics.GetOrCreateNodeMetrics(evt.NodeId);
        nodeMetrics.StartedAt = DateTimeOffset.UtcNow;
        nodeMetrics.Iteration = evt.LayerIndex ?? 0;

        // Track which node is active so agent events can be associated with it
        if (!string.IsNullOrEmpty(executionId))
            _activeNodePerExecution[executionId] = evt.NodeId;

        OnMetricsUpdated?.Invoke(metrics);
    }

    private void HandleNodeCompleted(NodeExecutionCompletedEvent evt)
    {
        var executionId = evt.GraphContext?.GraphId;
        var metrics = GetOrCreateWorkflowMetrics(executionId);
        if (metrics == null) return;

        var nodeMetrics = metrics.GetOrCreateNodeMetrics(evt.NodeId);
        nodeMetrics.CompletedAt = DateTimeOffset.UtcNow;
        nodeMetrics.Duration = evt.Duration;

        // Determine success/skip from Result
        nodeMetrics.Success = evt.Result is NodeExecutionResult.Success;
        nodeMetrics.WasSkipped = evt.Result is NodeExecutionResult.Skipped;

        if (evt.Result is NodeExecutionResult.Skipped skipped)
        {
            nodeMetrics.SkipReason = skipped.Message;
        }
        else if (evt.Result is NodeExecutionResult.Failure failure)
        {
            nodeMetrics.ErrorMessage = failure.Exception.Message;
        }

        // Clear the active node tracking for this execution
        if (!string.IsNullOrEmpty(executionId))
            _activeNodePerExecution.TryRemove(executionId, out _);

        OnMetricsUpdated?.Invoke(metrics);
    }

    private void HandleIterationStarted(IterationStartedEvent evt)
    {
        var executionId = evt.GraphContext?.GraphId;
        var metrics = GetOrCreateWorkflowMetrics(executionId);
        if (metrics == null) return;

        metrics.IterationCount = evt.IterationIndex + 1;
        OnMetricsUpdated?.Invoke(metrics);
    }

    private void HandleToolCallStart(ToolCallStartEvent evt)
    {
        var (metrics, nodeMetrics) = FindActiveNodeMetrics();
        if (nodeMetrics == null || metrics == null) return;

        nodeMetrics.ToolCallCount++;
        if (!string.IsNullOrEmpty(evt.Name))
            nodeMetrics.ToolsCalled.Add(evt.Name);

        OnMetricsUpdated?.Invoke(metrics);
    }

    private void HandleTurnFinished(MessageTurnFinishedEvent evt)
    {
        if (evt.Usage == null) return;

        var (metrics, nodeMetrics) = FindActiveNodeMetrics();
        if (nodeMetrics == null || metrics == null) return;

        nodeMetrics.InputTokens += (int)(evt.Usage.InputTokenCount ?? 0);
        nodeMetrics.OutputTokens += (int)(evt.Usage.OutputTokenCount ?? 0);

        OnMetricsUpdated?.Invoke(metrics);
    }

    /// <summary>
    /// Finds the workflow metrics and node metrics for the currently-executing node
    /// by looking up the active node per execution ID.
    /// </summary>
    private (WorkflowMetrics? Workflow, NodeMetrics? Node) FindActiveNodeMetrics()
    {
        foreach (var (executionId, nodeId) in _activeNodePerExecution)
        {
            if (_activeWorkflows.TryGetValue(executionId, out var metrics) &&
                metrics.NodeMetrics.TryGetValue(nodeId, out var nodeMetrics))
            {
                return (metrics, nodeMetrics);
            }
        }
        return (null, null);
    }

    private void HandleApprovalRequest(NodeApprovalRequestEvent evt)
    {
        // Find the workflow by searching active workflows for the node
        foreach (var metrics in _activeWorkflows.Values)
        {
            if (metrics.NodeMetrics.TryGetValue(evt.NodeId, out var nodeMetrics))
            {
                nodeMetrics.RequiredApproval = true;
                OnMetricsUpdated?.Invoke(metrics);
                break;
            }
        }
    }

    private void HandleApprovalResponse(NodeApprovalResponseEvent evt)
    {
        // Find the workflow and update approval status
        foreach (var metrics in _activeWorkflows.Values)
        {
            foreach (var nodeMetrics in metrics.NodeMetrics.Values)
            {
                if (nodeMetrics.RequiredApproval && nodeMetrics.ApprovalGranted == null)
                {
                    nodeMetrics.ApprovalGranted = evt.Approved;
                    if (nodeMetrics.StartedAt.HasValue)
                    {
                        nodeMetrics.ApprovalWaitTime = DateTimeOffset.UtcNow - nodeMetrics.StartedAt.Value;
                    }
                    OnMetricsUpdated?.Invoke(metrics);
                    return;
                }
            }
        }
    }

    private WorkflowMetrics? GetOrCreateWorkflowMetrics(string? executionId)
    {
        if (string.IsNullOrEmpty(executionId))
            return null;

        return _activeWorkflows.GetOrAdd(executionId, id => new WorkflowMetrics
        {
            ExecutionId = id,
            StartedAt = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Clear all metrics.
    /// </summary>
    public void Clear()
    {
        _activeWorkflows.Clear();
        _activeNodePerExecution.Clear();
        while (_completedWorkflows.TryDequeue(out _)) { }
    }
}
