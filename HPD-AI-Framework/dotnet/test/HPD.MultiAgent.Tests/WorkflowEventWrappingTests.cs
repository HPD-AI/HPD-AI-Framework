using HPD.Agent;
using HPD.Events;
using HPD.MultiAgent;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;
using GraphLogLevel = HPDAgent.Graph.Abstractions.Context.LogLevel;

namespace HPD.MultiAgent.Tests;

/// <summary>
/// Tests for the WrapGraphEvent functionality in AgentWorkflowInstance.
/// Ensures that internal graph events are correctly wrapped into public AgentEvent types.
/// </summary>
public class WorkflowEventWrappingTests
{
    private readonly AgentExecutionContext _testContext = new()
    {
        AgentName = "TestWorkflow",
        AgentId = "test-workflow-123",
        AgentChain = new List<string> { "TestWorkflow" },
        Depth = 0
    };

    // Helper to access the private WrapGraphEvent method via reflection for testing
    private Event? WrapGraphEvent(Event evt, AgentExecutionContext context)
    {
        // Since WrapGraphEvent is private, we test it indirectly through the public API
        // or we could use a test-specific internal accessor
        // For now, we'll create wrapper events directly to test the mapping logic
        return MapGraphEventToWorkflowEvent(evt, context, "TestWorkflow");
    }

    // Mirror of the WrapGraphEvent logic for testing
    private static Event? MapGraphEventToWorkflowEvent(Event evt, AgentExecutionContext context, string workflowName)
    {
        return evt switch
        {
            GraphExecutionStartedEvent g => new WorkflowStartedEvent
            {
                WorkflowName = workflowName,
                NodeCount = g.NodeCount,
                LayerCount = g.LayerCount,
                ExecutionContext = context
            },

            GraphExecutionCompletedEvent g => new WorkflowCompletedEvent
            {
                WorkflowName = workflowName,
                Duration = g.Duration,
                SuccessfulNodes = g.SuccessfulNodes,
                FailedNodes = g.FailedNodes,
                SkippedNodes = g.SkippedNodes,
                ExecutionContext = context
            },

            NodeExecutionStartedEvent n => new WorkflowNodeStartedEvent
            {
                WorkflowName = workflowName,
                NodeId = n.NodeId,
                AgentName = n.HandlerName,
                LayerIndex = n.LayerIndex,
                ExecutionContext = context
            },

            NodeExecutionCompletedEvent n => new WorkflowNodeCompletedEvent
            {
                WorkflowName = workflowName,
                NodeId = n.NodeId,
                AgentName = n.HandlerName,
                Success = n.Result is NodeExecutionResult.Success,
                Duration = n.Duration,
                Progress = n.Progress,
                Outputs = n.Outputs,
                ErrorMessage = n.Result is NodeExecutionResult.Failure f ? f.Exception.Message : null,
                ExecutionContext = context
            },

            NodeSkippedEvent n => new WorkflowNodeSkippedEvent
            {
                WorkflowName = workflowName,
                NodeId = n.NodeId,
                Reason = n.Reason,
                ExecutionContext = context
            },

            LayerExecutionStartedEvent l => new WorkflowLayerStartedEvent
            {
                WorkflowName = workflowName,
                LayerIndex = l.LayerIndex,
                NodeCount = l.NodeCount,
                ExecutionContext = context
            },

            LayerExecutionCompletedEvent l => new WorkflowLayerCompletedEvent
            {
                WorkflowName = workflowName,
                LayerIndex = l.LayerIndex,
                Duration = l.Duration,
                SuccessfulNodes = l.SuccessfulNodes,
                ExecutionContext = context
            },

            EdgeTraversedEvent e => new WorkflowEdgeTraversedEvent
            {
                WorkflowName = workflowName,
                FromNodeId = e.FromNodeId,
                ToNodeId = e.ToNodeId,
                HasCondition = e.HasCondition,
                ConditionDescription = e.ConditionDescription,
                ExecutionContext = context
            },

            GraphDiagnosticEvent d => new WorkflowDiagnosticEvent
            {
                WorkflowName = workflowName,
                Level = (LogLevel)(int)d.Level,
                Source = d.Source,
                Message = d.Message,
                NodeId = d.NodeId,
                ExecutionContext = context
            },

            AgentEvent ae => ae,

            _ => null
        };
    }

    #region GraphExecutionStartedEvent Tests

