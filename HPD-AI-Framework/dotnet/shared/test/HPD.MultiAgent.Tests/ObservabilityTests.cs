using System.Diagnostics;
using HPD.Events;
using HPD.Events.Core;
using HPD.MultiAgent;
using HPD.MultiAgent.Observability;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;
using MessageTurnFinishedEvent = HPD.Agent.MessageTurnFinishedEvent;
using ToolCallStartEvent = HPD.Agent.ToolCallStartEvent;

namespace HPD.MultiAgent.Tests;

/// <summary>
/// Tests for Phase 5: Observability features.
/// </summary>
public class ObservabilityTests
{
    #region WorkflowMetrics Tests

    [Fact]
    public void WorkflowMetrics_Calculates_Duration()
    {
        var startTime = DateTimeOffset.UtcNow;
        var metrics = new WorkflowMetrics
        {
            ExecutionId = "test-123",
            StartedAt = startTime
        };

        // Duration should be calculated from now
        metrics.Duration.Should().BeCloseTo(TimeSpan.Zero, TimeSpan.FromSeconds(1));

        // After completion
        metrics.CompletedAt = startTime.AddSeconds(5);
        metrics.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void WorkflowMetrics_Counts_Nodes_Correctly()
    {
        var metrics = new WorkflowMetrics
        {
            ExecutionId = "test-123",
            StartedAt = DateTimeOffset.UtcNow
        };

        // Add some node metrics
        var node1 = metrics.GetOrCreateNodeMetrics("node1");
        node1.Success = true;

        var node2 = metrics.GetOrCreateNodeMetrics("node2");
        node2.Success = false;

        var node3 = metrics.GetOrCreateNodeMetrics("node3");
        node3.WasSkipped = true;

        var node4 = metrics.GetOrCreateNodeMetrics("node4");
        node4.Success = true;

        metrics.TotalNodesExecuted.Should().Be(4);
        metrics.SuccessfulNodes.Should().Be(2);
        metrics.FailedNodes.Should().Be(1);
        metrics.SkippedNodes.Should().Be(1);
    }

    [Fact]
    public void WorkflowMetrics_Sums_Tokens()
    {
        var metrics = new WorkflowMetrics
        {
            ExecutionId = "test-123",
            StartedAt = DateTimeOffset.UtcNow
        };

        var node1 = metrics.GetOrCreateNodeMetrics("node1");
        node1.InputTokens = 100;
        node1.OutputTokens = 50;

        var node2 = metrics.GetOrCreateNodeMetrics("node2");
        node2.InputTokens = 200;
        node2.OutputTokens = 100;

        metrics.TotalInputTokens.Should().Be(300);
        metrics.TotalOutputTokens.Should().Be(150);
        metrics.TotalTokens.Should().Be(450);
    }

    [Fact]
    public void WorkflowMetrics_Counts_ToolCalls()
    {
        var metrics = new WorkflowMetrics
        {
            ExecutionId = "test-123",
            StartedAt = DateTimeOffset.UtcNow
        };

        var node1 = metrics.GetOrCreateNodeMetrics("node1");
        node1.ToolCallCount = 3;

        var node2 = metrics.GetOrCreateNodeMetrics("node2");
        node2.ToolCallCount = 2;

        metrics.TotalToolCalls.Should().Be(5);
    }

    [Fact]
    public void WorkflowMetrics_Tags_Can_Be_Added()
    {
        var metrics = new WorkflowMetrics
        {
            ExecutionId = "test-123",
            StartedAt = DateTimeOffset.UtcNow
        };

        metrics.Tags["environment"] = "production";
        metrics.Tags["user_id"] = "user-456";

        metrics.Tags.Should().ContainKey("environment");
        metrics.Tags["environment"].Should().Be("production");
    }

    #endregion

    #region NodeMetrics Tests

    [Fact]
    public void NodeMetrics_Default_Values()
    {
        var nodeMetrics = new NodeMetrics { NodeId = "test-node" };

        nodeMetrics.NodeId.Should().Be("test-node");
        nodeMetrics.StartedAt.Should().BeNull();
        nodeMetrics.CompletedAt.Should().BeNull();
        nodeMetrics.Success.Should().BeNull();
        nodeMetrics.WasSkipped.Should().BeFalse();
        nodeMetrics.RetryCount.Should().Be(0);
        nodeMetrics.InputTokens.Should().Be(0);
        nodeMetrics.OutputTokens.Should().Be(0);
        nodeMetrics.ToolCallCount.Should().Be(0);
        nodeMetrics.RequiredApproval.Should().BeFalse();
    }

    [Fact]
    public void NodeMetrics_TotalTokens_Calculates_Correctly()
    {
        var nodeMetrics = new NodeMetrics { NodeId = "test-node" };
        nodeMetrics.InputTokens = 150;
        nodeMetrics.OutputTokens = 75;

        nodeMetrics.TotalTokens.Should().Be(225);
    }

    [Fact]
    public void NodeMetrics_ToolsCalled_Tracks_Tools()
    {
        var nodeMetrics = new NodeMetrics { NodeId = "test-node" };

        nodeMetrics.ToolsCalled.Add("search");
        nodeMetrics.ToolsCalled.Add("calculate");
        nodeMetrics.ToolsCalled.Add("format");

        nodeMetrics.ToolsCalled.Should().HaveCount(3);
        nodeMetrics.ToolsCalled.Should().Contain("search");
    }

    [Fact]
    public void NodeMetrics_Metadata_Can_Be_Added()
    {
        var nodeMetrics = new NodeMetrics { NodeId = "test-node" };

        nodeMetrics.Metadata["custom_field"] = "custom_value";
        nodeMetrics.Metadata["count"] = 42;

        nodeMetrics.Metadata.Should().ContainKey("custom_field");
        nodeMetrics.Metadata["count"].Should().Be(42);
    }

    [Fact]
    public void NodeMetrics_Tracks_Approval()
    {
        var nodeMetrics = new NodeMetrics { NodeId = "test-node" };

        nodeMetrics.RequiredApproval = true;
        nodeMetrics.ApprovalGranted = true;
        nodeMetrics.ApprovalWaitTime = TimeSpan.FromSeconds(30);

        nodeMetrics.RequiredApproval.Should().BeTrue();
        nodeMetrics.ApprovalGranted.Should().BeTrue();
        nodeMetrics.ApprovalWaitTime.Should().Be(TimeSpan.FromSeconds(30));
    }

    #endregion

    #region MetricsObserver Tests

    [Fact]
    public void MetricsObserver_Creates_With_Default_Capacity()
    {
        var observer = new MetricsObserver();

        observer.ActiveWorkflows.Should().BeEmpty();
        observer.CompletedWorkflows.Should().BeEmpty();
    }

    [Fact]
    public void MetricsObserver_Creates_With_Custom_Capacity()
    {
        var observer = new MetricsObserver(maxCompletedWorkflows: 50);

        observer.Should().NotBeNull();
    }

    [Fact]
    public async Task MetricsObserver_ShouldProcess_Returns_True_For_Graph_Events()
    {
        var observer = new MetricsObserver();

        var graphEvent = new GraphExecutionStartedEvent { NodeCount = 5 };
        observer.ShouldProcess(graphEvent).Should().BeTrue();

        var nodeEvent = new NodeExecutionStartedEvent
        {
            NodeId = "test",
            HandlerName = "TestHandler"
        };
        observer.ShouldProcess(nodeEvent).Should().BeTrue();
    }

    [Fact]
    public async Task MetricsObserver_Handles_GraphStarted_Event()
    {
        var observer = new MetricsObserver();
        WorkflowMetrics? capturedMetrics = null;
        observer.OnMetricsUpdated += m => capturedMetrics = m;

        var evt = new GraphExecutionStartedEvent
        {
            NodeCount = 5,
            GraphContext = new GraphExecutionContext
            {
                GraphId = "workflow-123",
                TotalNodes = 5
            }
        };

        await observer.OnEventAsync(evt);

        capturedMetrics.Should().NotBeNull();
        capturedMetrics!.ExecutionId.Should().Be("workflow-123");
        observer.ActiveWorkflows.Should().HaveCount(1);
    }

    [Fact]
    public async Task MetricsObserver_Handles_GraphCompleted_Event()
    {
        var observer = new MetricsObserver();
        WorkflowMetrics? completedMetrics = null;
        observer.OnWorkflowCompleted += m => completedMetrics = m;

        // Start workflow
        var startEvt = new GraphExecutionStartedEvent
        {
            NodeCount = 5,
            GraphContext = new GraphExecutionContext { GraphId = "workflow-123", TotalNodes = 5 }
        };
        await observer.OnEventAsync(startEvt);

        // Complete workflow
        var endEvt = new GraphExecutionCompletedEvent
        {
            Duration = TimeSpan.FromSeconds(10),
            SuccessfulNodes = 5,
            FailedNodes = 0,
            GraphContext = new GraphExecutionContext { GraphId = "workflow-123", TotalNodes = 5 }
        };
        await observer.OnEventAsync(endEvt);

        completedMetrics.Should().NotBeNull();
        completedMetrics!.Success.Should().BeTrue();
        observer.ActiveWorkflows.Should().BeEmpty();
        observer.CompletedWorkflows.Should().HaveCount(1);
    }

    [Fact]
    public async Task MetricsObserver_Handles_NodeStarted_Event()
    {
        var observer = new MetricsObserver();

        // Start workflow first
        var startEvt = new GraphExecutionStartedEvent
        {
            NodeCount = 2,
            GraphContext = new GraphExecutionContext { GraphId = "workflow-123", TotalNodes = 2 }
        };
        await observer.OnEventAsync(startEvt);

        // Start node
        var nodeEvt = new NodeExecutionStartedEvent
        {
            NodeId = "node-1",
            HandlerName = "TestHandler",
            GraphContext = new GraphExecutionContext { GraphId = "workflow-123", TotalNodes = 2 }
        };
        await observer.OnEventAsync(nodeEvt);

        var metrics = observer.GetActiveWorkflow("workflow-123");
        metrics.Should().NotBeNull();
        metrics!.NodeMetrics.Should().ContainKey("node-1");
        metrics.NodeMetrics["node-1"].StartedAt.Should().NotBeNull();
    }

    [Fact]
    public void MetricsObserver_Clear_Removes_All_Metrics()
    {
        var observer = new MetricsObserver();

        // Add a workflow manually through the event
        var startEvt = new GraphExecutionStartedEvent
        {
            NodeCount = 1,
            GraphContext = new GraphExecutionContext { GraphId = "test", TotalNodes = 1 }
        };
        observer.OnEventAsync(startEvt);

        observer.ActiveWorkflows.Should().NotBeEmpty();

        observer.Clear();

        observer.ActiveWorkflows.Should().BeEmpty();
        observer.CompletedWorkflows.Should().BeEmpty();
    }

    // ── 2.1  HandleTurnFinished accumulates InputTokens ──────────────────────

    [Fact]
    public async Task MetricsObserver_HandleTurnFinished_AccumulatesInputTokens()
    {
        var observer = new MetricsObserver();
        var (executionId, nodeId) = await StartWorkflowAndNode(observer);

        var usage = new Microsoft.Extensions.AI.UsageDetails { InputTokenCount = 120, OutputTokenCount = 0 };
        await observer.OnEventAsync(new MessageTurnFinishedEvent("t", "c", "A", TimeSpan.Zero, Usage: usage));

        var node = observer.GetActiveWorkflow(executionId)!.NodeMetrics[nodeId];
        node.InputTokens.Should().Be(120);
    }

    // ── 2.2  HandleTurnFinished accumulates OutputTokens ─────────────────────

    [Fact]
    public async Task MetricsObserver_HandleTurnFinished_AccumulatesOutputTokens()
    {
        var observer = new MetricsObserver();
        var (executionId, nodeId) = await StartWorkflowAndNode(observer);

        var usage = new Microsoft.Extensions.AI.UsageDetails { InputTokenCount = 0, OutputTokenCount = 60 };
        await observer.OnEventAsync(new MessageTurnFinishedEvent("t", "c", "A", TimeSpan.Zero, Usage: usage));

        var node = observer.GetActiveWorkflow(executionId)!.NodeMetrics[nodeId];
        node.OutputTokens.Should().Be(60);
    }

    // ── 2.3  HandleTurnFinished: null Usage is a no-op ───────────────────────

    [Fact]
    public async Task MetricsObserver_HandleTurnFinished_NullUsage_DoesNothing()
    {
        var observer = new MetricsObserver();
        var (executionId, nodeId) = await StartWorkflowAndNode(observer);

        var act = async () => await observer.OnEventAsync(
            new MessageTurnFinishedEvent("t", "c", "A", TimeSpan.Zero, Usage: null));

        await act.Should().NotThrowAsync();
        observer.GetActiveWorkflow(executionId)!.NodeMetrics[nodeId].InputTokens.Should().Be(0);
    }

    // ── 2.4  HandleTurnFinished: no active node → no-op ──────────────────────

    [Fact]
    public async Task MetricsObserver_HandleTurnFinished_NoActiveNode_DoesNothing()
    {
        // Workflow exists but no node has been started (so _activeNodePerExecution is empty)
        var observer = new MetricsObserver();
        await observer.OnEventAsync(new GraphExecutionStartedEvent
        {
            NodeCount = 1,
            GraphContext = new GraphExecutionContext { GraphId = "wf-no-node", TotalNodes = 1 }
        });

        var usage = new Microsoft.Extensions.AI.UsageDetails { InputTokenCount = 50, OutputTokenCount = 25 };
        var act = async () => await observer.OnEventAsync(
            new MessageTurnFinishedEvent("t", "c", "A", TimeSpan.Zero, Usage: usage));

        await act.Should().NotThrowAsync();
    }

    // ── 2.5  HandleTurnFinished: multiple events sum correctly ────────────────

    [Fact]
    public async Task MetricsObserver_HandleTurnFinished_MultipleEvents_TokensSum()
    {
        var observer = new MetricsObserver();
        var (executionId, nodeId) = await StartWorkflowAndNode(observer);

        await observer.OnEventAsync(new MessageTurnFinishedEvent("t1", "c", "A", TimeSpan.Zero,
            Usage: new Microsoft.Extensions.AI.UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 }));

        await observer.OnEventAsync(new MessageTurnFinishedEvent("t2", "c", "A", TimeSpan.Zero,
            Usage: new Microsoft.Extensions.AI.UsageDetails { InputTokenCount = 200, OutputTokenCount = 75 }));

        var node = observer.GetActiveWorkflow(executionId)!.NodeMetrics[nodeId];
        node.InputTokens.Should().Be(300);
        node.OutputTokens.Should().Be(125);
    }

