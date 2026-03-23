using System.Collections.Concurrent;
using System.Diagnostics;
using HPD.Agent;
using HPD.Events;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;

namespace HPD.MultiAgent.Observability;

/// <summary>
/// Observer that creates distributed tracing spans for workflow execution.
/// Uses System.Diagnostics.Activity for OpenTelemetry compatibility.
/// </summary>
public class TracingObserver : IEventObserver<Event>
{
    private readonly ActivitySource _activitySource;
    private readonly ConcurrentDictionary<string, Activity> _workflowActivities = new();
    private readonly ConcurrentDictionary<string, Activity> _nodeActivities = new();

    /// <summary>
    /// The ActivitySource name used for tracing.
    /// </summary>
    public const string ActivitySourceName = "HPD.MultiAgent";

    /// <summary>
    /// Creates a new tracing observer.
    /// </summary>
    public TracingObserver()
    {
        _activitySource = new ActivitySource(ActivitySourceName, "1.0.0");
    }

    /// <summary>
    /// Creates a new tracing observer with a custom ActivitySource.
    /// </summary>
    public TracingObserver(ActivitySource activitySource)
    {
        _activitySource = activitySource ?? throw new ArgumentNullException(nameof(activitySource));
    }

    /// <summary>
    /// The ActivitySource used for creating spans.
    /// </summary>
    public ActivitySource ActivitySource => _activitySource;

    /// <inheritdoc/>
    public bool ShouldProcess(Event evt)
    {
        return evt is GraphEvent
            or WorkflowStartedEvent
            or ToolCallStartEvent
            or ToolCallEndEvent;
    }

    /// <inheritdoc/>
    public Task OnEventAsync(Event evt, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (evt)
            {
                case GraphExecutionStartedEvent started:
                    StartWorkflowSpan(started);
                    break;

                case WorkflowStartedEvent workflowStarted:
                    // Patch the span name now that we have the workflow name
                    PatchWorkflowSpanName(workflowStarted);
                    break;

                case GraphExecutionCompletedEvent completed:
                    EndWorkflowSpan(completed);
                    break;

                case NodeExecutionStartedEvent nodeStarted:
                    StartNodeSpan(nodeStarted);
                    break;

                case NodeExecutionCompletedEvent nodeCompleted:
                    EndNodeSpan(nodeCompleted);
                    break;

                case ToolCallStartEvent toolStart:
                    // Could create child spans for tool calls
                    break;

                case ToolCallEndEvent toolEnd:
                    // End tool call span
                    break;
            }
        }
        catch
        {
            // Observers should never crash the system
        }