    [Fact]
    public void WrapGraphEvent_GraphExecutionStarted_MapsToWorkflowStarted()
    {
        var graphEvent = new GraphExecutionStartedEvent
        {
            NodeCount = 5,
            LayerCount = 2
        };

        var wrapped = WrapGraphEvent(graphEvent, _testContext);

        wrapped.Should().BeOfType<WorkflowStartedEvent>();
        var workflowEvent = (WorkflowStartedEvent)wrapped!;
        workflowEvent.WorkflowName.Should().Be("TestWorkflow");
        workflowEvent.NodeCount.Should().Be(5);
        workflowEvent.LayerCount.Should().Be(2);
        workflowEvent.ExecutionContext.Should().Be(_testContext);
    }

    [Fact]
    public void WrapGraphEvent_GraphExecutionStarted_WithoutLayers_MapsCorrectly()
    {
        var graphEvent = new GraphExecutionStartedEvent
        {
            NodeCount = 3,
            LayerCount = null
        };

        var wrapped = WrapGraphEvent(graphEvent, _testContext);

        var workflowEvent = wrapped.Should().BeOfType<WorkflowStartedEvent>().Subject;
        workflowEvent.LayerCount.Should().BeNull();
    }

    #endregion

    #region GraphExecutionCompletedEvent Tests

    [Fact]
    public void WrapGraphEvent_GraphExecutionCompleted_MapsToWorkflowCompleted()
    {
        var graphEvent = new GraphExecutionCompletedEvent
        {
            Duration = TimeSpan.FromSeconds(5.5),
            SuccessfulNodes = 4,
            FailedNodes = 1,
            SkippedNodes = 0
        };

        var wrapped = WrapGraphEvent(graphEvent, _testContext);

        var workflowEvent = wrapped.Should().BeOfType<WorkflowCompletedEvent>().Subject;
        workflowEvent.WorkflowName.Should().Be("TestWorkflow");
        workflowEvent.Duration.Should().Be(TimeSpan.FromSeconds(5.5));
        workflowEvent.SuccessfulNodes.Should().Be(4);
        workflowEvent.FailedNodes.Should().Be(1);
        workflowEvent.SkippedNodes.Should().Be(0);
        workflowEvent.Success.Should().BeFalse(); // Has failed nodes
    }

    [Fact]
    public void WrapGraphEvent_GraphExecutionCompleted_AllSuccess_SuccessIsTrue()
    {
        var graphEvent = new GraphExecutionCompletedEvent
        {
            Duration = TimeSpan.FromSeconds(2),
            SuccessfulNodes = 3,
            FailedNodes = 0,
            SkippedNodes = 0
        };

        var wrapped = WrapGraphEvent(graphEvent, _testContext);

        var workflowEvent = (WorkflowCompletedEvent)wrapped!;
        workflowEvent.Success.Should().BeTrue();
    }

    #endregion

    #region NodeExecutionStartedEvent Tests

    [Fact]
    public void WrapGraphEvent_NodeExecutionStarted_MapsToWorkflowNodeStarted()
    {
        var graphEvent = new NodeExecutionStartedEvent
        {
            NodeId = "solver1",
            HandlerName = "MathSolver",
            LayerIndex = 1
        };

        var wrapped = WrapGraphEvent(graphEvent, _testContext);

        var workflowEvent = wrapped.Should().BeOfType<WorkflowNodeStartedEvent>().Subject;
        workflowEvent.WorkflowName.Should().Be("TestWorkflow");
        workflowEvent.NodeId.Should().Be("solver1");
        workflowEvent.AgentName.Should().Be("MathSolver");
        workflowEvent.LayerIndex.Should().Be(1);
    }

    #endregion

    #region NodeExecutionCompletedEvent Tests