    // ── 2.6  HandleToolCallStart: increments ToolCallCount ───────────────────

    [Fact]
    public async Task MetricsObserver_HandleToolCallStart_IncrementsToolCallCount()
    {
        var observer = new MetricsObserver();
        var (executionId, nodeId) = await StartWorkflowAndNode(observer);

        await observer.OnEventAsync(new ToolCallStartEvent("id1", "search", "msg1"));
        await observer.OnEventAsync(new ToolCallStartEvent("id2", "calculate", "msg1"));
        await observer.OnEventAsync(new ToolCallStartEvent("id3", "format", "msg1"));

        var node = observer.GetActiveWorkflow(executionId)!.NodeMetrics[nodeId];
        node.ToolCallCount.Should().Be(3);
    }

    // ── 2.7  HandleToolCallStart: records tool name ───────────────────────────

    [Fact]
    public async Task MetricsObserver_HandleToolCallStart_AddsToolName()
    {
        var observer = new MetricsObserver();
        var (executionId, nodeId) = await StartWorkflowAndNode(observer);

        await observer.OnEventAsync(new ToolCallStartEvent("id1", "search", "msg1"));

        var node = observer.GetActiveWorkflow(executionId)!.NodeMetrics[nodeId];
        node.ToolsCalled.Should().Contain("search");
    }

