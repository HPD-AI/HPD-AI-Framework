using FluentAssertions;
using System.Text.Json;
using HPD.Events;
using HPD.Events.Core;
using HPDAgent.Graph.Abstractions.Checkpointing;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.Core.Checkpointing;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Storage;
using HPDAgent.Graph.Hosting.Data;
using HPDAgent.Graph.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Tests.V21;

public sealed class HostingManagerTests
{
    [Fact]
    public async Task GraphManager_CreateGetListUpdateAndDeleteDefinitions()
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var manager = new GraphManager(graphStore, executionStore);

        var created = await manager.CreateDefinitionAsync(CreateConfig("graph-a", "Original"));

        created.GraphId.Should().Be("graph-a");
        created.Name.Should().Be("Original");
        (await manager.GetDefinitionAsync("graph-a")).Should().NotBeNull();
        (await manager.ListDefinitionsAsync()).Should().ContainSingle(summary => summary.GraphId == "graph-a");

        var updated = await manager.UpdateDefinitionAsync("graph-a", CreateConfig("ignored", "Updated"));

        updated.GraphId.Should().Be("graph-a");
        updated.Name.Should().Be("Updated");
        updated.Config.GraphId.Should().Be("graph-a");

        await manager.DeleteDefinitionAsync("graph-a");

