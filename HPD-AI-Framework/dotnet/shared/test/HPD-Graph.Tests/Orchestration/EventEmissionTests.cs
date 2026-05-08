using FluentAssertions;
using HPD.Events;
using HPD.Events.Core;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;

namespace HPD.Graph.Tests.Orchestration;

/// <summary>
/// Tests for event emission during graph execution (Primitive 2).
/// </summary>
public class EventEmissionTests
{
    [Fact]
    public async Task ExecuteAsync_WithEventCoordinator_EmitsGraphStartedEvent()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler1")
            .AddEdge("handler1", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var coordinator = new EventCoordinator();
        var context = new GraphContext(
            executionId: "test-exec",
            graph: graph,
            services: services)
        {
            EventCoordinator = coordinator
        };

        var orchestrator = new GraphOrchestrator<GraphContext>(services);
        await using var events = coordinator.SubscribeChannel(EventChannel.Synchronous);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        var collectedEvents = await CollectSynchronousEventsAsync(events.Reader, maxCount: 10);

        var startedEvent = collectedEvents.OfType<GraphExecutionStartedEvent>().FirstOrDefault();
        startedEvent.Should().NotBeNull();
        startedEvent!.NodeCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithEventCoordinator_EmitsGraphCompletedEvent()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler1")
            .AddEdge("handler1", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var coordinator = new EventCoordinator();
        var context = new GraphContext(
            executionId: "test-exec",
            graph: graph,
            services: services)
        {
            EventCoordinator = coordinator
        };

        var orchestrator = new GraphOrchestrator<GraphContext>(services);
        await using var events = coordinator.SubscribeChannel(EventChannel.Synchronous);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        var collectedEvents = await CollectSynchronousEventsAsync(events.Reader, evt => evt is GraphExecutionCompletedEvent);

        var completedEvent = collectedEvents.OfType<GraphExecutionCompletedEvent>().FirstOrDefault();
        completedEvent.Should().NotBeNull();
        completedEvent!.SuccessfulNodes.Should().BeGreaterThan(0);
        completedEvent.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecuteAsync_WithEventCoordinator_EmitsNodeEvents()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler1")
            .AddEdge("handler1", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var coordinator = new EventCoordinator();
        var context = new GraphContext(
            executionId: "test-exec",
            graph: graph,
            services: services)
        {
            EventCoordinator = coordinator
        };

        var orchestrator = new GraphOrchestrator<GraphContext>(services);
        await using var events = coordinator.SubscribeChannel(EventChannel.Synchronous);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        var collectedEvents = await CollectSynchronousEventsAsync(events.Reader, evt => evt is GraphExecutionCompletedEvent);

        var nodeStartedEvents = collectedEvents.OfType<NodeExecutionStartedEvent>().ToList();
        var nodeCompletedEvents = collectedEvents.OfType<NodeExecutionCompletedEvent>().ToList();

        nodeStartedEvents.Should().NotBeEmpty();
        nodeCompletedEvents.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutEventCoordinator_DoesNotCrash()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler1")
            .AddEdge("handler1", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext(
            executionId: "test-exec",
            graph: graph,
            services: services);
        // No EventCoordinator set

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act & Assert - should not crash when EventCoordinator is null
        await orchestrator.ExecuteAsync(context);
        context.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithEventCoordinator_EmitsLayerEvents()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler1", "SuccessHandler")
            .AddHandlerNode("handler2", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler1")
            .AddEdge("start", "handler2") // Parallel handlers in same layer
            .AddEdge("handler1", "end")
            .AddEdge("handler2", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var coordinator = new EventCoordinator();
        var context = new GraphContext(
            executionId: "test-exec",
            graph: graph,
            services: services)
        {
            EventCoordinator = coordinator
        };

        var orchestrator = new GraphOrchestrator<GraphContext>(services);
        await using var events = coordinator.SubscribeChannel(EventChannel.Synchronous);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        var collectedEvents = await CollectSynchronousEventsAsync(events.Reader, evt => evt is GraphExecutionCompletedEvent);

        var layerStartedEvents = collectedEvents.OfType<LayerExecutionStartedEvent>().ToList();
        var layerCompletedEvents = collectedEvents.OfType<LayerExecutionCompletedEvent>().ToList();

        layerStartedEvents.Should().NotBeEmpty();
        layerCompletedEvents.Should().NotBeEmpty();
    }

    private static async Task<List<Event>> CollectSynchronousEventsAsync(
        System.Threading.Channels.ChannelReader<Event> reader,
        Func<Event, bool>? stopWhen = null,
        int maxCount = int.MaxValue)
    {
        var events = new List<Event>();
        using var cts = new CancellationTokenSource(500);

        try
        {
            await foreach (var evt in reader.ReadAllAsync(cts.Token))
            {
                events.Add(evt);
                if (events.Count >= maxCount || stopWhen?.Invoke(evt) == true)
                    break;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }

        return events;
    }
}
