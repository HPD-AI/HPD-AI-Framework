using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;

namespace HPD.Graph.Tests.Orchestration;

/// <summary>
/// Tests for ConditionEvaluator upstream condition evaluation.
/// </summary>
public class ConditionEvaluatorTests
{
    private readonly IServiceProvider _services;

    public ConditionEvaluatorTests()
    {
        _services = TestServiceProvider.Create();
    }

    // ========================================
    // UpstreamOneSuccess Tests
    // ========================================

    [Fact]
    public void EvaluateUpstreamOneSuccess_OneSucceeded_ReturnsTrue()
    {
        // Arrange - Graph with two upstreams
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("upstream1", "Upstream1")
            .AddHandlerNode("upstream2", "Upstream2")
            .AddHandlerNode("target", "Target")
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "target")
            .AddEdge("upstream2", "target")
            .Build();

        var context = new GraphContext("test", graph, _services);

        // Mark upstream1 as succeeded
        context.MarkNodeComplete("upstream1");
        context.Channels["node_result:upstream1"].Set(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["result"] = "data1" },
            duration: TimeSpan.Zero,
            metadata: new NodeExecutionMetadata()
        ));

        // Mark upstream2 as failed
        context.MarkNodeComplete("upstream2");
        context.Channels["node_result:upstream2"].Set(new NodeExecutionResult.Failure(
            Exception: new InvalidOperationException("Failed"),
            Severity: ErrorSeverity.Fatal,
            IsTransient: false,
            Duration: TimeSpan.Zero
        ));

        var condition = new EdgeCondition { Type = ConditionType.UpstreamOneSuccess };
        var edge = graph.Edges.First(e => e.To == "target");

        // Act
        var result = ConditionEvaluator.Evaluate(condition, null, context, edge);

        // Assert
        result.Should().BeTrue("at least one upstream (upstream1) succeeded");
    }

    [Fact]
    public void EvaluateUpstreamOneSuccess_AllFailed_ReturnsFalse()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("upstream1", "Upstream1")
            .AddHandlerNode("upstream2", "Upstream2")
            .AddHandlerNode("target", "Target")
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "target")
            .AddEdge("upstream2", "target")
            .Build();

        var context = new GraphContext("test", graph, _services);

        // Mark both upstreams as failed
        context.MarkNodeComplete("upstream1");
        context.Channels["node_result:upstream1"].Set(new NodeExecutionResult.Failure(
            Exception: new InvalidOperationException("Failed 1"),
            Severity: ErrorSeverity.Fatal,
            IsTransient: false,
            Duration: TimeSpan.Zero
        ));

        context.MarkNodeComplete("upstream2");
        context.Channels["node_result:upstream2"].Set(new NodeExecutionResult.Failure(
            Exception: new InvalidOperationException("Failed 2"),
            Severity: ErrorSeverity.Fatal,
            IsTransient: false,
            Duration: TimeSpan.Zero
        ));

        var condition = new EdgeCondition { Type = ConditionType.UpstreamOneSuccess };
        var edge = graph.Edges.First(e => e.To == "target");

        // Act
        var result = ConditionEvaluator.Evaluate(condition, null, context, edge);

        // Assert
        result.Should().BeFalse("all upstreams failed");
    }

    [Fact]
    public void EvaluateUpstreamOneSuccess_StillWaiting_ReturnsFalse()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("upstream1", "Upstream1")
            .AddHandlerNode("upstream2", "Upstream2")
            .AddHandlerNode("target", "Target")
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "target")
            .AddEdge("upstream2", "target")
            .Build();

        var context = new GraphContext("test", graph, _services);

        // Mark upstream1 as complete (failed)
        context.MarkNodeComplete("upstream1");
        context.Channels["node_result:upstream1"].Set(new NodeExecutionResult.Failure(
            Exception: new InvalidOperationException("Failed"),
            Severity: ErrorSeverity.Fatal,
            IsTransient: false,
            Duration: TimeSpan.Zero
        ));

        // upstream2 still running (not marked complete)

        var condition = new EdgeCondition { Type = ConditionType.UpstreamOneSuccess };
        var edge = graph.Edges.First(e => e.To == "target");

        // Act
        var result = ConditionEvaluator.Evaluate(condition, null, context, edge);

        // Assert
        result.Should().BeFalse("still waiting for upstream2 to complete");
    }

    [Fact]
    public void EvaluateUpstreamOneSuccess_AllSucceeded_ReturnsTrue()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("upstream1", "Upstream1")
            .AddHandlerNode("upstream2", "Upstream2")
            .AddHandlerNode("target", "Target")
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "target")
            .AddEdge("upstream2", "target")
            .Build();

        var context = new GraphContext("test", graph, _services);

        // Mark both upstreams as succeeded
        context.MarkNodeComplete("upstream1");
        context.Channels["node_result:upstream1"].Set(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["result"] = "data1" },
            duration: TimeSpan.Zero,
            metadata: new NodeExecutionMetadata()
        ));

        context.MarkNodeComplete("upstream2");
        context.Channels["node_result:upstream2"].Set(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["result"] = "data2" },
            duration: TimeSpan.Zero,
            metadata: new NodeExecutionMetadata()
        ));

        var condition = new EdgeCondition { Type = ConditionType.UpstreamOneSuccess };
        var edge = graph.Edges.First(e => e.To == "target");

        // Act
        var result = ConditionEvaluator.Evaluate(condition, null, context, edge);

        // Assert
        result.Should().BeTrue("both upstreams succeeded");
    }

    // ========================================
    // UpstreamAllDone Tests
    // ========================================

    [Fact]
    public void EvaluateUpstreamAllDone_AllCompleted_ReturnsTrue()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("upstream1", "Upstream1")
            .AddHandlerNode("upstream2", "Upstream2")
            .AddHandlerNode("target", "Target")
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "target")
            .AddEdge("upstream2", "target")
            .Build();

        var context = new GraphContext("test", graph, _services);

        // Mark upstream1 as succeeded
        context.MarkNodeComplete("upstream1");
        context.Channels["node_result:upstream1"].Set(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["result"] = "data1" },
            duration: TimeSpan.Zero,
            metadata: new NodeExecutionMetadata()
        ));

        // Mark upstream2 as failed
        context.MarkNodeComplete("upstream2");
        context.Channels["node_result:upstream2"].Set(new NodeExecutionResult.Failure(
            Exception: new InvalidOperationException("Failed"),
            Severity: ErrorSeverity.Fatal,
            IsTransient: false,
            Duration: TimeSpan.Zero
        ));

        var condition = new EdgeCondition { Type = ConditionType.UpstreamAllDone };
        var edge = graph.Edges.First(e => e.To == "target");

        // Act
        var result = ConditionEvaluator.Evaluate(condition, null, context, edge);

        // Assert
        result.Should().BeTrue("all upstreams completed (one success, one failure)");
    }

    [Fact]
    public void EvaluateUpstreamAllDone_OneStillRunning_ReturnsFalse()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("upstream1", "Upstream1")
            .AddHandlerNode("upstream2", "Upstream2")
            .AddHandlerNode("target", "Target")
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "target")
            .AddEdge("upstream2", "target")
            .Build();

        var context = new GraphContext("test", graph, _services);

        // Mark upstream1 as completed
        context.MarkNodeComplete("upstream1");
        context.Channels["node_result:upstream1"].Set(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["result"] = "data1" },
            duration: TimeSpan.Zero,
            metadata: new NodeExecutionMetadata()
        ));

        // upstream2 still running (not marked complete)

        var condition = new EdgeCondition { Type = ConditionType.UpstreamAllDone };
        var edge = graph.Edges.First(e => e.To == "target");

        // Act
        var result = ConditionEvaluator.Evaluate(condition, null, context, edge);

        // Assert
        result.Should().BeFalse("upstream2 is still running");
    }

    [Fact]
    public void EvaluateUpstreamAllDone_AllFailed_ReturnsTrue()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("upstream1", "Upstream1")
            .AddHandlerNode("upstream2", "Upstream2")
            .AddHandlerNode("target", "Target")
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "target")
            .AddEdge("upstream2", "target")
            .Build();

        var context = new GraphContext("test", graph, _services);

        // Mark both upstreams as failed
        context.MarkNodeComplete("upstream1");
        context.Channels["node_result:upstream1"].Set(new NodeExecutionResult.Failure(
            Exception: new InvalidOperationException("Failed 1"),
            Severity: ErrorSeverity.Fatal,
            IsTransient: false,
            Duration: TimeSpan.Zero
        ));

        context.MarkNodeComplete("upstream2");
        context.Channels["node_result:upstream2"].Set(new NodeExecutionResult.Failure(
            Exception: new InvalidOperationException("Failed 2"),
            Severity: ErrorSeverity.Fatal,
            IsTransient: false,
            Duration: TimeSpan.Zero
        ));

        var condition = new EdgeCondition { Type = ConditionType.UpstreamAllDone };
        var edge = graph.Edges.First(e => e.To == "target");

        // Act
        var result = ConditionEvaluator.Evaluate(condition, null, context, edge);

        // Assert
        result.Should().BeTrue("all upstreams completed (both failed)");
    }

    // ========================================
    // UpstreamAllDoneOneSuccess Tests
    // ========================================

    [Fact]
    public void EvaluateUpstreamAllDoneOneSuccess_AllDoneOneSucceeded_ReturnsTrue()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("upstream1", "Upstream1")
            .AddHandlerNode("upstream2", "Upstream2")
            .AddHandlerNode("target", "Target")
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "target")
            .AddEdge("upstream2", "target")
            .Build();

        var context = new GraphContext("test", graph, _services);

        // Mark upstream1 as succeeded
        context.MarkNodeComplete("upstream1");
        context.Channels["node_result:upstream1"].Set(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["result"] = "data1" },
            duration: TimeSpan.Zero,
            metadata: new NodeExecutionMetadata()
        ));

        // Mark upstream2 as failed
        context.MarkNodeComplete("upstream2");
        context.Channels["node_result:upstream2"].Set(new NodeExecutionResult.Failure(
            Exception: new InvalidOperationException("Failed"),
            Severity: ErrorSeverity.Fatal,
            IsTransient: false,
            Duration: TimeSpan.Zero
        ));

        var condition = new EdgeCondition { Type = ConditionType.UpstreamAllDoneOneSuccess };
        var edge = graph.Edges.First(e => e.To == "target");

        // Act
        var result = ConditionEvaluator.Evaluate(condition, null, context, edge);

        // Assert
        result.Should().BeTrue("all upstreams completed and one succeeded");
    }

    [Fact]
    public void EvaluateUpstreamAllDoneOneSuccess_AllDoneAllFailed_ReturnsFalse()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("upstream1", "Upstream1")
            .AddHandlerNode("upstream2", "Upstream2")
            .AddHandlerNode("target", "Target")
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "target")
            .AddEdge("upstream2", "target")
            .Build();

        var context = new GraphContext("test", graph, _services);

        // Mark both upstreams as failed
        context.MarkNodeComplete("upstream1");
        context.Channels["node_result:upstream1"].Set(new NodeExecutionResult.Failure(
            Exception: new InvalidOperationException("Failed 1"),
            Severity: ErrorSeverity.Fatal,
            IsTransient: false,
            Duration: TimeSpan.Zero
        ));

        context.MarkNodeComplete("upstream2");
        context.Channels["node_result:upstream2"].Set(new NodeExecutionResult.Failure(
            Exception: new InvalidOperationException("Failed 2"),
            Severity: ErrorSeverity.Fatal,
            IsTransient: false,
            Duration: TimeSpan.Zero
        ));

        var condition = new EdgeCondition { Type = ConditionType.UpstreamAllDoneOneSuccess };
        var edge = graph.Edges.First(e => e.To == "target");

        // Act
        var result = ConditionEvaluator.Evaluate(condition, null, context, edge);

        // Assert
        result.Should().BeFalse("all upstreams completed but none succeeded");
    }

    [Fact]
    public void EvaluateUpstreamAllDoneOneSuccess_NotAllDone_ReturnsFalse()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("upstream1", "Upstream1")
            .AddHandlerNode("upstream2", "Upstream2")
            .AddHandlerNode("target", "Target")
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "target")
            .AddEdge("upstream2", "target")
            .Build();

        var context = new GraphContext("test", graph, _services);

        // Mark upstream1 as succeeded
        context.MarkNodeComplete("upstream1");
        context.Channels["node_result:upstream1"].Set(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["result"] = "data1" },
            duration: TimeSpan.Zero,
            metadata: new NodeExecutionMetadata()
        ));

        // upstream2 still running (not marked complete)

        var condition = new EdgeCondition { Type = ConditionType.UpstreamAllDoneOneSuccess };
        var edge = graph.Edges.First(e => e.To == "target");

        // Act
        var result = ConditionEvaluator.Evaluate(condition, null, context, edge);

        // Assert
        result.Should().BeFalse("upstream2 is still running");
    }

    [Fact]
    public void EvaluateUpstreamAllDoneOneSuccess_AllSucceeded_ReturnsTrue()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("upstream1", "Upstream1")
            .AddHandlerNode("upstream2", "Upstream2")
            .AddHandlerNode("target", "Target")
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "target")
            .AddEdge("upstream2", "target")
            .Build();

        var context = new GraphContext("test", graph, _services);

        // Mark both upstreams as succeeded
        context.MarkNodeComplete("upstream1");
        context.Channels["node_result:upstream1"].Set(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["result"] = "data1" },
            duration: TimeSpan.Zero,
            metadata: new NodeExecutionMetadata()
        ));

        context.MarkNodeComplete("upstream2");
        context.Channels["node_result:upstream2"].Set(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["result"] = "data2" },
            duration: TimeSpan.Zero,
            metadata: new NodeExecutionMetadata()
        ));

        var condition = new EdgeCondition { Type = ConditionType.UpstreamAllDoneOneSuccess };
        var edge = graph.Edges.First(e => e.To == "target");

        // Act
        var result = ConditionEvaluator.Evaluate(condition, null, context, edge);

        // Assert
        result.Should().BeTrue("all upstreams completed and all succeeded");
    }

    // ========================================
    // Legacy Evaluate Method Tests
    // ========================================

    [Fact]
    public void Evaluate_LegacyMethod_WithUpstreamCondition_ThrowsException()
    {
        // Arrange - Graph with upstream condition
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("upstream", "Upstream")
            .AddHandlerNode("target", "Target")
            .AddEdge("start", "upstream")
            .AddEdge("upstream", "target")
            .Build();

        var condition = new EdgeCondition { Type = ConditionType.UpstreamOneSuccess };
        var nodeOutputs = new Dictionary<string, object> { ["test"] = "value" };

        // Act & Assert
        var act = () => ConditionEvaluator.Evaluate(condition, nodeOutputs);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Upstream conditions require context*");
    }
}