        return Task.CompletedTask;
    }

    private void StartWorkflowSpan(GraphExecutionStartedEvent evt)
    {
        var executionId = evt.GraphContext?.GraphId ?? Guid.NewGuid().ToString();

        // Start with a placeholder name — PatchWorkflowSpanName updates it when
        // WorkflowStartedEvent arrives with the actual workflow name.
        var activity = _activitySource.StartActivity(
            "Workflow:unnamed",
            ActivityKind.Internal);

        if (activity != null)
        {
            activity.SetTag("workflow.execution_id", executionId);
            activity.SetTag("workflow.node_count", evt.NodeCount);

            _workflowActivities[executionId] = activity;
        }
    }

    private void PatchWorkflowSpanName(WorkflowStartedEvent evt)
    {
        // Find the activity for this workflow execution by matching on execution_id tag.
        // WorkflowStartedEvent carries the workflow name but not the execution ID directly,
        // so we patch the most recently created unnamed span.
        foreach (var activity in _workflowActivities.Values)
        {
            if (activity.DisplayName == "Workflow:unnamed")
            {
                activity.DisplayName = $"Workflow:{evt.WorkflowName}";
                activity.SetTag("workflow.name", evt.WorkflowName);
                break;
            }
        }
    }

    private void EndWorkflowSpan(GraphExecutionCompletedEvent evt)
    {
        var executionId = evt.GraphContext?.GraphId;
        if (string.IsNullOrEmpty(executionId)) return;

        if (_workflowActivities.TryRemove(executionId, out var activity))
        {
            var success = evt.FailedNodes == 0;
            activity.SetTag("workflow.success", success);
            activity.SetTag("workflow.duration_ms", evt.Duration.TotalMilliseconds);
            activity.SetTag("workflow.successful_nodes", evt.SuccessfulNodes);
            activity.SetTag("workflow.failed_nodes", evt.FailedNodes);
            activity.SetTag("workflow.skipped_nodes", evt.SkippedNodes);

            if (!success)
            {
                activity.SetStatus(ActivityStatusCode.Error, $"{evt.FailedNodes} nodes failed");
            }
            else
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }

            activity.Stop();
        }
    }

    private void StartNodeSpan(NodeExecutionStartedEvent evt)
    {
        var executionId = evt.GraphContext?.GraphId;

        // Get parent workflow activity
        Activity? parent = null;
        if (!string.IsNullOrEmpty(executionId))
        {
            _workflowActivities.TryGetValue(executionId, out parent);
        }

        var activity = _activitySource.StartActivity(
            $"Node:{evt.NodeId}",
            ActivityKind.Internal,
            parent?.Context ?? default);

        if (activity != null)
        {
            activity.SetTag("node.id", evt.NodeId);
            activity.SetTag("node.handler", evt.HandlerName);
            activity.SetTag("node.layer", evt.LayerIndex);
            activity.SetTag("workflow.execution_id", executionId);

            var key = $"{executionId}:{evt.NodeId}";
            _nodeActivities[key] = activity;
        }
    }

    private void EndNodeSpan(NodeExecutionCompletedEvent evt)
    {
        var executionId = evt.GraphContext?.GraphId;
        var key = $"{executionId}:{evt.NodeId}";

        if (_nodeActivities.TryRemove(key, out var activity))
        {
            var success = evt.Result is NodeExecutionResult.Success;
            var skipped = evt.Result is NodeExecutionResult.Skipped;

            activity.SetTag("node.success", success);
            activity.SetTag("node.duration_ms", evt.Duration.TotalMilliseconds);
            activity.SetTag("node.skipped", skipped);

            if (evt.Result is NodeExecutionResult.Skipped skipResult)
            {
                activity.SetTag("node.skip_reason", skipResult.Message);
                activity.SetStatus(ActivityStatusCode.Ok, "Skipped");
            }
            else if (evt.Result is NodeExecutionResult.Failure failResult)
            {
                activity.SetStatus(ActivityStatusCode.Error, failResult.Exception.Message);
                activity.SetTag("node.error", failResult.Exception.Message);
            }
            else
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }

            activity.Stop();
        }
    }

    /// <summary>
    /// Dispose the observer and stop all active spans.
    /// </summary>
    public void Dispose()
    {
        // Stop all active activities
        foreach (var activity in _nodeActivities.Values)
        {
            activity.Stop();
        }
        foreach (var activity in _workflowActivities.Values)
        {
            activity.Stop();
        }

        _nodeActivities.Clear();
        _workflowActivities.Clear();
        _activitySource.Dispose();
    }
}

/// <summary>
/// Span information for a workflow or node execution.
/// </summary>
public sealed record SpanInfo
{
    /// <summary>
    /// Unique span ID.
    /// </summary>
    public required string SpanId { get; init; }

    /// <summary>
    /// Parent span ID (null for root spans).
    /// </summary>
    public string? ParentSpanId { get; init; }

    /// <summary>
    /// Trace ID (same for all spans in a workflow).
    /// </summary>
    public required string TraceId { get; init; }

    /// <summary>
    /// Operation name.
    /// </summary>
    public required string OperationName { get; init; }

    /// <summary>
    /// When the span started.
    /// </summary>
    public DateTimeOffset StartTime { get; init; }

    /// <summary>
    /// When the span ended (null if still active).
    /// </summary>
    public DateTimeOffset? EndTime { get; init; }

    /// <summary>
    /// Span duration.
    /// </summary>
    public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;

    /// <summary>
    /// Tags/attributes on the span.
    /// </summary>
    public Dictionary<string, object> Tags { get; init; } = new();

    /// <summary>
    /// Span status.
    /// </summary>
    public SpanStatus Status { get; init; } = SpanStatus.Unset;

    /// <summary>
    /// Error message if status is Error.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Span status values.
/// </summary>
public enum SpanStatus
{
    /// <summary>Status not set.</summary>
    Unset,
    /// <summary>Operation completed successfully.</summary>
    Ok,
    /// <summary>Operation failed with an error.</summary>
    Error
}
