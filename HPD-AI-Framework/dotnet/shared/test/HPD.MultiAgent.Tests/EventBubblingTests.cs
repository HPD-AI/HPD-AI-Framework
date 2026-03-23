using HPD.Agent;
using HPD.Events;
using HPD.Events.Core;
using HPD.MultiAgent;

namespace HPD.MultiAgent.Tests;

/// <summary>
/// Tests for event bubbling in multi-agent workflows.
/// Ensures that child agent events properly bubble up to parent coordinators.
/// </summary>
public class EventBubblingTests
{
    #region EventCoordinator Parent-Child Tests

    [Fact]
    public void EventCoordinator_SetParent_EstablishesHierarchy()
    {
        // Arrange
        var parentCoordinator = new EventCoordinator();
        var childCoordinator = new EventCoordinator();

        // Act
        childCoordinator.SetParent(parentCoordinator);

        // Assert - no exception means success
        // The parent relationship is internal
    }

    [Fact]
    public async Task EventCoordinator_ChildEvent_BubblesUpToParent()
    {
        // Arrange
        var parentCoordinator = new EventCoordinator();
        var childCoordinator = new EventCoordinator();
        childCoordinator.SetParent(parentCoordinator);

        var receivedEvents = new List<Event>();
        var readTask = Task.Run(async () =>
        {
            await foreach (var evt in parentCoordinator.ReadAllAsync(CancellationToken.None))
            {
                receivedEvents.Add(evt);
                if (receivedEvents.Count >= 1) break;
            }
        });

        // Act - emit from child
        var testEvent = new TextDeltaEvent("Hello from child", "msg_123");
        childCoordinator.Emit(testEvent);

        // Wait for event to propagate
        await Task.WhenAny(readTask, Task.Delay(1000));

        // Assert
        receivedEvents.Should().ContainSingle();
        receivedEvents[0].Should().Be(testEvent);
    }

