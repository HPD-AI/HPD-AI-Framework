using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Builders;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;

namespace HPD.Graph.Tests.Integration;

/// <summary>
/// Integration tests for upstream condition patterns using GraphBuilder fluent API.
/// Tests the convenience methods RequireOneSuccess, RequireAllDone, RequirePartialSuccess.
/// </summary>
public class UpstreamConditionIntegrationTests
{
    private readonly IServiceProvider _services;

    public UpstreamConditionIntegrationTests()
    {
        _services = TestServiceProvider.Create();
    }

    // ========================================
    // GraphBuilder Fluent API Tests
    // ========================================

    [Fact]
    public void RequireOneSuccess_SetsCorrectConditionOnAllIncomingEdges()
    {
        // Arrange & Act
        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("upstream1", "Upstream 1", NodeType.Handler, "SuccessHandler")
            .AddNode("upstream2", "Upstream 2", NodeType.Handler, "FailureHandler")
            .AddNode("target", "Target", NodeType.Handler, "SuccessHandler")
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "target")
            .AddEdge("upstream2", "target")
            .RequireOneSuccess("target") // Apply condition
            .AddEdge("target", "end")
            .Build();

        // Assert
        var targetEdges = graph.Edges.Where(e => e.To == "target").ToList();
        targetEdges.Should().HaveCount(2, "target has two incoming edges");

        foreach (var edge in targetEdges)
        {
            edge.Condition.Should().NotBeNull();
            edge.Condition!.Type.Should().Be(ConditionType.UpstreamOneSuccess,
                $"edge {edge.From} → {edge.To} should have UpstreamOneSuccess condition");
        }
    }

    [Fact]
    public void RequireAllDone_SetsCorrectConditionOnAllIncomingEdges()
    {
        // Arrange & Act
        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("scraper1", "Scraper 1", NodeType.Handler, "SuccessHandler")
            .AddNode("scraper2", "Scraper 2", NodeType.Handler, "FailureHandler")
            .AddNode("aggregate", "Aggregate", NodeType.Handler, "SuccessHandler")
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "scraper1")
            .AddEdge("start", "scraper2")
            .AddEdge("scraper1", "aggregate")
            .AddEdge("scraper2", "aggregate")
            .RequireAllDone("aggregate")
            .AddEdge("aggregate", "end")
            .Build();

        // Assert
        var aggregateEdges = graph.Edges.Where(e => e.To == "aggregate").ToList();
        aggregateEdges.Should().HaveCount(2);

        foreach (var edge in aggregateEdges)
        {
            edge.Condition.Should().NotBeNull();
            edge.Condition!.Type.Should().Be(ConditionType.UpstreamAllDone);
        }
    }

    [Fact]
    public void RequirePartialSuccess_SetsCorrectConditionOnAllIncomingEdges()
    {
        // Arrange & Act
        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("validator1", "Validator 1", NodeType.Handler, "SuccessHandler")
            .AddNode("validator2", "Validator 2", NodeType.Handler, "FailureHandler")
            .AddNode("finalize", "Finalize", NodeType.Handler, "SuccessHandler")
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "validator1")
            .AddEdge("start", "validator2")
            .AddEdge("validator1", "finalize")
            .AddEdge("validator2", "finalize")
            .RequirePartialSuccess("finalize")
            .AddEdge("finalize", "end")
            .Build();

        // Assert
        var finalizeEdges = graph.Edges.Where(e => e.To == "finalize").ToList();
        finalizeEdges.Should().HaveCount(2);

        foreach (var edge in finalizeEdges)
        {
            edge.Condition.Should().NotBeNull();
            edge.Condition!.Type.Should().Be(ConditionType.UpstreamAllDoneOneSuccess);
        }
    }

    [Fact]
    public void WithUpstreamCondition_NoIncomingEdges_ThrowsException()
    {
        // Arrange & Act
        var act = () => new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("orphan", "Orphan", NodeType.Handler, "SuccessHandler")
            .AddNode("end", "End", NodeType.End)
            .WithUpstreamCondition("orphan", ConditionType.UpstreamOneSuccess) // No incoming edges!
            .Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*has no incoming edges*");
    }

    [Fact]
    public void WithUpstreamCondition_EdgeAlreadyHasCondition_ThrowsException()
    {
        // Arrange & Act
        var act = () => new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("upstream", "Upstream", NodeType.Handler, "SuccessHandler")
            .AddNode("target", "Target", NodeType.Handler, "SuccessHandler")
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "upstream")
            .AddEdge("upstream", "target", edge => edge.WithCondition(new EdgeCondition { Type = ConditionType.FieldExists, Field = "test" })) // Already has condition
            .WithUpstreamCondition("target", ConditionType.UpstreamOneSuccess) // Try to add upstream condition
            .Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already have conditions*");
    }

    [Fact]
    public void WithUpstreamCondition_InvalidConditionType_ThrowsException()
    {
        // Arrange & Act
        var act = () => new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("upstream", "Upstream", NodeType.Handler, "SuccessHandler")
            .AddNode("target", "Target", NodeType.Handler, "SuccessHandler")
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "upstream")
            .AddEdge("upstream", "target")
            .WithUpstreamCondition("target", ConditionType.FieldEquals) // Not an upstream condition!
            .Build();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*must be upstream condition*");
    }

    // ========================================
    // EdgeCondition Description Tests
    // ========================================

    [Fact]
    public void EdgeCondition_UpstreamOneSuccess_HasCorrectDescription()
    {
        // Arrange
        var condition = new EdgeCondition { Type = ConditionType.UpstreamOneSuccess };

        // Act
        var description = condition.GetDescription();

        // Assert
        description.Should().Be("At least one upstream succeeded");
    }

    [Fact]
    public void EdgeCondition_UpstreamAllDone_HasCorrectDescription()
    {
        // Arrange
        var condition = new EdgeCondition { Type = ConditionType.UpstreamAllDone };

        // Act
        var description = condition.GetDescription();

        // Assert
        description.Should().Be("All upstreams completed");
    }
}
