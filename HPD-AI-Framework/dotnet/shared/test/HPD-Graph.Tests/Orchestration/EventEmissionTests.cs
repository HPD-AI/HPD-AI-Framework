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

        // Act
        _ = Task.Run(async () => await orchestrator.ExecuteAsync(context));
        await Task.Delay(100); // Give time for start event

        // Assert
        var events = new List<Event>();
        await foreach (var evt in coordinator.ReadAllAsync(new CancellationTokenSource(500).Token))
        {
            events.Add(evt);
            if (events.Count >= 10) break;
        }

        var startedEvent = events.OfType<GraphExecutionStartedEvent>().FirstOrDefault();
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

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        var events = new List<Event>();
        await foreach (var evt in coordinator.ReadAllAsync(new CancellationTokenSource(500).Token))
        {
            events.Add(evt);
            if (evt is GraphExecutionCompletedEvent) break;
        }

        var completedEvent = events.OfType<GraphExecutionCompletedEvent>().FirstOrDefault();
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

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        var events = new List<Event>();
        await foreach (var evt in coordinator.ReadAllAsync(new CancellationTokenSource(500).Token))
        {
            events.Add(evt);
            if (evt is GraphExecutionCompletedEvent) break;
        }

        var nodeStartedEvents = events.OfType<NodeExecutionStartedEvent>().ToList();
        var nodeCompletedEvents = events.OfType<NodeExecutionCompletedEvent>().ToList();

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

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        var events = new List<Event>();
        await foreach (var evt in coordinator.ReadAllAsync(new CancellationTokenSource(500).Token))
        {
            events.Add(evt);
            if (evt is GraphExecutionCompletedEvent) break;
        }

        var layerStartedEvents = events.OfType<LayerExecutionStartedEvent>().ToList();
        var layerCompletedEvents = events.OfType<LayerExecutionCompletedEvent>().ToList();

        layerStartedEvents.Should().NotBeEmpty();
        layerCompletedEvents.Should().NotBeEmpty();
    }
}