    // ── 2.8  HandleToolCallStart: null/empty name → not added to set ─────────

    [Fact]
    public async Task MetricsObserver_HandleToolCallStart_EmptyName_DoesNotAddToSet()
    {
        var observer = new MetricsObserver();
        var (executionId, nodeId) = await StartWorkflowAndNode(observer);

        await observer.OnEventAsync(new ToolCallStartEvent("id1", "", "msg1"));

        var node = observer.GetActiveWorkflow(executionId)!.NodeMetrics[nodeId];
        node.ToolsCalled.Should().BeEmpty();
        node.ToolCallCount.Should().Be(1, "count still increments even when name is empty");
    }

    // ── 2.9  HandleToolCallStart: no active node → no-op ─────────────────────

    [Fact]
    public async Task MetricsObserver_HandleToolCallStart_NoActiveNode_DoesNothing()
    {
        var observer = new MetricsObserver();
        await observer.OnEventAsync(new GraphExecutionStartedEvent
        {
            NodeCount = 1,
            GraphContext = new GraphExecutionContext { GraphId = "wf-no-node2", TotalNodes = 1 }
        });

        var act = async () => await observer.OnEventAsync(
            new ToolCallStartEvent("id1", "search", "msg1"));

        await act.Should().NotThrowAsync();
    }

