using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Builders;

namespace HPD.Graph.Tests.Iteration;

/// <summary>
/// Tests for back-edge detection in cyclic graphs.
/// </summary>
public class BackEdgeDetectionTests
{
    [Fact]
    public void GetBackEdges_AcyclicGraph_ReturnsEmpty()
    {
        // Arrange: Simple DAG (A → B → C)
        var graph = new GraphBuilder()
            .WithName("acyclic-test")
            .AddStartNode()
            .AddNode("A", "NodeA", NodeType.Handler, "TestHandler")
            .AddNode("B", "NodeB", NodeType.Handler, "TestHandler")
            .AddNode("C", "NodeC", NodeType.Handler, "TestHandler")
            .AddEndNode()
            .AddEdge("START", "A")
            .AddEdge("A", "B")
            .AddEdge("B", "C")
            .AddEdge("C", "END")
            .Build();

        // Act
        var backEdges = graph.GetBackEdges();

        // Assert
        Assert.Empty(backEdges);
        Assert.False(graph.HasCycles);
    }

    [Fact]
    public void GetBackEdges_SimpleLoop_DetectsBackEdge()
    {
        // Arrange: A → B → C → B (back-edge from C to B)
        var graph = new GraphBuilder()
            .WithName("simple-loop")
            .AddStartNode()
            .AddNode("A", "NodeA", NodeType.Handler, "TestHandler")
            .AddNode("B", "NodeB", NodeType.Handler, "TestHandler")
            .AddNode("C", "NodeC", NodeType.Handler, "TestHandler")
            .AddEndNode()
            .AddEdge("START", "A")
            .AddEdge("A", "B")
            .AddEdge("B", "C")
            .AddEdge("C", "END")  // Forward to END
            .AddEdge("C", "B")   // Back-edge to B
            .Build();

        // Act
        var backEdges = graph.GetBackEdges();

        // Assert
        Assert.Single(backEdges);
        Assert.True(graph.HasCycles);

        var backEdge = backEdges[0];
        Assert.Equal("C", backEdge.SourceNodeId);
        Assert.Equal("B", backEdge.TargetNodeId);
        Assert.True(backEdge.JumpDistance > 0);
    }

    [Fact]
    public void GetBackEdges_MultipleBackEdges_DetectsAll()
    {
        // Arrange: Graph with two separate cycles
        // A → B → C → B (cycle 1)
        // A → D → E → D (cycle 2)
        var graph = new GraphBuilder()
            .WithName("multiple-cycles")
            .AddStartNode()
            .AddNode("A", "NodeA", NodeType.Handler, "TestHandler")
            .AddNode("B", "NodeB", NodeType.Handler, "TestHandler")
            .AddNode("C", "NodeC", NodeType.Handler, "TestHandler")
            .AddNode("D", "NodeD", NodeType.Handler, "TestHandler")
            .AddNode("E", "NodeE", NodeType.Handler, "TestHandler")
            .AddEndNode()
            .AddEdge("START", "A")
            .AddEdge("A", "B")
            .AddEdge("B", "C")
            .AddEdge("C", "B")   // Back-edge 1
            .AddEdge("C", "END")
            .AddEdge("A", "D")
            .AddEdge("D", "E")
            .AddEdge("E", "D")   // Back-edge 2
            .AddEdge("E", "END")
            .Build();

        // Act
        var backEdges = graph.GetBackEdges();

        // Assert
        Assert.Equal(2, backEdges.Count);
        Assert.True(graph.HasCycles);

        var sourceNodes = backEdges.Select(e => e.SourceNodeId).ToHashSet();
        var targetNodes = backEdges.Select(e => e.TargetNodeId).ToHashSet();

        Assert.Contains("C", sourceNodes);
        Assert.Contains("E", sourceNodes);
        Assert.Contains("B", targetNodes);
        Assert.Contains("D", targetNodes);
    }

    [Fact]
    public void GetBackEdges_LongJump_CalculatesCorrectJumpDistance()
    {
        // Arrange: A → B → C → D → A (long back-edge from D to A)
        var graph = new GraphBuilder()
            .WithName("long-jump")
            .AddStartNode()
            .AddNode("A", "NodeA", NodeType.Handler, "TestHandler")
            .AddNode("B", "NodeB", NodeType.Handler, "TestHandler")
            .AddNode("C", "NodeC", NodeType.Handler, "TestHandler")
            .AddNode("D", "NodeD", NodeType.Handler, "TestHandler")
            .AddEndNode()
            .AddEdge("START", "A")
            .AddEdge("A", "B")
            .AddEdge("B", "C")
            .AddEdge("C", "D")
            .AddEdge("D", "END")
            .AddEdge("D", "A")  // Long back-edge
            .Build();

        // Act
        var backEdges = graph.GetBackEdges();

        // Assert
        Assert.Single(backEdges);
        var backEdge = backEdges[0];
        Assert.Equal("D", backEdge.SourceNodeId);
        Assert.Equal("A", backEdge.TargetNodeId);
        Assert.Equal(3, backEdge.JumpDistance); // D is 3 positions after A
    }

