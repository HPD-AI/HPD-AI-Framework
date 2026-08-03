using FluentAssertions;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Infrastructure;
using HPD.Events.Core;
using HPD.Events.Struct;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Middleware;

public sealed class FunctionExecutionContextTests
{
    [Fact]
    public void FunctionExecutionContext_CopiesInvocationSnapshotValues()
    {
        var metadata = new ToolResultMetadata();
        var invocation = new ToolInvocationInfo("batch-1", "tool-call-1", "Search", 2);
        var context = CreateContext(
            callId: "tool-call-1",
            functionName: "Search",
            invocation: invocation,
            metadata: metadata,
            traceId: "trace-1",
            threadExecutionId: "execution-1");

        context.InvocationSnapshot.Should().BeEquivalentTo(new FunctionInvocationSnapshot
        {
            AgentName = "AgentA",
            ConversationId = "conversation-1",
            SessionId = "session-1",
            ThreadId = "thread-1",
            TraceId = "trace-1",
            ThreadExecutionId = "execution-1",
            FunctionCallId = "tool-call-1",
            FunctionName = "Search",
            Invocation = invocation
        });

        context.AgentName.Should().Be("AgentA");
        context.ConversationId.Should().Be("conversation-1");
        context.SessionId.Should().Be("session-1");
        context.ThreadId.Should().Be("thread-1");
        context.TraceId.Should().Be("trace-1");
        context.ThreadExecutionId.Should().Be("execution-1");
        context.FunctionCallId.Should().Be("tool-call-1");
        context.FunctionName.Should().Be("Search");
        context.Invocation.Should().Be(invocation);
        context.BatchId.Should().Be("batch-1");
        context.ToolCallIndex.Should().Be(2);
    }

    [Fact]
    public void FunctionExecutionContext_DoesNotStoreHookContext()
    {
        typeof(FunctionExecutionContext)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Should()
            .NotContain(field => field.FieldType == typeof(HookContext));
    }

    [Fact]
    public void FunctionExecutionContext_PublicApi_DoesNotExposeLiveStateMutation()
    {
        var type = typeof(FunctionExecutionContext);

        type.GetMethod("UpdateState").Should().BeNull();
        type.GetMethod("UpdateMiddlewareState").Should().BeNull();
        type.GetProperty("HookContext").Should().BeNull();
        type.GetProperty("AgentContext").Should().BeNull();
        type.GetProperty("Session").Should().BeNull();
        type.GetProperty("Thread").Should().BeNull();
    }

