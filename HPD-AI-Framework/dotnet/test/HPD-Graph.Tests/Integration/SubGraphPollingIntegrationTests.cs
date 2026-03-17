 using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Builders;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;

namespace HPD.Graph.Tests.Integration;

/// <summary>
/// Integration tests for SubGraph with upstream conditions.
/// Tests specification from Section 3.1 of HPD_GRAPH_WORKFLOW_PRIMITIVES_V5.md:
/// - SubGraph evaluates upstream conditions like any node
/// - Conditions apply to SubGraph as a whole (not internal nodes)
/// </summary>
public class SubGraphPollingIntegrationTests
{
    private readonly IServiceProvider _services;

    public SubGraphPollingIntegrationTests()
    {
        _services = TestServiceProvider.Create();
    }

    [Fact]
    public async Task SubGraph_WithUpstreamCondition_RequireOneSuccess_ShouldExecute()
    {
        // Arrange - Two upstreams with SubGraph requiring one success
        var subGraph = new TestGraphBuilder()
            .WithId("sub")
            .WithName("ConditionalSub")
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
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate())) // Isolate failures
            .AddSubGraphNode("subgraph", "SubGraph Node", subGraph)
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "subgraph")
            .AddEdge("upstream2", "subgraph")
            .RequireOneSuccess("subgraph") // SubGraph requires at least one upstream success
            .AddEdge("subgraph", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, _services);
        var orchestrator = new GraphOrchestrator<GraphContext>(_services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - SubGraph should execute because upstream1 succeeded
        context.ShouldHaveCompletedNode("subgraph");
        context.IsNodeComplete("subgraph").Should().BeTrue();
    }

    [Fact]
    public async Task SubGraph_WithUpstreamCondition_AllFailed_ShouldNotExecute()
    {
        // Arrange - Both upstreams fail, SubGraph requires one success
        var subGraph = new TestGraphBuilder()
            .WithId("sub")
            .WithName("ConditionalSub")
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_handler", "SuccessHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_handler")
            .AddEdge("sub_handler", "sub_end")
            .Build();

        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("upstream1", "Upstream 1", NodeType.Handler, "FailureHandler",
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddNode("upstream2", "Upstream 2", NodeType.Handler, "FailureHandler",
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddSubGraphNode("subgraph", "SubGraph Node", subGraph)
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "upstream1")
            .AddEdge("start", "upstream2")
            .AddEdge("upstream1", "subgraph")
            .AddEdge("upstream2", "subgraph")
            .RequireOneSuccess("subgraph") // Requires at least one success
            .AddEdge("subgraph", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, _services);
        var orchestrator = new GraphOrchestrator<GraphContext>(_services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - SubGraph should be skipped because all upstreams failed
        context.IsNodeComplete("subgraph").Should().BeTrue("node was evaluated and skipped");
        var subgraphResult = context.Channels["node_result:subgraph"].Get<NodeExecutionResult>();
        subgraphResult.Should().BeOfType<NodeExecutionResult.Skipped>("upstream condition not met");
    }

    [Fact]
    public async Task SubGraph_WithUpstreamCondition_RequireAllDone_ShouldExecuteAfterAllComplete()
    {
        // Arrange - SubGraph waits for all upstreams to complete (regardless of success/failure)
        var subGraph = new TestGraphBuilder()
            .WithId("sub")
            .WithName("AggregateSub")
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_aggregator", "SuccessHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_aggregator")
            .AddEdge("sub_aggregator", "sub_end")
            .Build();

        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("scraper1", "Scraper 1", NodeType.Handler, "SuccessHandler")
            .AddNode("scraper2", "Scraper 2", NodeType.Handler, "FailureHandler",
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddSubGraphNode("aggregator_sub", "Aggregator SubGraph", subGraph)
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "scraper1")
            .AddEdge("start", "scraper2")
            .AddEdge("scraper1", "aggregator_sub")
            .AddEdge("scraper2", "aggregator_sub")
            .RequireAllDone("aggregator_sub") // Wait for all to complete
            .AddEdge("aggregator_sub", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, _services);
        var orchestrator = new GraphOrchestrator<GraphContext>(_services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - SubGraph executes after both upstreams complete
        context.ShouldHaveCompletedNode("aggregator_sub");
        context.IsNodeComplete("scraper1").Should().BeTrue();
        context.IsNodeComplete("scraper2").Should().BeTrue();
    }

    [Fact]
    public async Task SubGraph_WithUpstreamCondition_RequirePartialSuccess_ShouldExecute()
    {
        // Arrange - SubGraph requires all done AND at least one success
        var subGraph = new TestGraphBuilder()
            .WithId("sub")
            .WithName("PartialSub")
            .AddStartNode("sub_start")
            .AddHandlerNode("sub_finalize", "SuccessHandler")
            .AddEndNode("sub_end")
            .AddEdge("sub_start", "sub_finalize")
            .AddEdge("sub_finalize", "sub_end")
            .Build();

        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("validator1", "Validator 1", NodeType.Handler, "SuccessHandler")
            .AddNode("validator2", "Validator 2", NodeType.Handler, "FailureHandler",
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddSubGraphNode("finalize_sub", "Finalize SubGraph", subGraph)
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "validator1")
            .AddEdge("start", "validator2")
            .AddEdge("validator1", "finalize_sub")
            .AddEdge("validator2", "finalize_sub")
            .RequirePartialSuccess("finalize_sub") // All done + at least one success
            .AddEdge("finalize_sub", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, _services);
        var orchestrator = new GraphOrchestrator<GraphContext>(_services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - SubGraph executes because all done and at least one (validator1) succeeded
        context.ShouldHaveCompletedNode("finalize_sub");
        context.IsNodeComplete("validator1").Should().BeTrue();
        context.IsNodeComplete("validator2").Should().BeTrue();
    }

    [Fact]
    public async Task SubGraph_Nested_WithUpstreamConditions_ShouldEvaluateBothLevels()
    {
        // Arrange - Nested SubGraphs, both with upstream conditions
        var innerSub = new TestGraphBuilder()
            .WithId("inner")
            .WithName("InnerSub")
            .AddStartNode("inner_start")
            .AddHandlerNode("inner_handler", "SuccessHandler")
            .AddEndNode("inner_end")
            .AddEdge("inner_start", "inner_handler")
            .AddEdge("inner_handler", "inner_end")
            .Build();

        var outerSub = new TestGraphBuilder()
            .WithId("outer")
            .WithName("OuterSub")
            .AddStartNode("outer_start")
            .AddHandlerNode("outer_upstream1", "SuccessHandler")
            .AddHandlerNode("outer_upstream2", "FailureHandler")
            .AddSubGraphNode("nested_sub", innerSub)
            .AddEndNode("outer_end")
            .AddEdge("outer_start", "outer_upstream1")
            .AddEdge("outer_start", "outer_upstream2")
            .AddEdge("outer_upstream1", "nested_sub")
            .AddEdge("outer_upstream2", "nested_sub")
            .Build();

        // Apply upstream condition to nested SubGraph within outer SubGraph
        var outerGraph = new GraphBuilder()
            .WithName("OuterSubWithCondition")
            .AddNode("outer_start", "Outer Start", NodeType.Start)
            .AddNode("outer_upstream1", "Outer Upstream 1", NodeType.Handler, "SuccessHandler")
            .AddNode("outer_upstream2", "Outer Upstream 2", NodeType.Handler, "FailureHandler",
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddSubGraphNode("nested_sub", "Nested SubGraph", innerSub)
            .AddNode("outer_end", "Outer End", NodeType.End)
            .AddEdge("outer_start", "outer_upstream1")
            .AddEdge("outer_start", "outer_upstream2")
            .AddEdge("outer_upstream1", "nested_sub")
            .AddEdge("outer_upstream2", "nested_sub")
            .RequireOneSuccess("nested_sub") // Condition on inner SubGraph
            .AddEdge("nested_sub", "outer_end")
            .Build();

        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddNode("start", "Start", NodeType.Start)
            .AddNode("top_upstream1", "Top Upstream 1", NodeType.Handler, "SuccessHandler")
            .AddNode("top_upstream2", "Top Upstream 2", NodeType.Handler, "FailureHandler",
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddSubGraphNode("top_sub", "Top SubGraph", outerGraph)
            .AddNode("end", "End", NodeType.End)
            .AddEdge("start", "top_upstream1")
            .AddEdge("start", "top_upstream2")
            .AddEdge("top_upstream1", "top_sub")
            .AddEdge("top_upstream2", "top_sub")
            .RequireOneSuccess("top_sub") // Condition on outer SubGraph
            .AddEdge("top_sub", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, _services);
        var orchestrator = new GraphOrchestrator<GraphContext>(_services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - All levels complete with upstream conditions satisfied
        context.ShouldHaveCompletedNode("top_sub");
    }
}