    [Fact]
    public void GetBackEdges_SortedByJumpDistance_LargestFirst()
    {
        // Arrange: Multiple back-edges with different jump distances
        var graph = new GraphBuilder()
            .WithName("sorted-jumps")
            .AddStartNode()
            .AddNode("A", "NodeA", NodeType.Handler, "TestHandler")
            .AddNode("B", "NodeB", NodeType.Handler, "TestHandler")
            .AddNode("C", "NodeC", NodeType.Handler, "TestHandler")
            .AddNode("D", "NodeD", NodeType.Handler, "TestHandler")
            .AddEndNode()
            .AddEdge("START", "A")
            .AddEdge("A", "B")
            .AddEdge("B", "C")
            .AddEdge("C", "D")
            .AddEdge("D", "END")
            .AddEdge("D", "A")   // Jump distance 3 (D→A)
            .AddEdge("C", "B")   // Jump distance 1 (C→B)
            .Build();

        // Act
        var backEdges = graph.GetBackEdges();

        // Assert
        Assert.Equal(2, backEdges.Count);

        // Largest jump should be first
        Assert.Equal("D", backEdges[0].SourceNodeId);
        Assert.Equal("A", backEdges[0].TargetNodeId);
        Assert.Equal(3, backEdges[0].JumpDistance);

        // Smaller jump second
        Assert.Equal("C", backEdges[1].SourceNodeId);
        Assert.Equal("B", backEdges[1].TargetNodeId);
        Assert.Equal(1, backEdges[1].JumpDistance);
    }

    [Fact]
    public void GetBackEdges_WithCondition_PreservesCondition()
    {
        // Arrange: Back-edge with condition
        var graph = new GraphBuilder()
            .WithName("conditional-back-edge")
            .AddStartNode()
            .AddNode("A", "NodeA", NodeType.Handler, "TestHandler")
            .AddNode("B", "NodeB", NodeType.Handler, "TestHandler")
            .AddEndNode()
            .AddEdge("START", "A")
            .AddEdge("A", "B")
            .AddEdge("B", "END", e => e.WithCondition(new EdgeCondition { Type = ConditionType.FieldEquals, Field = "retry", Value = false }))
            .AddEdge("B", "A", e => e.WithCondition(new EdgeCondition { Type = ConditionType.FieldEquals, Field = "retry", Value = true }))  // Conditional back-edge
            .Build();

        // Act
        var backEdges = graph.GetBackEdges();

        // Assert
        Assert.Single(backEdges);
        Assert.Equal("B", backEdges[0].SourceNodeId);
        Assert.Equal("A", backEdges[0].TargetNodeId);
        Assert.NotNull(backEdges[0].Condition);
        Assert.Equal("retry", backEdges[0].Condition!.Field);
    }

    [Fact]
    public void GetBackEdges_MultipleCallsReturnEquivalentResults()
    {
        // Arrange
        var graph = new GraphBuilder()
            .WithName("cached-back-edges")
            .AddStartNode()
            .AddNode("A", "NodeA", NodeType.Handler, "TestHandler")
            .AddNode("B", "NodeB", NodeType.Handler, "TestHandler")
            .AddEndNode()
            .AddEdge("START", "A")
            .AddEdge("A", "B")
            .AddEdge("B", "A")  // Back-edge
            .AddEdge("B", "END")
            .Build();

        // Act
        var backEdges1 = graph.GetBackEdges();
        var backEdges2 = graph.GetBackEdges();

        // Assert - results should be equivalent (not cached, but consistent)
        // Note: Internal caching was removed since orchestrator caches the result
        Assert.Equal(backEdges1.Count, backEdges2.Count);
        Assert.Single(backEdges1);
        Assert.Equal("B", backEdges1[0].SourceNodeId);
        Assert.Equal("A", backEdges1[0].TargetNodeId);
    }

    [Fact]
    public void GetBackEdges_ParallelBranches_DetectsCorrectly()
    {
        // Arrange: Parallel branches with one having a back-edge
        //     → B → C →
        // A →           → END
        //     → D → E → (with E→D back-edge)
        var graph = new GraphBuilder()
            .WithName("parallel-with-cycle")
            .AddStartNode()
            .AddNode("A", "NodeA", NodeType.Handler, "TestHandler")
            .AddNode("B", "NodeB", NodeType.Handler, "TestHandler")
            .AddNode("C", "NodeC", NodeType.Handler, "TestHandler")
            .AddNode("D", "NodeD", NodeType.Handler, "TestHandler")
            .AddNode("E", "NodeE", NodeType.Handler, "TestHandler")
            .AddEndNode()
            .AddEdge("START", "A")
            .AddEdge("A", "B")
            .AddEdge("A", "D")
            .AddEdge("B", "C")
            .AddEdge("D", "E")
            .AddEdge("C", "END")
            .AddEdge("E", "END")
            .AddEdge("E", "D")  // Back-edge in second branch
            .Build();

        // Act
        var backEdges = graph.GetBackEdges();

        // Assert
        Assert.Single(backEdges);
        Assert.Equal("E", backEdges[0].SourceNodeId);
        Assert.Equal("D", backEdges[0].TargetNodeId);
    }

    [Fact]
    public void HasCycles_CorrectlyReflectsBackEdges()
    {
        // Arrange - acyclic
        var acyclicGraph = new GraphBuilder()
            .WithName("acyclic")
            .AddStartNode()
            .AddNode("A", "NodeA", NodeType.Handler, "TestHandler")
            .AddEndNode()
            .AddEdge("START", "A")
            .AddEdge("A", "END")
            .Build();

        // Arrange - cyclic
        var cyclicGraph = new GraphBuilder()
            .WithName("cyclic")
            .AddStartNode()
            .AddNode("A", "NodeA", NodeType.Handler, "TestHandler")
            .AddNode("B", "NodeB", NodeType.Handler, "TestHandler")
            .AddEndNode()
            .AddEdge("START", "A")
            .AddEdge("A", "B")
            .AddEdge("B", "A")  // Back-edge
            .AddEdge("B", "END")
            .Build();

        // Assert
        Assert.False(acyclicGraph.HasCycles);
        Assert.True(cyclicGraph.HasCycles);
    }
}