    // ── 2.10  Active node tracking: set on NodeStart, cleared on NodeComplete ─

    [Fact]
    public async Task MetricsObserver_ActiveNodeTracking_SetOnNodeStart_ClearedOnNodeComplete()
    {
        const string executionId = "wf-tracking";
        const string nodeId = "node-track";
        var observer = new MetricsObserver();

        await observer.OnEventAsync(new GraphExecutionStartedEvent
        {
            NodeCount = 1,
            GraphContext = new GraphExecutionContext { GraphId = executionId, TotalNodes = 1 }
        });

        // After NodeStart a ToolCallStart should reach the node metrics
        await observer.OnEventAsync(new NodeExecutionStartedEvent
        {
            NodeId = nodeId,
            HandlerName = "H",
            GraphContext = new GraphExecutionContext { GraphId = executionId, TotalNodes = 1 }
        });

        await observer.OnEventAsync(new ToolCallStartEvent("id1", "tool-a", "msg"));
        observer.GetActiveWorkflow(executionId)!.NodeMetrics[nodeId].ToolCallCount.Should().Be(1);

        // After NodeComplete the active tracking is cleared → next tool call lands nowhere
        await observer.OnEventAsync(new NodeExecutionCompletedEvent
        {
            NodeId = nodeId,
            HandlerName = "H",
            Duration = TimeSpan.Zero,
            Result = new NodeExecutionResult.Skipped(SkipReason.ConditionNotMet),
            GraphContext = new GraphExecutionContext { GraphId = executionId, TotalNodes = 1 }
        });

        await observer.OnEventAsync(new ToolCallStartEvent("id2", "tool-b", "msg"));
        observer.GetActiveWorkflow(executionId)!.NodeMetrics[nodeId].ToolCallCount.Should().Be(1,
            "tool after node completes must not increment that node's count");
    }

