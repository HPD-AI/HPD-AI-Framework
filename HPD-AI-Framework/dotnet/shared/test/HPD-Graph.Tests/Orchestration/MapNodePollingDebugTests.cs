using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Builders;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;
using Xunit.Abstractions;

namespace HPD.Graph.Tests.Orchestration;

/// <summary>
/// Debug tests to understand Map node polling timeout behavior.
/// </summary>
public class MapNodePollingDebugTests
{
    private readonly ITestOutputHelper _output;

    public MapNodePollingDebugTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Test handler that always returns Suspended to trigger timeout.
    /// </summary>
    private class TimeoutHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "TimeoutHandler";

        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Suspended.ForPolling(
                    suspendToken: "test-timeout",
                    retryAfter: TimeSpan.FromMilliseconds(50),
                    maxWaitTime: TimeSpan.FromMilliseconds(200),
                    message: "Testing timeout"
                )
            );
        }
    }

    [Fact]
    public async Task SingleHandlerWithTimeout_ShouldCreateFailureResult()
    {
        // Arrange
        var handler = new TimeoutHandler();
        var services = TestServiceProvider.CreateWithHandler(handler);

        var graph = new GraphBuilder()
            .WithName("TestGraph")
            .AddStartNode("start")
            .AddNode("handler", "Handler", NodeType.Handler, "TimeoutHandler",
                n => n.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .AddEndNode("end")
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        _output.WriteLine($"Graph complete: {context.IsComplete}");
        _output.WriteLine($"Channels: {string.Join(", ", context.Channels.ChannelNames)}");

        // Check for node_result channel
        if (context.Channels.Contains("node_result:handler"))
        {
            var result = context.Channels["node_result:handler"].Get<NodeExecutionResult>();
            _output.WriteLine($"Handler result type: {result?.GetType().Name}");

            if (result is NodeExecutionResult.Failure failure)
            {
                _output.WriteLine($"Failure exception: {failure.Exception.Message}");
                result.Should().BeOfType<NodeExecutionResult.Failure>();
                failure.Exception.Should().BeOfType<TimeoutException>();
            }
            else
            {
                _output.WriteLine($"ERROR: Expected Failure, got {result?.GetType().Name}");
                throw new Exception($"Expected Failure result, got {result?.GetType().Name}");
            }
        }
        else
        {
            _output.WriteLine("ERROR: node_result:handler channel not found!");
            throw new Exception("node_result:handler channel not found");
        }
    }

    /// <summary>
    /// Test handler that polls once then succeeds.
    /// </summary>
    private class SuccessAfterPollingHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "SuccessAfterPollingHandler";
        private int _callCount = 0;

        public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
        {
            _callCount++;

            if (_callCount == 1)
            {
                // First call - return Suspended
                return Task.FromResult<NodeExecutionResult>(
                    NodeExecutionResult.Suspended.ForPolling(
                        suspendToken: "test-poll",
                        retryAfter: TimeSpan.FromMilliseconds(50),
                        maxWaitTime: TimeSpan.FromSeconds(10),
                        message: "Polling once"
                    )
                );
            }

            // Second call - return Success
            return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
                output: new Dictionary<string, object> { ["result"] = "success" },
                duration: TimeSpan.FromMilliseconds(10),
                metadata: new NodeExecutionMetadata()
            ));
        }
    }

    [Fact]
    public async Task SingleHandlerWithSuccessAfterPolling_ShouldCreateSuccessResult()
    {
        // Arrange
        var handler = new SuccessAfterPollingHandler();
        var services = TestServiceProvider.CreateWithHandler(handler);

        var graph = new TestGraphBuilder()
            .AddStartNode("start")
            .AddHandlerNode("handler", "SuccessAfterPollingHandler")
            .AddEndNode("end")
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var context = new GraphContext("test-exec", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Should complete successfully
        _output.WriteLine($"Graph complete: {context.IsComplete}");
        _output.WriteLine($"Channels: {string.Join(", ", context.Channels.ChannelNames)}");

        context.IsComplete.Should().BeTrue();

        // Check for node_result channel
        if (context.Channels.Contains("node_result:handler"))
        {
            var result = context.Channels["node_result:handler"].Get<NodeExecutionResult>();
            _output.WriteLine($"Handler result type: {result?.GetType().Name}");

            result.Should().BeOfType<NodeExecutionResult.Success>();
            var success = (NodeExecutionResult.Success)result;
            success.PortOutputs[0].Should().ContainKey("result");
            success.PortOutputs[0]["result"].Should().Be("success");
        }
        else
        {
            _output.WriteLine("ERROR: node_result:handler channel not found!");
            throw new Exception("node_result:handler channel not found");
        }
    }
}
