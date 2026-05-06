using FluentAssertions;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.Core.Builders;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using HPDAgent.Graph.Core.Storage;
using HPDAgent.Graph.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Tests.V21;

public sealed class GraphOrchestratorSuspensionHostingTests
{
    [Fact]
    public async Task ExecuteAsync_PublishesDurableSuspensionToWorkflowExecutionStore()
    {
        var executionStore = new InMemoryWorkflowExecutionStore();
        var logStore = new InMemoryWorkflowLogStore();
        await executionStore.SaveAsync(new WorkflowExecution
        {
            GraphId = "graph-a",
            ExecutionId = "exec-a",
            Status = WorkflowExecutionStatus.Running,
            CreatedAt = DateTimeOffset.UnixEpoch,
            StartedAt = DateTimeOffset.UnixEpoch
        });
        var executionManager = new ExecutionManager(executionStore, logStore);
        var services = new ServiceCollection()
            .AddSingleton<IWorkflowSuspensionSink>(executionManager)
            .AddSingleton<IGraphNodeHandler<GraphContext>>(new SuspendingHandler())
            .BuildServiceProvider();
        var graph = new GraphBuilder()
            .WithId("graph-a")
            .WithName("Suspension Bridge")
            .AddHandlerNode("approval", "Approval", "approval", node => node.WithImmediateSuspend())
            .Build();
        var context = new GraphContext("exec-a", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        var act = () => orchestrator.ExecuteAsync(context);

        await act.Should().ThrowAsync<GraphSuspendedException>()
            .Where(ex => ex.NodeId == "approval" && ex.SuspendToken == "token-a");

        var execution = await executionStore.LoadAsync("graph-a", "exec-a");
        execution.Should().NotBeNull();
        execution!.Status.Should().Be(WorkflowExecutionStatus.Suspended);
        execution.CurrentNodeId.Should().Be("approval");
        execution.SuspendedNodeId.Should().Be("approval");
        execution.SuspendToken.Should().Be("token-a");
        execution.SuspendReason.Should().Be(SuspendReason.HumanApproval);
        execution.SuspensionMessage.Should().Be("Awaiting approval");
        execution.SuspendedAt.Should().NotBeNull();

        var logs = await logStore.ListAsync("graph-a", "exec-a");
        logs.Should().ContainSingle(log =>
            log.Source == nameof(ExecutionManager) &&
            log.Level == LogLevel.Information &&
            log.Message == "Execution suspended at node 'approval' with token 'token-a'.");
    }

    [Fact]
    public async Task ExecuteAsync_PublishesFailedExecution_WhenPollingTimeoutStopsGraph()
    {
        var executionStore = new InMemoryWorkflowExecutionStore();
        var logStore = new InMemoryWorkflowLogStore();
        await executionStore.SaveAsync(CreateRunningExecution("graph-a", "exec-a"));
        var executionManager = new ExecutionManager(executionStore, logStore);
        var services = new ServiceCollection()
            .AddSingleton<IWorkflowExecutionStateSink>(executionManager)
            .AddSingleton<IGraphNodeHandler<GraphContext>>(new TimeoutPollingHandler())
            .BuildServiceProvider();
        var graph = new GraphBuilder()
            .WithId("graph-a")
            .WithName("Polling Timeout")
            .AddHandlerNode("sensor", "Sensor", "poll-timeout")
            .Build();
        var context = new GraphContext("exec-a", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        var act = () => orchestrator.ExecuteAsync(context);

        await act.Should().ThrowAsync<GraphExecutionException>()
            .WithMessage("*Sensor polling timeout*");

        var execution = await executionStore.LoadAsync("graph-a", "exec-a");
        execution.Should().NotBeNull();
        execution!.Status.Should().Be(WorkflowExecutionStatus.Failed);
        execution.CurrentNodeId.Should().Be("sensor");
        execution.SuspendedNodeId.Should().BeNull();
        execution.SuspendToken.Should().BeNull();
        execution.CompletedAt.Should().NotBeNull();
        execution.ErrorMessage.Should().Contain("Sensor polling timeout");

        var logs = await logStore.ListAsync("graph-a", "exec-a");
        logs.Should().ContainSingle(log =>
            log.Source == nameof(ExecutionManager) &&
            log.Level == LogLevel.Error &&
            log.Message.Contains("Execution failed at node 'sensor': Sensor polling timeout"));
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotMarkExecutionFailed_WhenPollingTimeoutIsIsolated()
    {
        var executionStore = new InMemoryWorkflowExecutionStore();
        await executionStore.SaveAsync(CreateRunningExecution("graph-a", "exec-a"));
        var executionManager = new ExecutionManager(executionStore);
        var services = new ServiceCollection()
            .AddSingleton<IWorkflowExecutionStateSink>(executionManager)
            .AddSingleton<IGraphNodeHandler<GraphContext>>(new TimeoutPollingHandler())
            .BuildServiceProvider();
        var graph = new GraphBuilder()
            .WithId("graph-a")
            .WithName("Isolated Polling Timeout")
            .AddHandlerNode(
                "sensor",
                "Sensor",
                "poll-timeout",
                node => node.WithErrorPolicy(ErrorPropagationPolicy.Isolate()))
            .Build();
        var context = new GraphContext("exec-a", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        await orchestrator.ExecuteAsync(context);

        var execution = await executionStore.LoadAsync("graph-a", "exec-a");
        execution.Should().NotBeNull();
        execution!.Status.Should().Be(WorkflowExecutionStatus.Running);
        execution.SuspendedNodeId.Should().BeNull();
        execution.SuspendToken.Should().BeNull();
        execution.CompletedAt.Should().BeNull();
        execution.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_PollingSuccessMarksNodeCompleteAndClearsPollingState()
    {
        var executionStore = new InMemoryWorkflowExecutionStore();
        await executionStore.SaveAsync(CreateRunningExecution("graph-a", "exec-a"));
        var executionManager = new ExecutionManager(executionStore);
        var handler = new EventuallyReadyPollingHandler();
        var services = new ServiceCollection()
            .AddSingleton<IWorkflowExecutionStateSink>(executionManager)
            .AddSingleton<IGraphNodeHandler<GraphContext>>(handler)
            .BuildServiceProvider();
        var graph = new GraphBuilder()
            .WithId("graph-a")
            .WithName("Polling Success")
            .AddHandlerNode("sensor", "Sensor", "poll-success")
            .Build();
        var context = new GraphContext("exec-a", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        await orchestrator.ExecuteAsync(context);

        handler.CallCount.Should().Be(2);
        context.IsNodeComplete("sensor").Should().BeTrue();
        context.Channels["node_output:sensor"].Get<Dictionary<string, object>>()["ready"].Should().Be(true);

        var execution = await executionStore.LoadAsync("graph-a", "exec-a");
        execution.Should().NotBeNull();
        execution!.Status.Should().Be(WorkflowExecutionStatus.Running);
        execution.SuspendedNodeId.Should().BeNull();
        execution.SuspendToken.Should().BeNull();
        execution.Suspensions.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_PublishesFailedExecution_WhenPollingMaxRetriesStopsGraph()
    {
        var executionStore = new InMemoryWorkflowExecutionStore();
        await executionStore.SaveAsync(CreateRunningExecution("graph-a", "exec-a"));
        var executionManager = new ExecutionManager(executionStore);
        var services = new ServiceCollection()
            .AddSingleton<IWorkflowExecutionStateSink>(executionManager)
            .AddSingleton<IGraphNodeHandler<GraphContext>>(new MaxRetriesPollingHandler())
            .BuildServiceProvider();
        var graph = new GraphBuilder()
            .WithId("graph-a")
            .WithName("Polling Max Retries")
            .AddHandlerNode("sensor", "Sensor", "poll-max-retries")
            .Build();
        var context = new GraphContext("exec-a", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        var act = () => orchestrator.ExecuteAsync(context);

        await act.Should().ThrowAsync<GraphExecutionException>()
            .WithMessage("*Max polling retries exceeded*");

        var execution = await executionStore.LoadAsync("graph-a", "exec-a");
        execution.Should().NotBeNull();
        execution!.Status.Should().Be(WorkflowExecutionStatus.Failed);
        execution.CurrentNodeId.Should().Be("sensor");
        execution.SuspendedNodeId.Should().BeNull();
        execution.SuspendToken.Should().BeNull();
        execution.CompletedAt.Should().NotBeNull();
        execution.ErrorMessage.Should().Contain("Max polling retries exceeded");
    }

    private static WorkflowExecution CreateRunningExecution(string graphId, string executionId) => new()
    {
        GraphId = graphId,
        ExecutionId = executionId,
        Status = WorkflowExecutionStatus.Running,
        CreatedAt = DateTimeOffset.UnixEpoch,
        StartedAt = DateTimeOffset.UnixEpoch
    };

    private sealed class SuspendingHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "approval";

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Suspended.ForHumanApproval(
                    "token-a",
                    message: "Awaiting approval"));
        }
    }

    private sealed class TimeoutPollingHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "poll-timeout";

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Suspended.ForPolling(
                    "timeout-token",
                    TimeSpan.FromMilliseconds(1),
                    maxWaitTime: TimeSpan.Zero,
                    maxRetries: 10,
                    message: "Still waiting"));
        }
    }

    private sealed class EventuallyReadyPollingHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "poll-success";
        public int CallCount { get; private set; }

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 1)
            {
                return Task.FromResult<NodeExecutionResult>(
                    NodeExecutionResult.Suspended.ForPolling(
                        "success-token",
                        TimeSpan.FromMilliseconds(1),
                        maxWaitTime: TimeSpan.FromSeconds(1),
                        maxRetries: 10,
                        message: "Not ready"));
            }

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Success.Single(
                    new Dictionary<string, object> { ["ready"] = true },
                    TimeSpan.Zero,
                    new NodeExecutionMetadata()));
        }
    }

    private sealed class MaxRetriesPollingHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "poll-max-retries";

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Suspended.ForPolling(
                    "max-retries-token",
                    TimeSpan.FromMilliseconds(1),
                    maxWaitTime: TimeSpan.FromSeconds(1),
                    maxRetries: 1,
                    message: "Still waiting"));
        }
    }
}