    // ── 2.11  Tokens routed to the active node, not a sibling ────────────────

    [Fact]
    public async Task MetricsObserver_HandleTurnFinished_TokensGoToActiveNode_NotSibling()
    {
        const string executionId = "wf-two-nodes";
        var observer = new MetricsObserver();

        await observer.OnEventAsync(new GraphExecutionStartedEvent
        {
            NodeCount = 2,
            GraphContext = new GraphExecutionContext { GraphId = executionId, TotalNodes = 2 }
        });

        // node-A runs, finishes, tokens emitted BEFORE node-A completes
        await observer.OnEventAsync(new NodeExecutionStartedEvent
        {
            NodeId = "node-A",
            HandlerName = "H",
            GraphContext = new GraphExecutionContext { GraphId = executionId, TotalNodes = 2 }
        });
        await observer.OnEventAsync(new MessageTurnFinishedEvent("t1", "c", "A", TimeSpan.Zero,
            Usage: new Microsoft.Extensions.AI.UsageDetails { InputTokenCount = 50, OutputTokenCount = 10 }));
        await observer.OnEventAsync(new NodeExecutionCompletedEvent
        {
            NodeId = "node-A",
            HandlerName = "H",
            Duration = TimeSpan.Zero,
            Result = new NodeExecutionResult.Skipped(SkipReason.ConditionNotMet),
            GraphContext = new GraphExecutionContext { GraphId = executionId, TotalNodes = 2 }
        });

        // node-B runs, different token counts
        await observer.OnEventAsync(new NodeExecutionStartedEvent
        {
            NodeId = "node-B",
            HandlerName = "H",
            GraphContext = new GraphExecutionContext { GraphId = executionId, TotalNodes = 2 }
        });
        await observer.OnEventAsync(new MessageTurnFinishedEvent("t2", "c", "A", TimeSpan.Zero,
            Usage: new Microsoft.Extensions.AI.UsageDetails { InputTokenCount = 200, OutputTokenCount = 80 }));

        var wf = observer.GetActiveWorkflow(executionId)!;
        wf.NodeMetrics["node-A"].InputTokens.Should().Be(50);
        wf.NodeMetrics["node-B"].InputTokens.Should().Be(200);
    }

