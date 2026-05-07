using FluentAssertions;
using HPD.Events;
using HPD.Events.Core;
using HPDAgent.Graph.Abstractions.Checkpointing;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.Core.Checkpointing;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Hosting.Data;
using HPDAgent.Graph.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Tests.V21;

public sealed class WorkflowResumeRunnerTests
{
    [Fact]
    public async Task InProcessResumeRunner_Rejects_WhenGraphIsMissing()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var runner = new InProcessWorkflowResumeRunner(
            new ServiceCollection().BuildServiceProvider(),
            checkpointStore);

        var result = await runner.ResumeAsync(new WorkflowResumeRunnerRequest
        {
            Execution = CreateExecution(),
            Checkpoint = CreateSuspensionCheckpoint()
        });

        result.Status.Should().Be(ResumeSuspensionStatus.Rejected);
        result.Message.Should().Contain("graph definition");
        result.ExecutionContinued.Should().BeFalse();
    }

    [Fact]
    public async Task InProcessResumeRunner_Rejects_WhenCheckpointIsMissing()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var runner = new InProcessWorkflowResumeRunner(
            new ServiceCollection().BuildServiceProvider(),
            checkpointStore);

        var result = await runner.ResumeAsync(new WorkflowResumeRunnerRequest
        {
            Execution = CreateExecution(),
            Graph = CreateStoredGraph()
        });

        result.Status.Should().Be(ResumeSuspensionStatus.Rejected);
        result.Message.Should().Contain("checkpoint");
        result.ExecutionContinued.Should().BeFalse();
    }

    [Fact]
    public async Task InProcessResumeRunner_Rejects_WhenIdentitiesDoNotMatch()
    {
        var checkpointStore = new InMemoryCheckpointStore();
        var runner = new InProcessWorkflowResumeRunner(
            new ServiceCollection().BuildServiceProvider(),
            checkpointStore);

        var result = await runner.ResumeAsync(new WorkflowResumeRunnerRequest
        {
            Execution = CreateExecution() with { GraphId = "other-graph" },
            Graph = CreateStoredGraph(),
            Checkpoint = CreateSuspensionCheckpoint()
        });

        result.Status.Should().Be(ResumeSuspensionStatus.Rejected);
        result.Message.Should().Contain("identity");
        result.ExecutionContinued.Should().BeFalse();
    }

    [Fact]
    public async Task InProcessResumeRunner_ContinuesExecutionAndInjectsResumeValue()
    {
        var approvalHandler = new ResumeApprovalHandler();
        var afterHandler = new ResumeConsumerHandler();
        var services = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<GraphContext>>(approvalHandler)
            .AddSingleton<IGraphNodeHandler<GraphContext>>(afterHandler)
            .BuildServiceProvider();
        var checkpointStore = new InMemoryCheckpointStore();
        var checkpoint = CreateSuspensionCheckpoint();
        await checkpointStore.SaveCheckpointAsync(checkpoint);
        var runner = new InProcessWorkflowResumeRunner(services, checkpointStore);

        var result = await runner.ResumeAsync(new WorkflowResumeRunnerRequest
        {
            Execution = CreateExecution(),
            Graph = CreateStoredGraph(),
            Checkpoint = checkpoint,
            ResumeValue = "approved"
        });

        result.Status.Should().Be(ResumeSuspensionStatus.Accepted);
        result.ExecutionContinued.Should().BeTrue();
        approvalHandler.ResumeValue.Should().Be("approved");
        afterHandler.Decision.Should().Be("approved");
    }

    [Fact]
    public async Task InProcessResumeRunner_WithEventCoordinator_EmitsGraphLifecycleEvents()
    {
        var approvalHandler = new ResumeApprovalHandler();
        var afterHandler = new ResumeConsumerHandler();
        var eventCoordinator = new EventCoordinator();
        var services = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<GraphContext>>(approvalHandler)
            .AddSingleton<IGraphNodeHandler<GraphContext>>(afterHandler)
            .BuildServiceProvider();
        var checkpointStore = new InMemoryCheckpointStore();
        var checkpoint = CreateSuspensionCheckpoint();
        await checkpointStore.SaveCheckpointAsync(checkpoint);
        var runner = new InProcessWorkflowResumeRunner(
            services,
            checkpointStore,
            eventCoordinator: eventCoordinator);

        var result = await runner.ResumeAsync(new WorkflowResumeRunnerRequest
        {
            Execution = CreateExecution(),
            Graph = CreateStoredGraph(),
            Checkpoint = checkpoint,
            ResumeValue = "approved"
        });

        result.Status.Should().Be(ResumeSuspensionStatus.Accepted);
        result.ExecutionContinued.Should().BeTrue();
        var events = await CollectSynchronousEventsAsync(eventCoordinator, evt => evt is GraphExecutionCompletedEvent);
        events.Should().ContainSingle(evt => evt is GraphExecutionStartedEvent);
        events.Should().ContainSingle(evt => evt is GraphExecutionCompletedEvent);
        events.OfType<NodeExecutionStartedEvent>().Should().Contain(evt => evt.NodeId == "approval");
        events.OfType<NodeExecutionCompletedEvent>().Should().Contain(evt => evt.NodeId == "approval");
        events.OfType<NodeExecutionStartedEvent>().Should().Contain(evt => evt.NodeId == "after");
        events.OfType<NodeExecutionCompletedEvent>().Should().Contain(evt => evt.NodeId == "after");
    }

    private static WorkflowExecution CreateExecution(
        string graphId = "resume-graph",
        string executionId = "exec-1") => new()
    {
        GraphId = graphId,
        ExecutionId = executionId,
        Status = WorkflowExecutionStatus.Running,
        CreatedAt = DateTimeOffset.UnixEpoch
    };

    private static StoredGraph CreateStoredGraph(
        string graphId = "resume-graph",
        string executionId = "exec-1") => new()
    {
        GraphId = graphId,
        Name = "Resume Graph",
        GraphVersion = "1.0.0",
        Config = CreateConfig(graphId),
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
        Metadata = new Dictionary<string, string>
        {
            ["testExecutionId"] = executionId
        }
    };

    private static GraphConfig CreateConfig(string graphId = "resume-graph") => new()
    {
        GraphId = graphId,
        Name = "Resume Graph",
        Nodes = new Dictionary<string, NodeConfig>
        {
            ["approval"] = new()
            {
                Id = "approval",
                Name = "Approval",
                Type = NodeKindConfig.Handler,
                HandlerName = "resume_approval"
            },
            ["after"] = new()
            {
                Id = "after",
                Name = "After",
                Type = NodeKindConfig.Handler,
                HandlerName = "resume_consumer"
            }
        },
        Edges =
        [
            new EdgeConfig { From = "START", To = "approval" },
            new EdgeConfig { From = "approval", To = "after" },
            new EdgeConfig { From = "after", To = "END" }
        ]
    };

    private static GraphCheckpoint CreateSuspensionCheckpoint(
        string graphId = "resume-graph",
        string executionId = "exec-1",
        string nodeId = "approval",
        string token = "token-1") => new()
    {
        CheckpointId = "checkpoint-1",
        GraphId = graphId,
        ExecutionId = executionId,
        CreatedAt = DateTimeOffset.UnixEpoch,
        CompletedNodes = new HashSet<string> { "START" },
        NodeOutputs = new Dictionary<string, object>
        {
            ["node_output:START"] = new Dictionary<string, object>()
        },
        ContextJson = "{}",
        Metadata = new CheckpointMetadata
        {
            Trigger = CheckpointTrigger.Suspension,
            SuspendedNodeId = nodeId,
            SuspendToken = token,
            SuspensionOutcome = SuspensionOutcome.Pending
        }
    };

    private static async Task<List<Event>> CollectSynchronousEventsAsync(
        EventCoordinator coordinator,
        Func<Event, bool>? stopWhen = null)
    {
        var events = new List<Event>();
        using var cts = new CancellationTokenSource(500);

        try
        {
            await foreach (var evt in coordinator.ReadSynchronousAsync(cts.Token))
            {
                events.Add(evt);
                if (stopWhen?.Invoke(evt) == true)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }

        return events;
    }

    private sealed class ResumeApprovalHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "resume_approval";
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

    private sealed class ResumeConsumerHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "resume_consumer";
        public string? Decision { get; private set; }

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken = default)
        {
            Decision = inputs.Get<string>("approval.decision");

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Success.Single(
                    new Dictionary<string, object> { ["seen"] = Decision },
                    TimeSpan.Zero,
                    new NodeExecutionMetadata()));
        }
    }
}
