using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Validation;
using Xunit;

namespace HPD.Graph.Tests.Primitives;

/// <summary>
/// Comprehensive tests for port-based routing and metadata tracking (Week 1):
/// - NodeExecutionMetadata extensions (ExecutionId, CorrelationId, ParentExecutionIds, StartedAt)
/// - Port-based multi-output routing (PortOutputs, FromPort, ToPort, Priority)
/// - GraphValidator port validation (7 rules)
/// - Success.Single() and Success.WithPorts() factories
/// </summary>
public class PortRoutingAndMetadataTests
{
    // ===== PART 1: NodeExecutionMetadata Tests (8 tests) =====

    [Fact]
    public void NodeExecutionMetadata_ShouldHaveExecutionId_WhenCreated()
    {
        // Arrange & Act
        var metadata = new NodeExecutionMetadata();

        // Assert
        metadata.ExecutionId.Should().NotBeNullOrEmpty();
        Guid.TryParse(metadata.ExecutionId, out _).Should().BeTrue("ExecutionId should be a valid GUID");
    }

    [Fact]
    public void NodeExecutionMetadata_ShouldHaveUniqueExecutionIds_WhenMultipleInstancesCreated()
    {
        // Arrange & Act
        var metadata1 = new NodeExecutionMetadata();
        var metadata2 = new NodeExecutionMetadata();
        var metadata3 = new NodeExecutionMetadata();

        // Assert
        var ids = new[] { metadata1.ExecutionId, metadata2.ExecutionId, metadata3.ExecutionId };
        ids.Should().OnlyHaveUniqueItems("each execution should have a unique ID");
    }

    [Fact]
    public void NodeExecutionMetadata_ShouldAcceptCorrelationId_WhenProvided()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");

        // Act
        var metadata = new NodeExecutionMetadata { CorrelationId = correlationId };

        // Assert
        metadata.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public void NodeExecutionMetadata_ShouldAcceptParentExecutionIds_WhenProvided()
    {
        // Arrange
        var parentIds = new List<string> { "parent1", "parent2", "parent3" };

        // Act
        var metadata = new NodeExecutionMetadata { ParentExecutionIds = parentIds };

        // Assert
        metadata.ParentExecutionIds.Should().BeEquivalentTo(parentIds);
        metadata.ParentExecutionIds.Should().HaveCount(3);
    }

    [Fact]
    public void NodeExecutionMetadata_ShouldAcceptStartedAt_WhenProvided()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;

        // Act
        var metadata = new NodeExecutionMetadata { StartedAt = startTime };