    // ── 2.12  Clear() also resets active node tracking ───────────────────────

    [Fact]
    public async Task MetricsObserver_Clear_AlsoClearsActiveNodeTracking()
    {
        var observer = new MetricsObserver();
        var (_, _) = await StartWorkflowAndNode(observer);

        observer.Clear();

        // After Clear, a ToolCallStart should land nowhere (no active node)
        var act = async () => await observer.OnEventAsync(
            new ToolCallStartEvent("id1", "search", "msg1"));
        await act.Should().NotThrowAsync();
        observer.ActiveWorkflows.Should().BeEmpty();
    }

    // ── helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fires GraphExecutionStartedEvent + NodeExecutionStartedEvent so that
    /// <see cref="MetricsObserver"/> has an active node ready to receive agent events.
    /// </summary>
    private static async Task<(string ExecutionId, string NodeId)> StartWorkflowAndNode(
        MetricsObserver observer,
        string executionId = "wf-active",
        string nodeId = "node-active")
    {
        await observer.OnEventAsync(new GraphExecutionStartedEvent
        {
            NodeCount = 1,
            GraphContext = new GraphExecutionContext { GraphId = executionId, TotalNodes = 1 }
        });

        await observer.OnEventAsync(new NodeExecutionStartedEvent
        {
            NodeId = nodeId,
            HandlerName = "TestHandler",
            GraphContext = new GraphExecutionContext { GraphId = executionId, TotalNodes = 1 }
        });

        return (executionId, nodeId);
    }

    #endregion

    #region TracingObserver Tests

    [Fact]
    public void TracingObserver_Creates_With_Default_ActivitySource()
    {
        var observer = new TracingObserver();

        observer.ActivitySource.Should().NotBeNull();
        observer.ActivitySource.Name.Should().Be(TracingObserver.ActivitySourceName);
    }

    [Fact]
    public void TracingObserver_Creates_With_Custom_ActivitySource()
    {
        var activitySource = new ActivitySource("Custom.Source");
        var observer = new TracingObserver(activitySource);

        observer.ActivitySource.Should().BeSameAs(activitySource);

        observer.Dispose();
        activitySource.Dispose();
    }

    [Fact]
    public void TracingObserver_ShouldProcess_Returns_True_For_Graph_Events()
    {
        var observer = new TracingObserver();

        var graphEvent = new GraphExecutionStartedEvent { NodeCount = 5 };
        observer.ShouldProcess(graphEvent).Should().BeTrue();
    }

    [Fact]
    public async Task TracingObserver_Handles_Events_Without_Throwing()
    {
        var observer = new TracingObserver();

        // Should not throw on any event
        var startEvt = new GraphExecutionStartedEvent
        {
            NodeCount = 1,
            GraphContext = new GraphExecutionContext { GraphId = "test", TotalNodes = 1 }
        };

        var act = async () => await observer.OnEventAsync(startEvt);
        await act.Should().NotThrowAsync();

        observer.Dispose();
    }

    [Fact]
    public void TracingObserver_Dispose_Cleans_Up()
    {
        var observer = new TracingObserver();

        var act = () => observer.Dispose();
        act.Should().NotThrow();
    }

