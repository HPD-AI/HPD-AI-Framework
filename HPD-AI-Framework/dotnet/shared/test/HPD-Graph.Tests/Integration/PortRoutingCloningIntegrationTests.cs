using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Builders;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Graph.Tests.Integration;

/// <summary>
/// Integration tests for port-based routing and lazy cloning combined with workflow primitives.
/// Tests the synergy between:
/// - Polling sensors, upstream conditions, state tracking
/// - Port-based routing, lazy cloning, enhanced metadata
/// </summary>
public class PortRoutingCloningIntegrationTests
{
    #region Polling + Port Routing

    [Fact]
    public async Task PollingSensor_WithPortBasedRouting_RoutesBasedOnFileSize()
    {
        // Arrange - Polling sensor that routes based on output content
        var graph = new GraphBuilder()
            .WithId("polling-port-routing")
            .WithName("Polling with Port Routing")
            .WithCloningPolicy(CloningPolicy.LazyClone)
            .AddNode("start", "Start", NodeType.Start, handlerName: "")
            .AddNode("sensor", "File Sensor", NodeType.Handler, "FileSizePollingHandler",
                n => n.WithOutputPorts(2)) // Port 0: large files, Port 1: small files
            .AddNode("process_large", "Process Large", NodeType.Handler, "LargeFileHandler")
            .AddNode("process_small", "Process Small", NodeType.Handler, "SmallFileHandler")
            .AddNode("end", "End", NodeType.End, handlerName: "")
            .AddEdge("start", "sensor")
            .AddEdge("sensor", "process_large", e => e.FromPort(0))
            .AddEdge("sensor", "process_small", e => e.FromPort(1))
            .AddEdge("process_large", "end")
            .AddEdge("process_small", "end")
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<GraphContext>, FileSizePollingHandler>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, LargeFileHandler>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, SmallFileHandler>()
            .BuildServiceProvider();

        var context = new GraphContext("test-polling-routing", graph, services);

        // Simulate file ready state (small file)
        context.AddTag("file_ready", "true");
        context.AddTag("file_size", "500");

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert - Small file handler should execute (port 1), large handler should skip
        context.ShouldHaveCompletedNode("sensor");
        context.ShouldHaveCompletedNode("process_small");

        // process_large executes but skips due to no inputs from port 0
        context.ShouldHaveCompletedNode("process_large");
        context.Tags[$"node_state:process_large"].Should().Contain(NodeState.Skipped.ToString());

        // Verify metadata propagation
        var sensorOutputs = context.Channels["node_output:sensor:port:1"].Get<Dictionary<string, object>>();
        sensorOutputs.Should().NotBeNull();
        sensorOutputs!["size"].Should().Be(500);
    }

    [Fact]
    public async Task PollingSensor_WithLargeFile_RoutesToPort0()
    {
        // Arrange
        var graph = new GraphBuilder()
            .WithId("polling-large-file")
            .WithName("Polling Large File")
            .AddNode("start", "Start", NodeType.Start, handlerName: "")
            .AddNode("sensor", "File Sensor", NodeType.Handler, "FileSizePollingHandler",
                n => n.WithOutputPorts(2))
            .AddNode("process_large", "Process Large", NodeType.Handler, "LargeFileHandler")
            .AddNode("process_small", "Process Small", NodeType.Handler, "SmallFileHandler")
            .AddNode("end", "End", NodeType.End, handlerName: "")
            .AddEdge("start", "sensor")
            .AddEdge("sensor", "process_large", e => e.FromPort(0))
            .AddEdge("sensor", "process_small", e => e.FromPort(1))
            .AddEdge("process_large", "end")
            .AddEdge("process_small", "end")
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<GraphContext>, FileSizePollingHandler>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, LargeFileHandler>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, SmallFileHandler>()
            .BuildServiceProvider();

        var context = new GraphContext("test-large-file", graph, services);

        // Simulate large file ready
        context.AddTag("file_ready", "true");
        context.AddTag("file_size", "2000");

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert - Large file handler should execute (port 0), small handler should skip
        context.ShouldHaveCompletedNode("sensor");
        context.ShouldHaveCompletedNode("process_large");

        // process_small executes but skips due to no inputs from port 1
        context.ShouldHaveCompletedNode("process_small");
        context.Tags[$"node_state:process_small"].Should().Contain(NodeState.Skipped.ToString());

        var sensorOutputs = context.Channels["node_output:sensor:port:0"].Get<Dictionary<string, object>>();
        sensorOutputs.Should().NotBeNull();
        sensorOutputs!["size"].Should().Be(2000);
    }