    [Fact]
    public void FunctionExecutionContext_ResultMetadata_IsSameInstance()
    {
        var metadata = new ToolResultMetadata();
        var context = CreateContext(metadata: metadata);

        context.ResultMetadata.Should().BeSameAs(metadata);

        context.ResultMetadata.Set("answer", 42);
        metadata.TryGet<int>("answer", out var value).Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    public void FunctionExecutionContext_RunConfig_IsSameInstance()
    {
        var runConfig = new AgentRunConfig
        {
            Context = new AgentContextRunConfig
            {
                Properties = new Dictionary<string, object> { ["coding.workspaceRoot"] = "/tmp/workspace" }
            }
        };

        var context = CreateContext(runConfig: runConfig);

        context.RunConfig.Should().BeSameAs(runConfig);
        context.RunConfig.Context!.Properties.Should().ContainKey("coding.workspaceRoot");
    }

    [Fact]
    public void FunctionExecutionContext_ExposesEventCoordinatorAndStreams()
    {
        var coordinator = new EventCoordinator();
        var context = CreateContext(
            eventCoordinator: coordinator,
            threadExecutionId: "execution-1");

        context.EventCoordinator.Should().BeSameAs(coordinator);
        context.EventFlows.Should().BeSameAs(coordinator.EventFlows);
        context.GetParentEventCoordinator().Should().BeSameAs(coordinator);
    }

    [Fact]
    public void FunctionExecutionContext_ExposesStructEvents()
    {
        var structEvents = new StructEventHub();
        var context = CreateContext(structEvents: structEvents);

        context.StructEvents.Should().BeSameAs(structEvents);
    }

    [Fact]
    public async Task ToolFunction_CanEmitStructEventThroughFunctionExecutionContext()
    {
        var chatClient = new FakeChatClient();
        chatClient.EnqueueToolCall("emit_struct_sample", "call-struct");
        chatClient.EnqueueTextResponse("done");

        var observed = new TaskCompletionSource<ToolStructSample>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var tool = HPDAIFunctionFactory.Create(
            (_, context, _) =>
            {
                var result = context.StructEvents!
                    .Route<ToolStructSample>()
                    .CreateEmitter()
                    .Emit(new ToolStructSample(context.FunctionCallId));

                result.Accepted.Should().BeTrue();
                return Task.FromResult<object?>("sample emitted");
            },
            new HPDAIFunctionFactoryOptions { Name = "emit_struct_sample" });

        var agent = TestAgentFactory.Create(chatClient: chatClient, tools: tool);
        using var subscription = agent.ObserveStruct<ToolStructSample>(sample =>
        {
            observed.TrySetResult(sample);
            return ValueTask.CompletedTask;
        });

        await agent.RunAsync("emit a struct sample");

        var sample = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        sample.FunctionCallId.Should().Be("call-struct");
    }

    [Fact]
    public async Task FunctionExecutionContext_PublishAsync_EmitsEvent()
    {
        var coordinator = new EventCoordinator();
        var observed = new TaskCompletionSource<TestAgentEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = coordinator.Subscribe<TestAgentEvent>(evt =>
        {
            observed.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        var context = CreateContext(eventCoordinator: coordinator, traceId: "trace-1");

        await context.PublishAsync(new TestAgentEvent());

        var evt = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        evt.TraceId.Should().Be("trace-1");
        evt.SessionId.Should().Be("session-1");
        evt.ThreadId.Should().Be("thread-1");
    }

    [Fact]
    public async Task FunctionExecutionContext_PublishAsync_PreservesExplicitEventScope()
    {
        var coordinator = new EventCoordinator();
        var observed = new TaskCompletionSource<TestAgentEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = coordinator.Subscribe<TestAgentEvent>(evt =>
        {
            observed.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        var context = CreateContext(eventCoordinator: coordinator, traceId: "trace-1");

        await context.PublishAsync(new TestAgentEvent
        {
            SessionId = "explicit-session",
            ThreadId = "explicit-thread",
            TraceId = "explicit-trace"
        });

        var evt = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        evt.TraceId.Should().Be("explicit-trace");
        evt.SessionId.Should().Be("explicit-session");
        evt.ThreadId.Should().Be("explicit-thread");
    }

    [Fact]
    public void FunctionExecutionContext_Services_ReturnsInjectedProvider()
    {
        var service = new TestService();
        var services = new SimpleServiceProvider(service);
        var context = CreateContext(services: services);

        context.Services.Should().BeSameAs(services);
        context.Services!.GetService(typeof(TestService)).Should().BeSameAs(service);
    }

    [Fact]
    public async Task FunctionExecutionContext_TryPublishAsync_ReturnsFalseWithoutCoordinator()
    {
        var context = CreateContext(eventCoordinator: null);

        (await context.TryPublishAsync(new TestAgentEvent())).Should().BeFalse();
    }

    [Fact]
    public async Task FunctionExecutionContext_PublishAsync_ThrowsWithoutCoordinator()
    {
        var context = CreateContext(eventCoordinator: null);

        var act = async () => await context.PublishAsync(new TestAgentEvent());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*event coordinator*");
    }

    [Fact]
    public async Task FunctionExecutionContext_RequestAsync_CancellationRemovesPendingRequest()
    {
        var coordinator = new EventCoordinator();
        var context = CreateContext(eventCoordinator: coordinator);
        using var cancellation = new CancellationTokenSource();

        var responseTask = context.RequestAsync<TestRequestEvent, TestResponseEvent>(
            new TestRequestEvent("request-1", "test"),
            cancellation.Token);

        var pending = coordinator.GetPendingRequests().Should().ContainSingle().Subject;
        pending.Request.Should().BeOfType<TestRequestEvent>()
            .Which.ThreadExecutionId.Should().Be("execution-1");

        cancellation.Cancel();

        Func<Task> waitForResponse = async () => await responseTask;
        await waitForResponse.Should().ThrowAsync<OperationCanceledException>();
        coordinator.GetPendingRequests().Should().BeEmpty();
        coordinator.Respond(new TestResponseEvent("request-1", "test"))
            .Status.Should().Be(HPD.Events.RespondStatus.Cancelled);
    }

    [Fact]
    public void RegisterBackgroundTask_WithoutRegistry_Throws()
    {
        var context = CreateContext(backgroundTasks: null);

        var act = () => context.RegisterBackgroundTask(
            "work",
            new BackgroundTaskNotificationRule.OnFinalStateRule(Faulted: true),
            (_, _) => Task.CompletedTask);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*active agent runtime*");
    }

    [Fact]
    public void RegisterBackgroundTask_WithRegistry_DelegatesInvocationSnapshot()
    {
        var registry = new CapturingBackgroundTaskRegistry();
        var invocation = new ToolInvocationInfo("batch-1", "call-1", "TestFunction", 3);
        var context = CreateContext(
            callId: "call-1",
            invocation: invocation,
            backgroundTasks: registry);

        context.CanRegisterBackgroundTasks.Should().BeTrue();

        var registration = context.RegisterBackgroundTask(
            "work",
            new BackgroundTaskNotificationRule.OnFinalStateRule(Faulted: true),
            (_, _) => Task.CompletedTask);

        registration.Should().Be(new BackgroundTaskRegistration("task-1", "work", BackgroundTaskSourceKind.ToolCall));
        registry.Descriptor.Should().NotBeNull();
        registry.Descriptor!.Name.Should().Be("work");
        registry.Descriptor.SourceKind.Should().Be(BackgroundTaskSourceKind.ToolCall);
        registry.Descriptor.SourceId.Should().Be("call-1");
        registry.Descriptor.Notification.Should().Be(new BackgroundTaskNotificationRule.OnFinalStateRule(Faulted: true));
        registry.Descriptor.Invocation.Should().BeSameAs(context.InvocationSnapshot);
        registry.Descriptor.Invocation!.BatchId.Should().Be("batch-1");
        registry.Descriptor.Invocation.ToolCallIndex.Should().Be(3);
        registry.TaskFactory.Should().NotBeNull();
    }

    [Fact]
    public void RegisterBackgroundTask_WithDescriptor_AllowsExplicitSourceKind()
    {
        var registry = new CapturingBackgroundTaskRegistry();
        var context = CreateContext(backgroundTasks: registry);

        var registration = context.RegisterBackgroundTask(
            new BackgroundTaskDescriptor
            {
                Name = "reviewer",
                SourceKind = BackgroundTaskSourceKind.SubAgent,
                Notification = new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true)
            },
            (_, _) => Task.CompletedTask);

        registration.SourceKind.Should().Be(BackgroundTaskSourceKind.SubAgent);
        registry.Descriptor.Should().NotBeNull();
        registry.Descriptor!.SourceKind.Should().Be(BackgroundTaskSourceKind.SubAgent);
        registry.Descriptor.SourceId.Should().Be(context.FunctionCallId);
        registry.Descriptor.Invocation.Should().BeSameAs(context.InvocationSnapshot);
    }

    [Fact]
    public void BackgroundTaskContext_PublicApi_DoesNotExposeLiveStateOrResultMetadata()
    {
        var type = typeof(BackgroundTaskContext);

        type.GetMethod("UpdateState").Should().BeNull();
        type.GetMethod("UpdateMiddlewareState").Should().BeNull();
        type.GetProperty("HookContext").Should().BeNull();
        type.GetProperty("AgentContext").Should().BeNull();
        type.GetProperty("AgentLoopState").Should().BeNull();
        type.GetProperty("ToolResultMetadata").Should().BeNull();
        type.GetProperty("Session").Should().BeNull();
        type.GetProperty("Thread").Should().BeNull();
    }

    private static FunctionExecutionContext CreateContext(
        string callId = "call-1",
        string functionName = "TestFunction",
        ToolInvocationInfo? invocation = null,
        ToolResultMetadata? metadata = null,
        EventCoordinator? eventCoordinator = null,
        string? traceId = null,
        string? threadExecutionId = null,
        IServiceProvider? services = null,
        IAgentBackgroundTaskRegistry? backgroundTasks = null,
        IStructEventHub? structEvents = null,
        AgentRunConfig? runConfig = null)
    {
        runConfig ??= new AgentRunConfig();
        var function = AIFunctionFactory.Create(
            () => "ok",
            new AIFunctionFactoryOptions
            {
                Name = functionName,
                Description = "Test function"
            });

        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var session = new global::HPD.Agent.Session("session-1");
        var thread = new global::HPD.Agent.Thread("session-1", "test-agent") { Id = "thread-1" };
        var agentContext = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            eventCoordinator ?? new EventCoordinator(),
            session,
            thread,
            CancellationToken.None,
            services: services,
            traceId: traceId,
            threadExecutionId: threadExecutionId);
        var beforeContext = agentContext.AsBeforeFunction(
            function,
            callId,
            new Dictionary<string, object?>(),
            runConfig,
            toolharnessName: null,
            skillName: null,
            invocation: invocation);
        var request = new FunctionRequest
        {
            Function = function,
            CallId = callId,
            Arguments = new Dictionary<string, object?>(),
            State = state,
            RunConfig = runConfig,
            Invocation = invocation,
            ResultMetadata = metadata ?? new ToolResultMetadata(),
            EventCoordinator = eventCoordinator,
            StructEvents = structEvents,
            BackgroundTasks = backgroundTasks
        };

        return new FunctionExecutionContext(beforeContext, request);
    }

    private sealed record TestAgentEvent : AgentEvent
    {
        public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
    }

    private sealed record TestRequestEvent(string RequestId, string SourceName)
        : AgentEvent, IAgentRequestEvent<TestResponseEvent>
    {
        public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
    }

    private sealed record TestResponseEvent(string RequestId, string SourceName)
        : AgentEvent, IAgentResponseEvent
    {
        public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
    }

    private readonly record struct ToolStructSample(
        string FunctionCallId,
        long TimestampNs = 0,
        long SequenceNumber = 0) : AgentStructEvent
    {
        public HPD.Events.EventKind Kind => HPD.Events.EventKind.Diagnostic;
    }

    private sealed class TestService;

    private sealed class SimpleServiceProvider(object service) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == service.GetType() ? service : null;
    }

    private sealed class CapturingBackgroundTaskRegistry : IAgentBackgroundTaskRegistry
    {
        public BackgroundTaskDescriptor? Descriptor { get; private set; }
        public Func<BackgroundTaskContext, CancellationToken, Task>? TaskFactory { get; private set; }

        public BackgroundTaskRegistration RegisterBackgroundTask(
            BackgroundTaskDescriptor descriptor,
            Func<BackgroundTaskContext, CancellationToken, Task> taskFactory)
        {
            Descriptor = descriptor;
            TaskFactory = taskFactory;
            return new BackgroundTaskRegistration("task-1", descriptor.Name, descriptor.SourceKind);
        }
    }
}
