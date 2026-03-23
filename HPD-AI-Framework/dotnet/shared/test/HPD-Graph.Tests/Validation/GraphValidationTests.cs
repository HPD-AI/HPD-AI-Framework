using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Validation;
using Xunit;

namespace HPD.Graph.Tests.Validation;

/// <summary>
/// Tests for graph validation logic.
/// </summary>
public class GraphValidationTests
{
    #region Basic Structure Validation

    [Fact]
    public void Validate_ValidGraph_ShouldSucceed()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler1")
            .AddEdge("handler1", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_MissingStartNode_ShouldFail()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .WithEntryNode("missing")
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddEndNode()
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "MISSING_START");
    }

    [Fact]
    public void Validate_MissingEndNode_ShouldFail()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .WithExitNode("missing")
            .AddHandlerNode("handler1", "SuccessHandler")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "MISSING_END");
    }

    [Fact]
    public void Validate_InvalidStartNodeType_ShouldFail()
    {
        // Arrange - Create a graph where entry node is not Start type
        var graph = new TestGraphBuilder()
            .AddHandlerNode("start", "SuccessHandler") // Handler instead of Start
            .WithEntryNode("start")
            .AddEndNode()
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "INVALID_START");
    }

    [Fact]
    public void Validate_InvalidEndNodeType_ShouldFail()
    {
        // Arrange - Create a graph where exit node is not End type
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("end", "SuccessHandler") // Handler instead of End
            .WithExitNode("end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "INVALID_END");
    }

    [Fact]
    public void Validate_DuplicateNodeIds_ShouldFail()
    {
        // Arrange - Manually create graph with duplicate IDs
        var nodes = new List<Node>
        {
            new() { Id = "start", Name = "Start", Type = NodeType.Start },
            new() { Id = "handler", Name = "Handler1", Type = NodeType.Handler, HandlerName = "Test" },
            new() { Id = "handler", Name = "Handler2", Type = NodeType.Handler, HandlerName = "Test" }, // Duplicate
            new() { Id = "end", Name = "End", Type = NodeType.End }
        };

        var graph = new HPDAgent.Graph.Abstractions.Graph.Graph
        {
            Id = "test",
            Name = "Test",
            Nodes = nodes,
            Edges = new List<Edge>(),
            EntryNodeId = "start",
            ExitNodeId = "end"
        };

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "DUPLICATE_NODE_ID");
    }

    [Fact]
    public void Validate_InvalidEdgeFrom_ShouldFail()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("nonexistent", "handler1") // Invalid source
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "INVALID_EDGE_FROM");
    }

    [Fact]
    public void Validate_InvalidEdgeTo_ShouldFail()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("handler1", "nonexistent") // Invalid target
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "INVALID_EDGE_TO");
    }

    #endregion

    #region Reachability Validation

    [Fact]
    public void Validate_UnreachableEnd_ShouldFail()
    {
        // Arrange - Start and End are disconnected
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler1")
            // No edge from handler1 to end
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "UNREACHABLE_END");
    }

    [Fact]
    public void Validate_UnreachableNode_ShouldWarn()
    {
        // Arrange - handler2 is not reachable
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddHandlerNode("handler2", "SuccessHandler") // Unreachable
            .AddEndNode()
            .AddEdge("start", "handler1")
            .AddEdge("handler1", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeTrue(); // Warnings don't fail validation
        result.Warnings.Should().ContainSingle(w => w.Code == "UNREACHABLE_NODE" && w.NodeId == "handler2");
    }

    #endregion

    #region Cycle Detection

    [Fact]
    public void Validate_SimpleCycle_ShouldWarn()
    {
        // Arrange - Create a cycle: handler1 -> handler2 -> handler1
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddHandlerNode("handler2", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler1")
            .AddEdge("handler1", "handler2")
            .AddEdge("handler2", "handler1") // Cycle
            .AddEdge("handler1", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeTrue(); // Cycles are warnings, not errors
        result.Warnings.Should().Contain(w => w.Code == "CYCLE_DETECTED");
    }

    [Fact]
    public void Validate_SelfLoop_ShouldWarn()
    {
        // Arrange - Node has edge to itself
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler1")
            .AddEdge("handler1", "handler1") // Self-loop
            .AddEdge("handler1", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Code == "CYCLE_DETECTED");
    }

    #endregion

    #region Orphaned Nodes

    [Fact]
    public void Validate_OrphanedNode_ShouldWarn()
    {
        // Arrange - handler2 has no edges
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddHandlerNode("handler2", "SuccessHandler") // Orphaned
            .AddEndNode()
            .AddEdge("start", "handler1")
            .AddEdge("handler1", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle(w => w.Code == "ORPHANED_NODE" && w.NodeId == "handler2");
    }

    #endregion

    #region Handler Name Validation

    [Fact]
    public void Validate_MissingHandlerName_ShouldWarn()
    {
        // Arrange - Create handler node without handler name
        var nodes = new List<Node>
        {
            new() { Id = "start", Name = "Start", Type = NodeType.Start },
            new() { Id = "handler", Name = "Handler", Type = NodeType.Handler }, // Missing HandlerName
            new() { Id = "end", Name = "End", Type = NodeType.End }
        };

        var graph = new HPDAgent.Graph.Abstractions.Graph.Graph
        {
            Id = "test",
            Name = "Test",
            Nodes = nodes,
            Edges = new List<Edge>
            {
                new() { From = "start", To = "handler" },
                new() { From = "handler", To = "end" }
            },
            EntryNodeId = "start",
            ExitNodeId = "end"
        };

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle(w => w.Code == "MISSING_HANDLER_NAME");
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void Validate_LinearGraph_ShouldSucceed()
    {
        // Arrange - Simple linear flow
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("step1", "Handler1")
            .AddHandlerNode("step2", "Handler2")
            .AddHandlerNode("step3", "Handler3")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "step2")
            .AddEdge("step2", "step3")
            .AddEdge("step3", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ParallelBranches_ShouldSucceed()
    {
        // Arrange - Graph with parallel execution paths
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("branch1", "Handler1")
            .AddHandlerNode("branch2", "Handler2")
            .AddHandlerNode("merge", "Handler3")
            .AddEndNode()
            .AddEdge("start", "branch1")
            .AddEdge("start", "branch2")
            .AddEdge("branch1", "merge")
            .AddEdge("branch2", "merge")
            .AddEdge("merge", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_MultipleErrors_ShouldReportAll()
    {
        // Arrange - Graph with multiple issues
        var nodes = new List<Node>
        {
            new() { Id = "handler", Name = "Handler", Type = NodeType.Handler }, // Wrong start type
            new() { Id = "end", Name = "End", Type = NodeType.End }
        };

        var graph = new HPDAgent.Graph.Abstractions.Graph.Graph
        {
            Id = "test",
            Name = "Test",
            Nodes = nodes,
            Edges = new List<Edge>
            {
                new() { From = "nonexistent", To = "handler" }, // Invalid edge
                new() { From = "handler", To = "missing" } // Invalid edge
            },
            EntryNodeId = "handler", // Not Start type
            ExitNodeId = "end"
        };

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(1);
    }

    #endregion

    #region Default Edge Validation

    [Fact]
    public void Validate_MultipleDefaultEdgesFromSameSource_ShouldFail()
    {
        // Arrange - Two default edges from same node
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddHandlerNode("handler2", "SuccessHandler")
            .AddHandlerNode("handler3", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler1")
            .AddEdge("handler1", "handler2", new EdgeCondition { Type = ConditionType.Default })
            .AddEdge("handler1", "handler3", new EdgeCondition { Type = ConditionType.Default })
            .AddEdge("handler2", "end")
            .AddEdge("handler3", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "MULTIPLE_DEFAULT_EDGES");
        result.Errors.First(e => e.Code == "MULTIPLE_DEFAULT_EDGES").Message
            .Should().Contain("handler1")
            .And.Contain("Only one default edge per source node is allowed");
    }

    [Fact]
    public void Validate_SingleDefaultEdge_ShouldSucceed()
    {
        // Arrange - One default edge from node
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddHandlerNode("handler2", "SuccessHandler")
            .AddHandlerNode("handler3", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler1")
            .AddEdge("handler1", "handler2", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "status",
                Value = "success"
            })
            .AddEdge("handler1", "handler3", new EdgeCondition { Type = ConditionType.Default })
            .AddEdge("handler2", "end")
            .AddEdge("handler3", "end")
            .Build();

        // Act
        var result = GraphValidator.Validate(graph);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion
}