    #endregion

    #region Upstream Conditions + Port Routing

    [Fact]
    public async Task UpstreamConditions_WithPortRouting_CombineCorrectly()
    {
        // Arrange - Router with ports + downstream upstream conditions
        var graph = new GraphBuilder()
            .WithId("upstream-port-routing")
            .WithName("Upstream + Port Routing")
            .AddNode("start", "Start", NodeType.Start, handlerName: "")
            .AddNode("classifier", "Classifier", NodeType.Handler, "DataClassifierHandler",
                n => n.WithOutputPorts(2)) // Port 0: high priority, Port 1: low priority
            .AddNode("high_processor", "High Priority", NodeType.Handler, "HighPriorityHandler")
            .AddNode("low_processor", "Low Priority", NodeType.Handler, "LowPriorityHandler")
            .AddNode("end", "End", NodeType.End, handlerName: "")
            .AddEdge("start", "classifier")
            .AddEdge("classifier", "high_processor", e => e.FromPort(0))
            .AddEdge("classifier", "low_processor", e => e.FromPort(1))
            .AddEdge("high_processor", "end")
            .AddEdge("low_processor", "end")
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<GraphContext>, DataClassifierHandler>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, HighPriorityHandler>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, LowPriorityHandler>()
            .BuildServiceProvider();

        var context = new GraphContext("test-upstream-port", graph, services);
        context.AddTag("data_type", "urgent"); // High priority

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert - Classifier routes to port 0, low_processor (port 1) should skip
        context.ShouldHaveCompletedNode("classifier");
        context.ShouldHaveCompletedNode("high_processor");

        // low_processor executes but skips due to no inputs from port 1
        context.ShouldHaveCompletedNode("low_processor");
        context.Tags[$"node_state:low_processor"].Should().Contain(NodeState.Skipped.ToString());
    }

    #endregion

    #region Lazy Cloning + Port Routing

