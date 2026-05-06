using FluentAssertions;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.Core.Checkpointing;
using HPDAgent.Graph.Core.Config;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using HPDAgent.Graph.Core.Storage;
using HPDAgent.Graph.Hosting.Data;
using HPDAgent.Graph.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Tests.V21;

public sealed class WorkflowRestartResumeIntegrationTests
{
    [Fact]
    public async Task SuspendedExecution_CanResumeFromCheckpointAfterRuntimeRestart()
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var checkpointStore = new InMemoryCheckpointStore();
        var logStore = new InMemoryWorkflowLogStore();
        var storedGraph = CreateStoredGraph();
        await graphStore.SaveAsync(storedGraph);
        await executionStore.SaveAsync(new WorkflowExecution
        {
            GraphId = "restart-graph",
            ExecutionId = "exec-restart",
            Status = WorkflowExecutionStatus.Running,
            CreatedAt = DateTimeOffset.UnixEpoch,
            StartedAt = DateTimeOffset.UnixEpoch
        });

        var firstExecutionManager = new ExecutionManager(
            executionStore,
            logStore,
            checkpointStore: checkpointStore,
            graphStore: graphStore);
        var firstServices = new ServiceCollection()
            .AddSingleton<IWorkflowExecutionStateSink>(firstExecutionManager)
            .AddSingleton<IWorkflowSuspensionSink>(firstExecutionManager)
            .AddSingleton<IGraphNodeHandler<GraphContext>>(new InitialApprovalHandler())
            .BuildServiceProvider();
        var graph = new GraphConfigCompiler().Compile(storedGraph.Config);
        var context = new GraphContext("exec-restart", graph, firstServices);
        var orchestrator = new GraphOrchestrator<GraphContext>(
            firstServices,
            checkpointStore: checkpointStore);

        var act = () => orchestrator.ExecuteAsync(context);

        await act.Should().ThrowAsync<GraphSuspendedException>()
            .Where(ex => ex.NodeId == "approval" && ex.SuspendToken == "restart-token");

        var suspended = await executionStore.LoadAsync("restart-graph", "exec-restart");
        suspended.Should().NotBeNull();
        suspended!.Status.Should().Be(WorkflowExecutionStatus.Suspended);
        suspended.SuspendToken.Should().Be("restart-token");
        (await checkpointStore.LoadLatestCheckpointAsync("exec-restart")).Should().NotBeNull();

        var approvalHandler = new ResumingApprovalHandler();
        var finalHandler = new FinalHandler();
        var restartedServices = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<GraphContext>>(approvalHandler)
            .AddSingleton<IGraphNodeHandler<GraphContext>>(finalHandler)
            .BuildServiceProvider();
        var resumeRunner = new InProcessWorkflowResumeRunner(restartedServices, checkpointStore);
        var restartedExecutionManager = new ExecutionManager(
            executionStore,
            logStore,
            checkpointStore: checkpointStore,
            graphStore: graphStore,
            resumeRunner: resumeRunner);

        var result = await restartedExecutionManager.ResumeSuspendedNodeAsync(
            "restart-graph",
            "restart-token",
            new ResumeSuspensionRequest { ResumeValue = "approved-after-restart" });

        result.Status.Should().Be(ResumeSuspensionStatus.Accepted);
        result.Message.Should().Be("Execution continued in-process.");
        approvalHandler.ResumeValue.Should().Be("approved-after-restart");
        finalHandler.Decision.Should().Be("approved-after-restart");