        (await manager.GetDefinitionAsync("graph-a")).Should().BeNull();
    }

    [Fact]
    public async Task GraphManager_CreateExecution_PersistsWorkflowExecutionStatus()
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var logStore = new InMemoryWorkflowLogStore();
        var graphManager = new GraphManager(graphStore, executionStore, logStore);
        var executionManager = new ExecutionManager(executionStore);
        await graphManager.CreateDefinitionAsync(CreateConfig("graph-a", "Workflow"));

        var execution = await graphManager.CreateExecutionAsync(
            "graph-a",
            new ExecuteWorkflowRequest { ExecutionId = "exec-a" });

        execution.ExecutionId.Should().Be("exec-a");
        execution.Status.Should().Be(WorkflowExecutionStatus.Running);

        var status = await executionManager.GetStatusAsync("graph-a", "exec-a");

        status.Should().NotBeNull();
        status!.Status.Should().Be(WorkflowExecutionStatus.Running);
        status.StartedAt.Should().NotBeNull();

        var logs = await logStore.ListAsync("graph-a", "exec-a");
        logs.Should().ContainSingle(log =>
            log.Source == nameof(GraphManager) &&
            log.Level == LogLevel.Information &&
            log.Message == "Execution started.");
    }

    [Fact]
    public async Task WorkflowExecutionRunner_StartAsync_ForegroundRunsGraphAndMarksCompleted()
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var logStore = new InMemoryWorkflowLogStore();
        var graphManager = new GraphManager(graphStore, executionStore, logStore);
        var executionManager = new ExecutionManager(executionStore, logStore);
        var handler = new RecordingInputHandler();
        using var services = new ServiceCollection()
            .AddSingleton<IWorkflowExecutionStateSink>(executionManager)
            .AddSingleton<IGraphNodeHandler<GraphContext>>(handler)
            .BuildServiceProvider();
        var runner = new InProcessWorkflowExecutionRunner(
            services,
            graphStore,
            executionStore,
            graphManager,
            executionManager,
            logStore);
        await graphManager.CreateDefinitionAsync(CreateConfig("graph-a", "Workflow"));

        var execution = await runner.StartAsync(
            "graph-a",
            new ExecuteWorkflowRequest
            {
                ExecutionId = "exec-a",
                Mode = WorkflowExecutionMode.Foreground,
                Input = ParseJsonElement("""{"message":"hello"}""")
            });

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        execution.CompletedAt.Should().NotBeNull();
        handler.Messages.Should().ContainSingle("hello");

        var stored = await executionStore.LoadAsync("graph-a", "exec-a");
        stored!.Status.Should().Be(WorkflowExecutionStatus.Completed);
        stored.ClaimedBy.Should().BeNull();
        stored.AttemptCount.Should().Be(1);

        var logs = await logStore.ListAsync("graph-a", "exec-a");
        logs.Should().Contain(log =>
            log.Source == nameof(ExecutionManager) &&
            log.Message == "Execution completed.");
    }

    [Fact]
    public async Task WorkflowExecutionRunner_StartAsync_WithEventCoordinator_EmitsGraphLifecycleEvents()
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var graphManager = new GraphManager(graphStore, executionStore);
        var executionManager = new ExecutionManager(executionStore);
        var eventCoordinator = new EventCoordinator();
        using var services = new ServiceCollection()
            .AddSingleton<IWorkflowExecutionStateSink>(executionManager)
            .AddSingleton<IGraphNodeHandler<GraphContext>>(new RecordingInputHandler())
            .BuildServiceProvider();
        var runner = new InProcessWorkflowExecutionRunner(
            services,
            graphStore,
            executionStore,
            graphManager,
            executionManager,
            eventCoordinator: eventCoordinator);
        await graphManager.CreateDefinitionAsync(CreateConfig("graph-a", "Workflow"));
        await using var eventSubscription = eventCoordinator.SubscribeChannel(EventChannel.Synchronous);

        var execution = await runner.StartAsync(
            "graph-a",
            new ExecuteWorkflowRequest
            {
                ExecutionId = "exec-a",
                Mode = WorkflowExecutionMode.Foreground,
                Input = ParseJsonElement("""{"message":"hello"}""")
            });

        execution.Status.Should().Be(WorkflowExecutionStatus.Completed);
        var events = await CollectSynchronousEventsAsync(eventSubscription.Reader, evt => evt is GraphExecutionCompletedEvent);
        events.Should().ContainSingle(evt => evt is GraphExecutionStartedEvent);
        events.Should().ContainSingle(evt => evt is GraphExecutionCompletedEvent);
        events.Should().Contain(evt => evt is NodeExecutionStartedEvent);
        events.Should().Contain(evt => evt is NodeExecutionCompletedEvent);
    }

    [Fact]
    public async Task WorkflowExecutionRunner_RunQueuedAsync_StartsOnlyWhenGraphHasNoActiveExecution()
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var graphManager = new GraphManager(graphStore, executionStore);
        var executionManager = new ExecutionManager(executionStore);
        using var services = new ServiceCollection()
            .AddSingleton<IWorkflowExecutionStateSink>(executionManager)
            .AddSingleton<IGraphNodeHandler<GraphContext>>(new RecordingInputHandler())
            .BuildServiceProvider();
        var runner = new InProcessWorkflowExecutionRunner(
            services,
            graphStore,
            executionStore,
            graphManager,
            executionManager);
        await graphManager.CreateDefinitionAsync(CreateConfig("graph-a", "Workflow"));
        await executionStore.SaveAsync(CreateExecution("graph-a", "active", WorkflowExecutionStatus.Running));
        await executionStore.SaveAsync(CreateExecution("graph-a", "queued", WorkflowExecutionStatus.Created) with
        {
            Input = ParseJsonElement("""{"message":"queued"}""")
        });

        var startedWithActive = await runner.RunQueuedAsync();

        startedWithActive.Should().Be(0);
        (await executionStore.LoadAsync("graph-a", "queued"))!.Status.Should().Be(WorkflowExecutionStatus.Created);

        await executionStore.SaveAsync(CreateExecution("graph-a", "active", WorkflowExecutionStatus.Completed));

        var startedAfterCompletion = await runner.RunQueuedAsync();

        startedAfterCompletion.Should().Be(1);
        await EventuallyAsync(async () =>
        {
            var queued = await executionStore.LoadAsync("graph-a", "queued");
            queued!.Status.Should().Be(WorkflowExecutionStatus.Completed);
        });
    }

    [Fact]
    public async Task WorkflowExecutionRunner_RequeueInterruptedAsync_RequeuesOnlyExpiredRunningLeases()
    {
        var now = DateTimeOffset.UnixEpoch.AddMinutes(10);
        var timeProvider = new ManualTimeProvider(now);
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var graphManager = new GraphManager(graphStore, executionStore, timeProvider: timeProvider);
        var executionManager = new ExecutionManager(executionStore, timeProvider: timeProvider);
        using var services = new ServiceCollection()
            .AddSingleton<IWorkflowExecutionStateSink>(executionManager)
            .BuildServiceProvider();
        var runner = new InProcessWorkflowExecutionRunner(
            services,
            graphStore,
            executionStore,
            graphManager,
            executionManager,
            timeProvider: timeProvider,
            workerId: "worker-b");
        await graphManager.CreateDefinitionAsync(CreateConfig("graph-a", "Workflow"));
        await executionStore.SaveAsync(CreateExecution("graph-a", "healthy", WorkflowExecutionStatus.Running) with
        {
            ClaimedBy = "worker-a",
            LeaseUntil = now.AddSeconds(30)
        });
        await executionStore.SaveAsync(CreateExecution("graph-a", "expired", WorkflowExecutionStatus.Running) with
        {
            ClaimedBy = "worker-a",
            LeaseUntil = now.AddSeconds(-1)
        });

        var requeued = await runner.RequeueInterruptedAsync();

        requeued.Should().Be(1);
        (await executionStore.LoadAsync("graph-a", "healthy"))!.Status.Should().Be(WorkflowExecutionStatus.Running);
        var expired = await executionStore.LoadAsync("graph-a", "expired");
        expired!.Status.Should().Be(WorkflowExecutionStatus.Created);
        expired.ClaimedBy.Should().BeNull();
    }

    [Fact]
    public async Task GraphManager_CreateExecution_RejectsMissingGraph()
    {
        var manager = new GraphManager(
            new InMemoryGraphDefinitionStore(),
            new InMemoryWorkflowExecutionStore());

        var act = () => manager.CreateExecutionAsync("missing", new ExecuteWorkflowRequest());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Graph definition 'missing' was not found*");
    }

    [Fact]
    public async Task SchedulingManager_CreateGetListUpdateAndDeleteSchedule()
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var scheduleStore = new InMemoryScheduledGraphStore();
        var graphManager = new GraphManager(graphStore, executionStore);
        var executionManager = new ExecutionManager(executionStore);
        var provider = new InProcessCronScheduleProvider(scheduleStore, executionStore, graphManager, executionManager);
        var manager = new SchedulingManager(scheduleStore, graphManager, provider);
        await graphManager.CreateDefinitionAsync(CreateConfig("graph-a", "Workflow"));

        var created = await manager.CreateScheduleAsync(
            "graph-a",
            new CreateScheduleRequest
            {
                Schedule = CreateSchedule("0 3 * * *"),
                Enabled = true
            });

        created.GraphId.Should().Be("graph-a");
        created.Enabled.Should().BeTrue();
        created.NextRunAt.Should().NotBeNull();

        var loaded = await manager.GetScheduleAsync("graph-a");
        loaded!.Schedule.CronExpression.Should().Be("0 3 * * *");

        var list = await manager.ListSchedulesAsync();
        list.Should().ContainSingle(schedule => schedule.GraphId == "graph-a");

        var updated = await manager.UpdateScheduleAsync(
            "graph-a",
            new UpdateScheduleRequest
            {
                Schedule = CreateSchedule("0 4 * * *"),
                Enabled = false
            });

        updated.Schedule.CronExpression.Should().Be("0 4 * * *");
        updated.Enabled.Should().BeFalse();
        updated.NextRunAt.Should().BeNull();

        await manager.DeleteScheduleAsync("graph-a");

        (await manager.GetScheduleAsync("graph-a")).Should().BeNull();
    }

    [Fact]
    public async Task SchedulingManager_Trigger_CreatesExecutionAndUpdatesSchedule()
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var scheduleStore = new InMemoryScheduledGraphStore();
        var graphManager = new GraphManager(graphStore, executionStore);
        var executionManager = new ExecutionManager(executionStore);
        var provider = new InProcessCronScheduleProvider(scheduleStore, executionStore, graphManager, executionManager);
        var manager = new SchedulingManager(scheduleStore, graphManager, provider);
        await graphManager.CreateDefinitionAsync(CreateConfig("graph-a", "Workflow"));
        await manager.CreateScheduleAsync(
            "graph-a",
            new CreateScheduleRequest
            {
                Schedule = CreateSchedule("0 3 * * *") with
                {
                    ConcurrencyPolicy = ScheduleConcurrencyPolicyConfig.AllowOverlap
                }
            });

        var result = await manager.TriggerAsync("graph-a");

        result.Status.Should().Be(ScheduleTriggerStatus.Started);
        result.ExecutionId.Should().NotBeNullOrWhiteSpace();

        var executions = await executionStore.ListAsync("graph-a");
        executions.Should().ContainSingle(execution =>
            execution.ExecutionId == result.ExecutionId &&
            execution.Status == WorkflowExecutionStatus.Running);

        var schedule = await scheduleStore.LoadAsync("graph-a");
        schedule!.LastRunAt.Should().NotBeNull();
        schedule.NextRunAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SchedulingManager_Trigger_PreservesDefaultInputAndTimeoutOnExecution()
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var scheduleStore = new InMemoryScheduledGraphStore();
        var graphManager = new GraphManager(graphStore, executionStore);
        var executionManager = new ExecutionManager(executionStore);
        var provider = new InProcessCronScheduleProvider(scheduleStore, executionStore, graphManager, executionManager);
        var manager = new SchedulingManager(scheduleStore, graphManager, provider);
        var input = JsonDocument.Parse("""{"source":"schedule"}""").RootElement.Clone();
        await graphManager.CreateDefinitionAsync(CreateConfig("graph-a", "Workflow"));
        await manager.CreateScheduleAsync(
            "graph-a",
            new CreateScheduleRequest
            {
                Schedule = CreateSchedule("0 3 * * *") with
                {
                    DefaultInput = input,
                    Timeout = TimeSpan.FromMinutes(30)
                }
            });

        var result = await manager.TriggerAsync("graph-a");

        var execution = (await executionStore.ListAsync("graph-a")).Single(execution =>
            execution.ExecutionId == result.ExecutionId);
        execution.Input!.Value.GetProperty("source").GetString().Should().Be("schedule");
        execution.Timeout.Should().Be(TimeSpan.FromMinutes(30));
        execution.DeadlineAt.Should().NotBeNull();
        execution.TriggeredBy.Should().Be("schedule:in-process-cronos");
    }

    [Fact]
    public async Task SchedulingManager_Trigger_ReturnsFailedAndSchedulesRetry_WhenGraphIsMissing()
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var scheduleStore = new InMemoryScheduledGraphStore();
        var graphManager = new GraphManager(graphStore, executionStore);
        var executionManager = new ExecutionManager(executionStore);
        var provider = new InProcessCronScheduleProvider(scheduleStore, executionStore, graphManager, executionManager);
        var manager = new SchedulingManager(scheduleStore, graphManager, provider);
        await scheduleStore.SaveAsync(CreateScheduledGraph(
            "missing",
            enabled: true,
            nextRunAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cronExpression: "* * * * *") with
            {
                Schedule = CreateSchedule("* * * * *") with
                {
                    MaxRetries = 3,
                    RetryAfter = TimeSpan.FromMinutes(5)
                }
            });

        var result = await manager.TriggerAsync("missing");

        result.Status.Should().Be(ScheduleTriggerStatus.Failed);
        result.Message.Should().Contain("Graph definition 'missing' was not found");

        var schedule = await scheduleStore.LoadAsync("missing");
        schedule!.NextRunAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(4));
    }

    [Theory]
    [InlineData(ScheduleConcurrencyPolicyConfig.SkipIfRunning, ScheduleTriggerStatus.Skipped, 1)]
    [InlineData(ScheduleConcurrencyPolicyConfig.Queue, ScheduleTriggerStatus.Queued, 2)]
    [InlineData(ScheduleConcurrencyPolicyConfig.CancelPrevious, ScheduleTriggerStatus.Started, 2)]
    public async Task SchedulingManager_Trigger_AppliesConcurrencyPolicy(
        ScheduleConcurrencyPolicyConfig concurrencyPolicy,
        ScheduleTriggerStatus expectedStatus,
        int expectedExecutionCount)
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var scheduleStore = new InMemoryScheduledGraphStore();
        var graphManager = new GraphManager(graphStore, executionStore);
        var executionManager = new ExecutionManager(executionStore);
        var provider = new InProcessCronScheduleProvider(scheduleStore, executionStore, graphManager, executionManager);
        var manager = new SchedulingManager(scheduleStore, graphManager, provider);
        await graphManager.CreateDefinitionAsync(CreateConfig("graph-a", "Workflow"));
        await executionStore.SaveAsync(CreateExecution("graph-a", "active", WorkflowExecutionStatus.Running));
        await manager.CreateScheduleAsync(
            "graph-a",
            new CreateScheduleRequest
            {
                Schedule = CreateSchedule("0 3 * * *") with
                {
                    ConcurrencyPolicy = concurrencyPolicy
                }
            });

        var result = await manager.TriggerAsync("graph-a");

        result.Status.Should().Be(expectedStatus);

        var executions = await executionStore.ListAsync("graph-a");
        executions.Should().HaveCount(expectedExecutionCount);

        if (concurrencyPolicy == ScheduleConcurrencyPolicyConfig.Queue)
        {
            executions.Should().Contain(execution => execution.Status == WorkflowExecutionStatus.Created);
        }

        if (concurrencyPolicy == ScheduleConcurrencyPolicyConfig.CancelPrevious)
        {
            executions.Should().Contain(execution =>
                execution.ExecutionId == "active" &&
                execution.Status == WorkflowExecutionStatus.Cancelled);
            executions.Should().Contain(execution => execution.Status == WorkflowExecutionStatus.Running);
        }
    }

    [Fact]
    public async Task SchedulingManager_RunDueSchedules_TriggersOnlyDueEnabledSchedules()
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var scheduleStore = new InMemoryScheduledGraphStore();
        var graphManager = new GraphManager(graphStore, executionStore);
        var executionManager = new ExecutionManager(executionStore);
        var provider = new InProcessCronScheduleProvider(scheduleStore, executionStore, graphManager, executionManager);
        var manager = new SchedulingManager(scheduleStore, graphManager, provider);
        await graphManager.CreateDefinitionAsync(CreateConfig("due", "Due Workflow"));
        await graphManager.CreateDefinitionAsync(CreateConfig("future", "Future Workflow"));
        await graphManager.CreateDefinitionAsync(CreateConfig("disabled", "Disabled Workflow"));

        var now = DateTimeOffset.UtcNow;
        await scheduleStore.SaveAsync(CreateScheduledGraph("due", enabled: true, nextRunAt: now.AddMinutes(-1)));
        await scheduleStore.SaveAsync(CreateScheduledGraph("future", enabled: true, nextRunAt: now.AddHours(1)));
        await scheduleStore.SaveAsync(CreateScheduledGraph("disabled", enabled: false, nextRunAt: now.AddMinutes(-1)));

        var results = await manager.RunDueSchedulesAsync();

        results.Should().ContainSingle(result =>
            result.GraphId == "due" &&
            result.Status == ScheduleTriggerStatus.Started);
        (await executionStore.ListAsync("due")).Should().ContainSingle(execution =>
            execution.Status == WorkflowExecutionStatus.Running);
        (await executionStore.ListAsync("future")).Should().BeEmpty();
        (await executionStore.ListAsync("disabled")).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ScheduleMisfirePolicyConfig.Skip, ScheduleTriggerStatus.Skipped, 0)]
    [InlineData(ScheduleMisfirePolicyConfig.RunOnce, ScheduleTriggerStatus.Started, 1)]
    public async Task SchedulingManager_RunDueSchedules_AppliesSingleOccurrenceMisfirePolicy(
        ScheduleMisfirePolicyConfig misfirePolicy,
        ScheduleTriggerStatus expectedStatus,
        int expectedExecutionCount)
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var scheduleStore = new InMemoryScheduledGraphStore();
        var graphManager = new GraphManager(graphStore, executionStore);
        var executionManager = new ExecutionManager(executionStore);
        var provider = new InProcessCronScheduleProvider(scheduleStore, executionStore, graphManager, executionManager);
        var manager = new SchedulingManager(scheduleStore, graphManager, provider);
        await graphManager.CreateDefinitionAsync(CreateConfig("graph-a", "Workflow"));
        await scheduleStore.SaveAsync(CreateScheduledGraph(
            "graph-a",
            enabled: true,
            nextRunAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            misfirePolicy: misfirePolicy));

        var results = await manager.RunDueSchedulesAsync();

        results.Should().ContainSingle(result => result.Status == expectedStatus);
        (await executionStore.ListAsync("graph-a")).Should().HaveCount(expectedExecutionCount);
        (await scheduleStore.LoadAsync("graph-a"))!.NextRunAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SchedulingManager_RunDueSchedules_RunAllMissed_BackfillsMissedOccurrences()
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var scheduleStore = new InMemoryScheduledGraphStore();
        var graphManager = new GraphManager(graphStore, executionStore);
        var executionManager = new ExecutionManager(executionStore);
        var provider = new InProcessCronScheduleProvider(scheduleStore, executionStore, graphManager, executionManager);
        var manager = new SchedulingManager(scheduleStore, graphManager, provider);
        await graphManager.CreateDefinitionAsync(CreateConfig("graph-a", "Workflow"));
        await scheduleStore.SaveAsync(CreateScheduledGraph(
            "graph-a",
            enabled: true,
            nextRunAt: DateTimeOffset.UtcNow.AddMinutes(-3),
            misfirePolicy: ScheduleMisfirePolicyConfig.RunAllMissed,
            cronExpression: "* * * * *"));

        var results = await manager.RunDueSchedulesAsync();

        results.Should().HaveCountGreaterThanOrEqualTo(2);
        results.Should().OnlyContain(result => result.Status == ScheduleTriggerStatus.Started);
        (await executionStore.ListAsync("graph-a")).Should().HaveCount(results.Count);
    }

    [Fact]
    public async Task ExecutionManager_CancelAsync_MarksExecutionCancelled()
    {
        var store = new InMemoryWorkflowExecutionStore();
        var logStore = new InMemoryWorkflowLogStore();
        await store.SaveAsync(CreateExecution("graph-a", "exec-a", WorkflowExecutionStatus.Running));
        var manager = new ExecutionManager(store, logStore);

        await manager.CancelAsync("graph-a", "exec-a");

        var status = await manager.GetStatusAsync("graph-a", "exec-a");
        status!.Status.Should().Be(WorkflowExecutionStatus.Cancelled);
        status.CompletedAt.Should().NotBeNull();

        var logs = await logStore.ListAsync("graph-a", "exec-a");
        logs.Should().ContainSingle(log =>
            log.Source == nameof(ExecutionManager) &&
            log.Level == LogLevel.Warning &&
            log.Message == "Execution cancelled.");
    }

    [Fact]
    public async Task ExecutionManager_GetSuspendedNodes_ReturnsActiveSuspension()
    {
        var store = new InMemoryWorkflowExecutionStore();
        var logStore = new InMemoryWorkflowLogStore();
        await store.SaveAsync(CreateExecution(
            "graph-a",
            "exec-a",
            WorkflowExecutionStatus.Suspended,
            suspendedNodeId: "approval",
            suspendToken: "token-a",
            reason: SuspendReason.HumanApproval,
            message: "Awaiting approval"));
        var manager = new ExecutionManager(store, logStore);

        var suspended = await manager.GetSuspendedNodesAsync("graph-a", "exec-a");

        suspended.Should().ContainSingle(node =>
            node.NodeId == "approval" &&
            node.SuspendToken == "token-a" &&
            node.Reason == SuspendReason.HumanApproval &&
            node.Message == "Awaiting approval" &&
            node.Status == WorkflowExecutionStatus.Suspended);
    }

    [Fact]
    public async Task ExecutionManager_GetPollingStatus_ReturnsMatchingPollingExecution()
    {
        var store = new InMemoryWorkflowExecutionStore();
        await store.SaveAsync(CreateExecution(
            "graph-a",
            "exec-a",
            WorkflowExecutionStatus.Polling,
            suspendedNodeId: "sensor",
            suspendToken: "poll-a",
            reason: SuspendReason.PollingCondition,
            retryAfter: TimeSpan.FromSeconds(5),
            maxWaitTime: TimeSpan.FromMinutes(1),
            pollingAttemptNumber: 2));
        var manager = new ExecutionManager(store);

        var status = await manager.GetPollingStatusAsync("graph-a", "poll-a");

        status.Should().NotBeNull();
        status!.ExecutionId.Should().Be("exec-a");
        status.NodeId.Should().Be("sensor");
        status.Status.Should().Be(WorkflowExecutionStatus.Polling);
        status.AttemptNumber.Should().Be(2);
        status.RetryAfter.Should().Be(TimeSpan.FromSeconds(5));
        status.MaxWaitTime.Should().Be(TimeSpan.FromMinutes(1));
        status.ElapsedTime.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecutionManager_ResumeSuspendedNode_MarksExecutionRunning()
    {
        var store = new InMemoryWorkflowExecutionStore();
        var logStore = new InMemoryWorkflowLogStore();
        await store.SaveAsync(CreateExecution(
            "graph-a",
            "exec-a",
            WorkflowExecutionStatus.Suspended,
            suspendedNodeId: "approval",
            suspendToken: "resume-a"));
        var manager = new ExecutionManager(store, logStore);

        var result = await manager.ResumeSuspendedNodeAsync(
            "graph-a",
            "resume-a",
            new ResumeSuspensionRequest { ResumeValue = true });

        result.ExecutionId.Should().Be("exec-a");
        result.NodeId.Should().Be("approval");
        result.Status.Should().Be(ResumeSuspensionStatus.Accepted);

        var status = await manager.GetStatusAsync("graph-a", "exec-a");
        status!.Status.Should().Be(WorkflowExecutionStatus.Running);
        status.CurrentNodeId.Should().Be("approval");
        status.SuspendToken.Should().BeNull();
        status.SuspendedNodeId.Should().BeNull();
        status.SuspendReason.Should().BeNull();

        var logs = await logStore.ListAsync("graph-a", "exec-a");
        logs.Should().ContainSingle(log =>
            log.Source == nameof(ExecutionManager) &&
            log.Level == LogLevel.Information &&
            log.Message == "Suspension 'resume-a' resumed.");
    }

    [Fact]
    public async Task ExecutionManager_ResumeSuspendedNode_ReturnsNotFoundStatusForMissingToken()
    {
        var manager = new ExecutionManager(new InMemoryWorkflowExecutionStore());

        var result = await manager.ResumeSuspendedNodeAsync(
            "graph-a",
            "missing-token",
            new ResumeSuspensionRequest());

        result.Status.Should().Be(ResumeSuspensionStatus.NotFound);
        result.Message.Should().Contain("missing-token");
    }

    [Fact]
    public async Task ExecutionManager_MarkSuspendedAsync_IndexesPollingMetadata()
    {
        var store = new InMemoryWorkflowExecutionStore();
        var logStore = new InMemoryWorkflowLogStore();
        await store.SaveAsync(CreateExecution("graph-a", "exec-a", WorkflowExecutionStatus.Running));
        var manager = new ExecutionManager(store, logStore);

        await manager.MarkSuspendedAsync(
            "graph-a",
            "exec-a",
            "sensor",
            "poll-token",
            SuspendReason.PollingCondition,
            message: "Waiting for file",
            retryAfter: TimeSpan.FromSeconds(10),
            maxWaitTime: TimeSpan.FromMinutes(5),
            maxRetries: 30,
            pollingAttemptNumber: 1);

        var suspended = await manager.GetSuspendedNodesAsync("graph-a", "exec-a");
        suspended.Should().ContainSingle(node =>
            node.NodeId == "sensor" &&
            node.SuspendToken == "poll-token" &&
            node.Reason == SuspendReason.PollingCondition &&
            node.Message == "Waiting for file" &&
            node.RetryAfter == TimeSpan.FromSeconds(10) &&
            node.MaxWaitTime == TimeSpan.FromMinutes(5) &&
            node.MaxRetries == 30 &&
            node.Status == WorkflowExecutionStatus.Polling);

        var polling = await manager.GetPollingStatusAsync("graph-a", "poll-token");
        polling.Should().NotBeNull();
        polling!.AttemptNumber.Should().Be(1);
        polling.RetryAfter.Should().Be(TimeSpan.FromSeconds(10));
        polling.MaxWaitTime.Should().Be(TimeSpan.FromMinutes(5));
        polling.NextRetryAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecutionManager_MarkSuspendedAsync_PreservesPollingStartAndAdvancesRetryProgress()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(1);
        var timeProvider = new ManualTimeProvider(now);
        var store = new InMemoryWorkflowExecutionStore();
        await store.SaveAsync(CreateExecution("graph-a", "exec-a", WorkflowExecutionStatus.Running));
        var manager = new ExecutionManager(store, timeProvider: timeProvider);

        await manager.MarkSuspendedAsync(
            "graph-a",
            "exec-a",
            "sensor",
            "poll-token",
            SuspendReason.PollingCondition,
            message: "Waiting",
            retryAfter: TimeSpan.FromSeconds(10),
            maxWaitTime: TimeSpan.FromMinutes(5),
            maxRetries: 30,
            pollingAttemptNumber: 0);

        timeProvider.Advance(TimeSpan.FromSeconds(30));

        await manager.MarkSuspendedAsync(
            "graph-a",
            "exec-a",
            "sensor",
            "poll-token",
            SuspendReason.PollingCondition,
            pollingAttemptNumber: 2);

        var execution = await store.LoadAsync("graph-a", "exec-a");
        execution.Should().NotBeNull();
        execution!.Status.Should().Be(WorkflowExecutionStatus.Polling);
        execution.SuspendedAt.Should().Be(now);
        execution.PollingStartedAt.Should().Be(now);
        execution.PollingAttemptNumber.Should().Be(2);
        execution.RetryAfter.Should().Be(TimeSpan.FromSeconds(10));
        execution.MaxWaitTime.Should().Be(TimeSpan.FromMinutes(5));
        execution.MaxRetries.Should().Be(30);
        execution.NextRetryAt.Should().Be(now.AddSeconds(40));
        execution.SuspensionMessage.Should().Be("Waiting");

        var polling = await manager.GetPollingStatusAsync("graph-a", "poll-token");
        polling.Should().NotBeNull();
        polling!.AttemptNumber.Should().Be(2);
        polling.ElapsedTime.Should().Be(TimeSpan.FromSeconds(30));
        polling.NextRetryAt.Should().Be(now.AddSeconds(40));
    }

    [Fact]
    public async Task ExecutionManager_MarkFailedAsync_ClearsSuspensionAndStoresError()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(2);
        var timeProvider = new ManualTimeProvider(now);
        var store = new InMemoryWorkflowExecutionStore();
        var logStore = new InMemoryWorkflowLogStore();
        await store.SaveAsync(CreateExecution(
            "graph-a",
            "exec-a",
            WorkflowExecutionStatus.Polling,
            suspendedNodeId: "sensor",
            suspendToken: "poll-token",
            reason: SuspendReason.PollingCondition,
            retryAfter: TimeSpan.FromSeconds(5),
            maxWaitTime: TimeSpan.FromMinutes(1),
            pollingAttemptNumber: 3));
        var manager = new ExecutionManager(store, logStore, timeProvider: timeProvider);

        await manager.MarkFailedAsync("graph-a", "exec-a", "sensor", "Max polling retries exceeded");

        var execution = await store.LoadAsync("graph-a", "exec-a");
        execution.Should().NotBeNull();
        execution!.Status.Should().Be(WorkflowExecutionStatus.Failed);
        execution.CurrentNodeId.Should().Be("sensor");
        execution.SuspendedNodeId.Should().BeNull();
        execution.SuspendToken.Should().BeNull();
        execution.SuspendReason.Should().BeNull();
        execution.RetryAfter.Should().BeNull();
        execution.MaxWaitTime.Should().BeNull();
        execution.PollingAttemptNumber.Should().BeNull();
        execution.PollingStartedAt.Should().BeNull();
        execution.NextRetryAt.Should().BeNull();
        execution.CompletedAt.Should().Be(now);
        execution.ErrorMessage.Should().Be("Max polling retries exceeded");

        var logs = await logStore.ListAsync("graph-a", "exec-a");
        logs.Should().ContainSingle(log =>
            log.Level == LogLevel.Error &&
            log.Message == "Execution failed at node 'sensor': Max polling retries exceeded");
    }

    [Fact]
    public async Task ExecutionManager_CanTrackAndResumeMultipleSuspendedNodes()
    {
        var store = new InMemoryWorkflowExecutionStore();
        await store.SaveAsync(CreateExecution("graph-a", "exec-a", WorkflowExecutionStatus.Running));
        var manager = new ExecutionManager(store);

        await manager.MarkSuspendedAsync(
            "graph-a",
            "exec-a",
            "approval-a",
            "token-a",
            SuspendReason.HumanApproval,
            message: "Approval A");
        await manager.MarkSuspendedAsync(
            "graph-a",
            "exec-a",
            "approval-b",
            "token-b",
            SuspendReason.ExternalTaskWait,
            message: "Approval B");

        var suspended = await manager.GetSuspendedNodesAsync("graph-a", "exec-a");
        suspended.Should().HaveCount(2);
        suspended.Should().Contain(node =>
            node.NodeId == "approval-a" &&
            node.SuspendToken == "token-a" &&
            node.Reason == SuspendReason.HumanApproval);
        suspended.Should().Contain(node =>
            node.NodeId == "approval-b" &&
            node.SuspendToken == "token-b" &&
            node.Reason == SuspendReason.ExternalTaskWait);

        var result = await manager.ResumeSuspendedNodeAsync(
            "graph-a",
            "token-a",
            new ResumeSuspensionRequest { ResumeValue = true });

        result.Status.Should().Be(ResumeSuspensionStatus.Accepted);
        result.NodeId.Should().Be("approval-a");

        var afterResume = await manager.GetSuspendedNodesAsync("graph-a", "exec-a");
        afterResume.Should().ContainSingle(node =>
            node.NodeId == "approval-b" &&
            node.SuspendToken == "token-b");

        var execution = await store.LoadAsync("graph-a", "exec-a");
        execution.Should().NotBeNull();
        execution!.Status.Should().Be(WorkflowExecutionStatus.Suspended);
        execution.SuspendedNodeId.Should().Be("approval-b");
        execution.SuspendToken.Should().Be("token-b");
        execution.Suspensions.Should().ContainSingle(suspension => suspension.SuspendToken == "token-b");
    }

    [Fact]
    public async Task ExecutionManager_GetSuspendedNodes_FallsBackToLatestSuspensionCheckpoint()
    {
        var executionStore = new InMemoryWorkflowExecutionStore();
        var checkpointStore = new InMemoryCheckpointStore();
        await executionStore.SaveAsync(CreateExecution("graph-a", "exec-a", WorkflowExecutionStatus.Running));
        await checkpointStore.SaveCheckpointAsync(CreateSuspensionCheckpoint(
            graphId: "graph-a",
            executionId: "exec-a",
            nodeId: "approval",
            suspendToken: "token-a",
            reason: SuspendReason.HumanApproval,
            message: "Awaiting approval"));
        var manager = new ExecutionManager(executionStore, checkpointStore: checkpointStore);

        var suspended = await manager.GetSuspendedNodesAsync("graph-a", "exec-a");

        suspended.Should().ContainSingle(node =>
            node.NodeId == "approval" &&
            node.SuspendToken == "token-a" &&
            node.Reason == SuspendReason.HumanApproval &&
            node.Message == "Awaiting approval" &&
            node.Status == WorkflowExecutionStatus.Suspended);
    }

    [Fact]
    public async Task ExecutionManager_GetPollingStatus_FallsBackToLatestSuspensionCheckpoint()
    {
        var executionStore = new InMemoryWorkflowExecutionStore();
        var checkpointStore = new InMemoryCheckpointStore();
        await executionStore.SaveAsync(CreateExecution("graph-a", "exec-a", WorkflowExecutionStatus.Running));
        await checkpointStore.SaveCheckpointAsync(CreateSuspensionCheckpoint(
            graphId: "graph-a",
            executionId: "exec-a",
            nodeId: "sensor",
            suspendToken: "poll-token",
            reason: SuspendReason.PollingCondition,
            message: "Waiting",
            retryAfter: TimeSpan.FromSeconds(15),
            maxWaitTime: TimeSpan.FromMinutes(2),
            pollingAttemptNumber: 3));
        var manager = new ExecutionManager(executionStore, checkpointStore: checkpointStore);

        var status = await manager.GetPollingStatusAsync("graph-a", "poll-token");

        status.Should().NotBeNull();
        status!.ExecutionId.Should().Be("exec-a");
        status.NodeId.Should().Be("sensor");
        status.AttemptNumber.Should().Be(3);
        status.RetryAfter.Should().Be(TimeSpan.FromSeconds(15));
        status.MaxWaitTime.Should().Be(TimeSpan.FromMinutes(2));
        status.Status.Should().Be(WorkflowExecutionStatus.Polling);
    }

    [Fact]
    public async Task ExecutionManager_ResumeSuspendedNode_CanUseCheckpointFallback()
    {
        var executionStore = new InMemoryWorkflowExecutionStore();
        var checkpointStore = new InMemoryCheckpointStore();
        await executionStore.SaveAsync(CreateExecution("graph-a", "exec-a", WorkflowExecutionStatus.Running));
        await checkpointStore.SaveCheckpointAsync(CreateSuspensionCheckpoint(
            graphId: "graph-a",
            executionId: "exec-a",
            nodeId: "approval",
            suspendToken: "resume-token",
            reason: SuspendReason.HumanApproval));
        var manager = new ExecutionManager(executionStore, checkpointStore: checkpointStore);

        var result = await manager.ResumeSuspendedNodeAsync(
            "graph-a",
            "resume-token",
            new ResumeSuspensionRequest { ResumeValue = true });

        result.Status.Should().Be(ResumeSuspensionStatus.Accepted);
        result.ExecutionId.Should().Be("exec-a");
        result.NodeId.Should().Be("approval");

        var execution = await executionStore.LoadAsync("graph-a", "exec-a");
        execution!.Status.Should().Be(WorkflowExecutionStatus.Running);
        execution.CurrentNodeId.Should().Be("approval");
        execution.SuspendToken.Should().BeNull();
    }

    [Fact]
    public async Task ExecutionManager_ResumeSuspendedNode_InvokesResumeRunnerWithGraphCheckpointAndResumeValue()
    {
        var graphStore = new InMemoryGraphDefinitionStore();
        var executionStore = new InMemoryWorkflowExecutionStore();
        var checkpointStore = new InMemoryCheckpointStore();
        var runner = new RecordingResumeRunner();
        await graphStore.SaveAsync(CreateStoredGraph("graph-a"));
        await executionStore.SaveAsync(CreateExecution(
            "graph-a",
            "exec-a",
            WorkflowExecutionStatus.Suspended,
            suspendedNodeId: "approval",
            suspendToken: "resume-token"));
        await checkpointStore.SaveCheckpointAsync(CreateSuspensionCheckpoint(
            graphId: "graph-a",
            executionId: "exec-a",
            nodeId: "approval",
            suspendToken: "resume-token",
            reason: SuspendReason.HumanApproval));
        var manager = new ExecutionManager(
            executionStore,
            checkpointStore: checkpointStore,
            graphStore: graphStore,
            resumeRunner: runner);
        var resumeValue = new Dictionary<string, object> { ["approved"] = true };

        var result = await manager.ResumeSuspendedNodeAsync(
            "graph-a",
            "resume-token",
            new ResumeSuspensionRequest { ResumeValue = resumeValue });

        result.Status.Should().Be(ResumeSuspensionStatus.Accepted);
        result.Message.Should().Be("continued");
        runner.Request.Should().NotBeNull();
        runner.Request!.Execution.ExecutionId.Should().Be("exec-a");
        runner.Request.Graph!.GraphId.Should().Be("graph-a");
        runner.Request.Checkpoint!.Metadata!.SuspendToken.Should().Be("resume-token");
        runner.Request.ResumeValue.Should().BeSameAs(resumeValue);
    }

    [Fact]
    public async Task ExecutionManager_ResumeSuspendedNode_PreservesSuspension_WhenRunnerRejects()
    {
        var executionStore = new InMemoryWorkflowExecutionStore();
        var logStore = new InMemoryWorkflowLogStore();
        var runner = new RecordingResumeRunner
        {
            Result = new WorkflowResumeRunnerResult
            {
                Status = ResumeSuspensionStatus.Rejected,
                Message = "checkpoint missing"
            }
        };
        await executionStore.SaveAsync(CreateExecution(
            "graph-a",
            "exec-a",
            WorkflowExecutionStatus.Suspended,
            suspendedNodeId: "approval",
            suspendToken: "resume-token"));
        var manager = new ExecutionManager(executionStore, logStore, resumeRunner: runner);

        var result = await manager.ResumeSuspendedNodeAsync(
            "graph-a",
            "resume-token",
            new ResumeSuspensionRequest { ResumeValue = true });

        result.Status.Should().Be(ResumeSuspensionStatus.Rejected);
        result.Message.Should().Be("checkpoint missing");

        var execution = await executionStore.LoadAsync("graph-a", "exec-a");
        execution!.Status.Should().Be(WorkflowExecutionStatus.Suspended);
        execution.SuspendedNodeId.Should().Be("approval");
        execution.SuspendToken.Should().Be("resume-token");
        execution.ErrorMessage.Should().Be("checkpoint missing");

        var logs = await logStore.ListAsync("graph-a", "exec-a");
        logs.Should().ContainSingle(log =>
            log.Level == LogLevel.Warning &&
            log.Message == "Suspension 'resume-token' resume rejected: checkpoint missing");
    }

    [Fact]
    public async Task ExecutionManager_ResumeSuspendedNode_MarksExecutionFailed_WhenRunnerFails()
    {
        var executionStore = new InMemoryWorkflowExecutionStore();
        var logStore = new InMemoryWorkflowLogStore();
        var runner = new RecordingResumeRunner
        {
            Result = new WorkflowResumeRunnerResult
            {
                Status = ResumeSuspensionStatus.Failed,
                Message = "handler exploded"
            }
        };
        await executionStore.SaveAsync(CreateExecution(
            "graph-a",
            "exec-a",
            WorkflowExecutionStatus.Suspended,
            suspendedNodeId: "approval",
            suspendToken: "resume-token"));
        var manager = new ExecutionManager(executionStore, logStore, resumeRunner: runner);

        var result = await manager.ResumeSuspendedNodeAsync(
            "graph-a",
            "resume-token",
            new ResumeSuspensionRequest { ResumeValue = true });

        result.Status.Should().Be(ResumeSuspensionStatus.Failed);
        result.Message.Should().Be("handler exploded");

        var execution = await executionStore.LoadAsync("graph-a", "exec-a");
        execution!.Status.Should().Be(WorkflowExecutionStatus.Failed);
        execution.CurrentNodeId.Should().Be("approval");
        execution.SuspendedNodeId.Should().BeNull();
        execution.SuspendToken.Should().BeNull();
        execution.CompletedAt.Should().NotBeNull();
        execution.ErrorMessage.Should().Be("handler exploded");

        var logs = await logStore.ListAsync("graph-a", "exec-a");
        logs.Should().ContainSingle(log =>
            log.Level == LogLevel.Error &&
            log.Message == "Suspension 'resume-token' resume failed: handler exploded");
    }

    [Fact]
    public async Task ExecutionManager_StreamLogs_ReturnsLifecycleEvents()
    {
        var logStore = new InMemoryWorkflowLogStore();
        await logStore.AppendAsync(new WorkflowLogEntry
        {
            GraphId = "graph-a",
            ExecutionId = "exec-a",
            Timestamp = DateTimeOffset.UtcNow,
            Source = "test",
            Level = LogLevel.Information,
            Message = "one"
        });
        await logStore.AppendAsync(new WorkflowLogEntry
        {
            GraphId = "graph-a",
            ExecutionId = "exec-a",
            Timestamp = DateTimeOffset.UtcNow.AddSeconds(1),
            Source = "test",
            Level = LogLevel.Warning,
            Message = "two",
            NodeId = "work",
            Exception = "boom"
        });

        var manager = new ExecutionManager(new InMemoryWorkflowExecutionStore(), logStore);
        var logs = new List<GraphLogEntryDto>();

        await foreach (var log in manager.StreamLogsAsync("graph-a", "exec-a"))
        {
            logs.Add(log);
        }

        logs.Should().HaveCount(2);
        logs[0].Should().Match<GraphLogEntryDto>(log =>
            log.Source == "test" &&
            log.Level == nameof(LogLevel.Information) &&
            log.Message == "one");
        logs[1].Should().Match<GraphLogEntryDto>(log =>
            log.Source == "test" &&
            log.Level == nameof(LogLevel.Warning) &&
            log.Message == "two" &&
            log.NodeId == "work" &&
            log.Exception == "boom");
    }

    [Fact]
    public async Task ExecutionManager_StreamLogs_HandlesCancellation()
    {
        var logStore = new InMemoryWorkflowLogStore();
        await logStore.AppendAsync(new WorkflowLogEntry
        {
            GraphId = "graph-a",
            ExecutionId = "exec-a",
            Timestamp = DateTimeOffset.UtcNow,
            Source = "test",
            Level = LogLevel.Information,
            Message = "one"
        });
        var manager = new ExecutionManager(new InMemoryWorkflowExecutionStore(), logStore);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () =>
        {
            await foreach (var _ in manager.StreamLogsAsync("graph-a", "exec-a", cts.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task JsonWorkflowLogStore_RoundTripsLogs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hpd-graph-logs-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonWorkflowLogStore(root);

            await store.AppendAsync(new WorkflowLogEntry
            {
                GraphId = "graph/a",
                ExecutionId = "exec/a",
                Timestamp = DateTimeOffset.UtcNow,
                Source = "test",
                Level = LogLevel.Error,
                Message = "failed",
                NodeId = "node-a",
                Exception = "nope"
            });

            var logs = await store.ListAsync("graph/a", "exec/a");

            logs.Should().ContainSingle(log =>
                log.Source == "test" &&
                log.Level == LogLevel.Error &&
                log.Message == "failed" &&
                log.NodeId == "node-a" &&
                log.Exception == "nope");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static GraphConfig CreateConfig(string graphId, string name) => new()
    {
        GraphId = graphId,
        Name = name,
        Nodes = new Dictionary<string, NodeConfig>
        {
            ["work"] = new()
            {
                Id = "work",
                Name = "Work",
                Type = NodeKindConfig.Handler,
                HandlerName = "work"
            }
        },
        Edges =
        [
            new EdgeConfig { From = "START", To = "work" },
            new EdgeConfig { From = "work", To = "END" }
        ]
    };

    private static GraphScheduleConfig CreateSchedule(string cronExpression) => new()
    {
        CronExpression = cronExpression,
        TimeZoneId = "UTC"
    };

    private static ScheduledGraph CreateScheduledGraph(
        string graphId,
        bool enabled,
        DateTimeOffset? nextRunAt,
        ScheduleMisfirePolicyConfig misfirePolicy = ScheduleMisfirePolicyConfig.RunOnce,
        string cronExpression = "0 3 * * *") => new()
        {
            GraphId = graphId,
            Schedule = CreateSchedule(cronExpression) with
            {
                ConcurrencyPolicy = ScheduleConcurrencyPolicyConfig.AllowOverlap,
                MisfirePolicy = misfirePolicy
            },
            Enabled = enabled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            NextRunAt = nextRunAt
        };

    private static StoredGraph CreateStoredGraph(string graphId) => new()
    {
        GraphId = graphId,
        Name = $"Workflow {graphId}",
        GraphVersion = "1.0.0",
        Config = CreateConfig(graphId, $"Workflow {graphId}"),
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch
    };

    private static WorkflowExecution CreateExecution(
        string graphId,
        string executionId,
        WorkflowExecutionStatus status,
        string? suspendedNodeId = null,
        string? suspendToken = null,
        SuspendReason? reason = null,
        string? message = null,
        TimeSpan? retryAfter = null,
        TimeSpan? maxWaitTime = null,
        int? pollingAttemptNumber = null) => new()
        {
            GraphId = graphId,
            ExecutionId = executionId,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow,
            SuspendedNodeId = suspendedNodeId,
            SuspendToken = suspendToken,
            SuspendReason = reason,
            SuspensionMessage = message,
            SuspendedAt = suspendedNodeId is null ? null : DateTimeOffset.UtcNow,
            RetryAfter = retryAfter,
            MaxWaitTime = maxWaitTime,
            PollingAttemptNumber = pollingAttemptNumber,
            PollingStartedAt = status == WorkflowExecutionStatus.Polling ? DateTimeOffset.UtcNow : null,
            NextRetryAt = retryAfter.HasValue ? DateTimeOffset.UtcNow + retryAfter.Value : null
        };

    private static GraphCheckpoint CreateSuspensionCheckpoint(
        string graphId,
        string executionId,
        string nodeId,
        string suspendToken,
        SuspendReason reason,
        string? message = null,
        TimeSpan? retryAfter = null,
        TimeSpan? maxWaitTime = null,
        int? pollingAttemptNumber = null)
    {
        var metadata = new Dictionary<string, object>
        {
            ["reason"] = reason.ToString()
        };

        if (message is not null)
        {
            metadata["message"] = message;
        }

        if (retryAfter.HasValue)
        {
            metadata["retryAfter"] = retryAfter.Value;
            metadata["nextRetryAt"] = DateTimeOffset.UtcNow + retryAfter.Value;
        }

        if (maxWaitTime.HasValue)
        {
            metadata["maxWaitTime"] = maxWaitTime.Value;
        }

        if (pollingAttemptNumber.HasValue)
        {
            metadata["pollingAttemptNumber"] = pollingAttemptNumber.Value;
            metadata["pollingStartedAt"] = DateTimeOffset.UtcNow;
        }

        return new GraphCheckpoint
        {
            CheckpointId = Guid.NewGuid().ToString("n"),
            ExecutionId = executionId,
            GraphId = graphId,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedNodes = new HashSet<string>(),
            NodeOutputs = new Dictionary<string, object>(),
            ContextJson = "{}",
            Metadata = new CheckpointMetadata
            {
                Trigger = CheckpointTrigger.Suspension,
                SuspendedNodeId = nodeId,
                SuspendToken = suspendToken,
                SuspensionOutcome = SuspensionOutcome.Pending,
                CustomMetadata = metadata
            }
        };
    }

    private static async Task EventuallyAsync(Func<Task> assertion)
    {
        Exception? last = null;

        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                await assertion();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(25);
            }
        }

        throw last ?? new InvalidOperationException("Assertion did not complete.");
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task<List<Event>> CollectSynchronousEventsAsync(
        System.Threading.Channels.ChannelReader<Event> reader,
        Func<Event, bool>? stopWhen = null)
    {
        var events = new List<Event>();
        using var cts = new CancellationTokenSource(500);

        try
        {
            await foreach (var evt in reader.ReadAllAsync(cts.Token))
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

    private sealed class RecordingInputHandler : IGraphNodeHandler<GraphContext>
    {
        public string HandlerName => "work";
        public List<string> Messages { get; } = new();

        public Task<NodeExecutionResult> ExecuteAsync(
            GraphContext context,
            HandlerInputs inputs,
            CancellationToken cancellationToken)
        {
            Messages.Add(inputs.Get<string>("message"));

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Success.Single(
                    output: new Dictionary<string, object> { ["ok"] = true },
                    duration: TimeSpan.Zero,
                    metadata: new NodeExecutionMetadata()));
        }
    }

    private sealed class RecordingResumeRunner : IWorkflowResumeRunner
    {
        public WorkflowResumeRunnerRequest? Request { get; private set; }
        public WorkflowResumeRunnerResult Result { get; init; } =
            WorkflowResumeRunnerResult.Accepted("continued", executionContinued: true);

        public Task<WorkflowResumeRunnerResult> ResumeAsync(
            WorkflowResumeRunnerRequest request,
            CancellationToken ct = default)
        {
            Request = request;
            return Task.FromResult(Result);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration)
        {
            _now += duration;
        }
    }
}