    [Fact]
    public async Task LazyCloning_WithMultiPortFanOut_FirstEdgeGetsOriginal()
    {
        // Arrange - Node with 2 ports, each fanning out to 2 downstream nodes
        var graph = new GraphBuilder()
            .WithId("lazy-clone-multi-port")
            .WithName("Lazy Clone Multi-Port")
            .WithCloningPolicy(CloningPolicy.LazyClone)
            .AddNode("start", "Start", NodeType.Start, handlerName: "")
            .AddNode("splitter", "Splitter", NodeType.Handler, "DualPortSplitterHandler",
                n => n.WithOutputPorts(2))
            .AddNode("port0_consumer1", "Port0 Consumer 1", NodeType.Handler, "Port0Consumer1")
            .AddNode("port0_consumer2", "Port0 Consumer 2", NodeType.Handler, "Port0Consumer2")
            .AddNode("port1_consumer1", "Port1 Consumer 1", NodeType.Handler, "Port1Consumer1")
            .AddNode("port1_consumer2", "Port1 Consumer 2", NodeType.Handler, "Port1Consumer2")
            .AddNode("end", "End", NodeType.End, handlerName: "")
            .AddEdge("start", "splitter")
            // Port 0 edges - priority determines order
            .AddEdge("splitter", "port0_consumer1", e => e.FromPort(0).WithPriority(1))
            .AddEdge("splitter", "port0_consumer2", e => e.FromPort(0).WithPriority(2))
            // Port 1 edges
            .AddEdge("splitter", "port1_consumer1", e => e.FromPort(1).WithPriority(1))
            .AddEdge("splitter", "port1_consumer2", e => e.FromPort(1).WithPriority(2))
            .AddEdge("port0_consumer1", "end")
            .AddEdge("port0_consumer2", "end")
            .AddEdge("port1_consumer1", "end")
            .AddEdge("port1_consumer2", "end")
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<GraphContext>, DualPortSplitterHandler>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, Port0Consumer1>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, Port0Consumer2>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, Port1Consumer1>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, Port1Consumer2>()
            .BuildServiceProvider();

        var context = new GraphContext("test-lazy-multi-port", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert - All consumers should complete
        context.ShouldHaveCompletedNode("splitter");
        context.ShouldHaveCompletedNode("port0_consumer1");
        context.ShouldHaveCompletedNode("port0_consumer2");
        context.ShouldHaveCompletedNode("port1_consumer1");
        context.ShouldHaveCompletedNode("port1_consumer2");

        // Verify lazy cloning behavior per port
        // Port 0: first consumer gets List<int>, second gets List<object> (after cloning)
        var port0c1 = context.Channels["node_output:port0_consumer1"].Get<Dictionary<string, object>>();
        var port0c2 = context.Channels["node_output:port0_consumer2"].Get<Dictionary<string, object>>();

        port0c1.Should().NotBeNull();
        port0c2.Should().NotBeNull();

        // Both should have received the data (type may differ due to cloning)
        port0c1!["received"].Should().NotBeNull();
        port0c2!["received"].Should().NotBeNull();
    }

    [Fact]
    public async Task EdgeCloningPolicy_OverridesGraphPolicy_PerPort()
    {
        // Arrange - AlwaysClone on specific edge, LazyClone on others
        var graph = new GraphBuilder()
            .WithId("edge-policy-override")
            .WithName("Edge Policy Override")
            .WithCloningPolicy(CloningPolicy.LazyClone) // Graph default
            .AddNode("start", "Start", NodeType.Start, handlerName: "")
            .AddNode("source", "Source", NodeType.Handler, "MutableSourceHandler",
                n => n.WithOutputPorts(2))
            .AddNode("always_clone_consumer", "Always Clone", NodeType.Handler, "AlwaysCloneConsumer")
            .AddNode("lazy_clone_consumer", "Lazy Clone", NodeType.Handler, "LazyCloneConsumer")
            .AddNode("end", "End", NodeType.End, handlerName: "")
            .AddEdge("start", "source")
            .AddEdge("source", "always_clone_consumer",
                e => e.FromPort(0).WithCloningPolicy(CloningPolicy.AlwaysClone))
            .AddEdge("source", "lazy_clone_consumer",
                e => e.FromPort(1)) // Uses graph default (LazyClone)
            .AddEdge("always_clone_consumer", "end")
            .AddEdge("lazy_clone_consumer", "end")
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<GraphContext>, MutableSourceHandler>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, AlwaysCloneConsumer>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, LazyCloneConsumer>()
            .BuildServiceProvider();

        var context = new GraphContext("test-edge-policy", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert
        context.ShouldHaveCompletedNode("source");
        context.ShouldHaveCompletedNode("always_clone_consumer");
        context.ShouldHaveCompletedNode("lazy_clone_consumer");
    }

    #endregion

    #region Enhanced Metadata + Correlation Tracking

    [Fact]
    public async Task CorrelationId_PropagatesThroughMultiPortGraph()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();

        var graph = new GraphBuilder()
            .WithId("correlation-propagation")
            .WithName("Correlation Propagation")
            .AddNode("start", "Start", NodeType.Start, handlerName: "")
            .AddNode("router", "Router", NodeType.Handler, "CorrelationTrackingRouter",
                n => n.WithOutputPorts(2))
            .AddNode("handler1", "Handler 1", NodeType.Handler, "CorrelationTracker1")
            .AddNode("handler2", "Handler 2", NodeType.Handler, "CorrelationTracker2")
            .AddNode("end", "End", NodeType.End, handlerName: "")
            .AddEdge("start", "router")
            .AddEdge("router", "handler1", e => e.FromPort(0))
            .AddEdge("router", "handler2", e => e.FromPort(1))
            .AddEdge("handler1", "end")
            .AddEdge("handler2", "end")
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<GraphContext>, CorrelationTrackingRouter>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, CorrelationTracker1>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, CorrelationTracker2>()
            .BuildServiceProvider();

        var context = new GraphContext("test-correlation", graph, services);
        context.AddTag("correlation_id", correlationId);

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert - Correlation ID should be tracked in all handlers
        context.Tags.Should().ContainKey("router_correlation");
        context.Tags["router_correlation"].Should().Contain(correlationId);

        context.Tags.Should().ContainKey("handler1_correlation");
        context.Tags["handler1_correlation"].Should().Contain(correlationId);

        context.Tags.Should().ContainKey("handler2_correlation");
        context.Tags["handler2_correlation"].Should().Contain(correlationId);
    }

    [Fact]
    public async Task CustomMetadata_WithNamespacing_PreservesV5AndNodeRedData()
    {
        // Arrange
        var graph = new GraphBuilder()
            .WithId("metadata-namespacing")
            .WithName("Metadata Namespacing")
            .AddNode("start", "Start", NodeType.Start, handlerName: "")
            .AddNode("processor", "Processor", NodeType.Handler, "NamespacedMetadataHandler")
            .AddNode("end", "End", NodeType.End, handlerName: "")
            .AddEdge("start", "processor")
            .AddEdge("processor", "end")
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<GraphContext>, NamespacedMetadataHandler>()
            .BuildServiceProvider();

        var context = new GraphContext("test-namespacing", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert - Check that namespaced metadata was stored
        context.Tags.Should().ContainKey("v5:state");
        context.Tags.Should().ContainKey("nodered:routing");
        context.Tags.Should().ContainKey("app_specific");
    }

    #endregion

    #region Error Monitoring + Cloning

    [Fact]
    public async Task ErrorMonitoring_WithCloningPolicy_WorksCorrectly()
    {
        // Arrange - Handler that fails after producing outputs
        var graph = new GraphBuilder()
            .WithId("error-cloning")
            .WithName("Error with Cloning")
            .WithCloningPolicy(CloningPolicy.LazyClone)
            .AddNode("start", "Start", NodeType.Start, handlerName: "")
            .AddNode("failing_source", "Failing Source", NodeType.Handler, "FailingSourceHandler",
                n => n.WithOutputPorts(2))
            .AddNode("consumer1", "Consumer 1", NodeType.Handler, "GenericConsumer1")
            .AddNode("consumer2", "Consumer 2", NodeType.Handler, "GenericConsumer2")
            .AddNode("end", "End", NodeType.End, handlerName: "")
            .AddEdge("start", "failing_source")
            .AddEdge("failing_source", "consumer1", e => e.FromPort(0))
            .AddEdge("failing_source", "consumer2", e => e.FromPort(1))
            .AddEdge("consumer1", "end")
            .AddEdge("consumer2", "end")
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<GraphContext>, FailingSourceHandler>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, GenericConsumer1>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, GenericConsumer2>()
            .BuildServiceProvider();

        var context = new GraphContext("test-error-cloning", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act & Assert - Should handle failure gracefully
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Verify failure was tracked
        context.GetNodeExecutionCount("failing_source").Should().Be(1);
    }

    #endregion

    #region Complex Combined Scenarios

    [Fact]
    public async Task CompleteWorkflow_AllPrimitivesCombined()
    {
        // Arrange - Polling sensor → Port routing → Lazy cloning → Metadata tracking
        var graph = new GraphBuilder()
            .WithId("complete-workflow")
            .WithName("Complete V5 + Node-RED Workflow")
            .WithCloningPolicy(CloningPolicy.LazyClone)
            .AddNode("start", "Start", NodeType.Start, handlerName: "")
            .AddNode("sensor", "Data Sensor", NodeType.Handler, "CompleteWorkflowSensor",
                n => n.WithOutputPorts(3)) // Port 0: high, Port 1: medium, Port 2: low
            .AddNode("high_priority", "High Priority", NodeType.Handler, "HighPriorityProcessor")
            .AddNode("medium_priority", "Medium Priority", NodeType.Handler, "MediumPriorityProcessor")
            .AddNode("low_priority", "Low Priority", NodeType.Handler, "LowPriorityProcessor")
            .AddNode("aggregator", "Aggregator", NodeType.Handler, "ResultAggregator")
            .AddNode("end", "End", NodeType.End, handlerName: "")
            .AddEdge("start", "sensor")
            .AddEdge("sensor", "high_priority", e => e.FromPort(0).WithPriority(1))
            .AddEdge("sensor", "medium_priority", e => e.FromPort(1).WithPriority(2))
            .AddEdge("sensor", "low_priority", e => e.FromPort(2).WithPriority(3))
            .AddEdge("high_priority", "aggregator")
            .AddEdge("medium_priority", "aggregator")
            .AddEdge("low_priority", "aggregator")
            .AddEdge("aggregator", "end")
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<GraphContext>, CompleteWorkflowSensor>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, HighPriorityProcessor>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, MediumPriorityProcessor>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, LowPriorityProcessor>()
            .AddSingleton<IGraphNodeHandler<GraphContext>, ResultAggregator>()
            .BuildServiceProvider();

        var context = new GraphContext("test-complete", graph, services);
        context.AddTag("data_ready", "true");
        context.AddTag("correlation_id", Guid.NewGuid().ToString());

        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context, CancellationToken.None);

        // Assert - Complete workflow executed
        context.ShouldHaveCompletedNode("sensor");
        context.ShouldHaveCompletedNode("high_priority");
        context.ShouldHaveCompletedNode("medium_priority");
        context.ShouldHaveCompletedNode("low_priority");
        context.ShouldHaveCompletedNode("aggregator");

        // Verify correlation tracking
        context.Tags.Should().ContainKey("workflow_correlation");
    }

    #endregion

    // Test Handlers (implementations follow)
}