        var resumed = await executionStore.LoadAsync("restart-graph", "exec-restart");
        resumed.Should().NotBeNull();
        resumed!.Status.Should().Be(WorkflowExecutionStatus.Running);
        resumed.CurrentNodeId.Should().Be("approval");
        resumed.SuspendToken.Should().BeNull();
        resumed.SuspendedNodeId.Should().BeNull();
    }

    [Fact]
    public async Task ExternalTaskSuspension_CanResumeFromCheckpointAfterCallback()
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var checkpointStore = new InMemoryCheckpointStore();
        var storedGraph = CreateStoredGraph();
        await graphStore.SaveAsync(storedGraph);
        await executionStore.SaveAsync(new WorkflowExecution
        {
            GraphId = "restart-graph",
            ExecutionId = "exec-external",
            Status = WorkflowExecutionStatus.Running,
            CreatedAt = DateTimeOffset.UnixEpoch,
            StartedAt = DateTimeOffset.UnixEpoch
        });

        var firstExecutionManager = new ExecutionManager(
            executionStore,
            checkpointStore: checkpointStore,
            graphStore: graphStore);
        var firstServices = new ServiceCollection()
            .AddSingleton<IWorkflowExecutionStateSink>(firstExecutionManager)
            .AddSingleton<IWorkflowSuspensionSink>(firstExecutionManager)
            .AddSingleton<IGraphNodeHandler<GraphContext>>(new InitialExternalTaskHandler())
            .BuildServiceProvider();
        var graph = new GraphConfigCompiler().Compile(storedGraph.Config);
        var context = new GraphContext("exec-external", graph, firstServices);
        var orchestrator = new GraphOrchestrator<GraphContext>(
            firstServices,
            checkpointStore: checkpointStore);

        var act = () => orchestrator.ExecuteAsync(context);

        await act.Should().ThrowAsync<GraphSuspendedException>()
            .Where(ex => ex.NodeId == "approval" && ex.SuspendToken == "external-token");

        var suspended = await executionStore.LoadAsync("restart-graph", "exec-external");
        suspended.Should().NotBeNull();
        suspended!.SuspendReason.Should().Be(SuspendReason.ExternalTaskWait);

        var externalHandler = new ResumingExternalTaskHandler();
        var finalHandler = new FinalHandler();
        var restartedServices = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<GraphContext>>(externalHandler)
            .AddSingleton<IGraphNodeHandler<GraphContext>>(finalHandler)
            .BuildServiceProvider();
        var resumeRunner = new InProcessWorkflowResumeRunner(restartedServices, checkpointStore);
        var restartedExecutionManager = new ExecutionManager(
            executionStore,
            checkpointStore: checkpointStore,
            graphStore: graphStore,
            resumeRunner: resumeRunner);

        var result = await restartedExecutionManager.ResumeSuspendedNodeAsync(
            "restart-graph",
            "external-token",
            new ResumeSuspensionRequest { ResumeValue = "webhook-complete" });

        result.Status.Should().Be(ResumeSuspensionStatus.Accepted);
        externalHandler.ResumeValue.Should().Be("webhook-complete");
        finalHandler.Decision.Should().Be("webhook-complete");
    }

    private static StoredGraph CreateStoredGraph() => new()
    {
        GraphId = "restart-graph",
        Name = "Restart Graph",
        GraphVersion = "1.0.0",
        Config = new GraphConfig
        {
            GraphId = "restart-graph",
            Name = "Restart Graph",
            Nodes = new Dictionary<string, NodeConfig>
            {
                ["approval"] = new()
                {
                    Id = "approval",
                    Name = "Approval",
                    Type = NodeKindConfig.Handler,
                    HandlerName = "restart_approval",
                    SuspensionOptions = new SuspensionOptionsConfig
                    {
                        ActiveWaitTimeout = TimeSpan.Zero,
                        EmitEvents = false,
                        SaveCheckpointFirst = true
                    }
                },
                ["final"] = new()
                {
                    Id = "final",
                    Name = "Final",
                    Type = NodeKindConfig.Handler,
                    HandlerName = "restart_final"
                }
            },
            Edges =
            [
                new EdgeConfig { From = "START", To = "approval" },
                new EdgeConfig { From = "approval", To = "final" },
                new EdgeConfig { From = "final", To = "END" }
            ]
        },
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch
    };

    private sealed class InitialApprovalHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "restart_approval";

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Suspended.ForHumanApproval(
                    "restart-token",
                    message: "Waiting for restart approval"));
        }
    }

    private sealed class InitialExternalTaskHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "restart_approval";

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Suspended.ForExternalTask(
                    "external-token",
                    maxWaitTime: TimeSpan.FromHours(1),
                    message: "Waiting for webhook"));
        }
    }

    private sealed class ResumingApprovalHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "restart_approval";
        public string? ResumeValue { get; private set; }

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            ResumeValue = context.Channels["suspend_response:approval"].Get<string>();

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Success.Single(
                    new Dictionary<string, object> { ["decision"] = ResumeValue },
                    TimeSpan.Zero,
                    new NodeExecutionMetadata()));
        }
    }

    private sealed class FinalHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "restart_final";
        public string? Decision { get; private set; }

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            Decision = inputs.Get<string>("approval.decision");

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Success.Single(
                    new Dictionary<string, object> { ["decision"] = Decision },
                    TimeSpan.Zero,
                    new NodeExecutionMetadata()));
        }
    }

    private sealed class ResumingExternalTaskHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "restart_approval";
        public string? ResumeValue { get; private set; }

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            ResumeValue = context.Channels["suspend_response:approval"].Get<string>();

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Success.Single(
                    new Dictionary<string, object> { ["decision"] = ResumeValue },
                    TimeSpan.Zero,
                    new NodeExecutionMetadata()));
        }
    }
}