    // ── 6.1  ShouldProcess includes WorkflowStartedEvent ─────────────────────

    [Fact]
    public void TracingObserver_ShouldProcess_WorkflowStartedEvent_ReturnsTrue()
    {
        var observer = new TracingObserver();

        var evt = new WorkflowStartedEvent { WorkflowName = "W", NodeCount = 1 };
        observer.ShouldProcess(evt).Should().BeTrue();

        observer.Dispose();
    }

    // ── 6.2  PatchWorkflowSpanName patches DisplayName ───────────────────────

    [Fact]
    public async Task TracingObserver_WorkflowStartedEvent_PatchesUnnamedSpan()
    {
        using var listener = CreateSamplingListener(TracingObserver.ActivitySourceName);
        var observer = new TracingObserver();

        // 1. GraphExecutionStartedEvent creates "Workflow:unnamed" span
        await observer.OnEventAsync(new GraphExecutionStartedEvent
        {
            NodeCount = 1,
            GraphContext = new GraphExecutionContext { GraphId = "gx-1", TotalNodes = 1 }
        });

        // 2. WorkflowStartedEvent patches it
        await observer.OnEventAsync(new WorkflowStartedEvent
        {
            WorkflowName = "MyFlow",
            NodeCount = 1
        });

        // The activity stored internally should now carry the patched name.
        // We verify indirectly via the observer not throwing and then completing it.
        var act = async () => await observer.OnEventAsync(new GraphExecutionCompletedEvent
        {
            Duration = TimeSpan.FromSeconds(1),
            SuccessfulNodes = 1,
            FailedNodes = 0,
            GraphContext = new GraphExecutionContext { GraphId = "gx-1", TotalNodes = 1 }
        });
        await act.Should().NotThrowAsync();

        observer.Dispose();
    }

    // ── 6.3  PatchWorkflowSpanName sets workflow.name tag ────────────────────

    [Fact]
    public async Task TracingObserver_WorkflowStartedEvent_SetsWorkflowNameTag()
    {
        Activity? capturedActivity = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == TracingObserver.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a =>
            {
                if (a.DisplayName == "Workflow:unnamed") capturedActivity = a;
            }
        };
        ActivitySource.AddActivityListener(listener);

        var observer = new TracingObserver();

        await observer.OnEventAsync(new GraphExecutionStartedEvent
        {
            NodeCount = 1,
            GraphContext = new GraphExecutionContext { GraphId = "gx-tag", TotalNodes = 1 }
        });

        await observer.OnEventAsync(new WorkflowStartedEvent
        {
            WorkflowName = "TagFlow",
            NodeCount = 1
        });

        // The activity should have had its name and tag patched
        capturedActivity.Should().NotBeNull("listener must capture the span");
        capturedActivity!.DisplayName.Should().Be("Workflow:TagFlow");
        capturedActivity.GetTagItem("workflow.name").Should().Be("TagFlow");

