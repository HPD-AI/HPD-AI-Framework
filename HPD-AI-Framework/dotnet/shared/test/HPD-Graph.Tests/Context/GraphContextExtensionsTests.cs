using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Core.Context;

namespace HPD.Graph.Tests.Context;

/// <summary>
/// Tests for GraphContextExtensions methods.
/// </summary>
public class GraphContextExtensionsTests
{
    private readonly IServiceProvider _services;

    public GraphContextExtensionsTests()
    {
        _services = TestServiceProvider.Create();
    }

    [Fact]
    public void GetNodeState_NodeWithState_ReturnsCorrectState()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);
        context.AddTag("node_state:start", NodeState.Running.ToString());

        // Act
        var state = context.GetNodeState("start");

        // Assert
        state.Should().Be(NodeState.Running);
    }

    [Fact]
    public void GetNodeState_NodeWithoutState_ReturnsNull()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);

        // Act
        var state = context.GetNodeState("start");

        // Assert
        state.Should().BeNull();
    }

    [Fact]
    public void GetNodeState_InvalidNodeId_ReturnsNull()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);

        // Act
        var state = context.GetNodeState("nonexistent");

        // Assert
        state.Should().BeNull();
    }

    [Fact]
    public void GetNodeState_EmptyNodeId_ReturnsNull()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);

        // Act
        var state = context.GetNodeState("");

        // Assert
        state.Should().BeNull();
    }

    [Fact]
    public void GetNodeState_MultipleStates_ReturnsOneOfTheStates()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);

        // Add multiple states (simulating lack of tag clearing)
        context.AddTag("node_state:start", NodeState.Pending.ToString());
        context.AddTag("node_state:start", NodeState.Running.ToString());
        context.AddTag("node_state:start", NodeState.Succeeded.ToString());

        // Act
        var state = context.GetNodeState("start");

        // Assert
        // ConcurrentBag doesn't guarantee order, so we just verify it returns a valid state
        state.Should().NotBeNull();
        state.Should().BeOneOf(NodeState.Pending, NodeState.Running, NodeState.Succeeded);
    }

    [Fact]
    public void GetNodesInState_MultipleNodesInState_ReturnsAllMatching()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "Handler1")
            .AddHandlerNode("node2", "Handler2")
            .AddHandlerNode("node3", "Handler3")
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);
        context.AddTag("node_state:node1", NodeState.Polling.ToString());
        context.AddTag("node_state:node2", NodeState.Running.ToString());
        context.AddTag("node_state:node3", NodeState.Polling.ToString());

        // Act
        var pollingNodes = context.GetNodesInState(NodeState.Polling);

        // Assert
        pollingNodes.Should().HaveCount(2);
        pollingNodes.Should().Contain("node1");
        pollingNodes.Should().Contain("node3");
    }

    [Fact]
    public void GetNodesInState_NoNodesInState_ReturnsEmpty()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "Handler1")
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);
        context.AddTag("node_state:node1", NodeState.Running.ToString());

        // Act
        var pollingNodes = context.GetNodesInState(NodeState.Polling);

        // Assert
        pollingNodes.Should().BeEmpty();
    }

    [Fact]
    public void HasPollingNodes_WithPollingNodes_ReturnsTrue()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "Handler1")
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);
        context.AddTag("node_state:node1", NodeState.Polling.ToString());

        // Act
        var hasPolling = context.HasPollingNodes();

        // Assert
        hasPolling.Should().BeTrue();
    }

    [Fact]
    public void HasPollingNodes_WithoutPollingNodes_ReturnsFalse()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "Handler1")
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);
        context.AddTag("node_state:node1", NodeState.Running.ToString());

        // Act
        var hasPolling = context.HasPollingNodes();

        // Assert
        hasPolling.Should().BeFalse();
    }

    [Fact]
    public void GetPollingNodes_WithPollingNodes_ReturnsCorrectNodes()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "Handler1")
            .AddHandlerNode("node2", "Handler2")
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);
        context.AddTag("node_state:node1", NodeState.Polling.ToString());
        context.AddTag("node_state:node2", NodeState.Succeeded.ToString());

        // Act
        var pollingNodes = context.GetPollingNodes();

        // Assert
        pollingNodes.Should().HaveCount(1);
        pollingNodes.Should().Contain("node1");
    }

    [Fact]
    public void HasActiveNodes_WithActiveNodes_ReturnsTrue()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "Handler1")
            .AddHandlerNode("node2", "Handler2")
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);
        context.AddTag("node_state:node1", NodeState.Running.ToString());
        context.AddTag("node_state:node2", NodeState.Succeeded.ToString());

        // Act
        var hasActive = context.HasActiveNodes();

        // Assert
        hasActive.Should().BeTrue();
    }

    [Fact]
    public void HasActiveNodes_OnlyTerminalNodes_ReturnsFalse()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "Handler1")
            .AddHandlerNode("node2", "Handler2")
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);
        context.AddTag("node_state:node1", NodeState.Succeeded.ToString());
        context.AddTag("node_state:node2", NodeState.Failed.ToString());

        // Act
        var hasActive = context.HasActiveNodes();

        // Assert
        hasActive.Should().BeFalse();
    }

    [Fact]
    public void GetStateDistribution_MultipleStates_ReturnsCorrectCounts()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "Handler1")
            .AddHandlerNode("node2", "Handler2")
            .AddHandlerNode("node3", "Handler3")
            .AddHandlerNode("node4", "Handler4")
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);
        context.AddTag("node_state:node1", NodeState.Running.ToString());
        context.AddTag("node_state:node2", NodeState.Polling.ToString());
        context.AddTag("node_state:node3", NodeState.Polling.ToString());
        context.AddTag("node_state:node4", NodeState.Succeeded.ToString());

        // Act
        var distribution = context.GetStateDistribution();

        // Assert
        distribution[NodeState.Running].Should().Be(1);
        distribution[NodeState.Polling].Should().Be(2);
        distribution[NodeState.Succeeded].Should().Be(1);
        distribution[NodeState.Pending].Should().Be(0);
        distribution[NodeState.Failed].Should().Be(0);
    }

    [Fact]
    public void GetStateDistribution_NoStates_ReturnsAllZeros()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "Handler1")
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);

        // Act
        var distribution = context.GetStateDistribution();

        // Assert
        foreach (var state in Enum.GetValues<NodeState>())
        {
            distribution[state].Should().Be(0);
        }
    }

    [Fact]
    public void IsNodeWaiting_PollingNode_ReturnsTrue()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "Handler1")
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);
        context.AddTag("node_state:node1", NodeState.Polling.ToString());

        // Act
        var isWaiting = context.IsNodeWaiting("node1");

        // Assert
        isWaiting.Should().BeTrue();
    }

    [Fact]
    public void IsNodeWaiting_SuspendedNode_ReturnsTrue()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "Handler1")
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);
        context.AddTag("node_state:node1", NodeState.Suspended.ToString());

        // Act
        var isWaiting = context.IsNodeWaiting("node1");

        // Assert
        isWaiting.Should().BeTrue();
    }

    [Fact]
    public void IsNodeWaiting_RunningNode_ReturnsFalse()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "Handler1")
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);
        context.AddTag("node_state:node1", NodeState.Running.ToString());

        // Act
        var isWaiting = context.IsNodeWaiting("node1");

        // Assert
        isWaiting.Should().BeFalse();
    }

    [Fact]
    public void IsNodeTerminal_SucceededNode_ReturnsTrue()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "Handler1")
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);
        context.AddTag("node_state:node1", NodeState.Succeeded.ToString());

        // Act
        var isTerminal = context.IsNodeTerminal("node1");

        // Assert
        isTerminal.Should().BeTrue();
    }

    [Fact]
    public void IsNodeTerminal_FailedNode_ReturnsTrue()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "Handler1")
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);
        context.AddTag("node_state:node1", NodeState.Failed.ToString());

        // Act
        var isTerminal = context.IsNodeTerminal("node1");

        // Assert
        isTerminal.Should().BeTrue();
    }

    [Fact]
    public void IsNodeTerminal_RunningNode_ReturnsFalse()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("node1", "Handler1")
            .AddEndNode()
            .Build();
        var context = new GraphContext("test", graph, _services);
        context.AddTag("node_state:node1", NodeState.Running.ToString());

        // Act
        var isTerminal = context.IsNodeTerminal("node1");

        // Assert
        isTerminal.Should().BeFalse();
    }

    [Fact]
    public void ExtensionMethods_NullContext_ThrowsArgumentNullException()
    {
        // Arrange
        IGraphContext? context = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => context!.GetNodeState("test"));
        Assert.Throws<ArgumentNullException>(() => context!.GetNodesInState(NodeState.Running));
        Assert.Throws<ArgumentNullException>(() => context!.HasPollingNodes());
        Assert.Throws<ArgumentNullException>(() => context!.GetPollingNodes());
        Assert.Throws<ArgumentNullException>(() => context!.HasActiveNodes());
        Assert.Throws<ArgumentNullException>(() => context!.GetStateDistribution());
        Assert.Throws<ArgumentNullException>(() => context!.IsNodeWaiting("test"));
        Assert.Throws<ArgumentNullException>(() => context!.IsNodeTerminal("test"));
    }
}
