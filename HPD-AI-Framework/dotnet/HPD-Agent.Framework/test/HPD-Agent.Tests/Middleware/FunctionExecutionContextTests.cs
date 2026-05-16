using FluentAssertions;
using HPD.Agent.Middleware;
using HPD.Events.Core;
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
            traceId: "trace-1");

        context.InvocationSnapshot.Should().BeEquivalentTo(new FunctionInvocationSnapshot
        {
            AgentName = "AgentA",
            ConversationId = "conversation-1",
            SessionId = "session-1",
            BranchId = "branch-1",
            TraceId = "trace-1",
            FunctionCallId = "tool-call-1",
            FunctionName = "Search",
            Invocation = invocation
        });

        context.AgentName.Should().Be("AgentA");
        context.ConversationId.Should().Be("conversation-1");
        context.SessionId.Should().Be("session-1");
        context.BranchId.Should().Be("branch-1");
        context.TraceId.Should().Be("trace-1");
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
        type.GetProperty("Branch").Should().BeNull();
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
    public void FunctionExecutionContext_ExposesEventCoordinatorAndStreams()
    {
        var coordinator = new EventCoordinator();
        var context = CreateContext(eventCoordinator: coordinator);

        context.EventCoordinator.Should().BeSameAs(coordinator);
        context.Streams.Should().BeSameAs(coordinator.Streams);
        context.GetParentEventCoordinator().Should().BeSameAs(coordinator);
    }

    [Fact]
    public async Task FunctionExecutionContext_Emit_EmitsEvent()
    {
        var coordinator = new EventCoordinator();
        var observed = new TaskCompletionSource<TestAgentEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = coordinator.Subscribe<TestAgentEvent>(evt =>
        {
            observed.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        var context = CreateContext(eventCoordinator: coordinator, traceId: "trace-1");

        context.Emit(new TestAgentEvent());

        var evt = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        evt.TraceId.Should().Be("trace-1");
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
    public void FunctionExecutionContext_TryEmit_ReturnsFalseWithoutCoordinator()
    {
        var context = CreateContext(eventCoordinator: null);

        context.TryEmit(new TestAgentEvent()).Should().BeFalse();
    }

    [Fact]
    public void FunctionExecutionContext_Emit_ThrowsWithoutCoordinator()
    {
        var context = CreateContext(eventCoordinator: null);

        var act = () => context.Emit(new TestAgentEvent());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*event coordinator*");
    }

    [Fact]
    public void RegisterBackgroundTask_WithoutRegistry_Throws()
    {
        var context = CreateContext(backgroundTasks: null);

        var act = () => context.RegisterBackgroundTask(
            "work",
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

        context.RegisterBackgroundTask(
            "work",
            (_, _) => Task.CompletedTask);

        registry.Name.Should().Be("work");
        registry.Invocation.Should().BeSameAs(context.InvocationSnapshot);
        registry.Invocation!.BatchId.Should().Be("batch-1");
        registry.Invocation.ToolCallIndex.Should().Be(3);
        registry.TaskFactory.Should().NotBeNull();
    }

    [Fact]
    public void FunctionBackgroundContext_PublicApi_DoesNotExposeLiveStateOrResultMetadata()
    {
        var type = typeof(FunctionBackgroundContext);

        type.GetMethod("UpdateState").Should().BeNull();
        type.GetMethod("UpdateMiddlewareState").Should().BeNull();
        type.GetProperty("HookContext").Should().BeNull();
        type.GetProperty("AgentContext").Should().BeNull();
        type.GetProperty("AgentLoopState").Should().BeNull();
        type.GetProperty("ToolResultMetadata").Should().BeNull();
        type.GetProperty("Session").Should().BeNull();
        type.GetProperty("Branch").Should().BeNull();
    }

    private static FunctionExecutionContext CreateContext(
        string callId = "call-1",
        string functionName = "TestFunction",
        ToolInvocationInfo? invocation = null,
        ToolResultMetadata? metadata = null,
        EventCoordinator? eventCoordinator = null,
        string? traceId = null,
        IServiceProvider? services = null,
        IAgentBackgroundTaskRegistry? backgroundTasks = null)
    {
        var function = AIFunctionFactory.Create(
            () => "ok",
            new AIFunctionFactoryOptions
            {
                Name = functionName,
                Description = "Test function"
            });

        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var session = new global::HPD.Agent.Session("session-1");
        var branch = new global::HPD.Agent.Branch("session-1") { Id = "branch-1" };
        var agentContext = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            eventCoordinator ?? new EventCoordinator(),
            session,
            branch,
            CancellationToken.None,
            services: services,
            traceId: traceId);
        var beforeContext = agentContext.AsBeforeFunction(
            function,
            callId,
            new Dictionary<string, object?>(),
            new AgentRunConfig(),
            harnessName: null,
            skillName: null,
            invocation: invocation);
        var request = new FunctionRequest
        {
            Function = function,
            CallId = callId,
            Arguments = new Dictionary<string, object?>(),
            State = state,
            Invocation = invocation,
            ResultMetadata = metadata ?? new ToolResultMetadata(),
            EventCoordinator = eventCoordinator,
            BackgroundTasks = backgroundTasks
        };

        return new FunctionExecutionContext(beforeContext, request);
    }

    private sealed record TestAgentEvent : AgentEvent
    {
        public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
    }

    private sealed class TestService;

    private sealed class SimpleServiceProvider(object service) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == service.GetType() ? service : null;
    }

    private sealed class CapturingBackgroundTaskRegistry : IAgentBackgroundTaskRegistry
    {
        public string? Name { get; private set; }
        public FunctionInvocationSnapshot? Invocation { get; private set; }
        public Func<FunctionBackgroundContext, CancellationToken, Task>? TaskFactory { get; private set; }

        public void RegisterBackgroundTask(Task task)
        {
        }

        public void RegisterBackgroundTask(Func<CancellationToken, Task> taskFactory)
        {
        }

        public void RegisterBackgroundTask(
            string name,
            FunctionInvocationSnapshot invocation,
            Func<FunctionBackgroundContext, CancellationToken, Task> taskFactory)
        {
            Name = name;
            Invocation = invocation;
            TaskFactory = taskFactory;
        }
    }
}