        observer.Dispose();
    }

    // ── 6.4  WorkflowStartedEvent with no matching span → no throw ───────────

    [Fact]
    public async Task TracingObserver_WorkflowStartedEvent_NoMatchingSpan_DoesNotThrow()
    {
        // No GraphExecutionStartedEvent first → nothing in _workflowActivities
        var observer = new TracingObserver();

        var act = async () => await observer.OnEventAsync(new WorkflowStartedEvent
        {
            WorkflowName = "Orphan",
            NodeCount = 1
        });

        await act.Should().NotThrowAsync();
        observer.Dispose();
    }

    // ── 6.5  Already-named span is not re-patched by a second event ───────────

    [Fact]
    public async Task TracingObserver_WorkflowStartedEvent_DoesNotPatchAlreadyNamedSpan()
    {
        Activity? capturedActivity = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == TracingObserver.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a =>
            {
                if (a.DisplayName == "Workflow:unnamed") capturedActivity = a;
            }
        };
        ActivitySource.AddActivityListener(listener);

        var observer = new TracingObserver();

        await observer.OnEventAsync(new GraphExecutionStartedEvent
        {
            NodeCount = 1,
            GraphContext = new GraphExecutionContext { GraphId = "gx-once", TotalNodes = 1 }
        });

        // First patch
        await observer.OnEventAsync(new WorkflowStartedEvent { WorkflowName = "First", NodeCount = 1 });
        capturedActivity!.DisplayName.Should().Be("Workflow:First");

        // Second patch should not match (DisplayName is no longer "Workflow:unnamed")
        await observer.OnEventAsync(new WorkflowStartedEvent { WorkflowName = "Second", NodeCount = 1 });
        capturedActivity.DisplayName.Should().Be("Workflow:First",
            "already-named span must not be overwritten by a second WorkflowStartedEvent");

        observer.Dispose();
    }

    // ── 6.6  Via coordinator ─────────────────────────────────────────────────

    [Fact]
    public async Task TracingObserver_WorkflowStartedEvent_Via_Coordinator_Does_Not_Throw()
    {
        var observer = new TracingObserver();
        var coordinator = new WorkflowEventCoordinator();
        coordinator.AddObserver(observer);

        var act = async () =>
        {
            await coordinator.DispatchToObserversAsync(new GraphExecutionStartedEvent
            {
                NodeCount = 1,
                GraphContext = new GraphExecutionContext { GraphId = "coord-trace", TotalNodes = 1 }
            });
            await coordinator.DispatchToObserversAsync(new WorkflowStartedEvent
            {
                WorkflowName = "CoordFlow",
                NodeCount = 1
            });
        };

        await act.Should().NotThrowAsync();
        observer.Dispose();
    }

    // ── helper ────────────────────────────────────────────────────────────────

    private static ActivityListener CreateSamplingListener(string sourceName)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    #endregion

    #region SpanInfo Tests

    [Fact]
    public void SpanInfo_Calculates_Duration()
    {
        var start = DateTimeOffset.UtcNow;
        var end = start.AddSeconds(5);

        var span = new SpanInfo
        {
            SpanId = "span-1",
            TraceId = "trace-1",
            OperationName = "test",
            StartTime = start,
            EndTime = end
        };

        span.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public void SpanInfo_Duration_Null_When_Not_Completed()
    {
        var span = new SpanInfo
        {
            SpanId = "span-1",
            TraceId = "trace-1",
            OperationName = "test",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = null
        };

        span.Duration.Should().BeNull();
    }

    [Fact]
    public void SpanStatus_Has_Expected_Values()
    {
        var statuses = Enum.GetValues<SpanStatus>();

        statuses.Should().Contain(SpanStatus.Unset);
        statuses.Should().Contain(SpanStatus.Ok);
        statuses.Should().Contain(SpanStatus.Error);
    }

    #endregion

    #region WorkflowEventCoordinator + Observer Integration

    [Fact]
    public async Task MetricsObserver_Receives_Events_Via_WorkflowEventCoordinator()
    {
        // Arrange
        var observer = new MetricsObserver();
        var coordinator = new WorkflowEventCoordinator();
        coordinator.AddObserver(observer);

        var startEvt = new GraphExecutionStartedEvent
        {
            NodeCount = 2,
            GraphContext = new GraphExecutionContext { GraphId = "coord-test-1", TotalNodes = 2 }
        };

        // Act — dispatch directly through the coordinator
        await coordinator.DispatchToObserversAsync(startEvt);

        // Assert — MetricsObserver registered the workflow
        observer.ActiveWorkflows.Should().HaveCount(1);
        observer.GetActiveWorkflow("coord-test-1").Should().NotBeNull();
    }

    [Fact]
    public async Task TracingObserver_Receives_Events_Via_WorkflowEventCoordinator_Without_Throwing()
    {
        // Arrange
        var observer = new TracingObserver();
        var coordinator = new WorkflowEventCoordinator();
        coordinator.AddObserver(observer);

        var startEvt = new GraphExecutionStartedEvent
        {
            NodeCount = 1,
            GraphContext = new GraphExecutionContext { GraphId = "coord-test-2", TotalNodes = 1 }
        };

        // Act & Assert — must not throw
        var act = async () => await coordinator.DispatchToObserversAsync(startEvt);
        await act.Should().NotThrowAsync();

        observer.Dispose();
    }

    #endregion
}