        // Assert
        metadata.StartedAt.Should().Be(startTime);
    }

    [Fact]
    public void NodeExecutionMetadata_ShouldAcceptCustomMetrics_WhenProvided()
    {
        // Arrange
        var customMetrics = new Dictionary<string, object>
        {
            ["v5:poll_attempts"] = 5,
            ["nodered:routing_decision"] = 0,
            ["tokens_used"] = 1234
        };

        // Act
        var metadata = new NodeExecutionMetadata { CustomMetrics = customMetrics };

        // Assert
        metadata.CustomMetrics.Should().BeEquivalentTo(customMetrics);
        metadata.CustomMetrics!["v5:poll_attempts"].Should().Be(5);
        metadata.CustomMetrics!["nodered:routing_decision"].Should().Be(0);
    }

    [Fact]
    public void NodeExecutionMetadata_ShouldSupportNamespacedKeys_InCustomMetrics()
    {
        // Arrange & Act
        var metadata = new NodeExecutionMetadata
        {
            CustomMetrics = new Dictionary<string, object>
            {
                ["v5:poll_attempts"] = 3,
                ["v5:retry_count"] = 2,
                ["nodered:correlation_id"] = "abc123",
                ["nodered:clone_count"] = 4,
                ["user_id"] = "user123"
            }
        };

        // Assert - V5 namespace
        metadata.CustomMetrics!["v5:poll_attempts"].Should().Be(3);
        metadata.CustomMetrics!["v5:retry_count"].Should().Be(2);

        // Assert - Node-RED namespace
        metadata.CustomMetrics!["nodered:correlation_id"].Should().Be("abc123");
        metadata.CustomMetrics!["nodered:clone_count"].Should().Be(4);

        // Assert - Application-specific (no namespace)
        metadata.CustomMetrics!["user_id"].Should().Be("user123");
    }

    [Fact]
    public void NodeExecutionMetadata_ShouldSupportAttemptNumberAndExecutionReason_FromExistingFields()
    {
        // Arrange & Act
        var metadata = new NodeExecutionMetadata
        {
            AttemptNumber = 3,
            ExecutionReason = "Retry after transient failure"
        };

        // Assert
        metadata.AttemptNumber.Should().Be(3);
        metadata.ExecutionReason.Should().Be("Retry after transient failure");
    }

    // ===== PART 2: Success Factory Methods Tests (4 tests) =====

    [Fact]
    public void Success_Single_ShouldCreateSuccessWithPort0Output()
    {
        // Arrange
        var output = new Dictionary<string, object> { ["result"] = "test" };
        var duration = TimeSpan.FromMilliseconds(100);
        var metadata = new NodeExecutionMetadata();

        // Act
        var result = NodeExecutionResult.Success.Single(output, duration, metadata);

        // Assert
        result.Should().BeOfType<NodeExecutionResult.Success>();
        result.PortOutputs.Should().ContainKey(0);
        result.PortOutputs[0].Should().BeEquivalentTo(output);
        result.Duration.Should().Be(duration);
        result.Metadata.Should().Be(metadata);
    }

    [Fact]
    public void Success_WithPorts_ShouldCreateSuccessWithMultiplePortOutputs()
    {
        // Arrange
        var ports = new PortOutputs()
            .Add(0, new Dictionary<string, object> { ["high"] = "value1" })
            .Add(1, new Dictionary<string, object> { ["low"] = "value2" });
        var duration = TimeSpan.FromMilliseconds(50);
        var metadata = new NodeExecutionMetadata();

        // Act
        var result = NodeExecutionResult.Success.WithPorts(ports, duration, metadata);

        // Assert
        result.Should().BeOfType<NodeExecutionResult.Success>();
        result.PortOutputs.Should().HaveCount(2);
        result.PortOutputs[0]["high"].Should().Be("value1");
        result.PortOutputs[1]["low"].Should().Be("value2");
    }

    [Fact]
    public void PortOutputs_Add_ShouldSupportAnonymousObjects()
    {
        // Arrange
        var ports = new PortOutputs();

        // Act
        ports.Add(0, new Dictionary<string, object> { ["value"] = "test", ["count"] = 42 });

        // Use Success.WithPorts to validate the ports work correctly
        var result = NodeExecutionResult.Success.WithPorts(
            ports,
            TimeSpan.Zero,
            new NodeExecutionMetadata());

        // Assert
        result.PortOutputs[0].Should().ContainKey("value");
        result.PortOutputs[0].Should().ContainKey("count");
        result.PortOutputs[0]["value"].Should().Be("test");
        result.PortOutputs[0]["count"].Should().Be(42);
    }

    [Fact]
    public void PortOutputs_Add_ShouldThrowException_WhenPortNumberIsNegative()
    {
        // Arrange
        var ports = new PortOutputs();
        var output = new Dictionary<string, object>();

        // Act
        var act = () => ports.Add(-1, output);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Port number must be non-negative*");
    }

    // ===== PART 3: Port Validation Tests (9 tests covering 7 rules) =====

    [Fact]
    public void GraphValidator_Rule1_ShouldFailValidation_WhenOutputPortCountIsZero()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddNode("node1", "Test", NodeType.Handler, "Test", outputPortCount: 0)
            .AddEndNode()
            .AddEdge("start", "node1")
            .AddEdge("node1", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "INVALID_PORT_COUNT");
        result.Errors.First(e => e.Code == "INVALID_PORT_COUNT").Message.Should().Contain("Must be at least 1");
    }

    [Fact]
    public void GraphValidator_Rule2_ShouldWarn_WhenOutputPortCountIsVeryHigh()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddNode("node1", "Test", NodeType.Handler, "Test", outputPortCount: 150)
            .AddEndNode()
            .AddEdge("start", "node1")
            .AddEdge("node1", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Code == "HIGH_PORT_COUNT");
        result.Warnings.First(w => w.Code == "HIGH_PORT_COUNT").Message.Should().Contain("unusually high");
    }

    [Fact]
    public void GraphValidator_Rule3_ShouldFailValidation_WhenFromPortIsNegative()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddNode("node1", "Node1", NodeType.Handler, "Test", outputPortCount: 2)
            .AddNode("node2", "Node2", NodeType.Handler, "Test")
            .AddEndNode()
            .AddEdge("start", "node1")
            .AddEdge("node1", "node2", fromPort: -1)
            .AddEdge("node2", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "NEGATIVE_FROM_PORT");
        result.Errors.First(e => e.Code == "NEGATIVE_FROM_PORT").Message.Should().Contain("negative FromPort");
    }

    [Fact]
    public void GraphValidator_Rule3_ShouldFailValidation_WhenFromPortExceedsOutputPortCount()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddNode("node1", "Node1", NodeType.Handler, "Test", outputPortCount: 2)
            .AddNode("node2", "Node2", NodeType.Handler, "Test")
            .AddEndNode()
            .AddEdge("start", "node1")
            .AddEdge("node1", "node2", fromPort: 2) // Valid ports are 0, 1
            .AddEdge("node2", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "INVALID_FROM_PORT");
        result.Errors.First(e => e.Code == "INVALID_FROM_PORT").Message.Should().Contain("only has 2 output port(s) (0-1)");
    }

    [Fact]
    public void GraphValidator_Rule4_ShouldWarn_WhenMultiOutputNodeHasUnusedPorts()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddNode("node1", "Node1", NodeType.Handler, "Test", outputPortCount: 3)
            .AddNode("node2", "Node2", NodeType.Handler, "Test")
            .AddEndNode()
            .AddEdge("start", "node1")
            .AddEdge("node1", "node2", fromPort: 0) // Only port 0 used, ports 1 and 2 unused
            .AddEdge("node2", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Code == "UNUSED_OUTPUT_PORTS");
        result.Warnings.First(w => w.Code == "UNUSED_OUTPUT_PORTS").Message.Should().Contain("port(s) [1, 2]");
        result.Warnings.First(w => w.Code == "UNUSED_OUTPUT_PORTS").Message.Should().Contain("will be dropped");
    }

    [Fact]
    public void GraphValidator_Rule5_ShouldFailValidation_WhenToPortIsNegative()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddNode("node1", "Node1", NodeType.Handler, "Test")
            .AddNode("node2", "Node2", NodeType.Handler, "Test")
            .AddEndNode()
            .AddEdge("start", "node1")
            .AddEdge("node1", "node2", toPort: -1)
            .AddEdge("node2", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "NEGATIVE_TO_PORT");
        result.Errors.First(e => e.Code == "NEGATIVE_TO_PORT").Message.Should().Contain("negative ToPort");
    }

    [Fact]
    public void GraphValidator_Rule6_ShouldWarn_WhenSingleOutputNodeHasExplicitPort0()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddNode("node1", "Node1", NodeType.Handler, "Test", outputPortCount: 1)
            .AddNode("node2", "Node2", NodeType.Handler, "Test")
            .AddEndNode()
            .AddEdge("start", "node1")
            .AddEdge("node1", "node2", fromPort: 0) // Redundant - single output defaults to port 0
            .AddEdge("node2", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Code == "REDUNDANT_PORT_0");
        result.Warnings.First(w => w.Code == "REDUNDANT_PORT_0").Message.Should().Contain("redundant");
    }

    [Fact]
    public void GraphValidator_Rule7_ShouldFailValidation_WhenPriorityIsNegative()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddNode("node1", "Node1", NodeType.Handler, "Test")
            .AddNode("node2", "Node2", NodeType.Handler, "Test")
            .AddEndNode()
            .AddEdge("start", "node1")
            .AddEdge("node1", "node2", priority: -1)
            .AddEdge("node2", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "NEGATIVE_PRIORITY");
        result.Errors.First(e => e.Code == "NEGATIVE_PRIORITY").Message.Should().Contain("Priority must be non-negative");
    }

    [Fact]
    public void GraphValidator_ShouldPassValidation_WhenAllPortsAreValid()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddNode("node1", "Node1", NodeType.Handler, "Test", outputPortCount: 2)
            .AddNode("node2", "Node2", NodeType.Handler, "Test")
            .AddNode("node3", "Node3", NodeType.Handler, "Test")
            .AddEndNode()
            .AddEdge("start", "node1")
            .AddEdge("node1", "node2", fromPort: 0, priority: 1)
            .AddEdge("node1", "node3", fromPort: 1, priority: 2)
            .AddEdge("node2", "end")
            .AddEdge("node3", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // ===== PART 4: Edge Properties Tests (4 tests) =====

    [Fact]
    public void Edge_ShouldDefaultFromPort_ToNull()
    {
        // Arrange & Act
        var edge = new Edge { From = "node1", To = "node2" };

        // Assert
        edge.FromPort.Should().BeNull("FromPort should default to null (meaning port 0)");
    }

    [Fact]
    public void Edge_ShouldAcceptFromPort_WhenProvided()
    {
        // Arrange & Act
        var edge = new Edge { From = "node1", To = "node2", FromPort = 1 };

        // Assert
        edge.FromPort.Should().Be(1);
    }

    [Fact]
    public void Edge_ShouldAcceptToPort_WhenProvided()
    {
        // Arrange & Act
        var edge = new Edge { From = "node1", To = "node2", ToPort = 0 };

        // Assert
        edge.ToPort.Should().Be(0);
    }

    [Fact]
    public void Edge_ShouldAcceptPriority_WhenProvided()
    {
        // Arrange & Act
        var edge = new Edge { From = "node1", To = "node2", Priority = 5 };

        // Assert
        edge.Priority.Should().Be(5);
    }

    // ===== PART 5: Node Properties Tests (3 tests) =====

    [Fact]
    public void Node_ShouldDefaultOutputPortCount_ToOne()
    {
        // Arrange & Act
        var node = new Node
        {
            Id = "node1",
            Name = "Test",
            Type = NodeType.Handler,
            HandlerName = "Test"
        };

        // Assert
        node.OutputPortCount.Should().Be(1, "single-output nodes default to 1 port (port 0)");
    }

    [Fact]
    public void Node_ShouldAcceptCustomOutputPortCount_WhenProvided()
    {
        // Arrange & Act
        var node = new Node
        {
            Id = "node1",
            Name = "Test",
            Type = NodeType.Handler,
            HandlerName = "Test",
            OutputPortCount = 3
        };

        // Assert
        node.OutputPortCount.Should().Be(3);
    }

    [Fact]
    public void Node_ShouldSupportMultipleOutputPorts_ForRouting()
    {
        // Arrange & Act
        var routerNode = new Node
        {
            Id = "router",
            Name = "Value Router",
            Type = NodeType.Handler,
            HandlerName = "ValueRouter",
            OutputPortCount = 2 // High path (port 0) and low path (port 1)
        };

        // Assert
        routerNode.OutputPortCount.Should().Be(2);
    }

    // ===== PART 6: Integration Tests - Metadata + Port Routing (7 tests) =====

    [Fact]
    public void Integration_Metadata_ShouldFlowThroughSuccessResult()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString("N");
        var parentIds = new List<string> { "parent1", "parent2" };
        var startTime = DateTimeOffset.UtcNow;

        var metadata = new NodeExecutionMetadata
        {
            CorrelationId = correlationId,
            ParentExecutionIds = parentIds,
            StartedAt = startTime,
            AttemptNumber = 2,
            ExecutionReason = "Retry",
            CustomMetrics = new Dictionary<string, object>
            {
                ["v5:poll_attempts"] = 3,
                ["nodered:routing_decision"] = 0
            }
        };

        // Act
        var result = NodeExecutionResult.Success.Single(
            new Dictionary<string, object> { ["data"] = "test" },
            TimeSpan.FromMilliseconds(100),
            metadata);

        // Assert
        result.Metadata.CorrelationId.Should().Be(correlationId);
        result.Metadata.ParentExecutionIds.Should().BeEquivalentTo(parentIds);
        result.Metadata.StartedAt.Should().Be(startTime);
        result.Metadata.AttemptNumber.Should().Be(2);
        result.Metadata.ExecutionReason.Should().Be("Retry");
        result.Metadata.CustomMetrics!["v5:poll_attempts"].Should().Be(3);
        result.Metadata.CustomMetrics!["nodered:routing_decision"].Should().Be(0);
    }

    [Fact]
    public void Integration_MultiPort_WithMetadata_ShouldWork()
    {
        // Arrange
        var ports = new PortOutputs()
            .Add(0, new Dictionary<string, object> { ["highValue"] = 100 })
            .Add(1, new Dictionary<string, object> { ["lowValue"] = 10 });

        var metadata = new NodeExecutionMetadata
        {
            CorrelationId = "test-correlation",
            CustomMetrics = new Dictionary<string, object>
            {
                ["nodered:ports_used"] = 2,
                ["nodered:routing_strategy"] = "value-based"
            }
        };

        // Act
        var result = NodeExecutionResult.Success.WithPorts(ports, TimeSpan.FromMilliseconds(50), metadata);

        // Assert
        result.PortOutputs.Should().HaveCount(2);
        result.PortOutputs[0]["highValue"].Should().Be(100);
        result.PortOutputs[1]["lowValue"].Should().Be(10);
        result.Metadata.CorrelationId.Should().Be("test-correlation");
        result.Metadata.CustomMetrics!["nodered:ports_used"].Should().Be(2);
    }

    [Fact]
    public void Integration_GraphWithMultiPortNode_ShouldValidate()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddNode("router", "Value Router", NodeType.Handler, "ValueRouter", outputPortCount: 2)
            .AddHandlerNode("high_path", "SuccessHandler")
            .AddHandlerNode("low_path", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "router")
            .AddEdge("router", "high_path", fromPort: 0)
            .AddEdge("router", "low_path", fromPort: 1)
            .AddEdge("high_path", "end")
            .AddEdge("low_path", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Integration_GraphWithPriority_ShouldValidate()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddNode("router", "Router", NodeType.Handler, "Router", outputPortCount: 2)
            .AddHandlerNode("path1", "SuccessHandler")
            .AddHandlerNode("path2", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "router")
            .AddEdge("router", "path1", fromPort: 0, priority: 1)
            .AddEdge("router", "path2", fromPort: 1, priority: 2)
            .AddEdge("path1", "end")
            .AddEdge("path2", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Integration_MultipleMetadataInstances_ShouldHaveUniqueExecutionIds()
    {
        // Arrange & Act
        var results = Enumerable.Range(0, 10)
            .Select(_ => NodeExecutionResult.Success.Single(
                new Dictionary<string, object> { ["data"] = "test" },
                TimeSpan.Zero,
                new NodeExecutionMetadata()))
            .ToList();

        // Assert
        var executionIds = results.Select(r => r.Metadata.ExecutionId).ToList();
        executionIds.Should().OnlyHaveUniqueItems("each execution should have a unique ID");
        executionIds.Should().HaveCount(10);
    }

    [Fact]
    public void Integration_PortOutputs_WithComplexObjects()
    {
        // Arrange
        var ports = new PortOutputs()
            .Add(0, new Dictionary<string, object>
            {
                ["user"] = new Dictionary<string, object> { ["id"] = 123, ["name"] = "John" },
                ["items"] = new[] { "item1", "item2", "item3" },
                ["metadata"] = new Dictionary<string, object> { ["processed"] = true }
            });

        // Act
        var result = NodeExecutionResult.Success.WithPorts(ports, TimeSpan.Zero, new NodeExecutionMetadata());

        // Assert
        result.PortOutputs[0].Should().ContainKey("user");
        result.PortOutputs[0].Should().ContainKey("items");
        result.PortOutputs[0].Should().ContainKey("metadata");
    }

    [Fact]
    public void Integration_Metadata_WithEmptyParentExecutionIds_ShouldWork()
    {
        // Arrange
        var metadata = new NodeExecutionMetadata
        {
            ParentExecutionIds = new List<string>() // Empty list
        };

        // Act
        var result = NodeExecutionResult.Success.Single(
            new Dictionary<string, object> { ["data"] = "test" },
            TimeSpan.Zero,
            metadata);

        // Assert
        result.Metadata.ParentExecutionIds.Should().BeEmpty();
    }
}