    [Fact]
    public async Task EventCoordinator_MultipleChildEvents_AllBubbleUp()
    {
        // Arrange
        var parentCoordinator = new EventCoordinator();
        var childCoordinator = new EventCoordinator();
        childCoordinator.SetParent(parentCoordinator);

        var receivedEvents = new List<Event>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var readTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in parentCoordinator.ReadAllAsync(cts.Token))
                {
                    receivedEvents.Add(evt);
                    if (receivedEvents.Count >= 3) break;
                }
            }
            catch (OperationCanceledException) { }
        });

        // Act - emit multiple events from child
        childCoordinator.Emit(new TextDeltaEvent("Event 1", "msg_1"));
        childCoordinator.Emit(new TextDeltaEvent("Event 2", "msg_2"));
        childCoordinator.Emit(new TextDeltaEvent("Event 3", "msg_3"));

        await Task.WhenAny(readTask, Task.Delay(1500));
        cts.Cancel();

        // Assert
        receivedEvents.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    #endregion

    #region ExecutionContext Propagation Tests

    [Fact]
    public void AgentExecutionContext_Creation_SetsAllProperties()
    {
        // Arrange & Act
        var context = new AgentExecutionContext
        {
            AgentName = "TestAgent",
            AgentId = "test-agent-123",
            ParentAgentId = "parent-456",
            AgentChain = new List<string> { "Root", "TestAgent" },
            Depth = 1
        };

        // Assert
        context.AgentName.Should().Be("TestAgent");
        context.AgentId.Should().Be("test-agent-123");
        context.ParentAgentId.Should().Be("parent-456");
        context.AgentChain.Should().HaveCount(2);
        context.Depth.Should().Be(1);
        context.IsSubAgent.Should().BeTrue();
    }

    [Fact]
    public void AgentExecutionContext_RootAgent_IsSubAgentIsFalse()
    {
        // Arrange & Act
        var context = new AgentExecutionContext
        {
            AgentName = "RootAgent",
            AgentId = "root-123",
            ParentAgentId = null,
            AgentChain = new List<string> { "RootAgent" },
            Depth = 0
        };

        // Assert
        context.IsSubAgent.Should().BeFalse();
    }

    [Fact]
    public void AgentEvent_WithExecutionContext_PreservesContext()
    {
        // Arrange
        var context = new AgentExecutionContext
        {
            AgentName = "SubAgent",
            AgentId = "sub-123",
            ParentAgentId = "parent-456",
            AgentChain = new List<string> { "Parent", "SubAgent" },
            Depth = 1
        };

        // Act
        var evt = new TextDeltaEvent("Test", "msg_123") { ExecutionContext = context };

        // Assert
        evt.ExecutionContext.Should().Be(context);
        evt.ExecutionContext!.AgentName.Should().Be("SubAgent");
        evt.ExecutionContext.IsSubAgent.Should().BeTrue();
    }

    #endregion

    #region Workflow Event Context Tests

    [Fact]
    public void WorkflowStartedEvent_HasExecutionContext()
    {
        // Arrange
        var context = new AgentExecutionContext
        {
            AgentName = "MathWorkflow",
            AgentId = "workflow-123",
            AgentChain = new List<string> { "MathWorkflow" },
            Depth = 0
        };

        // Act
        var evt = new WorkflowStartedEvent
        {
            WorkflowName = "MathWorkflow",
            NodeCount = 3,
            ExecutionContext = context
        };

        // Assert
        evt.ExecutionContext.Should().Be(context);
        evt.WorkflowName.Should().Be("MathWorkflow");
    }

    [Fact]
    public void WorkflowNodeCompletedEvent_HasExecutionContext()
    {
        // Arrange
        var context = new AgentExecutionContext
        {
            AgentName = "MathWorkflow",
            AgentId = "workflow-123",
            AgentChain = new List<string> { "MathWorkflow" },
            Depth = 0
        };

        // Act
        var evt = new WorkflowNodeCompletedEvent
        {
            WorkflowName = "MathWorkflow",
            NodeId = "solver",
            Success = true,
            Duration = TimeSpan.FromSeconds(1.5),
            ExecutionContext = context
        };

        // Assert
        evt.ExecutionContext.Should().Be(context);
        evt.NodeId.Should().Be("solver");
    }

    #endregion

    #region Nested Workflow Context Tests

    [Fact]
    public void NestedWorkflow_ExecutionContext_HasCorrectDepth()
    {
        // Arrange - Root workflow
        var rootContext = new AgentExecutionContext
        {
            AgentName = "RootWorkflow",
            AgentId = "root-123",
            ParentAgentId = null,
            AgentChain = new List<string> { "RootWorkflow" },
            Depth = 0
        };

        // Arrange - Nested workflow (child of root)
        var nestedContext = new AgentExecutionContext
        {
            AgentName = "NestedWorkflow",
            AgentId = "root-123-nested-456",
            ParentAgentId = "root-123",
            AgentChain = new List<string> { "RootWorkflow", "NestedWorkflow" },
            Depth = 1
        };

        // Arrange - Agent inside nested workflow
        var agentContext = new AgentExecutionContext
        {
            AgentName = "SolverAgent",
            AgentId = "root-123-nested-456-solver-789",
            ParentAgentId = "root-123-nested-456",
            AgentChain = new List<string> { "RootWorkflow", "NestedWorkflow", "SolverAgent" },
            Depth = 2
        };

        // Assert
        rootContext.IsSubAgent.Should().BeFalse();
        rootContext.Depth.Should().Be(0);

        nestedContext.IsSubAgent.Should().BeTrue();
        nestedContext.Depth.Should().Be(1);
        nestedContext.ParentAgentId.Should().Be("root-123");

        agentContext.IsSubAgent.Should().BeTrue();
        agentContext.Depth.Should().Be(2);
        agentContext.AgentChain.Should().HaveCount(3);
    }

    #endregion

    #region Event Type Hierarchy Tests

    [Fact]
    public void WorkflowEvents_AreAgentEvents()
    {
        // All workflow events should be AgentEvents for unified handling
        var workflowStarted = new WorkflowStartedEvent { WorkflowName = "Test", NodeCount = 1 };
        var workflowCompleted = new WorkflowCompletedEvent { WorkflowName = "Test", Duration = TimeSpan.Zero };
        var nodeStarted = new WorkflowNodeStartedEvent { WorkflowName = "Test", NodeId = "n1" };
        var nodeCompleted = new WorkflowNodeCompletedEvent { WorkflowName = "Test", NodeId = "n1", Success = true, Duration = TimeSpan.Zero };
        var nodeSkipped = new WorkflowNodeSkippedEvent { WorkflowName = "Test", NodeId = "n1", Reason = "skipped" };
        var edgeTraversed = new WorkflowEdgeTraversedEvent { WorkflowName = "Test", FromNodeId = "a", ToNodeId = "b" };
        var layerStarted = new WorkflowLayerStartedEvent { WorkflowName = "Test", LayerIndex = 0, NodeCount = 1 };
        var layerCompleted = new WorkflowLayerCompletedEvent { WorkflowName = "Test", LayerIndex = 0, Duration = TimeSpan.Zero };
        var diagnostic = new WorkflowDiagnosticEvent { WorkflowName = "Test", Level = LogLevel.Information, Source = "s", Message = "m" };

        // Assert all are AgentEvents
        workflowStarted.Should().BeAssignableTo<AgentEvent>();
        workflowCompleted.Should().BeAssignableTo<AgentEvent>();
        nodeStarted.Should().BeAssignableTo<AgentEvent>();
        nodeCompleted.Should().BeAssignableTo<AgentEvent>();
        nodeSkipped.Should().BeAssignableTo<AgentEvent>();
        edgeTraversed.Should().BeAssignableTo<AgentEvent>();
        layerStarted.Should().BeAssignableTo<AgentEvent>();
        layerCompleted.Should().BeAssignableTo<AgentEvent>();
        diagnostic.Should().BeAssignableTo<AgentEvent>();
    }

    #endregion
}
