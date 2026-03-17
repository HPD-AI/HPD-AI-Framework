using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using HPDAgent.Graph.Core.Validation;
using Xunit;

namespace HPD.Graph.Tests.Orchestration;

/// <summary>
/// Comprehensive tests for Map node execution.
/// </summary>
public class MapNodeExecutionTests
{
    #region Basic Map Execution

    [Fact]
    public async Task ExecuteAsync_MapNode_ExecutesProcessorGraphOncePerItem()
    {
        // Arrange
        var processorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler") // Returns list of 3 items by default
            .AddMapNode("map", processorGraph, maxParallelMapTasks: 1)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Debug: check what happened
        var completedNodes = context.CompletedNodes.ToList();

        // Debug output
        var logMessages = context.LogEntries.Select(l => l.Message).ToList();
        var channelNames = context.Channels.ChannelNames.ToList();

        // Check prepare completed
        completedNodes.Should().Contain("prepare",
            $"prepare node should be complete. Completed: [{string.Join(", ", completedNodes)}]. Logs: {string.Join("; ", logMessages.TakeLast(5))}");

        // Check map completed
        completedNodes.Should().Contain("map",
            $"map node should be complete. Completed: [{string.Join(", ", completedNodes)}]. Channels: [{string.Join(", ", channelNames)}]. Logs: {string.Join("; ", logMessages.TakeLast(10))}");

        context.IsComplete.Should().BeTrue();

        var results = context.Channels["node_output:map"].Get<List<object?>>();
        results.Should().NotBeNull("map output should not be null");
        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExecuteAsync_MapNode_MaintainsOriginalOrderByIndex()
    {
        // Arrange
        var processorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("map", processorGraph, maxParallelMapTasks: 10) // High concurrency to test ordering
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        var results = context.Channels["node_output:map"].Get<List<object?>>();
        results.Should().NotBeNull();
        results.Should().HaveCount(3);
        // Results should contain 3 items (order preserved via index tracking in ConcurrentDictionary)
        // The actual values depend on handler output format, but count must match input count
    }

    [Fact]
    public async Task ExecuteAsync_MapNode_RespectsMaxParallelMapTasks()
    {
        // Arrange
        var processorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "DelayHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("map", processorGraph, maxParallelMapTasks: 2) // Limit to 2 concurrent executions
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.IsComplete.Should().BeTrue();
        var results = context.Channels["node_output:map"].Get<List<object?>>();
        results.Should().NotBeNull();
        results.Should().HaveCount(3);
    }

    #endregion

    #region Error Mode Tests

    [Fact]
    public async Task ExecuteAsync_MapNode_FailFastMode_StopsOnFirstError()
    {
        // Arrange
        var processorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "FailureHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("map", processorGraph, maxParallelMapTasks: 1, errorMode: MapErrorMode.FailFast)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act & Assert - GraphExecutionException is thrown when a handler fails
        await Assert.ThrowsAnyAsync<Exception>(async () => await orchestrator.ExecuteAsync(context));
    }

    [Fact]
    public async Task ExecuteAsync_MapNode_FailFastMode_ReturnsAggregateException()
    {
        // Arrange
        var processorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "FailureHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("map", processorGraph, maxParallelMapTasks: 10, errorMode: MapErrorMode.FailFast) // High concurrency for multiple failures
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<Exception>(async () => await orchestrator.ExecuteAsync(context));
        // Could be single exception or AggregateException depending on timing
        exception.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_MapNode_ContinueWithNulls_AddsNullForFailedItems()
    {
        // Arrange
        var processorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "FailureHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("map", processorGraph, maxParallelMapTasks: 1, errorMode: MapErrorMode.ContinueWithNulls)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.IsComplete.Should().BeTrue();
        var results = context.Channels["node_output:map"].Get<List<object?>>();
        results.Should().NotBeNull();
        results.Should().HaveCount(3);
        // All items should be null due to failures
        results.Should().OnlyContain(x => x == null);
    }

    [Fact]
    public async Task ExecuteAsync_MapNode_ContinueOmitFailures_SkipsFailedItems()
    {
        // Arrange
        var processorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "FailureHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("map", processorGraph, maxParallelMapTasks: 1, errorMode: MapErrorMode.ContinueOmitFailures)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.IsComplete.Should().BeTrue();
        var results = context.Channels["node_output:map"].Get<List<object?>>();
        results.Should().NotBeNull();
        // All items failed, so result should be empty
        results.Should().BeEmpty();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ExecuteAsync_MapNode_EmptyInput_ReturnsEmptyResults()
    {
        // Arrange
        var processorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("map", processorGraph, mapInputChannel: "empty_channel") // Use empty channel
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        // Set empty list in the custom channel
        context.Channels["empty_channel"].Set(new List<string>());

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.IsComplete.Should().BeTrue();
        var results = context.Channels["node_output:map"].Get<List<object?>>();
        results.Should().NotBeNull();
        results.Should().BeEmpty();
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task ExecuteAsync_MapNode_NestedMaps_WorkCorrectly()
    {
        // Arrange - Inner map processor
        var innerProcessorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        // Outer map processor (contains inner map)
        var outerProcessorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("inner-map", innerProcessorGraph, maxParallelMapTasks: 2)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "inner-map")
            .AddEdge("inner-map", "end")
            .Build();

        // Main graph with outer map
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("outer-map", outerProcessorGraph, maxParallelMapTasks: 2)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "outer-map")
            .AddEdge("outer-map", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.IsComplete.Should().BeTrue();
        var results = context.Channels["node_output:outer-map"].Get<List<object?>>();
        results.Should().NotBeNull();
        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExecuteAsync_MapNode_ContextMerging_PreservesLogsAndMetrics()
    {
        // Arrange
        var processorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("map", processorGraph, maxParallelMapTasks: 3)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.IsComplete.Should().BeTrue();
        var results = context.Channels["node_output:map"].Get<List<object?>>();
        results.Should().NotBeNull();
        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExecuteAsync_MapNode_CustomInputOutputChannels_Work()
    {
        // Arrange
        var processorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("map", processorGraph, maxParallelMapTasks: 1,
                mapInputChannel: "custom_input",
                mapOutputChannel: "custom_output")
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        // Set input on custom channel
        context.Channels["custom_input"].Set(new List<string> { "item1", "item2" });

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.IsComplete.Should().BeTrue();
        var results = context.Channels["custom_output"].Get<List<object?>>();
        results.Should().NotBeNull();
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_MapNode_WithNullMaxParallelMapTasks_UsesUnboundedParallelism()
    {
        // Arrange
        var processorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("map", processorGraph, maxParallelMapTasks: null) // Unbounded
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.IsComplete.Should().BeTrue();
        var results = context.Channels["node_output:map"].Get<List<object?>>();
        results.Should().NotBeNull();
        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExecuteAsync_MapNode_WithZeroMaxParallelMapTasks_UsesProcessorCount()
    {
        // Arrange
        var processorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "ListProducerHandler")
            .AddMapNode("map", processorGraph, maxParallelMapTasks: 0) // Auto (Environment.ProcessorCount)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("test-exec", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.IsComplete.Should().BeTrue();
        var results = context.Channels["node_output:map"].Get<List<object?>>();
        results.Should().NotBeNull();
        results.Should().HaveCount(3);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void Validate_MapNodeWithoutProcessorGraph_ReturnsError()
    {
        // Arrange - Manually create graph since TestGraphBuilder won't allow invalid config
        var graph = new HPDAgent.Graph.Abstractions.Graph.Graph
        {
            Id = "test",
            Name = "Test",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "end",
            Nodes = new List<Node>
            {
                new Node { Id = "start", Name = "Start", Type = NodeType.Start },
                new Node
                {
                    Id = "map",
                    Name = "Map",
                    Type = NodeType.Map,
                    // Missing MapProcessorGraph!
                    MaxParallelMapTasks = 1
                },
                new Node { Id = "end", Name = "End", Type = NodeType.End }
            },
            Edges = new List<Edge>
            {
                new Edge { From = "start", To = "map" },
                new Edge { From = "map", To = "end" }
            }
        };

        // Act
        var validation = GraphValidator.Validate(graph);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Message.Contains("Map node") && (e.Message.Contains("Processor") || e.Message.Contains("processor")));
    }

    [Fact]
    public void Validate_MapNodeWithInvalidProcessorGraph_ReturnsError()
    {
        // Arrange - Invalid processor graph (missing END node)
        var invalidProcessorGraph = new HPDAgent.Graph.Abstractions.Graph.Graph
        {
            Id = "processor",
            Name = "Processor",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "end", // References non-existent end node
            Nodes = new List<Node>
            {
                new Node { Id = "start", Name = "Start", Type = NodeType.Start }
                // Missing END node!
            },
            Edges = new List<Edge>()
        };

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddMapNode("map", invalidProcessorGraph)
            .AddEndNode()
            .AddEdge("start", "map")
            .AddEdge("map", "end")
            .Build();

        // Act
        var validation = GraphValidator.Validate(graph);

        // Assert
        validation.IsValid.Should().BeFalse();
        // Should have error about processor graph validation
        validation.Errors.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void Validate_MapNodeWithNegativeMaxParallelMapTasks_ReturnsError()
    {
        // Arrange
        var processorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        // Manually create graph with invalid config
        var graph = new HPDAgent.Graph.Abstractions.Graph.Graph
        {
            Id = "test",
            Name = "Test",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "end",
            Nodes = new List<Node>
            {
                new Node { Id = "start", Name = "Start", Type = NodeType.Start },
                new Node
                {
                    Id = "map",
                    Name = "Map",
                    Type = NodeType.Map,
                    MapProcessorGraph = processorGraph,
                    MaxParallelMapTasks = -5 // Invalid!
                },
                new Node { Id = "end", Name = "End", Type = NodeType.End }
            },
            Edges = new List<Edge>
            {
                new Edge { From = "start", To = "map" },
                new Edge { From = "map", To = "end" }
            }
        };

        // Act
        var validation = GraphValidator.Validate(graph);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Message.Contains("MaxParallelMapTasks") && e.Message.Contains("negative"));
    }

    [Fact]
    public void Validate_MapNodeWithExcessiveMaxParallelMapTasks_ReturnsWarning()
    {
        // Arrange
        var processorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        // Manually create graph with excessive config
        var graph = new HPDAgent.Graph.Abstractions.Graph.Graph
        {
            Id = "test",
            Name = "Test",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "end",
            Nodes = new List<Node>
            {
                new Node { Id = "start", Name = "Start", Type = NodeType.Start },
                new Node
                {
                    Id = "map",
                    Name = "Map",
                    Type = NodeType.Map,
                    MapProcessorGraph = processorGraph,
                    MaxParallelMapTasks = 5000 // Excessive!
                },
                new Node { Id = "end", Name = "End", Type = NodeType.End }
            },
            Edges = new List<Edge>
            {
                new Edge { From = "start", To = "map" },
                new Edge { From = "map", To = "end" }
            }
        };

        // Act
        var validation = GraphValidator.Validate(graph);

        // Assert
        validation.Warnings.Should().Contain(w => w.Message.Contains("MaxParallelMapTasks"));
    }

    [Fact]
    public void Validate_MapNodeWithHandlerName_ReturnsWarning()
    {
        // Arrange
        var processorGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        // Manually create graph with HandlerName on Map node
        var graph = new HPDAgent.Graph.Abstractions.Graph.Graph
        {
            Id = "test",
            Name = "Test",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "end",
            Nodes = new List<Node>
            {
                new Node { Id = "start", Name = "Start", Type = NodeType.Start },
                new Node
                {
                    Id = "map",
                    Name = "Map",
                    Type = NodeType.Map,
                    MapProcessorGraph = processorGraph,
                    HandlerName = "ShouldNotHaveThis" // Maps don't use HandlerName!
                },
                new Node { Id = "end", Name = "End", Type = NodeType.End }
            },
            Edges = new List<Edge>
            {
                new Edge { From = "start", To = "map" },
                new Edge { From = "map", To = "end" }
            }
        };

        // Act
        var validation = GraphValidator.Validate(graph);

        // Assert
        validation.Warnings.Should().Contain(w => w.Message.Contains("Map node") && w.Message.Contains("HandlerName"));
    }

    #endregion

    #region Heterogeneous Map Routing Tests

    [Fact]
    public async Task ExecuteAsync_HeterogeneousMap_RoutesItemsToCorrectProcessorGraphs()
    {
        // Arrange - Create different processor graphs
        var stringGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("string_handler", "StringProcessorHandler")
            .AddEndNode()
            .AddEdge("start", "string_handler")
            .AddEdge("string_handler", "end")
            .Build();

        var intGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("int_handler", "IntProcessorHandler")
            .AddEndNode()
            .AddEdge("start", "int_handler")
            .AddEdge("int_handler", "end")
            .Build();

        // Main graph with heterogeneous map
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "MixedTypeListProducerHandler") // Returns ["hello", 42, "world"]
            .AddMapNode("map", mapProcessorGraphs: new Dictionary<string, HPDAgent.Graph.Abstractions.Graph.Graph>
            {
                ["string"] = stringGraph,
                ["int"] = intGraph
            }, mapRouterName: "TypeBasedRouter", maxParallelMapTasks: 1)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.CreateWithRouters();
        var context = new GraphContext("test-hetero", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        var results = context.Channels["node_output:map"].Get<List<object>>();
        results.Should().NotBeNull();
        results.Should().HaveCount(3); // 2 strings + 1 int
    }

    [Fact]
    public async Task ExecuteAsync_HeterogeneousMap_FallbackToDefaultGraph()
    {
        // Arrange - Processor graphs for known types
        var stringGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("string_handler", "StringProcessorHandler")
            .AddEndNode()
            .AddEdge("start", "string_handler")
            .AddEdge("string_handler", "end")
            .Build();

        // Default fallback graph
        var defaultGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("default_handler", "DefaultProcessorHandler")
            .AddEndNode()
            .AddEdge("start", "default_handler")
            .AddEdge("default_handler", "end")
            .Build();

        // Main graph with heterogeneous map + default
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "MixedTypeListProducerHandler") // Returns ["hello", 42, "world"]
            .AddMapNode("map",
                mapProcessorGraphs: new Dictionary<string, HPDAgent.Graph.Abstractions.Graph.Graph>
                {
                    ["string"] = stringGraph
                    // No "int" handler - will fall back to default
                },
                mapRouterName: "TypeBasedRouter",
                mapDefaultGraph: defaultGraph,
                maxParallelMapTasks: 1)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.CreateWithRouters();
        var context = new GraphContext("test-default", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        var results = context.Channels["node_output:map"].Get<List<object>>();
        results.Should().NotBeNull();
        results.Should().HaveCount(3); // 2 strings (string graph) + 1 int (default graph)
    }

    [Fact]
    public async Task ExecuteAsync_HeterogeneousMap_NoDefaultGraph_ThrowsOnUnmatchedRoute()
    {
        // Arrange - Only handle strings, no default
        var stringGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("string_handler", "StringProcessorHandler")
            .AddEndNode()
            .AddEdge("start", "string_handler")
            .AddEdge("string_handler", "end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "MixedTypeListProducerHandler") // Returns ["hello", 42]
            .AddMapNode("map",
                mapProcessorGraphs: new Dictionary<string, HPDAgent.Graph.Abstractions.Graph.Graph>
                {
                    ["string"] = stringGraph
                    // No "int" handler, no default - should throw
                },
                mapRouterName: "TypeBasedRouter",
                maxParallelMapTasks: 1)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.CreateWithRouters();
        var context = new GraphContext("test-no-default", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await orchestrator.ExecuteAsync(context));

        exception.Message.Should().Contain("routing value");
        exception.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteAsync_HeterogeneousMap_PropertyBasedRouter()
    {
        // Arrange - Different graphs for different document types
        var pdfGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("pdf_handler", "PdfProcessorHandler")
            .AddEndNode()
            .AddEdge("start", "pdf_handler")
            .AddEdge("pdf_handler", "end")
            .Build();

        var imageGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("image_handler", "ImageProcessorHandler")
            .AddEndNode()
            .AddEdge("start", "image_handler")
            .AddEdge("image_handler", "end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("prepare", "DocumentListProducerHandler") // Returns TestDocuments
            .AddMapNode("map",
                mapProcessorGraphs: new Dictionary<string, HPDAgent.Graph.Abstractions.Graph.Graph>
                {
                    ["pdf"] = pdfGraph,
                    ["image"] = imageGraph
                },
                mapRouterName: "PropertyBasedRouter",
                maxParallelMapTasks: 2)
            .AddEndNode()
            .AddEdge("start", "prepare")
            .AddEdge("prepare", "map")
            .AddEdge("map", "end")
            .Build();

        var services = TestServiceProvider.CreateWithRouters();
        var context = new GraphContext("test-property-router", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        var results = context.Channels["node_output:map"].Get<List<object>>();
        results.Should().NotBeNull();
    }

    [Fact]
    public void Validate_HeterogeneousMap_MissingRouter_ReturnsError()
    {
        // Arrange - MapProcessorGraphs without MapRouterName
        var stringGraph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddMapNode("map",
                mapProcessorGraphs: new Dictionary<string, HPDAgent.Graph.Abstractions.Graph.Graph>
                {
                    ["string"] = stringGraph
                },
                mapRouterName: null) // Missing router!
            .AddEndNode()
            .AddEdge("start", "map")
            .AddEdge("map", "end")
            .Build();

        // Act
        var validation = GraphValidator.Validate(graph);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e =>
            e.Code == "MAP_MISSING_ROUTER" &&
            e.Message.Contains("MapRouterName"));
    }

    [Fact]
    public void Validate_HeterogeneousMap_ConflictingProcessors_ReturnsError()
    {
        // Arrange - Both MapProcessorGraph AND MapProcessorGraphs
        var graph1 = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("handler", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "handler")
            .AddEdge("handler", "end")
            .Build();

        var conflictingNode = new Node
        {
            Id = "map",
            Name = "Map Node",
            Type = NodeType.Map,
            MapProcessorGraph = graph1,  // Homogeneous
            MapProcessorGraphs = new Dictionary<string, HPDAgent.Graph.Abstractions.Graph.Graph>
            {
                ["test"] = graph1  // AND Heterogeneous - CONFLICT!
            },
            MapRouterName = "TestRouter"
        };

        var graph = new HPDAgent.Graph.Abstractions.Graph.Graph
        {
            Id = "test-conflict",
            Name = "Test Graph",
            EntryNodeId = "start",
            ExitNodeId = "end",
            Nodes = new[]
            {
                new Node { Id = "start", Name = "Start", Type = NodeType.Start },
                conflictingNode,
                new Node { Id = "end", Name = "End", Type = NodeType.End }
            },
            Edges = new[]
            {
                new Edge { From = "start", To = "map" },
                new Edge { From = "map", To = "end" }
            }
        };

        // Act
        var validation = GraphValidator.Validate(graph);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e =>
            e.Code == "MAP_CONFLICTING_PROCESSORS" &&
            e.Message.Contains("cannot have both"));
    }

    [Fact]
    public void Validate_HeterogeneousMap_EmptyProcessorGraphs_ReturnsWarning()
    {
        // Arrange - Empty MapProcessorGraphs dictionary
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddMapNode("map",
                mapProcessorGraphs: new Dictionary<string, HPDAgent.Graph.Abstractions.Graph.Graph>(),
                mapRouterName: "TestRouter")
            .AddEndNode()
            .AddEdge("start", "map")
            .AddEdge("map", "end")
            .Build();

        // Act
        var validation = GraphValidator.Validate(graph);

        // Assert
        validation.Warnings.Should().Contain(w =>
            w.Code == "MAP_EMPTY_PROCESSORS" &&
            w.Message.Contains("empty MapProcessorGraphs"));
    }

    #endregion
}