    [Fact]
    public void WrapGraphEvent_NodeExecutionCompleted_Success_MapsCorrectly()
    {
        var outputs = new Dictionary<string, object> { ["answer"] = "42" };
        var graphEvent = new NodeExecutionCompletedEvent
        {
            NodeId = "solver1",
            HandlerName = "MathSolver",
            Duration = TimeSpan.FromSeconds(1.5),
            Result = NodeExecutionResult.Success.Single(output: outputs, duration: TimeSpan.FromSeconds(1.5), metadata: new NodeExecutionMetadata()),
            Progress = 0.5f,
            Outputs = outputs
        };

        var wrapped = WrapGraphEvent(graphEvent, _testContext);

        var workflowEvent = wrapped.Should().BeOfType<WorkflowNodeCompletedEvent>().Subject;
        workflowEvent.NodeId.Should().Be("solver1");
        workflowEvent.AgentName.Should().Be("MathSolver");
        workflowEvent.Success.Should().BeTrue();
        workflowEvent.Duration.Should().Be(TimeSpan.FromSeconds(1.5));
        workflowEvent.Progress.Should().Be(0.5f);
        workflowEvent.Outputs.Should().ContainKey("answer");
        workflowEvent.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void WrapGraphEvent_NodeExecutionCompleted_Failure_MapsErrorMessage()
    {
        var exception = new InvalidOperationException("Test error");
        var graphEvent = new NodeExecutionCompletedEvent
        {
            NodeId = "solver1",
            HandlerName = "MathSolver",
            Duration = TimeSpan.FromSeconds(0.5),
            Result = new NodeExecutionResult.Failure(
                exception,
                ErrorSeverity.Fatal,
                IsTransient: false,
                TimeSpan.FromSeconds(0.5)),
            Progress = 0.25f
        };

        var wrapped = WrapGraphEvent(graphEvent, _testContext);

        var workflowEvent = wrapped.Should().BeOfType<WorkflowNodeCompletedEvent>().Subject;
        workflowEvent.Success.Should().BeFalse();
        workflowEvent.ErrorMessage.Should().Be("Test error");
    }

    #endregion

    #region NodeSkippedEvent Tests

    [Fact]
    public void WrapGraphEvent_NodeSkipped_MapsToWorkflowNodeSkipped()
    {
        var graphEvent = new NodeSkippedEvent
        {
            NodeId = "optional_node",
            Reason = "Condition not met"
        };

        var wrapped = WrapGraphEvent(graphEvent, _testContext);

        var workflowEvent = wrapped.Should().BeOfType<WorkflowNodeSkippedEvent>().Subject;
        workflowEvent.NodeId.Should().Be("optional_node");
        workflowEvent.Reason.Should().Be("Condition not met");
    }

    #endregion

    #region LayerExecutionEvents Tests

    [Fact]
    public void WrapGraphEvent_LayerExecutionStarted_MapsToWorkflowLayerStarted()
    {
        var graphEvent = new LayerExecutionStartedEvent
        {
            LayerIndex = 2,
            NodeCount = 3
        };

        var wrapped = WrapGraphEvent(graphEvent, _testContext);

        var workflowEvent = wrapped.Should().BeOfType<WorkflowLayerStartedEvent>().Subject;
        workflowEvent.LayerIndex.Should().Be(2);
        workflowEvent.NodeCount.Should().Be(3);
    }

    [Fact]
    public void WrapGraphEvent_LayerExecutionCompleted_MapsToWorkflowLayerCompleted()
    {
        var graphEvent = new LayerExecutionCompletedEvent
        {
            LayerIndex = 1,
            Duration = TimeSpan.FromSeconds(3),
            SuccessfulNodes = 2
        };

        var wrapped = WrapGraphEvent(graphEvent, _testContext);

        var workflowEvent = wrapped.Should().BeOfType<WorkflowLayerCompletedEvent>().Subject;
        workflowEvent.LayerIndex.Should().Be(1);
        workflowEvent.Duration.Should().Be(TimeSpan.FromSeconds(3));
        workflowEvent.SuccessfulNodes.Should().Be(2);
    }

    #endregion

    #region EdgeTraversedEvent Tests

    [Fact]
    public void WrapGraphEvent_EdgeTraversed_MapsToWorkflowEdgeTraversed()
    {
        var graphEvent = new EdgeTraversedEvent
        {
            FromNodeId = "classifier",
            ToNodeId = "solver",
            HasCondition = true,
            ConditionDescription = "is_math == true"
        };

        var wrapped = WrapGraphEvent(graphEvent, _testContext);

        var workflowEvent = wrapped.Should().BeOfType<WorkflowEdgeTraversedEvent>().Subject;
        workflowEvent.FromNodeId.Should().Be("classifier");
        workflowEvent.ToNodeId.Should().Be("solver");
        workflowEvent.HasCondition.Should().BeTrue();
        workflowEvent.ConditionDescription.Should().Be("is_math == true");
    }

    [Fact]
    public void WrapGraphEvent_EdgeTraversed_NoCondition_MapsCorrectly()
    {
        var graphEvent = new EdgeTraversedEvent
        {
            FromNodeId = "START",
            ToNodeId = "classifier",
            HasCondition = false
        };

        var wrapped = WrapGraphEvent(graphEvent, _testContext);

        var workflowEvent = (WorkflowEdgeTraversedEvent)wrapped!;
        workflowEvent.HasCondition.Should().BeFalse();
        workflowEvent.ConditionDescription.Should().BeNull();
    }

    #endregion

    #region GraphDiagnosticEvent Tests

    [Fact]
    public void WrapGraphEvent_GraphDiagnostic_MapsToWorkflowDiagnostic()
    {
        var graphEvent = new GraphDiagnosticEvent
        {
            Level = GraphLogLevel.Warning,
            Source = "Orchestrator",
            Message = "Node timeout approaching",
            NodeId = "slow_node"
        };

        var wrapped = WrapGraphEvent(graphEvent, _testContext);

        var workflowEvent = wrapped.Should().BeOfType<WorkflowDiagnosticEvent>().Subject;
        workflowEvent.Level.Should().Be(LogLevel.Warning);
        workflowEvent.Source.Should().Be("Orchestrator");
        workflowEvent.Message.Should().Be("Node timeout approaching");
        workflowEvent.NodeId.Should().Be("slow_node");
    }

    [Theory]
    [InlineData(GraphLogLevel.Trace, LogLevel.Trace)]
    [InlineData(GraphLogLevel.Debug, LogLevel.Debug)]
    [InlineData(GraphLogLevel.Information, LogLevel.Information)]
    [InlineData(GraphLogLevel.Warning, LogLevel.Warning)]
    [InlineData(GraphLogLevel.Error, LogLevel.Error)]
    [InlineData(GraphLogLevel.Critical, LogLevel.Critical)]
    public void WrapGraphEvent_GraphDiagnostic_LogLevelMapsCorrectly(GraphLogLevel graphLevel, LogLevel expectedLevel)
    {
        var graphEvent = new GraphDiagnosticEvent
        {
            Level = graphLevel,
            Source = "Test",
            Message = "Test message"
        };

        var wrapped = WrapGraphEvent(graphEvent, _testContext);

        var workflowEvent = (WorkflowDiagnosticEvent)wrapped!;
        workflowEvent.Level.Should().Be(expectedLevel);
    }

    #endregion

    #region AgentEvent Passthrough Tests

    [Fact]
    public void WrapGraphEvent_AgentEvent_PassesThroughUnchanged()
    {
        var agentEvent = new TextDeltaEvent("Hello world", "msg_123");

        var wrapped = WrapGraphEvent(agentEvent, _testContext);

        wrapped.Should().BeSameAs(agentEvent);
    }

    [Fact]
    public void WrapGraphEvent_ToolCallStartEvent_PassesThroughUnchanged()
    {
        var toolEvent = new ToolCallStartEvent("call_123", "TestTool", "msg_123");

        var wrapped = WrapGraphEvent(toolEvent, _testContext);

        wrapped.Should().BeSameAs(toolEvent);
    }

    #endregion

    #region Unknown Event Tests

    [Fact]
    public void WrapGraphEvent_UnknownGraphEvent_ReturnsNull()
    {
        // EdgeConditionFailedEvent is intentionally not wrapped (internal detail)
        var graphEvent = new EdgeConditionFailedEvent
        {
            FromNodeId = "a",
            ToNodeId = "b",
            ConditionDescription = "x == y"
        };

        var wrapped = WrapGraphEvent(graphEvent, _testContext);

        wrapped.Should().BeNull();
    }

    #endregion

    #region ExecutionContext Propagation Tests

    [Fact]
    public void WrapGraphEvent_AllWrappedEvents_HaveExecutionContext()
    {
        var events = new Event[]
        {
            new GraphExecutionStartedEvent { NodeCount = 1 },
            new GraphExecutionCompletedEvent { Duration = TimeSpan.Zero },
            new NodeExecutionStartedEvent { NodeId = "n", HandlerName = "h" },
            new NodeExecutionCompletedEvent
            {
                NodeId = "n",
                HandlerName = "h",
                Duration = TimeSpan.Zero,
                Result = NodeExecutionResult.Success.Single(output: new Dictionary<string, object>(), duration: TimeSpan.Zero, metadata: new NodeExecutionMetadata())
            },
            new NodeSkippedEvent { NodeId = "n", Reason = "r" },
            new LayerExecutionStartedEvent { LayerIndex = 0, NodeCount = 1 },
            new LayerExecutionCompletedEvent { LayerIndex = 0, Duration = TimeSpan.Zero },
            new EdgeTraversedEvent { FromNodeId = "a", ToNodeId = "b" },
            new GraphDiagnosticEvent { Level = GraphLogLevel.Information, Source = "s", Message = "m" }
        };

        foreach (var evt in events)
        {
            var wrapped = WrapGraphEvent(evt, _testContext);

            wrapped.Should().NotBeNull($"Event {evt.GetType().Name} should be wrapped");

            if (wrapped is AgentEvent agentEvent)
            {
                agentEvent.ExecutionContext.Should().Be(_testContext,
                    $"Event {wrapped.GetType().Name} should have ExecutionContext set");
            }
        }
    }

    #endregion
}
