using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Builders;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;
using Xunit.Abstractions;

namespace HPD.Graph.Tests.Integration;

/// <summary>
/// Debug tests to understand SubGraph upstream condition behavior.
/// </summary>
public class SubGraphUpstreamDebugTests
{
    private readonly IServiceProvider _services;
    private readonly ITestOutputHelper _output;

    public SubGraphUpstreamDebugTests(ITestOutputHelper output)
    {
        _services = TestServiceProvider.Create();
        _output = output;
    }

    [Fact]
    public async Task Debug_SimpleHandlerWithUpstreamCondition_ShouldExecute()
    {
        // Arrange - Test with regular handler first (not SubGraph)
        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("upstream1", "Upstream 1", NodeType.Handler, "SuccessHandler")
            .AddNode("upstream2", "Upstream 2", NodeType.Handler, "FailureHandler",
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddNode("target", "Target", NodeType.Handler, "SuccessHandler")
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "target")
            .AddEdge("upstream2", "target")
            .RequireOneSuccess("target")
            .AddEdge("target", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, _services);
        var orchestrator = new GraphOrchestrator<GraphContext>(_services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        _output.WriteLine($"upstream1 complete: {context.IsNodeComplete("upstream1")}");
        _output.WriteLine($"upstream2 complete: {context.IsNodeComplete("upstream2")}");
        _output.WriteLine($"target complete: {context.IsNodeComplete("target")}");

        context.IsNodeComplete("upstream1").Should().BeTrue();
        context.IsNodeComplete("upstream2").Should().BeTrue();
        context.IsNodeComplete("target").Should().BeTrue("target should execute when upstream1 succeeded");
    }

    [Fact]
    public async Task Debug_SubGraphWithUpstreamCondition_CheckState()
    {
        // Arrange
        var subGraph = new TestGraphBuilder()
            .WithId("sub")
            .WithName("SubGraph")
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_handler", "SuccessHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_handler")
            .AddEdge("sub_handler", "sub_end")
            .Build();

        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("upstream1", "Upstream 1", NodeType.Handler, "SuccessHandler")
            .AddNode("upstream2", "Upstream 2", NodeType.Handler, "FailureHandler",
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddSubGraphNode("subgraph", "SubGraph Node", subGraph)
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "subgraph")
            .AddEdge("upstream2", "subgraph")
            .RequireOneSuccess("subgraph")
            .AddEdge("subgraph", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, _services);
        var orchestrator = new GraphOrchestrator<GraphContext>(_services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Debug output
        _output.WriteLine($"upstream1 complete: {context.IsNodeComplete("upstream1")}");
        _output.WriteLine($"upstream2 complete: {context.IsNodeComplete("upstream2")}");
        _output.WriteLine($"subgraph complete: {context.IsNodeComplete("subgraph")}");

        // Check all channels
        _output.WriteLine($"All channels:");
        foreach (var channelName in context.Channels.ChannelNames)
        {
            _output.WriteLine($"  - {channelName}");
        }

        // Check upstream results
        if (context.IsNodeComplete("upstream1"))
        {
            if (context.Channels.Contains("node_result:upstream1"))
            {
                var result1 = context.Channels["node_result:upstream1"].Get<NodeExecutionResult>();
                _output.WriteLine($"upstream1 result: {result1} (type={result1?.GetType().Name}, isSuccess={result1 is NodeExecutionResult.Success})");
            }
            else
            {
                _output.WriteLine($"upstream1: node_result:upstream1 channel NOT FOUND");
            }
        }

        if (context.IsNodeComplete("upstream2"))
        {
            if (context.Channels.Contains("node_result:upstream2"))
            {
                var result2 = context.Channels["node_result:upstream2"].Get<NodeExecutionResult>();
                _output.WriteLine($"upstream2 result: {result2} (type={result2?.GetType().Name}, isFailure={result2 is NodeExecutionResult.Failure})");
            }
            else
            {
                _output.WriteLine($"upstream2: node_result:upstream2 channel NOT FOUND");
            }
        }

        // Check edge conditions and evaluate them manually
        var targetEdges = graph.Edges.Where(e => e.To == "subgraph").ToList();
        _output.WriteLine($"SubGraph has {targetEdges.Count} incoming edges");
        foreach (var edge in targetEdges)
        {
            _output.WriteLine($"  Edge {edge.From} -> {edge.To}: condition={edge.Condition?.Type}");

            // Manually evaluate condition to see what it returns
            var outputs = context.Channels.Contains($"node_output:{edge.From}")
                ? context.Channels[$"node_output:{edge.From}"].Get<Dictionary<string, object>>()
                : null;
            var conditionResult = HPDAgent.Graph.Core.Orchestration.ConditionEvaluator.Evaluate(
                edge.Condition, outputs, context, edge);
            _output.WriteLine($"    Condition evaluation result: {conditionResult}");
        }

        // Assert
        context.IsNodeComplete("upstream1").Should().BeTrue();
        context.IsNodeComplete("upstream2").Should().BeTrue();

        // This is the failing assertion
        _output.WriteLine("Expected subgraph to be complete, but it's not");
        context.IsNodeComplete("subgraph").Should().BeTrue("subgraph should execute when upstream1 succeeded");
    }
}
