using System.Diagnostics;
using System.Threading.Channels;
using HPD.Agent.Tests.Infrastructure;
using HPD.Agent.ClientTools;
using HPD.Agent.Middleware;
using HPD.Events;
using HPD.Events.Struct;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Core;

public class RuntimeLifecycleTests : AgentTestBase
{
    private sealed record CustomRequestResponseEvent(
        string RequestId,
        string SourceName,
        string Value) : AgentEvent, IAgentResponseEvent
    {
        public override EventChannel Channel { get; init; } = EventChannel.Interactive;
        public override EventKind Kind { get; init; } = EventKind.Control;
        public override EventDirection Direction { get; init; } = EventDirection.Upstream;
    }

    private sealed record CustomRequestEvent(
        string RequestId,
        string SourceName) : AgentEvent, IAgentRequestEvent
    {
        public override EventChannel Channel { get; init; } = EventChannel.Interactive;
        public override EventKind Kind { get; init; } = EventKind.Control;
    }

    private sealed class NonEventResponseEvent : IResponseEvent
    {
        public string RequestId { get; init; } = "non-event-response";
        public string SourceName { get; init; } = "test";
    }

    private sealed class BlockingChatClient : IChatClient
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ChatClientMetadata Metadata { get; } = new(
            providerName: "BlockingChatClient",
            providerUri: null,
            defaultModelId: "blocking-model");

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }

            throw new UnreachableException();
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }

            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class DelayedChatClient(TimeSpan delay) : IChatClient
    {
        private int _responseNumber;

        public List<string> Requests { get; } = new();

        public ChatClientMetadata Metadata { get; } = new(
            providerName: "DelayedChatClient",
            providerUri: null,
            defaultModelId: "delayed-model");

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var requestText = chatMessages.LastOrDefault()?.Text ?? string.Empty;
            Requests.Add(requestText);
            await Task.Delay(delay, cancellationToken);
            var responseNumber = Interlocked.Increment(ref _responseNumber);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, $"response {responseNumber}"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var requestText = chatMessages.LastOrDefault()?.Text ?? string.Empty;
            Requests.Add(requestText);
            await Task.Delay(delay, cancellationToken);
            var responseNumber = Interlocked.Increment(ref _responseNumber);
            yield return new ChatResponseUpdate
            {
                Contents = [new Microsoft.Extensions.AI.TextContent($"response {responseNumber}")],
                FinishReason = ChatFinishReason.Stop
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private readonly record struct TestStructFrame(
        int Value,
        long SequenceNumber = 0,
        long TimestampNs = 0) : AgentStructEvent
    {
        public EventKind Kind => EventKind.Content;
    }

    private interface IBeforeStartRuntimeCapability;

    private interface IAfterStartedRuntimeCapability;

    private sealed record RuntimeCapabilityProbe(string Value) : IBeforeStartRuntimeCapability, IAfterStartedRuntimeCapability;

    private sealed record RuntimeHookProbeEvent(string Stage, string RuntimeId) : AgentEvent
    {
        public override EventKind Kind { get; init; } = EventKind.Diagnostic;
    }

    private sealed record RuntimeBridgePersistableEvent(string Value) : AgentEvent
    {
        public override EventKind Kind { get; init; } = EventKind.Diagnostic;
        public override bool ShouldPersistToThread() => true;
    }

    private sealed record RuntimeBridgeLiveOnlyEvent(string Value) : AgentEvent
    {
        public override EventKind Kind { get; init; } = EventKind.Diagnostic;
    }

    private sealed class RuntimeHookRecordingMiddleware(
        string name,
        List<string> order) : IAgentMiddleware
    {
        public Func<BeforeStartContext, CancellationToken, Task>? OnBeforeStart { get; init; }
        public Func<AfterStartedContext, CancellationToken, Task>? OnAfterStarted { get; init; }
        public Func<BeforeStopContext, CancellationToken, Task>? OnBeforeStop { get; init; }
        public Func<AfterStoppedContext, CancellationToken, Task>? OnAfterStopped { get; init; }

        public async Task BeforeStartAsync(BeforeStartContext context, CancellationToken cancellationToken)
        {
            order.Add($"{name}:before-start");
            if (OnBeforeStart != null)
                await OnBeforeStart(context, cancellationToken);
        }

        public async Task AfterStartedAsync(AfterStartedContext context, CancellationToken cancellationToken)
        {
            order.Add($"{name}:after-started");
            if (OnAfterStarted != null)
                await OnAfterStarted(context, cancellationToken);
        }

        public async Task BeforeStopAsync(BeforeStopContext context, CancellationToken cancellationToken)
        {
            order.Add($"{name}:before-stop");
            if (OnBeforeStop != null)
                await OnBeforeStop(context, cancellationToken);
        }

        public async Task AfterStoppedAsync(AfterStoppedContext context, CancellationToken cancellationToken)
        {
            order.Add($"{name}:after-stopped");
            if (OnAfterStopped != null)
                await OnAfterStopped(context, cancellationToken);
        }
    }

    private sealed class TestDisposable(string name, List<string> order) : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
            order.Add($"{name}:disposed");
        }
    }

    private sealed class TestAsyncDisposable(string name, List<string> order) : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            order.Add($"{name}:async-disposed");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingDisposable(string name, List<string> order) : IDisposable
    {
        public void Dispose()
        {
            order.Add($"{name}:dispose-throw");
            throw new InvalidOperationException($"{name} failed");
        }
    }

    private sealed class RecordingObserver(List<string> order, object? gate = null)
    {
        public ValueTask HandleAsync(AgentEvent evt)
        {
            if (evt is TextDeltaEvent)
            {
                if (gate is null)
                {
                    order.Add("observer");
                }
                else
                {
                    lock (gate)
                        order.Add("observer");
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
                return;

            await Task.Delay(10);
        }

        Assert.True(predicate());
    }

    private static int CountOccurrences(string value, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    private async Task<AgentConfig> CreateBackgroundNotificationConfigAsync()
    {
        var store = new InMemorySessionStore();
        var session = new global::HPD.Agent.Session("session-1");
        var thread = new global::HPD.Agent.Thread("session-1", "thread-1") { Session = session };
        await store.SaveSessionAsync(session, TestCancellationToken);
        await store.AppendThreadEventAsync(
            session.Id,
            thread.Id,
            ThreadEventFactory.ThreadCreated(thread),
            cancellationToken: TestCancellationToken);

        var config = DefaultConfig();
        config.SessionStore = store;
        config.SessionStoreOptions = new SessionStoreOptions { PersistAfterTurn = true };
        return config;
    }

    private static string GetSingleNotificationSystemMessage(FakeChatClient fakeClient)
        => GetSingleNotificationSystemChatMessage(fakeClient).Text!;

    private static ChatMessage GetSingleNotificationSystemChatMessage(FakeChatClient fakeClient)
    {
        var notificationMessages = fakeClient.CapturedRequests
            .SelectMany(request => request)
            .Where(message =>
                message.Role == ChatRole.System &&
                message.Text?.Contains("<background-task-notifications>", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Single(notificationMessages);
        return notificationMessages[0];
    }

    [Fact]
    public void BackgroundTaskNotificationInput_CreatesHiddenModelContextOnlySystemMessage()
    {
        var input = new BackgroundTaskNotificationInputEvent([
            new BackgroundTaskNotification(
                "notification-1",
                ["task-1"],
                "Background task completed.",
                new Dictionary<string, string>
                {
                    ["status"] = "completed"
                })
        ]);

        var messagesInput = BackgroundTaskNotificationDispatcher.ToUserMessagesInput(input);
        var message = Assert.Single(messagesInput.Messages);

        Assert.Equal(ChatRole.System, message.Role);
        Assert.Equal(AgentMessageSource.BackgroundNotification, message.GetSource());
        Assert.Equal(AgentMessageVisibility.Hidden, message.GetVisibility());
        Assert.Equal(AgentMessagePersistence.ModelContextOnly, message.GetPersistence());
        Assert.Contains("<background-task-notifications>", message.Text);
    }

    [Fact]
    public void ThreadMessageEventConverter_PreservesMessagePolicyOnTextEvents()
    {
        var message = new ChatMessage(ChatRole.System, "hidden context")
            .WithPolicy(
                AgentMessageSource.RuntimeContext,
                AgentMessageVisibility.Hidden,
                AgentMessagePersistence.ThreadHistory);

        var events = ThreadMessageEventConverter.ToThreadEvents("session-1", "thread-1", message);

        var textStarted = Assert.Single(events.OfType<TextMessageStartEvent>());
        Assert.Equal(AgentMessageSource.RuntimeContext, textStarted.Source);
        Assert.Equal(AgentMessageVisibility.Hidden, textStarted.Visibility);
        Assert.Equal(AgentMessagePersistence.ThreadHistory, textStarted.Persistence);
    }

    private static BackgroundTaskCompletedEvent CreateCompletedBackgroundTaskEvent(
        string taskId,
        string name,
        BackgroundTaskNotificationRule policy,
        string? sessionId = "session-1",
        string? threadId = "thread-1",
        FunctionInvocationSnapshot? invocation = null,
        bool includeInvocation = true,
        BackgroundTaskSourceKind sourceKind = BackgroundTaskSourceKind.ToolCall,
        string? sourceId = "call-1",
        IReadOnlyDictionary<string, string>? metadata = null,
        string? summary = null)
        => new()
        {
            TaskId = taskId,
            Name = name,
            SourceKind = sourceKind,
            SourceId = sourceId,
            SessionId = sessionId,
            ThreadId = threadId,
            Invocation = includeInvocation ? invocation ?? CreateInvocationSnapshot(sourceId ?? "call-1") : null,
            Notification = policy,
            Metadata = metadata,
            CompletedAt = DateTimeOffset.UtcNow,
            DurationMilliseconds = 1,
            Summary = summary
        };

    private static BackgroundTaskFaultedEvent CreateFaultedBackgroundTaskEvent(
        string taskId,
        string name,
        BackgroundTaskNotificationRule policy,
        string? sessionId = "session-1",
        string? threadId = "thread-1",
        FunctionInvocationSnapshot? invocation = null,
        string? sourceId = "call-1")
        => new()
        {
            TaskId = taskId,
            Name = name,
            SourceKind = BackgroundTaskSourceKind.ToolCall,
            SourceId = sourceId,
            SessionId = sessionId,
            ThreadId = threadId,
            Invocation = invocation ?? CreateInvocationSnapshot(sourceId ?? "call-1"),
            Notification = policy,
            FaultedAt = DateTimeOffset.UtcNow,
            ExceptionType = typeof(InvalidOperationException).FullName!,
            ErrorMessage = "boom"
        };

    private static BackgroundTaskDescriptor RuntimeBackgroundTaskDescriptor(string name = "test-runtime-background") =>
        new()
        {
            Name = name,
            SourceKind = BackgroundTaskSourceKind.Runtime,
            Notification = BackgroundTaskNotificationRule.None
        };

    private static void RegisterRuntimeBackgroundTask(
        RuntimeHookContext context,
        Func<CancellationToken, Task> taskFactory,
        string name = "test-runtime-background")
    {
        context.RegisterBackgroundTask(
            RuntimeBackgroundTaskDescriptor(name),
            (_, runtimeToken) => taskFactory(runtimeToken));
    }

    private static void RegisterRuntimeBackgroundTask(
        RuntimeHookContext context,
        Task task,
        string name = "test-runtime-background")
    {
        ArgumentNullException.ThrowIfNull(task);

        context.RegisterBackgroundTask(
            RuntimeBackgroundTaskDescriptor(name),
            (_, _) => task);
    }

    private sealed class RuntimeProbeObserver(
        List<RuntimeHookProbeEvent> events,
        TaskCompletionSource? observed = null)
    {
        public ValueTask HandleAsync(AgentEvent evt)
        {
            if (evt is RuntimeHookProbeEvent probe)
            {
                events.Add(probe);
                observed?.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TurnCountingMiddleware : IAgentMiddleware
    {
        public int BeforeMessageTurnCalls { get; private set; }

        public Task BeforeMessageTurnAsync(
            BeforeMessageTurnContext context,
            CancellationToken cancellationToken)
        {
            BeforeMessageTurnCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class PermissionWaitMiddleware : IAgentMiddleware
    {
        public const string PermissionId = "runtime-permission-1";

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<PermissionResponseEvent> Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task BeforeMessageTurnAsync(
            BeforeMessageTurnContext context,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();

            var response = await context.RequestAsync<PermissionRequestEvent, PermissionResponseEvent>(
                new PermissionRequestEvent(
                    PermissionId,
                    "PermissionWaitMiddleware",
                    "RuntimeTool",
                    "Approve runtime continuation",
                    "call-1",
                    null),
                TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Completed.TrySetResult(response);
        }
    }

    [Fact]
    public async Task StartAsync_SetsIsRunning_StopAsyncClearsIt()
    {
        var agent = CreateAgent(client: new FakeChatClient());

        Assert.False(agent.IsRunning);

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        Assert.True(agent.IsRunning);

        await agent.StopAsync(TestCancellationToken);

        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task StopAsync_WhenNotRunning_IsNoOp()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order);
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StopAsync(TestCancellationToken);

        Assert.Empty(order);
        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task Dispose_CallsStopAsync_AndRunsRuntimeStopHooks()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order);
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        agent.Dispose();

        Assert.Contains("A:before-stop", order);
        Assert.Contains("A:after-stopped", order);
        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task StartStopAsync_ExecutesRuntimeHooks_InExpectedOrder()
    {
        var order = new List<string>();
        var middlewareA = new RuntimeHookRecordingMiddleware("A", order);
        var middlewareB = new RuntimeHookRecordingMiddleware("B", order);
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middlewareA, middlewareB]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal(
            [
                "A:before-start",
                "B:before-start",
                "B:after-started",
                "A:after-started",
                "B:before-stop",
                "A:before-stop",
                "B:after-stopped",
                "A:after-stopped"
            ],
            order);
    }

    [Fact]
    public async Task StartAsync_CancelStart_PreventsRuntimeLoop()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                context.CancelStart = true;
                context.CancelReason = "nope";
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.StartAsync(cancellationToken: TestCancellationToken));

        Assert.Contains("nope", ex.Message);
        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task StartAsync_CancelStart_DisposesRegisteredResources()
    {
        var order = new List<string>();
        var disposable = new TestDisposable("resource", order);
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                context.RegisterDisposable(disposable);
                context.CancelStart = true;
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.StartAsync(cancellationToken: TestCancellationToken));

        Assert.False(agent.IsRunning);
        Assert.True(disposable.Disposed);
    }

    [Fact]
    public async Task StartAsync_CancelStart_DisposesRuntimeCoordinator()
    {
        var order = new List<string>();
        IEventCoordinator? runtimeCoordinator = null;
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                runtimeCoordinator = context.EventCoordinator;
                context.CancelStart = true;
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.StartAsync(cancellationToken: TestCancellationToken));

        Assert.NotNull(runtimeCoordinator);
        Assert.Throws<ObjectDisposedException>(() =>
            runtimeCoordinator.Emit(new RuntimeHookProbeEvent("after-cancel", "runtime")));
    }

    [Fact]
    public async Task RuntimeCapabilities_CanBeRegisteredDuringStartupAndAreSealedAfterStarted()
    {
        var order = new List<string>();
        IRuntimeCapabilityRegistry? registry = null;
        var beforeStartCapability = new RuntimeCapabilityProbe("before-start");
        var afterStartedCapability = new RuntimeCapabilityProbe("after-started");
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                registry = context.RuntimeCapabilities;
                Assert.False(context.RuntimeCapabilities.IsSealed);
                context.RuntimeCapabilities.Set<IBeforeStartRuntimeCapability>(beforeStartCapability);
                return Task.CompletedTask;
            },
            OnAfterStarted = (context, _) =>
            {
                Assert.False(context.RuntimeCapabilities.IsSealed);
                context.RuntimeCapabilities.Set<IAfterStartedRuntimeCapability>(afterStartedCapability);
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        Assert.NotNull(registry);
        Assert.True(registry.IsSealed);
        Assert.Same(beforeStartCapability, registry.GetRequired<IBeforeStartRuntimeCapability>());
        Assert.Same(afterStartedCapability, registry.GetRequired<IAfterStartedRuntimeCapability>());
        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.Set<IBeforeStartRuntimeCapability>(new RuntimeCapabilityProbe("late")));
        Assert.Contains("sealed", ex.Message);

        await agent.StopAsync(TestCancellationToken);
    }

    [Fact]
    public async Task RuntimeFunctionExecutor_ExecutesRegisteredToolWithoutChatModelCall()
    {
        IRuntimeFunctionExecutor? executor = null;
        var middleware = new RuntimeHookRecordingMiddleware("A", [])
        {
            OnAfterStarted = (context, _) =>
            {
                executor = context.RuntimeCapabilities.GetRequired<IRuntimeFunctionExecutor>();
                return Task.CompletedTask;
            }
        };
        var fakeClient = new FakeChatClient();
        var tool = AIFunctionFactory.Create(
            (string value) => $"echo:{value}",
            name: "echo");
        var agent = CreateAgentWithMiddlewares(
            client: fakeClient,
            middlewares: [middleware],
            tools: tool);

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        Assert.NotNull(executor);
        var results = await executor.ExecuteFunctionCallsAsync(
            [
                new FunctionCallContent(
                    "call-1",
                    "echo",
                    new Dictionary<string, object?> { ["value"] = "runtime" })
            ],
            cancellationToken: TestCancellationToken);

        var result = Assert.Single(results);
        Assert.Equal("call-1", result.CallId);
        Assert.Equal("echo", result.FunctionName);
        Assert.True(result.Succeeded);
        Assert.False(result.WasUnknown);
        Assert.False(result.WasBlocked);
        Assert.Equal("echo:runtime", result.Payload.Json?.GetString());
        Assert.Empty(fakeClient.CapturedRequests);

        await agent.StopAsync(TestCancellationToken);
    }

    [Fact]
    public async Task RuntimeCapabilities_AreNotSealedWhenStartIsCancelled()
    {
        var order = new List<string>();
        IRuntimeCapabilityRegistry? registry = null;
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                registry = context.RuntimeCapabilities;
                context.RuntimeCapabilities.Set<IBeforeStartRuntimeCapability>(
                    new RuntimeCapabilityProbe("before-start"));
                context.CancelStart = true;
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.StartAsync(cancellationToken: TestCancellationToken));

        Assert.NotNull(registry);
        Assert.False(registry.IsSealed);
    }

    [Fact]
    public async Task StartAsync_CancellationTokenCancelledBeforeStart_DoesNotCreateRuntime()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order);
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            agent.StartAsync(cancellationToken: cts.Token));

        Assert.Empty(order);
        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task StartAsync_CancellationDuringBeforeStart_DoesNotAcceptInput()
    {
        var order = new List<string>();
        using var cts = new CancellationTokenSource();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (_, cancellationToken) =>
            {
                cts.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            agent.StartAsync(cancellationToken: cts.Token));

        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task RuntimeHook_Emit_DispatchesToAgentOnHandlers()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStarted = (context, _) =>
            {
                context.Emit(new RuntimeHookProbeEvent("started", context.RuntimeId));
                return Task.CompletedTask;
            },
            OnAfterStopped = (context, _) =>
            {
                context.Emit(new RuntimeHookProbeEvent("stopped", context.RuntimeId));
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);
        var events = new List<RuntimeHookProbeEvent>();

        agent.Subscribe<RuntimeHookProbeEvent>(events.Add);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Contains(events, e => e.Stage == "started");
        Assert.Contains(events, e => e.Stage == "stopped");
    }

    [Fact]
    public async Task StopAsync_DisposesRegisteredResources_InReverseOrder()
    {
        var order = new List<string>();
        var disposableA = new TestDisposable("resource-a", order);
        var disposableB = new TestDisposable("resource-b", order);
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                context.RegisterDisposable(disposableA);
                context.RegisterDisposable(disposableB);
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.True(disposableA.Disposed);
        Assert.True(disposableB.Disposed);
        Assert.True(order.IndexOf("resource-b:disposed") < order.IndexOf("resource-a:disposed"));
    }

    [Fact]
    public async Task StopAsync_BeforeStopFailure_StillStopsRuntime_AndRunsAfterStopped()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStop = (_, _) => throw new InvalidOperationException("before stop failed")
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            agent.StopAsync(TestCancellationToken));

        Assert.Contains("before stop failed", ex.Flatten().InnerExceptions.Select(e => e.Message));
        Assert.False(agent.IsRunning);
        Assert.Contains("A:after-stopped", order);
    }

    [Fact]
    public async Task StopAsync_DisposableFailure_IsAggregated_AfterAfterStoppedRuns()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                context.RegisterDisposable(new ThrowingDisposable("resource", order));
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            agent.StopAsync(TestCancellationToken));

        Assert.Contains("resource failed", ex.Flatten().InnerExceptions.Select(e => e.Message));
        Assert.Contains("A:after-stopped", order);
        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task AfterStoppedContext_IncludesReasonAndDuration()
    {
        var order = new List<string>();
        RuntimeStopReason? reason = null;
        TimeSpan? duration = null;
        DateTimeOffset? startedAt = null;
        DateTimeOffset? stoppedAt = null;
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStopped = (context, _) =>
            {
                reason = context.Reason;
                duration = context.Duration;
                startedAt = context.StartedAt;
                stoppedAt = context.StoppedAt;
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await Task.Delay(10, TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal(RuntimeStopReason.UserRequested, reason);
        Assert.True(duration.HasValue);
        Assert.True(duration.Value >= TimeSpan.Zero);
        Assert.True(startedAt.HasValue);
        Assert.True(stoppedAt.HasValue);
        Assert.True(stoppedAt.Value >= startedAt.Value);
    }

    [Fact]
    public async Task RuntimeContext_CreatedAt_BeforeStartedAt()
    {
        var order = new List<string>();
        DateTimeOffset? createdAt = null;
        DateTimeOffset? startedAt = null;
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStarted = (context, _) =>
            {
                createdAt = context.CreatedAt;
                startedAt = context.StartedAt;
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.True(createdAt.HasValue);
        Assert.True(startedAt.HasValue);
        Assert.True(createdAt.Value <= startedAt.Value);
    }

    [Fact]
    public async Task AfterStoppedContext_Error_IsSetOnFaultedStop()
    {
        var order = new List<string>();
        Exception? stopError = null;
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                context.RegisterDisposable(new ThrowingDisposable("resource", order));
                return Task.CompletedTask;
            },
            OnAfterStopped = (context, _) =>
            {
                stopError = context.Error;
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await Assert.ThrowsAsync<AggregateException>(() =>
            agent.StopAsync(TestCancellationToken));

        Assert.NotNull(stopError);
        Assert.Contains("resource failed", stopError.Message);
    }

    [Fact]
    public async Task BeforeStopContext_DefaultsToDrainPendingInputsTrue()
    {
        var order = new List<string>();
        bool? drainPendingInputs = null;
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStop = (context, _) =>
            {
                drainPendingInputs = context.DrainPendingInputs;
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.True(drainPendingInputs);
    }

    [Fact]
    public async Task RuntimeContext_HasStableRuntimeId()
    {
        var order = new List<string>();
        string? beforeStartRuntimeId = null;
        string? afterStartedRuntimeId = null;
        string? beforeStopRuntimeId = null;
        string? afterStoppedRuntimeId = null;
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                beforeStartRuntimeId = context.RuntimeId;
                return Task.CompletedTask;
            },
            OnAfterStarted = (context, _) =>
            {
                afterStartedRuntimeId = context.RuntimeId;
                return Task.CompletedTask;
            },
            OnBeforeStop = (context, _) =>
            {
                beforeStopRuntimeId = context.RuntimeId;
                return Task.CompletedTask;
            },
            OnAfterStopped = (context, _) =>
            {
                afterStoppedRuntimeId = context.RuntimeId;
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(beforeStartRuntimeId));
        Assert.Equal(beforeStartRuntimeId, afterStartedRuntimeId);
        Assert.Equal(beforeStartRuntimeId, beforeStopRuntimeId);
        Assert.Equal(beforeStartRuntimeId, afterStoppedRuntimeId);
    }

    [Fact]
    public void RuntimeHooks_DoNotExposeSessionOrThread()
    {
        var publicProperties = typeof(RuntimeHookContext)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet();

        Assert.DoesNotContain("Session", publicProperties);
        Assert.DoesNotContain("Thread", publicProperties);
        Assert.DoesNotContain("SessionId", publicProperties);
        Assert.DoesNotContain("ThreadId", publicProperties);
    }

    [Fact]
    public void RuntimeHooks_DoNotExposeTurnState()
    {
        var publicProperties = typeof(RuntimeHookContext)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet();

        Assert.DoesNotContain("State", publicProperties);
        Assert.DoesNotContain("ConversationId", publicProperties);
        Assert.DoesNotContain("TraceId", publicProperties);
    }

    [Fact]
    public async Task StartStopStartAsync_CreatesNewRuntimeContext_AndRunsHooksAgain()
    {
        var order = new List<string>();
        var runtimeIds = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStarted = (context, _) =>
            {
                runtimeIds.Add(context.RuntimeId);
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);
        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal(2, runtimeIds.Count);
        Assert.NotEqual(runtimeIds[0], runtimeIds[1]);
        Assert.Equal(2, order.Count(item => item == "A:before-start"));
        Assert.Equal(2, order.Count(item => item == "A:after-stopped"));
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_DoesNotRunHooksAgain()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order);
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Single(order, item => item == "A:before-start");
        Assert.Single(order, item => item == "A:after-started");
    }

    [Fact]
    public async Task ConcurrentStartAsync_OnlyStartsOneRuntime()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order);
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await Task.WhenAll(
            agent.StartAsync(cancellationToken: TestCancellationToken),
            agent.StartAsync(cancellationToken: TestCancellationToken),
            agent.StartAsync(cancellationToken: TestCancellationToken));

        await agent.StopAsync(TestCancellationToken);

        Assert.Single(order, item => item == "A:before-start");
        Assert.Single(order, item => item == "A:after-started");
    }

    [Fact]
    public async Task ConcurrentStopAsync_OnlyStopsOnce()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order);
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await Task.WhenAll(
            agent.StopAsync(TestCancellationToken),
            agent.StopAsync(TestCancellationToken),
            agent.StopAsync(TestCancellationToken));

        Assert.Single(order, item => item == "A:before-stop");
        Assert.Single(order, item => item == "A:after-stopped");
    }

    [Fact]
    public async Task StartAsync_BeforeStartThrows_DoesNotStartRuntime_AndDisposesRegisteredResources()
    {
        var order = new List<string>();
        var disposable = new TestDisposable("resource", order);
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                context.RegisterDisposable(disposable);
                throw new InvalidOperationException("startup failed");
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.StartAsync(cancellationToken: TestCancellationToken));

        Assert.Equal("startup failed", ex.Message);
        Assert.False(agent.IsRunning);
        Assert.True(disposable.Disposed);
    }

    [Fact]
    public async Task StartAsync_AfterStartedThrows_StopsRuntimeAndRunsAfterStopped()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStarted = (_, _) => throw new InvalidOperationException("after failed")
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.StartAsync(cancellationToken: TestCancellationToken));

        Assert.Equal("after failed", ex.Message);
        Assert.False(agent.IsRunning);
        Assert.Contains("A:after-stopped", order);
    }

    [Fact]
    public async Task StartAsync_AfterStartedThrows_PropagatesOriginalFailure()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStarted = (_, _) => throw new InvalidOperationException("original after-start failure")
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.StartAsync(cancellationToken: TestCancellationToken));

        Assert.Equal("original after-start failure", ex.Message);
    }

    [Fact]
    public async Task StartAsync_AfterStartedThrows_LeavesIsRunningFalse()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStarted = (_, _) => throw new InvalidOperationException("after failed")
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.StartAsync(cancellationToken: TestCancellationToken));

        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task RuntimeHook_Emit_DispatchesToOnAny_WithoutDuplicateParentBubble()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStarted = (context, _) =>
            {
                context.Emit(new RuntimeHookProbeEvent("started", context.RuntimeId));
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);
        var events = new List<RuntimeHookProbeEvent>();
        var seenStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        agent.SubscribeAny(evt =>
        {
            if (evt is RuntimeHookProbeEvent probe)
            {
                events.Add(probe);
                if (probe.Stage == "started")
                    seenStarted.TrySetResult();
            }
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await seenStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Single(events, e => e.Stage == "started");
    }

    [Fact]
    public async Task RuntimeHookEmit_DispatchesToObservers()
    {
        var order = new List<string>();
        var observed = new List<RuntimeHookProbeEvent>();
        var observedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new RuntimeProbeObserver(observed, observedSignal);
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStarted = (context, _) =>
            {
                context.Emit(new RuntimeHookProbeEvent("started", context.RuntimeId));
                return Task.CompletedTask;
            }
        };
        var agent = await new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithMiddleware(middleware)
            .WithEventSubscription(coordinator =>
                coordinator.Subscribe<AgentEvent>(observer.HandleAsync))
            .BuildAsync(TestCancellationToken);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await observedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Single(observed, e => e.Stage == "started");
    }

    [Fact]
    public async Task StopAsync_DisposesRegisteredAsyncDisposables_InReverseOrder()
    {
        var order = new List<string>();
        var disposableA = new TestAsyncDisposable("resource-a", order);
        var disposableB = new TestAsyncDisposable("resource-b", order);
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                context.RegisterAsyncDisposable(disposableA);
                context.RegisterAsyncDisposable(disposableB);
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.True(disposableA.Disposed);
        Assert.True(disposableB.Disposed);
        Assert.True(order.IndexOf("resource-b:async-disposed") < order.IndexOf("resource-a:async-disposed"));
    }

    [Fact]
    public async Task StopAsync_BeforeStopCanDisableDrain_AndCancelActiveTurn()
    {
        var blockingClient = new BlockingChatClient();
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStop = (context, _) =>
            {
                context.DrainPendingInputs = false;
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: blockingClient,
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync("block", cancellationToken: TestCancellationToken);
        await blockingClient.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        await agent.StopAsync(TestCancellationToken);

        await blockingClient.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task StopAsync_BeforeStopCanSetDrainTimeout()
    {
        var blockingClient = new BlockingChatClient();
        var order = new List<string>();
        TimeSpan? drainTimeout = null;
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStop = (context, _) =>
            {
                context.DrainTimeout = TimeSpan.FromMilliseconds(50);
                drainTimeout = context.DrainTimeout;
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: blockingClient,
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync("block", cancellationToken: TestCancellationToken);
        await blockingClient.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal(TimeSpan.FromMilliseconds(50), drainTimeout);
    }

    [Fact]
    public async Task StopAsync_DrainTimeout_CancelsActiveTurn()
    {
        var blockingClient = new BlockingChatClient();
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStop = (context, _) =>
            {
                context.DrainTimeout = TimeSpan.FromMilliseconds(50);
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: blockingClient,
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync("block", cancellationToken: TestCancellationToken);
        await blockingClient.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        await blockingClient.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task RegisterBackgroundTask_FactoryReceivesRuntimeToken_AndTokenIsCancelledOnStop()
    {
        var order = new List<string>();
        var tokenCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                RegisterRuntimeBackgroundTask(context, async runtimeToken =>
                {
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, runtimeToken);
                    }
                    catch (OperationCanceledException) when (runtimeToken.IsCancellationRequested)
                    {
                        tokenCancelled.TrySetResult();
                    }
                });

                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        await tokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
    }

    [Fact]
    public async Task RegisterBackgroundTask_TaskStartsDuringBeforeStart()
    {
        var order = new List<string>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                RegisterRuntimeBackgroundTask(context, _ =>
                {
                    started.TrySetResult();
                    return Task.CompletedTask;
                });

                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);
    }

    [Fact]
    public async Task StopAsync_BackgroundTaskCancellation_IsNotTreatedAsFailure()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                RegisterRuntimeBackgroundTask(context, runtimeToken =>
                    Task.Delay(Timeout.InfiniteTimeSpan, runtimeToken));
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task StopAsync_WaitsForRegisteredBackgroundTasks()
    {
        var order = new List<string>();
        var taskCompleted = false;
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                RegisterRuntimeBackgroundTask(context, async _ =>
                {
                    await Task.Delay(50, CancellationToken.None);
                    taskCompleted = true;
                });
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.True(taskCompleted);
    }

    [Fact]
    public async Task StopAsync_BackgroundTaskFault_IsAggregated()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                RegisterRuntimeBackgroundTask(context, Task.FromException(new InvalidOperationException("background failed")));
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            agent.StopAsync(TestCancellationToken));

        Assert.Contains("background failed", ex.Flatten().InnerExceptions.Select(e => e.Message));
        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task StopAsync_AfterStoppedFailure_IsAggregated()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStopped = (_, _) => throw new InvalidOperationException("after stopped failed")
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            agent.StopAsync(TestCancellationToken));

        Assert.Contains("after stopped failed", ex.Flatten().InnerExceptions.Select(e => e.Message));
        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task RegisteredResources_DisposeEvenIfStopHookThrows()
    {
        var order = new List<string>();
        var disposable = new TestDisposable("resource", order);
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                context.RegisterDisposable(disposable);
                return Task.CompletedTask;
            },
            OnBeforeStop = (_, _) => throw new InvalidOperationException("stop hook failed")
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await Assert.ThrowsAsync<AggregateException>(() =>
            agent.StopAsync(TestCancellationToken));

        Assert.True(disposable.Disposed);
    }

    [Fact]
    public async Task RegisteredResources_DisposeBeforeAfterStopped()
    {
        var order = new List<string>();
        var disposable = new TestDisposable("resource", order);
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                context.RegisterDisposable(disposable);
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.True(order.IndexOf("resource:disposed") < order.IndexOf("A:after-stopped"));
    }

    [Fact]
    public async Task AfterStopped_CanEmitFinalTelemetry()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStopped = (context, _) =>
            {
                context.Emit(new RuntimeHookProbeEvent("final", context.RuntimeId));
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);
        var events = new List<RuntimeHookProbeEvent>();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        agent.Subscribe<RuntimeHookProbeEvent>(evt =>
        {
            events.Add(evt);
            if (evt.Stage == "final")
                observed.TrySetResult();
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        Assert.Single(events, e => e.Stage == "final");
    }

    [Fact]
    public async Task RegisterDisposable_NullThrows()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                context.RegisterDisposable(null!);
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            agent.StartAsync(cancellationToken: TestCancellationToken));
    }

    [Fact]
    public async Task RegisterAsyncDisposable_NullThrows()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                context.RegisterAsyncDisposable(null!);
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            agent.StartAsync(cancellationToken: TestCancellationToken));
    }

    [Fact]
    public async Task RegisterBackgroundTask_NullThrows()
    {
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = (context, _) =>
            {
                RegisterRuntimeBackgroundTask(context, (Task)null!);
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            agent.StartAsync(cancellationToken: TestCancellationToken));
    }

    [Fact]
    public async Task RegisterBackgroundTask_WhenStopping_Throws()
    {
        var order = new List<string>();
        InvalidOperationException? registrationError = null;
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStop = (context, _) =>
            {
                registrationError = Assert.Throws<InvalidOperationException>(() =>
                    RegisterRuntimeBackgroundTask(context, Task.CompletedTask));
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.NotNull(registrationError);
        Assert.Contains("stopping or stopped", registrationError.Message);
    }

    [Fact]
    public async Task BackgroundTaskEvents_StartedAndCompleted_AreEmitted()
    {
        using var runtimeCts = new CancellationTokenSource();
        var runtimeContext = CreateRuntimeContext(runtimeCts.Token);
        var started = new TaskCompletionSource<BackgroundTaskStartedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<BackgroundTaskCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var startedSubscription = runtimeContext.EventCoordinator.Subscribe<BackgroundTaskStartedEvent>(evt =>
        {
            started.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        using var completedSubscription = runtimeContext.EventCoordinator.Subscribe<BackgroundTaskCompletedEvent>(evt =>
        {
            completed.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        var invocation = CreateInvocationSnapshot();

        RegisterDescriptorBackgroundTask(runtimeContext,
            "index-workspace",
            invocation,
            (_, _) => Task.CompletedTask);

        await runtimeContext.DisposeRegisteredResourcesAsync(TestCancellationToken);

        var startedEvent = await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        var completedEvent = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.Equal(startedEvent.TaskId, completedEvent.TaskId);
        Assert.Equal("index-workspace", startedEvent.Name);
        Assert.Equal(invocation, startedEvent.Invocation);
        Assert.True(completedEvent.DurationMilliseconds >= 0);
    }

    [Fact]
    public async Task BackgroundTaskCancelledEvent_EmittedOnRuntimeCancel()
    {
        using var runtimeCts = new CancellationTokenSource();
        var runtimeContext = CreateRuntimeContext(runtimeCts.Token);
        var cancelled = new TaskCompletionSource<BackgroundTaskCancelledEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = runtimeContext.EventCoordinator.Subscribe<BackgroundTaskCancelledEvent>(evt =>
        {
            cancelled.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        var invocation = CreateInvocationSnapshot();

        RegisterDescriptorBackgroundTask(runtimeContext,
            "watch-files",
            invocation,
            (_, runtimeToken) => Task.Delay(Timeout.InfiniteTimeSpan, runtimeToken));

        runtimeCts.Cancel();
        await runtimeContext.DisposeRegisteredResourcesAsync(TestCancellationToken);

        var cancelledEvent = await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.Equal("watch-files", cancelledEvent.Name);
        Assert.Equal(invocation, cancelledEvent.Invocation);
    }

    [Fact]
    public async Task BackgroundTaskFaultedEvent_EmittedOnException()
    {
        using var runtimeCts = new CancellationTokenSource();
        var runtimeContext = CreateRuntimeContext(runtimeCts.Token);
        var faulted = new TaskCompletionSource<BackgroundTaskFaultedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = runtimeContext.EventCoordinator.Subscribe<BackgroundTaskFaultedEvent>(evt =>
        {
            faulted.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        RegisterDescriptorBackgroundTask(runtimeContext,
            "crash",
            CreateInvocationSnapshot(),
            (_, _) => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<AggregateException>(() =>
            runtimeContext.DisposeRegisteredResourcesAsync(TestCancellationToken));

        var faultedEvent = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.Equal("crash", faultedEvent.Name);
        Assert.Equal(typeof(InvalidOperationException).FullName, faultedEvent.ExceptionType);
        Assert.Equal("boom", faultedEvent.ErrorMessage);
        Assert.Equal(EventKind.Diagnostic, faultedEvent.Kind);
    }

    [Fact]
    public async Task BackgroundTaskRegistration_GeneratesStableUniqueTaskIds()
    {
        using var runtimeCts = new CancellationTokenSource();
        var runtimeContext = CreateRuntimeContext(runtimeCts.Token);
        var startedEvents = new List<BackgroundTaskStartedEvent>();
        var completedEvents = new List<BackgroundTaskCompletedEvent>();
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var startedSubscription = runtimeContext.EventCoordinator.Subscribe<BackgroundTaskStartedEvent>(evt =>
        {
            lock (startedEvents)
            {
                startedEvents.Add(evt);
                if (startedEvents.Count == 5)
                    allStarted.TrySetResult();
            }
            return ValueTask.CompletedTask;
        });
        using var completedSubscription = runtimeContext.EventCoordinator.Subscribe<BackgroundTaskCompletedEvent>(evt =>
        {
            lock (completedEvents)
            {
                completedEvents.Add(evt);
                if (completedEvents.Count == 5)
                    allCompleted.TrySetResult();
            }
            return ValueTask.CompletedTask;
        });
        var invocation = CreateInvocationSnapshot();
        var registrations = new List<BackgroundTaskRegistration>();

        for (var i = 0; i < 5; i++)
        {
            registrations.Add(RegisterDescriptorBackgroundTask(runtimeContext,
                $"task-{i}",
                invocation,
                (_, _) => Task.CompletedTask));
        }

        await runtimeContext.DisposeRegisteredResourcesAsync(TestCancellationToken);
        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await allCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        Assert.Equal(5, startedEvents.Count);
        Assert.Equal(5, completedEvents.Count);
        Assert.Equal(5, registrations.Select(registration => registration.TaskId).Distinct().Count());
        Assert.Empty(registrations.Select(registration => registration.TaskId).Except(startedEvents.Select(evt => evt.TaskId)));
        Assert.Equal(5, startedEvents.Select(evt => evt.TaskId).Distinct().Count());
        Assert.Empty(startedEvents.Select(evt => evt.TaskId).Except(completedEvents.Select(evt => evt.TaskId)));
    }

    [Fact]
    public async Task BackgroundTaskCompletion_PopulatesCompletedEventSummaryAndMetadata()
    {
        using var runtimeCts = new CancellationTokenSource();
        var runtimeContext = CreateRuntimeContext(runtimeCts.Token);
        var completed = new TaskCompletionSource<BackgroundTaskCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var completedSubscription = runtimeContext.EventCoordinator.Subscribe<BackgroundTaskCompletedEvent>(evt =>
        {
            completed.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        RegisterDescriptorBackgroundTask(runtimeContext,
            "summarize",
            CreateInvocationSnapshot(),
            (background, _) =>
            {
                background.SetCompletion(
                    "done with useful details",
                    new Dictionary<string, string> { ["result.kind"] = "summary" });
                return Task.CompletedTask;
            });

        await runtimeContext.DisposeRegisteredResourcesAsync(TestCancellationToken);

        var completedEvent = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.Equal("done with useful details", completedEvent.Summary);
        Assert.NotNull(completedEvent.Metadata);
        Assert.Equal("summary", completedEvent.Metadata!["result.kind"]);
    }

    [Fact]
    public async Task BackgroundTask_CanEmitEvents()
    {
        using var runtimeCts = new CancellationTokenSource();
        var runtimeContext = CreateRuntimeContext(runtimeCts.Token);
        var observed = new TaskCompletionSource<RuntimeHookProbeEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = runtimeContext.EventCoordinator.Subscribe<RuntimeHookProbeEvent>(evt =>
        {
            observed.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        RegisterDescriptorBackgroundTask(runtimeContext,
            "emit-event",
            CreateInvocationSnapshot(),
            (background, _) =>
            {
                background.EventCoordinator!.Emit(new RuntimeHookProbeEvent("background", background.TaskId));
                return Task.CompletedTask;
            });

        await runtimeContext.DisposeRegisteredResourcesAsync(TestCancellationToken);

        var evt = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.Equal("background", evt.Stage);
        Assert.False(string.IsNullOrWhiteSpace(evt.RuntimeId));
    }

    [Fact]
    public async Task BackgroundTask_CanUseServices()
    {
        using var runtimeCts = new CancellationTokenSource();
        var service = new TestRuntimeService("runtime-service");
        var runtimeContext = CreateRuntimeContext(
            runtimeCts.Token,
            new SingleServiceProvider(service));
        TestRuntimeService? resolved = null;

        RegisterDescriptorBackgroundTask(runtimeContext,
            "use-services",
            CreateInvocationSnapshot(),
            (background, _) =>
            {
                resolved = (TestRuntimeService?)background.Services?.GetService(typeof(TestRuntimeService));
                return Task.CompletedTask;
            });

        await runtimeContext.DisposeRegisteredResourcesAsync(TestCancellationToken);

        Assert.Same(service, resolved);
    }

    [Fact]
    public async Task RuntimeStop_DoesNotMissTaskRegisteredBeforeStop()
    {
        using var runtimeCts = new CancellationTokenSource();
        var runtimeContext = CreateRuntimeContext(runtimeCts.Token);
        var taskStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var taskCompleted = false;

        RegisterDescriptorBackgroundTask(runtimeContext,
            "accepted-before-stop",
            CreateInvocationSnapshot(),
            async (_, _) =>
            {
                taskStarted.TrySetResult();
                await releaseTask.Task.WaitAsync(TestCancellationToken);
                taskCompleted = true;
            });

        await taskStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        runtimeContext.StopAcceptingBackgroundTaskRegistrations();
        var stopTask = runtimeContext.DisposeRegisteredResourcesAsync(TestCancellationToken);

        Assert.False(stopTask.IsCompleted);
        releaseTask.SetResult();
        await stopTask;

        Assert.True(taskCompleted);
    }

    [Fact]
    public async Task RuntimeStop_RejectsRegistrationAfterStopBegins()
    {
        using var runtimeCts = new CancellationTokenSource();
        var runtimeContext = CreateRuntimeContext(runtimeCts.Token);
        var taskStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        RegisterDescriptorBackgroundTask(runtimeContext,
            "block-stop",
            CreateInvocationSnapshot(),
            async (_, _) =>
            {
                taskStarted.TrySetResult();
                await releaseTask.Task.WaitAsync(TestCancellationToken);
            });

        await taskStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        runtimeContext.StopAcceptingBackgroundTaskRegistrations();
        var stopTask = runtimeContext.DisposeRegisteredResourcesAsync(TestCancellationToken);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RegisterDescriptorBackgroundTask(runtimeContext,
                "too-late",
                CreateInvocationSnapshot(),
                (_, _) => Task.CompletedTask));

        releaseTask.SetResult();
        await stopTask;
        Assert.Contains("stopping or stopped", ex.Message);
    }

    [Fact]
    public async Task BackgroundTask_FaultDuringStop_IsObserved()
    {
        using var runtimeCts = new CancellationTokenSource();
        var runtimeContext = CreateRuntimeContext(runtimeCts.Token);
        var faulted = new TaskCompletionSource<BackgroundTaskFaultedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = runtimeContext.EventCoordinator.Subscribe<BackgroundTaskFaultedEvent>(evt =>
        {
            faulted.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        RegisterDescriptorBackgroundTask(runtimeContext,
            "fault-during-stop",
            CreateInvocationSnapshot(),
            async (_, runtimeToken) =>
            {
                while (!runtimeToken.IsCancellationRequested)
                {
                    await Task.Delay(10, TestCancellationToken);
                }

                throw new InvalidOperationException("faulted during stop");
            });

        runtimeCts.Cancel();
        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            runtimeContext.DisposeRegisteredResourcesAsync(TestCancellationToken));
        var faultedEvent = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        Assert.Contains("faulted during stop", ex.Flatten().InnerExceptions.Select(error => error.Message));
        Assert.Equal("fault-during-stop", faultedEvent.Name);
        Assert.Equal("faulted during stop", faultedEvent.ErrorMessage);
    }

    [Fact]
    public async Task ManyBackgroundTasks_AllCompleteOrCancelCleanly()
    {
        using var runtimeCts = new CancellationTokenSource();
        var runtimeContext = CreateRuntimeContext(runtimeCts.Token);
        var completed = 0;
        const int taskCount = 200;

        for (var i = 0; i < taskCount; i++)
        {
            RegisterDescriptorBackgroundTask(runtimeContext,
                $"task-{i}",
                CreateInvocationSnapshot(functionCallId: $"call-{i}", toolCallIndex: i),
                async (_, runtimeToken) =>
                {
                    await Task.Yield();
                    runtimeToken.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref completed);
                });
        }

        await runtimeContext.DisposeRegisteredResourcesAsync(TestCancellationToken);

        Assert.Equal(taskCount, completed);
    }

    [Fact]
    public async Task EndToEnd_ToolRegistersBackgroundTask_RuntimeStopCleansUp()
    {
        using var runtimeCts = new CancellationTokenSource();
        var runtimeContext = CreateRuntimeContext(runtimeCts.Token);
        var taskStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var taskCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledEvent = new TaskCompletionSource<BackgroundTaskCancelledEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = runtimeContext.EventCoordinator.Subscribe<BackgroundTaskCancelledEvent>(evt =>
        {
            cancelledEvent.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        RegisterDescriptorBackgroundTask(runtimeContext,
            "long-running-tool-work",
            CreateInvocationSnapshot(functionCallId: "tool-call-99", toolCallIndex: 99),
            async (_, runtimeToken) =>
            {
                taskStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, runtimeToken);
                }
                catch (OperationCanceledException) when (runtimeToken.IsCancellationRequested)
                {
                    taskCancelled.TrySetResult();
                    throw;
                }
            });

        await taskStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        runtimeCts.Cancel();
        await runtimeContext.DisposeRegisteredResourcesAsync(TestCancellationToken);

        await taskCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        var evt = await cancelledEvent.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.Equal("long-running-tool-work", evt.Name);
        Assert.Equal("tool-call-99", evt.Invocation.FunctionCallId);
        Assert.Equal(99, evt.Invocation.ToolCallIndex);
    }

    [Fact]
    public async Task StartedRuntime_StructEvent_RoutesToRuntimeCoordinatorStructSubscriber()
    {
        var order = new List<string>();
        var received = new TaskCompletionSource<TestStructFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStarted = (context, _) =>
            {
                var subscription = context.StructEvents.Route<TestStructFrame>().Subscribe();
                context.RegisterDisposable(subscription);
                RegisterRuntimeBackgroundTask(context, async runtimeToken =>
                {
                    while (!runtimeToken.IsCancellationRequested)
                    {
                        if (subscription.TryRead(out var frame))
                        {
                            received.TrySetResult(frame);
                            return;
                        }

                        await Task.Delay(1, runtimeToken);
                    }
                });
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync(new TestStructFrame(123), TestCancellationToken);

        var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal(123, frame.Value);
    }

    [Fact]
    public async Task RuntimeMiddleware_CanSubscribeToRuntimeStructEvents()
    {
        var order = new List<string>();
        var received = new TaskCompletionSource<TestStructFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStarted = (context, _) =>
            {
                var subscription = context.StructEvents.Route<TestStructFrame>().Subscribe();
                context.RegisterDisposable(subscription);
                RegisterRuntimeBackgroundTask(context, async runtimeToken =>
                {
                    while (!runtimeToken.IsCancellationRequested)
                    {
                        if (subscription.TryRead(out var frame))
                        {
                            received.TrySetResult(frame);
                            return;
                        }

                        await Task.Delay(1, runtimeToken);
                    }
                });
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync(new TestStructFrame(321), TestCancellationToken);

        var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal(321, frame.Value);
    }

    [Fact]
    public async Task StartedRuntime_StructEvent_DispatchesAgentOnStructHandlersOnce()
    {
        var agent = CreateAgent(client: new FakeChatClient());
        var frames = new List<TestStructFrame>();

        agent.ObserveStruct<TestStructFrame>(frame =>
        {
            frames.Add(frame);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync(new TestStructFrame(456), TestCancellationToken);
        await Task.Delay(100, TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Single(frames);
        Assert.Equal(456, frames[0].Value);
    }

    [Fact]
    public async Task InterruptionRequest_WithStreamId_InterruptsRuntimeScopedStream()
    {
        var order = new List<string>();
        IEventFlowHandle? runtimeStream = null;
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStarted = (context, _) =>
            {
                runtimeStream = context.EventFlows.Create("runtime-stream");
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync(new InterruptionRequestEvent(
            eventFlowId: "runtime-stream",
            Reason: "runtime scoped",
            Source: InterruptionSource.User), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.NotNull(runtimeStream);
        Assert.True(runtimeStream.IsInterrupted);
    }

    [Fact]
    public async Task StartedAgent_RunAsync_QueuesTextInput_AndDispatchesOutputHandlers()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("runtime response");

        var agent = CreateAgent(client: fakeClient);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<AgentEvent>();

        using var subscription = agent.SubscribeAny(evt =>
        {
            events.Add(evt);
            if (evt is MessageTurnFinishedEvent)
                finished.TrySetResult();

            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync("hello", cancellationToken: TestCancellationToken);

        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Contains(events, e => e is TextDeltaEvent text && text.Text.Contains("runtime response"));
        Assert.Contains(events, e => e is MessageTurnFinishedEvent);
    }

    [Fact]
    public async Task RuntimeCoordinator_PersistsScopedPersistableEvents()
    {
        var store = new InMemorySessionStore();
        var session = new global::HPD.Agent.Session("session-bridge");
        var thread = new global::HPD.Agent.Thread(session.Id, "thread-bridge") { Session = session };
        await store.SaveSessionAsync(session, TestCancellationToken);
        await store.AppendThreadEventAsync(
            session.Id,
            thread.Id,
            ThreadEventFactory.ThreadCreated(thread),
            cancellationToken: TestCancellationToken);

        var config = DefaultConfig();
        config.SessionStore = store;
        var middleware = new RuntimeHookRecordingMiddleware("A", [])
        {
            OnAfterStarted = (context, _) =>
            {
                context.Emit(new RuntimeBridgePersistableEvent("durable-runtime-event")
                {
                    SessionId = session.Id,
                    ThreadId = thread.Id
                });
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            config: config,
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        var events = await WaitForThreadEventsAsync(
            store,
            session.Id,
            thread.Id,
            snapshot => snapshot.OfType<RuntimeBridgePersistableEvent>().Any(),
            TestCancellationToken);

        await agent.StopAsync(TestCancellationToken);

        var persisted = Assert.Single(events.OfType<RuntimeBridgePersistableEvent>());
        Assert.Equal("durable-runtime-event", persisted.Value);
        Assert.False(string.IsNullOrWhiteSpace(persisted.EventId));
        Assert.Equal(session.Id, persisted.SessionId);
        Assert.Equal(thread.Id, persisted.ThreadId);
    }

    [Fact]
    public async Task RuntimeCoordinator_DoesNotPersistScopedLiveOnlyEvents()
    {
        var store = new InMemorySessionStore();
        var session = new global::HPD.Agent.Session("session-live-only");
        var thread = new global::HPD.Agent.Thread(session.Id, "thread-live-only") { Session = session };
        await store.SaveSessionAsync(session, TestCancellationToken);
        await store.AppendThreadEventAsync(
            session.Id,
            thread.Id,
            ThreadEventFactory.ThreadCreated(thread),
            cancellationToken: TestCancellationToken);

        var config = DefaultConfig();
        config.SessionStore = store;
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var middleware = new RuntimeHookRecordingMiddleware("A", [])
        {
            OnAfterStarted = (context, _) =>
            {
                context.Emit(new RuntimeBridgeLiveOnlyEvent("live-runtime-event")
                {
                    SessionId = session.Id,
                    ThreadId = thread.Id
                });
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            config: config,
            client: new FakeChatClient(),
            middlewares: [middleware]);

        using var subscription = agent.Subscribe<RuntimeBridgeLiveOnlyEvent>(_ =>
        {
            observed.TrySetResult();
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await Task.Delay(100, TestCancellationToken);

        var events = await ReadThreadEventsAsync(store, session.Id, thread.Id, TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.DoesNotContain(events, evt => evt is RuntimeBridgeLiveOnlyEvent);
    }

    [Fact]
    public async Task RuntimeCoordinator_PersistsRuntimeInputLifecycleEventsOnce()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("runtime input response");
        var store = new InMemorySessionStore();
        var session = new global::HPD.Agent.Session("session-runtime-input");
        var thread = new global::HPD.Agent.Thread(session.Id, "thread-runtime-input") { Session = session };
        await store.SaveSessionAsync(session, TestCancellationToken);
        await store.AppendThreadEventAsync(
            session.Id,
            thread.Id,
            ThreadEventFactory.ThreadCreated(thread),
            cancellationToken: TestCancellationToken);

        var config = DefaultConfig();
        config.SessionStore = store;
        var agent = CreateAgentWithMiddlewares(config: config, client: fakeClient);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string runtimeRunId = "runtime-run-bridge";

        using var subscription = agent.Subscribe<ThreadRunCompletedEvent>(evt =>
        {
            if (evt.RuntimeRunId == runtimeRunId)
                completed.TrySetResult();

            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync(new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "hello runtime")],
            Session = session,
            Thread = thread,
            SessionId = session.Id,
            ThreadId = thread.Id,
            RuntimeRunId = runtimeRunId
        }, TestCancellationToken);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        var events = await WaitForThreadEventsAsync(
            store,
            session.Id,
            thread.Id,
            snapshot =>
                snapshot.OfType<ThreadRunStartedEvent>().Count(evt => evt.RuntimeRunId == runtimeRunId) == 1 &&
                snapshot.OfType<ThreadRunCompletedEvent>().Count(evt => evt.RuntimeRunId == runtimeRunId) == 1,
            TestCancellationToken);

        await agent.StopAsync(TestCancellationToken);

        Assert.Single(events.OfType<ThreadRunStartedEvent>(), evt => evt.RuntimeRunId == runtimeRunId);
        Assert.Single(events.OfType<ThreadRunCompletedEvent>(), evt => evt.RuntimeRunId == runtimeRunId);
    }

    [Fact]
    public async Task BackgroundTaskCompletion_WithNotificationRule_QueuesNotificationTurn()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("notification response");
        var store = new InMemorySessionStore();
        var session = new global::HPD.Agent.Session("session-1");
        var thread = new global::HPD.Agent.Thread("session-1", "thread-1") { Session = session };
        await store.SaveSessionAsync(session, TestCancellationToken);
        await store.AppendThreadEventAsync(
            session.Id,
            thread.Id,
            ThreadEventFactory.ThreadCreated(thread),
            cancellationToken: TestCancellationToken);
        var config = DefaultConfig();
        config.SessionStore = store;
        config.SessionStoreOptions = new SessionStoreOptions { PersistAfterTurn = true };
        var order = new List<string>();
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStarted = (context, _) =>
            {
                context.RegisterBackgroundTask(
                    new BackgroundTaskDescriptor
                    {
                        Name = "index-workspace",
                        SourceKind = BackgroundTaskSourceKind.ToolCall,
                        SourceId = "call-1",
                        Invocation = CreateInvocationSnapshot(),
                        Notification = new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true)
                    },
                    (_, _) => Task.CompletedTask);
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            config: config,
            client: fakeClient,
            middlewares: [middleware]);
        var events = new List<AgentEvent>();
        var delivered = new TaskCompletionSource<BackgroundTaskNotificationDeliveredEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = agent.SubscribeAny(evt =>
        {
            lock (events)
            {
                events.Add(evt);
            }

            if (evt is BackgroundTaskNotificationDeliveredEvent deliveredEvent)
                delivered.TrySetResult(deliveredEvent);

            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        var deliveredEvent = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        List<AgentEvent> snapshot;
        lock (events)
        {
            snapshot = events.ToList();
        }

        Assert.Contains(snapshot, evt => evt is BackgroundTaskNotificationQueuedEvent queued &&
            queued.NotificationId == deliveredEvent.NotificationId);
        Assert.Contains(snapshot, evt => evt is ThreadRunStartedEvent started &&
            started.RuntimeRunId == deliveredEvent.RuntimeRunId);
        Assert.Contains(snapshot, evt => evt is ThreadRunCompletedEvent completed &&
            completed.RuntimeRunId == deliveredEvent.RuntimeRunId);
        Assert.Contains(fakeClient.CapturedRequests, request =>
            request.Any(message =>
                message.Role == ChatRole.System &&
                message.Text?.Contains("<background-task-notifications>", StringComparison.Ordinal) == true));
    }

    [Fact]
    public async Task BackgroundTaskCompletion_BatchesNotificationsForSameThread()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("batched notification response");
        var store = new InMemorySessionStore();
        var session = new global::HPD.Agent.Session("session-1");
        var thread = new global::HPD.Agent.Thread("session-1", "thread-1") { Session = session };
        await store.SaveSessionAsync(session, TestCancellationToken);
        await store.AppendThreadEventAsync(
            session.Id,
            thread.Id,
            ThreadEventFactory.ThreadCreated(thread),
            cancellationToken: TestCancellationToken);
        var config = DefaultConfig();
        config.SessionStore = store;
        config.SessionStoreOptions = new SessionStoreOptions { PersistAfterTurn = true };
        var middleware = new RuntimeHookRecordingMiddleware("A", [])
        {
            OnAfterStarted = (context, _) =>
            {
                for (var i = 0; i < 2; i++)
                {
                    var callId = $"call-{i}";
                    context.RegisterBackgroundTask(
                        new BackgroundTaskDescriptor
                        {
                            Name = $"index-workspace-{i}",
                            SourceKind = BackgroundTaskSourceKind.ToolCall,
                            SourceId = callId,
                            Invocation = CreateInvocationSnapshot(callId, i),
                            Notification = new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true)
                        },
                        (_, _) => Task.CompletedTask);
                }

                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            config: config,
            client: fakeClient,
            middlewares: [middleware]);
        var deliveredCount = 0;
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = agent.Subscribe<BackgroundTaskNotificationDeliveredEvent>(_ =>
        {
            if (Interlocked.Increment(ref deliveredCount) == 2)
                delivered.TrySetResult();
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        var notificationRequests = fakeClient.CapturedRequests
            .Where(request => request.Any(message =>
                message.Role == ChatRole.System &&
                message.Text?.Contains("<background-task-notifications>", StringComparison.Ordinal) == true))
            .ToList();

        Assert.Single(notificationRequests);
        var notificationText = notificationRequests[0].Last(message =>
            message.Role == ChatRole.System &&
            message.Text?.Contains("<background-task-notifications>", StringComparison.Ordinal) == true).Text!;
        Assert.Equal(2, CountOccurrences(notificationText, "<notification id=\""));
    }

    [Fact]
    public async Task BackgroundTaskNotificationRule_None_SuppressesNotificationTurn()
    {
        var fakeClient = new FakeChatClient();
        var config = await CreateBackgroundNotificationConfigAsync();
        var middleware = new RuntimeHookRecordingMiddleware("A", [])
        {
            OnAfterStarted = (context, _) =>
            {
                context.Emit(CreateCompletedBackgroundTaskEvent(
                    taskId: "task-none",
                    name: "quiet-work",
                    policy: BackgroundTaskNotificationRule.None));
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            config: config,
            client: fakeClient,
            middlewares: [middleware]);
        var suppressed = new TaskCompletionSource<BackgroundTaskNotificationSuppressedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = agent.Subscribe<BackgroundTaskNotificationSuppressedEvent>(evt =>
        {
            suppressed.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        var suppressedEvent = await suppressed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal("rule-suppressed:none:completed", suppressedEvent.Reason);
        Assert.Empty(fakeClient.CapturedRequests);
    }

    [Fact]
    public async Task BackgroundTaskNotificationRule_OnFault_QueuesOnlyFaultedTasks()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("fault notification response");
        var config = await CreateBackgroundNotificationConfigAsync();
        var middleware = new RuntimeHookRecordingMiddleware("A", [])
        {
            OnAfterStarted = (context, _) =>
            {
                context.Emit(CreateCompletedBackgroundTaskEvent(
                    taskId: "task-completed",
                    name: "completed-work",
                    policy: new BackgroundTaskNotificationRule.OnFinalStateRule(Faulted: true)));
                context.Emit(CreateFaultedBackgroundTaskEvent(
                    taskId: "task-faulted",
                    name: "faulted-work",
                    policy: new BackgroundTaskNotificationRule.OnFinalStateRule(Faulted: true)));
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            config: config,
            client: fakeClient,
            middlewares: [middleware]);
        var suppressed = new TaskCompletionSource<BackgroundTaskNotificationSuppressedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource<BackgroundTaskNotificationDeliveredEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = agent.SubscribeAny(evt =>
        {
            if (evt is BackgroundTaskNotificationSuppressedEvent suppressedEvent)
                suppressed.TrySetResult(suppressedEvent);
            if (evt is BackgroundTaskNotificationDeliveredEvent deliveredEvent)
                delivered.TrySetResult(deliveredEvent);

            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        var suppressedEvent = await suppressed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal("rule-suppressed:on-final-state:completed", suppressedEvent.Reason);
        var notificationText = GetSingleNotificationSystemMessage(fakeClient);
        Assert.Contains("task-faulted", notificationText);
        Assert.DoesNotContain("task-completed", notificationText);
        Assert.Equal(1, CountOccurrences(notificationText, "<notification id=\""));
    }

    [Fact]
    public async Task BackgroundTaskNotification_MissingThreadScope_SuppressesNotificationTurn()
    {
        var fakeClient = new FakeChatClient();
        var config = await CreateBackgroundNotificationConfigAsync();
        var middleware = new RuntimeHookRecordingMiddleware("A", [])
        {
            OnAfterStarted = (context, _) =>
            {
                context.Emit(CreateCompletedBackgroundTaskEvent(
                    taskId: "task-missing-scope",
                    name: "orphan-work",
                    policy: new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true),
                    sessionId: null,
                    threadId: null,
                    includeInvocation: false));
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            config: config,
            client: fakeClient,
            middlewares: [middleware]);
        var suppressed = new TaskCompletionSource<BackgroundTaskNotificationSuppressedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = agent.Subscribe<BackgroundTaskNotificationSuppressedEvent>(evt =>
        {
            suppressed.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        var suppressedEvent = await suppressed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal("missing-thread-scope", suppressedEvent.Reason);
        Assert.Empty(fakeClient.CapturedRequests);
    }

    [Fact]
    public async Task BackgroundTaskNotification_RuntimeStoppingCancellation_SuppressesNotificationTurn()
    {
        var fakeClient = new FakeChatClient();
        var config = await CreateBackgroundNotificationConfigAsync();
        var middleware = new RuntimeHookRecordingMiddleware("A", [])
        {
            OnAfterStarted = (context, _) =>
            {
                context.Emit(new BackgroundTaskCancelledEvent
                {
                    TaskId = "task-cancelled",
                    Name = "stopping-work",
                    SourceKind = BackgroundTaskSourceKind.ToolCall,
                    SourceId = "call-cancelled",
                    SessionId = "session-1",
                    ThreadId = "thread-1",
                    Invocation = CreateInvocationSnapshot("call-cancelled"),
                    Notification = new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true, Faulted: true),
                    CancelledAt = DateTimeOffset.UtcNow,
                    Reason = "runtime-stopping"
                });
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            config: config,
            client: fakeClient,
            middlewares: [middleware]);
        var suppressed = new TaskCompletionSource<BackgroundTaskNotificationSuppressedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = agent.Subscribe<BackgroundTaskNotificationSuppressedEvent>(evt =>
        {
            suppressed.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        var suppressedEvent = await suppressed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal("runtime-stopping-cancellation", suppressedEvent.Reason);
        Assert.Empty(fakeClient.CapturedRequests);
    }

    [Fact]
    public async Task BackgroundTaskNotification_MetadataSuppression_SuppressesNotificationTurn()
    {
        var fakeClient = new FakeChatClient();
        var config = await CreateBackgroundNotificationConfigAsync();
        var middleware = new RuntimeHookRecordingMiddleware("A", [])
        {
            OnAfterStarted = (context, _) =>
            {
                context.Emit(CreateCompletedBackgroundTaskEvent(
                    taskId: "task-handled",
                    name: "handled-work",
                    policy: new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true),
                    metadata: new Dictionary<string, string>
                    {
                        [BackgroundTaskNotificationMetadataKeys.SuppressNotification] = "true",
                        [BackgroundTaskNotificationMetadataKeys.SuppressNotificationReason] = "handled-by-foreground-stop"
                    }));
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            config: config,
            client: fakeClient,
            middlewares: [middleware]);
        var suppressed = new TaskCompletionSource<BackgroundTaskNotificationSuppressedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = agent.Subscribe<BackgroundTaskNotificationSuppressedEvent>(evt =>
        {
            suppressed.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        var suppressedEvent = await suppressed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal("handled-by-foreground-stop", suppressedEvent.Reason);
        Assert.Empty(fakeClient.CapturedRequests);
    }

    [Fact]
    public async Task StopAsync_DrainsPendingBackgroundTaskNotificationBatch()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("drained notification response");
        var config = await CreateBackgroundNotificationConfigAsync();
        var middleware = new RuntimeHookRecordingMiddleware("A", [])
        {
            OnAfterStarted = (context, _) =>
            {
                context.Emit(CreateCompletedBackgroundTaskEvent(
                    taskId: "task-drained",
                    name: "drained-work",
                    policy: new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true),
                    summary: "Drained background task completed."));
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            config: config,
            client: fakeClient,
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        var notificationText = GetSingleNotificationSystemMessage(fakeClient);
        Assert.Contains("Drained background task completed.", notificationText);
    }

    [Fact]
    public async Task BackgroundTaskNotification_CommandMetadata_AppearsInSystemMessage()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("command notification response");
        var config = await CreateBackgroundNotificationConfigAsync();
        var middleware = new RuntimeHookRecordingMiddleware("A", [])
        {
            OnAfterStarted = (context, _) =>
            {
                context.Emit(CreateCompletedBackgroundTaskEvent(
                    taskId: "task-command",
                    name: "dotnet-test",
                    policy: new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true, Faulted: true),
                    sourceKind: BackgroundTaskSourceKind.Command,
                    sourceId: "command-1",
                    metadata: new Dictionary<string, string>
                    {
                        ["command"] = "dotnet test",
                        ["cwd"] = "/repo",
                        ["baseCommand"] = "dotnet",
                        ["category"] = "test"
                    },
                    summary: "Command completed."));
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            config: config,
            client: fakeClient,
            middlewares: [middleware]);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = agent.Subscribe<BackgroundTaskNotificationDeliveredEvent>(_ =>
        {
            delivered.TrySetResult();
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        var notificationText = GetSingleNotificationSystemMessage(fakeClient);
        Assert.Contains("<entry key=\"sourceKind\">Command</entry>", notificationText);
        Assert.Contains("<entry key=\"sourceId\">command-1</entry>", notificationText);
        Assert.Contains("<entry key=\"task.command\">dotnet test</entry>", notificationText);
        Assert.Contains("<entry key=\"task.cwd\">/repo</entry>", notificationText);
        Assert.Contains("<entry key=\"task.baseCommand\">dotnet</entry>", notificationText);
        Assert.Contains("<entry key=\"task.category\">test</entry>", notificationText);
    }

    [Fact]
    public async Task BackgroundTaskNotification_UsesLatestRuntimeInputRunConfig()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("user response");
        fakeClient.EnqueueTextResponse("notification response");
        var config = await CreateBackgroundNotificationConfigAsync();
        config.Clients = null;
        AfterStartedContext? runtimeContext = null;
        var middleware = new RuntimeHookRecordingMiddleware("A", [])
        {
            OnAfterStarted = (context, _) =>
            {
                runtimeContext = context;
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            config: config,
            client: new FakeChatClient(),
            middlewares: [middleware]);
        var userFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource<BackgroundTaskNotificationDeliveredEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = agent.SubscribeAny(evt =>
        {
            if (evt is MessageTurnFinishedEvent)
                userFinished.TrySetResult();
            if (evt is BackgroundTaskNotificationDeliveredEvent deliveredEvent)
                delivered.TrySetResult(deliveredEvent);

            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        Assert.NotNull(runtimeContext);

        await agent.RunAsync(
            new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "hello")],
                AgentId = "TestAgent",
                SessionId = "session-1",
                ThreadId = "thread-1",
                RuntimeRunId = "user-run",
                RunConfig = new AgentRunConfig { OverrideChatClient = fakeClient }
            },
            TestCancellationToken);

        await userFinished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        runtimeContext!.Emit(CreateCompletedBackgroundTaskEvent(
            taskId: "task-runtime-config",
            name: "runtime-config-work",
            policy: new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true)));

        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal(2, fakeClient.CapturedRequests.Count);
        var notificationText = GetSingleNotificationSystemMessage(fakeClient);
        Assert.Contains("runtime-config-work", notificationText);
    }

    [Fact]
    public async Task BackgroundTaskNotification_DuplicateFinalStateEvent_IsSuppressed()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("duplicate notification response");
        var config = await CreateBackgroundNotificationConfigAsync();
        var finalStateEvent = CreateCompletedBackgroundTaskEvent(
            taskId: "task-duplicate",
            name: "duplicate-work",
            policy: new BackgroundTaskNotificationRule.OnFinalStateRule(Completed: true));
        var middleware = new RuntimeHookRecordingMiddleware("A", [])
        {
            OnAfterStarted = (context, _) =>
            {
                context.Emit(finalStateEvent);
                context.Emit(finalStateEvent);
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            config: config,
            client: fakeClient,
            middlewares: [middleware]);
        var delivered = new TaskCompletionSource<BackgroundTaskNotificationDeliveredEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var duplicateSuppressed = new TaskCompletionSource<BackgroundTaskNotificationSuppressedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = agent.SubscribeAny(evt =>
        {
            if (evt is BackgroundTaskNotificationDeliveredEvent deliveredEvent)
                delivered.TrySetResult(deliveredEvent);
            if (evt is BackgroundTaskNotificationSuppressedEvent suppressedEvent &&
                suppressedEvent.Reason == "duplicate-final-state-notification")
            {
                duplicateSuppressed.TrySetResult(suppressedEvent);
            }

            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        var suppressed = await duplicateSuppressed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal("duplicate-final-state-notification", suppressed.Reason);
        var notificationText = GetSingleNotificationSystemMessage(fakeClient);
        Assert.Equal(1, CountOccurrences(notificationText, "<notification id=\""));
    }

    [Fact]
    public async Task StartedAgent_QueuesTextInputsInOrder()
    {
        var client = new DelayedChatClient(TimeSpan.FromMilliseconds(20));
        var agent = CreateAgent(client: client);
        var finishedCount = 0;
        var allFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        agent.Subscribe<MessageTurnFinishedEvent>(_ =>
        {
            if (Interlocked.Increment(ref finishedCount) == 2)
                allFinished.TrySetResult();
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync("first", cancellationToken: TestCancellationToken);
        await agent.RunAsync("second", cancellationToken: TestCancellationToken);
        await allFinished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal(["first", "second"], client.Requests);
    }

    [Fact]
    public async Task RuntimeContext_RunAsync_FromBeforeStart_QueuesInputForRuntimeLoop()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("queued from before start");
        var order = new List<string>();
        var turnCounter = new TurnCountingMiddleware();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = async (context, cancellationToken) =>
            {
                await context.RunAsync(
                    new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "from-before-start")] },
                    cancellationToken);
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: fakeClient,
            middlewares: [middleware, turnCounter]);

        agent.Subscribe<MessageTurnFinishedEvent>(_ => finished.TrySetResult());

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Equal(1, turnCounter.BeforeMessageTurnCalls);
        Assert.Single(fakeClient.CapturedRequests);
        Assert.Contains(fakeClient.CapturedRequests[0], message => message.Text == "from-before-start");
    }

    [Fact]
    public async Task RuntimeContext_RunAsync_FromBackgroundTask_EnqueuesWithoutRunningInline()
    {
        var blockingClient = new BlockingChatClient();
        var order = new List<string>();
        var enqueueReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStarted = (context, _) =>
            {
                RegisterRuntimeBackgroundTask(context, async runtimeToken =>
                {
                    await context.RunAsync(new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "from-background")] }, runtimeToken);
                    enqueueReturned.TrySetResult();
                });

                return Task.CompletedTask;
            },
            OnBeforeStop = (context, _) =>
            {
                context.DrainPendingInputs = false;
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: blockingClient,
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        await enqueueReturned.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await blockingClient.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        await agent.StopAsync(TestCancellationToken);

        Assert.False(agent.IsRunning);
    }

    [Fact]
    public async Task RuntimeContext_RunAsync_AfterStop_Throws()
    {
        var order = new List<string>();
        AfterStartedContext? capturedContext = null;
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnAfterStarted = (context, _) =>
            {
                capturedContext = context;
                return Task.CompletedTask;
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.NotNull(capturedContext);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await capturedContext!.RunAsync(
                new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "after-stop")] },
                TestCancellationToken));
    }

    [Fact]
    public async Task RunAsync_DuringStart_DoesNotAcceptInputBeforeStarted()
    {
        var order = new List<string>();
        var beforeStartEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBeforeStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStart = async (_, _) =>
            {
                beforeStartEntered.TrySetResult();
                await releaseBeforeStart.Task.ConfigureAwait(false);
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);
        var startTask = agent.StartAsync(cancellationToken: TestCancellationToken);

        await beforeStartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.RunAsync("input-before-started", cancellationToken: TestCancellationToken));

        releaseBeforeStart.TrySetResult();
        await startTask;
        await agent.StopAsync(TestCancellationToken);
    }

    [Fact]
    public async Task RunAsync_DuringStop_DoesNotEnqueueNewInput()
    {
        var client = new DelayedChatClient(TimeSpan.FromMilliseconds(50));
        var order = new List<string>();
        var beforeStopEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBeforeStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var middleware = new RuntimeHookRecordingMiddleware("A", order)
        {
            OnBeforeStop = async (_, _) =>
            {
                beforeStopEntered.TrySetResult();
                await releaseBeforeStop.Task.ConfigureAwait(false);
            }
        };
        var agent = CreateAgentWithMiddlewares(
            client: client,
            middlewares: [middleware]);

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        var stopTask = agent.StopAsync(TestCancellationToken);
        await beforeStopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.RunAsync("input-during-stop", cancellationToken: TestCancellationToken));

        releaseBeforeStop.TrySetResult();
        await stopTask;

        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task StopAsync_DrainsQueuedTurnBeforeReturning()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("drained response");

        var agent = CreateAgent(client: fakeClient);
        var events = new List<AgentEvent>();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = agent.SubscribeAny(evt =>
        {
            events.Add(evt);
            if (evt is MessageTurnFinishedEvent)
                finished.TrySetResult();

            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync("hello", cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        Assert.False(agent.IsRunning);
        Assert.Contains(events, e => e is TextDeltaEvent text && text.Text.Contains("drained response"));
        Assert.Contains(events, e => e is MessageTurnFinishedEvent);
    }

    [Fact]
    public async Task StartedAgent_QueuesSameThreadInputsInOrder()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("response one");
        fakeClient.EnqueueTextResponse("response two");

        var agent = CreateAgent(client: fakeClient);
        var session = new global::HPD.Agent.Session("session-1");
        var thread = new Thread("session-1");

        await agent.StartAsync(cancellationToken: TestCancellationToken);

        await agent.RunAsync(new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "question one")],
            Session = session,
            Thread = thread
        }, TestCancellationToken);

        await agent.RunAsync(new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "question two")],
            Session = session,
            Thread = thread
        }, TestCancellationToken);

        await agent.StopAsync(TestCancellationToken);

        Assert.Contains(thread.Messages, m => m.Text == "question one");
        Assert.Contains(thread.Messages, m => m.Text == "response one");
        Assert.Contains(thread.Messages, m => m.Text == "question two");
        Assert.Contains(thread.Messages, m => m.Text == "response two");

        var texts = thread.Messages.Select(m => m.Text).ToList();
        Assert.True(texts.IndexOf("question one") < texts.IndexOf("response one"));
        Assert.True(texts.IndexOf("response one") < texts.IndexOf("question two"));
        Assert.True(texts.IndexOf("question two") < texts.IndexOf("response two"));
    }

    [Fact]
    public async Task InterruptionRequest_CancelsActiveRuntimeTurn_WhenNoStreamId()
    {
        var blockingClient = new BlockingChatClient();
        var agent = CreateAgent(client: blockingClient);
        var interruptions = new List<InterruptionHandledEvent>();
        var gate = new object();

        using var subscription = agent.Subscribe<InterruptionHandledEvent>((Func<InterruptionHandledEvent, ValueTask>)(evt =>
        {
            lock (gate)
                interruptions.Add(evt);
            return ValueTask.CompletedTask;
        }));

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync("block", cancellationToken: TestCancellationToken);

        await blockingClient.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        await agent.RunAsync(new InterruptionRequestEvent(
            eventFlowId: null,
            Reason: "stop",
            Source: InterruptionSource.User), TestCancellationToken);

        await blockingClient.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await WaitForAsync(() =>
        {
            lock (gate)
                return interruptions.Count == 1;
        });
        await agent.StopAsync(TestCancellationToken);

        List<InterruptionHandledEvent> snapshot;
        lock (gate)
            snapshot = interruptions.ToList();

        Assert.Single(snapshot);
        Assert.Equal("stop", snapshot[0].Reason);
    }

    [Fact]
    public async Task RunAsync_StructEvent_DispatchesOnStructHandlers()
    {
        var agent = CreateAgent(client: new FakeChatClient());
        var received = new TaskCompletionSource<TestStructFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

        agent.ObserveStruct<TestStructFrame>(frame =>
        {
            received.TrySetResult(frame);
            return ValueTask.CompletedTask;
        });

        await agent.RunAsync(new TestStructFrame(42), TestCancellationToken);

        var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.Equal(42, frame.Value);
    }

    [Fact]
    public async Task StoppedRuntime_StructEvent_RoutesToRootStructRouter()
    {
        var agent = CreateAgent(client: new FakeChatClient());
        var received = new TaskCompletionSource<TestStructFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

        agent.ObserveStruct<TestStructFrame>(frame =>
        {
            received.TrySetResult(frame);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);
        await agent.RunAsync(new TestStructFrame(77), TestCancellationToken);

        var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.Equal(77, frame.Value);
    }

    [Fact]
    public async Task OnStruct_ReceivesCoordinatorStructEventsWithoutCoordinatorPump()
    {
        var agent = CreateAgent(client: new FakeChatClient());
        var received = new TaskCompletionSource<TestStructFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

        agent.ObserveStruct<TestStructFrame>(frame =>
        {
            received.TrySetResult(frame);
            return ValueTask.CompletedTask;
        });

        agent.StructEvents.Route<TestStructFrame>().CreateEmitter().Emit(new TestStructFrame(7));

        var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.Equal(7, frame.Value);
    }

    [Fact]
    public async Task StartedAgent_RunAsync_StructEvent_DispatchesOnStructHandlers()
    {
        var agent = CreateAgent(client: new FakeChatClient());
        var received = new TaskCompletionSource<TestStructFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

        agent.ObserveStruct<TestStructFrame>(frame =>
        {
            received.TrySetResult(frame);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync(new TestStructFrame(99), TestCancellationToken);

        var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.Equal(99, frame.Value);

        await agent.StopAsync(TestCancellationToken);
    }

    [Fact]
    public async Task OutputHandlers_RunTypedAndAnySubscribers()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("ordered");

        var agent = CreateAgent(client: fakeClient);
        var order = new List<string>();
        var gate = new object();

        using var typed1 = agent.Subscribe<TextDeltaEvent>(_ =>
        {
            lock (gate)
                order.Add("typed-1");
            return ValueTask.CompletedTask;
        });
        using var typed2 = agent.Subscribe<TextDeltaEvent>(_ =>
        {
            lock (gate)
                order.Add("typed-2");
            return ValueTask.CompletedTask;
        });
        using var any1 = agent.SubscribeAny(evt =>
        {
            if (evt is TextDeltaEvent)
            {
                lock (gate)
                    order.Add("any-1");
            }

            return ValueTask.CompletedTask;
        });
        using var any2 = agent.SubscribeAny(evt =>
        {
            if (evt is TextDeltaEvent)
            {
                lock (gate)
                    order.Add("any-2");
            }

            return ValueTask.CompletedTask;
        });

        await agent.RunAsync("hello", cancellationToken: TestCancellationToken);
        await WaitForAsync(() =>
        {
            lock (gate)
                return order.Count >= 4;
        });

        List<string> snapshot;
        lock (gate)
            snapshot = order.ToList();

        Assert.Equal(["any-1", "any-2", "typed-1", "typed-2"], snapshot.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Subscriptions_ReceiveEventsFromAgentAndBuilderRegistrations()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("ordered");

        var order = new List<string>();
        var gate = new object();
        var observer = new RecordingObserver(order, gate);

        var agent = await new AgentBuilder(DefaultConfig(), new TestProviderRegistry(fakeClient))
            .WithEventSubscription(coordinator =>
                coordinator.Subscribe<AgentEvent>(observer.HandleAsync))
            .WithCircuitBreaker(5)
            .WithErrorTracking(maxConsecutiveErrors: 3)
            .BuildAsync(TestCancellationToken);

        using var typed1 = agent.Subscribe<TextDeltaEvent>(_ =>
        {
            lock (gate)
                order.Add("typed-1");
            return ValueTask.CompletedTask;
        });
        using var typed2 = agent.Subscribe<TextDeltaEvent>(_ =>
        {
            lock (gate)
                order.Add("typed-2");
            return ValueTask.CompletedTask;
        });
        using var any1 = agent.SubscribeAny(evt =>
        {
            if (evt is TextDeltaEvent)
            {
                lock (gate)
                    order.Add("any-1");
            }

            return ValueTask.CompletedTask;
        });
        using var any2 = agent.SubscribeAny(evt =>
        {
            if (evt is TextDeltaEvent)
            {
                lock (gate)
                    order.Add("any-2");
            }

            return ValueTask.CompletedTask;
        });

        await agent.RunAsync("hello", cancellationToken: TestCancellationToken);
        await WaitForAsync(() =>
        {
            lock (gate)
                return order.Count >= 5;
        });

        List<string> snapshot;
        lock (gate)
            snapshot = order.ToList();

        Assert.Equal(["any-1", "any-2", "observer", "typed-1", "typed-2"], snapshot.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task OutputHandler_Exception_DoesNotStopLaterHandlersOrRun()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("still completes");

        var agent = CreateAgent(client: fakeClient);
        var laterHandlerCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var throwingSubscription = agent.Subscribe<TextDeltaEvent>(
            (Action<TextDeltaEvent>)(_ => throw new InvalidOperationException("handler failed")));
        using var laterSubscription = agent.Subscribe<TextDeltaEvent>(_ =>
        {
            laterHandlerCalled.TrySetResult();
        });
        using var finishedSubscription = agent.Subscribe<MessageTurnFinishedEvent>(_ =>
        {
            finished.TrySetResult();
        });

        await agent.RunAsync("hello", cancellationToken: TestCancellationToken);

        await laterHandlerCalled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
    }

    [Fact]
    public async Task UserMessagesInputEvent_IsNotAnOutputEvent_ButStillProducesOutput()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("processed");

        var agent = CreateAgent(client: fakeClient);
        var textOutputSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var textSubscription = agent.Subscribe<TextDeltaEvent>(_ => textOutputSeen.TrySetResult());

        await agent.RunAsync(new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "hello")] }, TestCancellationToken);

        await textOutputSeen.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
    }

    [Fact]
    public async Task UserMessagesInputEvent_StillUsesBeforeMessageTurn()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("processed");
        var middleware = new TurnCountingMiddleware();
        var agent = CreateAgentWithMiddlewares(
            client: fakeClient,
            middlewares: [middleware]);

        await agent.RunAsync(new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, "hello")] }, TestCancellationToken);

        Assert.Equal(1, middleware.BeforeMessageTurnCalls);
    }

    [Fact]
    public async Task PermissionResponseEvent_DoesNotRunBeforeMessageTurn()
    {
        var middleware = new TurnCountingMiddleware();
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.TryAnswerRequestAsync(new PermissionResponseEvent("perm-1", "source", true), TestCancellationToken);

        Assert.Equal(0, middleware.BeforeMessageTurnCalls);
    }

    [Fact]
    public async Task ClarificationResponseEvent_DoesNotRunBeforeMessageTurn()
    {
        var middleware = new TurnCountingMiddleware();
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.TryAnswerRequestAsync(new ClarificationResponseEvent("clar-1", "source", "question?", "answer"), TestCancellationToken);

        Assert.Equal(0, middleware.BeforeMessageTurnCalls);
    }

    [Fact]
    public async Task ContinuationResponseEvent_DoesNotRunBeforeMessageTurn()
    {
        var middleware = new TurnCountingMiddleware();
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.TryAnswerRequestAsync(new ContinuationResponseEvent("cont-1", "source", true), TestCancellationToken);

        Assert.Equal(0, middleware.BeforeMessageTurnCalls);
    }

    [Fact]
    public async Task ClientToolInvokeOutcomeEvent_DoesNotRunBeforeMessageTurn()
    {
        var middleware = new TurnCountingMiddleware();
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.TryAnswerRequestAsync(new ClientToolInvokeOutcomeEvent
        {
            RequestId = "client-tool-1",
            Outcome = ClientToolInvokeOutcomeKind.Completed,
            Content = [new HPD.Agent.ClientTools.TextContent("done")]
        }, TestCancellationToken);

        Assert.Equal(0, middleware.BeforeMessageTurnCalls);
    }

    [Fact]
    public async Task InterruptionRequestEvent_DoesNotRunBeforeMessageTurn()
    {
        var middleware = new TurnCountingMiddleware();
        var agent = CreateAgentWithMiddlewares(
            client: new FakeChatClient(),
            middlewares: [middleware]);

        await agent.RunAsync(new InterruptionRequestEvent(
            eventFlowId: null,
            Reason: "stop",
            Source: InterruptionSource.User), TestCancellationToken);

        Assert.Equal(0, middleware.BeforeMessageTurnCalls);
    }

    [Fact]
    public async Task RunAsync_WhenNotStarted_RunsOneShotDirectly()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("one shot");
        var agent = CreateAgent(client: fakeClient);
        var seenText = false;

        agent.Subscribe<TextDeltaEvent>(text =>
        {
            if (text.Text.Contains("one shot"))
                seenText = true;
        });

        await agent.RunAsync("hello", cancellationToken: TestCancellationToken);
        await WaitForAsync(() => seenText);

        Assert.False(agent.IsRunning);
        Assert.True(seenText);
    }

    [Fact]
    public async Task AnswerRequestAsync_NonEventResponseEvent_ThrowsArgumentException()
    {
        var agent = CreateAgent(client: new FakeChatClient());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            agent.AnswerRequestAsync(new NonEventResponseEvent(), TestCancellationToken));

        Assert.Contains("must also be an HPD.Events.Event", ex.Message);
    }

    [Fact]
    public async Task TryAnswerRequestAsync_ResponseEvents_WithNoActiveWaiter_ReturnsNotFound()
    {
        var agent = CreateAgent(client: new FakeChatClient());

        Assert.Equal(RespondStatus.NotFound, (await agent.TryAnswerRequestAsync(new PermissionResponseEvent("perm-1", "source", true), TestCancellationToken)).Status);
        Assert.Equal(RespondStatus.NotFound, (await agent.TryAnswerRequestAsync(new ContinuationResponseEvent("cont-1", "source", true), TestCancellationToken)).Status);
        Assert.Equal(RespondStatus.NotFound, (await agent.TryAnswerRequestAsync(new ClarificationResponseEvent("clar-1", "source", "question?", "answer"), TestCancellationToken)).Status);
        Assert.Equal(RespondStatus.NotFound, (await agent.TryAnswerRequestAsync(new ClientToolInvokeOutcomeEvent
        {
            RequestId = "client-tool-1",
            Outcome = ClientToolInvokeOutcomeKind.Completed,
            Content = [new HPD.Agent.ClientTools.TextContent("done")]
        }, TestCancellationToken)).Status);
    }

    [Fact]
    public async Task AnswerRequestAsync_CustomRequestEvent_RoutesByRequestId()
    {
        var agent = CreateAgent(client: new FakeChatClient());
        var waitTask = agent.EventCoordinator.RequestAsync<CustomRequestEvent, CustomRequestResponseEvent>(
            new CustomRequestEvent("custom-request", "custom-source"),
            TimeSpan.FromSeconds(5),
            TestCancellationToken);

        await agent.AnswerRequestAsync(new CustomRequestResponseEvent(
            "custom-request",
            "custom-source",
            "done"), TestCancellationToken);

        var response = await waitTask.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.Equal("done", response.Value);
    }

    [Fact]
    public async Task StartedRuntime_ResponseEvent_BypassesQueueAndUnblocksWaiter()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("after approval");

        var middleware = new PermissionWaitMiddleware();
        var agent = CreateAgentWithMiddlewares(
            client: fakeClient,
            middlewares: [middleware]);
        var requestSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = agent.Subscribe<PermissionRequestEvent>(evt =>
        {
            if (evt.PermissionId == PermissionWaitMiddleware.PermissionId)
                requestSeen.TrySetResult();
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync("needs approval", cancellationToken: TestCancellationToken);

        await requestSeen.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        await agent.AnswerRequestAsync(new PermissionResponseEvent(
            PermissionWaitMiddleware.PermissionId,
            "PermissionWaitMiddleware",
            Approved: true), TestCancellationToken);

        var response = await middleware.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.True(response.Approved);
        Assert.Single(fakeClient.CapturedRequests);
    }

    [Fact]
    public async Task StoppedRuntime_ResponseEvent_RoutesToRootCoordinator()
    {
        var agent = CreateAgent(client: new FakeChatClient());
        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        var waitTask = agent.EventCoordinator.RequestAsync<PermissionRequestEvent, PermissionResponseEvent>(
            new PermissionRequestEvent(
                "root-permission",
                "root",
                "RuntimeTool",
                "Approve",
                "call-1",
                null),
            TimeSpan.FromSeconds(5),
            TestCancellationToken);

        await agent.AnswerRequestAsync(new PermissionResponseEvent(
            "root-permission",
            "root",
            Approved: true), TestCancellationToken);

        var response = await waitTask.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.True(response.Approved);
    }

    [Fact]
    public async Task OneShotRunAsync_ResponseEvent_StillRoutesToRootCoordinator()
    {
        var agent = CreateAgent(client: new FakeChatClient());
        var waitTask = agent.EventCoordinator.RequestAsync<PermissionRequestEvent, PermissionResponseEvent>(
            new PermissionRequestEvent(
                "one-shot-permission",
                "root",
                "RuntimeTool",
                "Approve",
                "call-1",
                null),
            TimeSpan.FromSeconds(5),
            TestCancellationToken);

        await agent.AnswerRequestAsync(new PermissionResponseEvent(
            "one-shot-permission",
            "root",
            Approved: true), TestCancellationToken);

        var response = await waitTask.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.True(response.Approved);
    }

    [Fact]
    public async Task RuntimeWaiter_CompletesFromRootCoordinatorResponse()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("after approval");

        var middleware = new PermissionWaitMiddleware();
        var agent = CreateAgentWithMiddlewares(
            client: fakeClient,
            middlewares: [middleware]);
        var requestSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = agent.Subscribe<PermissionRequestEvent>(evt =>
        {
            if (evt.PermissionId == PermissionWaitMiddleware.PermissionId)
                requestSeen.TrySetResult();
        });

        await agent.StartAsync(cancellationToken: TestCancellationToken);
        await agent.RunAsync("needs approval", cancellationToken: TestCancellationToken);
        await requestSeen.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        agent.EventCoordinator.Respond(
            PermissionWaitMiddleware.PermissionId,
            new PermissionResponseEvent(
                PermissionWaitMiddleware.PermissionId,
                "root",
                Approved: true));

        var response = await middleware.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.True(response.Approved);
    }

    [Fact]
    public async Task InterruptionRequest_WithStreamId_InterruptsOnlyMatchingStream()
    {
        var agent = CreateAgent(client: new FakeChatClient());
        var stream1 = agent.EventCoordinator.EventFlows.Create("stream-1");
        var stream2 = agent.EventCoordinator.EventFlows.Create("stream-2");
        var interruptions = new List<InterruptionHandledEvent>();
        var gate = new object();

        using var subscription = agent.Subscribe<InterruptionHandledEvent>(evt =>
        {
            lock (gate)
                interruptions.Add(evt);
        });

        await agent.RunAsync(new InterruptionRequestEvent(
            eventFlowId: "stream-1",
            Reason: "targeted stop",
            Source: InterruptionSource.User), TestCancellationToken);
        await WaitForAsync(() =>
        {
            lock (gate)
                return interruptions.Count == 1;
        });

        Assert.True(stream1.IsInterrupted);
        Assert.False(stream2.IsInterrupted);

        List<InterruptionHandledEvent> snapshot;
        lock (gate)
            snapshot = interruptions.ToList();

        Assert.Single(snapshot);
        Assert.Equal("stream-1", snapshot[0].EventFlowId);
    }

    [Fact]
    public async Task Subscribe_Dispose_RemovesOutputHandlers()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("first");
        fakeClient.EnqueueTextResponse("second");

        var agent = CreateAgent(client: fakeClient);
        var typedCalls = 0;
        var anyTextDeltaCalls = 0;

        using (agent.Subscribe<TextDeltaEvent>(_ => typedCalls++))
        using (agent.SubscribeAny(evt =>
        {
            if (evt is TextDeltaEvent)
                anyTextDeltaCalls++;
        }))
        {
            await agent.RunAsync("first", cancellationToken: TestCancellationToken);
        }

        await agent.RunAsync("second", cancellationToken: TestCancellationToken);

        Assert.Equal(1, typedCalls);
        Assert.Equal(1, anyTextDeltaCalls);
    }

    [Fact]
    public async Task On_ActionAndTaskOverloads_InvokeCorrectly()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("overloads");

        var agent = CreateAgent(client: fakeClient);
        var actionCalled = false;
        var taskCalled = false;

        using var actionSubscription = agent.Subscribe<TextDeltaEvent>(
            (Action<TextDeltaEvent>)(_ => actionCalled = true));
        using var taskSubscription = agent.SubscribeAny((Func<AgentEvent, Task>)(evt =>
        {
            if (evt is TextDeltaEvent)
                taskCalled = true;

            return Task.CompletedTask;
        }));

        await agent.RunAsync("hello", cancellationToken: TestCancellationToken);
        await WaitForAsync(() => actionCalled && taskCalled);

        Assert.True(actionCalled);
        Assert.True(taskCalled);
    }

    private static AgentRuntimeContext CreateRuntimeContext(
        CancellationToken runtimeToken,
        IServiceProvider? services = null)
    {
        var inbox = Channel.CreateUnbounded<AgentInputEvent>();
        return new AgentRuntimeContext(
            "TestAgent",
            new AgentConfig(),
            services,
            new HPD.Events.Core.EventCoordinator(),
            new HPD.Events.Struct.StructEventHub(),
            inbox.Writer,
            (_, _) => ValueTask.CompletedTask,
            () => false,
            runtimeToken);
    }

    private static FunctionInvocationSnapshot CreateInvocationSnapshot(
        string functionCallId = "call-1",
        int toolCallIndex = 0)
        => new()
        {
            AgentName = "TestAgent",
            FunctionCallId = functionCallId,
            FunctionName = "TestFunction",
            ConversationId = "conversation-1",
            SessionId = "session-1",
            ThreadId = "thread-1",
            TraceId = "trace-1",
            Invocation = new ToolInvocationInfo("batch-1", functionCallId, "TestFunction", toolCallIndex)
        };

    private static async Task<List<AgentEvent>> WaitForThreadEventsAsync(
        ISessionStore store,
        string sessionId,
        string threadId,
        Func<IReadOnlyList<AgentEvent>, bool> predicate,
        CancellationToken cancellationToken)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            var events = await ReadThreadEventsAsync(store, sessionId, threadId, cancellationToken);
            if (predicate(events))
                return events;

            await Task.Delay(25, cancellationToken);
        }

        return await ReadThreadEventsAsync(store, sessionId, threadId, cancellationToken);
    }

    private static async Task<List<AgentEvent>> ReadThreadEventsAsync(
        ISessionStore store,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken)
    {
        var events = new List<AgentEvent>();
        await foreach (var evt in store.ReadThreadEventsAsync(
                           sessionId,
                           threadId,
                           ReplayReadOptions.All,
                           cancellationToken).ConfigureAwait(false))
        {
            events.Add(evt);
        }

        return events;
    }

    private static BackgroundTaskRegistration RegisterDescriptorBackgroundTask(
        AgentRuntimeContext runtimeContext,
        string name,
        FunctionInvocationSnapshot invocation,
        Func<BackgroundTaskContext, CancellationToken, Task> taskFactory)
    {
        return runtimeContext.RegisterBackgroundTask(
            new BackgroundTaskDescriptor
            {
                Name = name,
                SourceKind = BackgroundTaskSourceKind.ToolCall,
                SourceId = invocation.FunctionCallId,
                Invocation = invocation,
                Notification = new BackgroundTaskNotificationRule.OnFinalStateRule(Faulted: true)
            },
            taskFactory);
    }

    private sealed record TestRuntimeService(string Name);

    private sealed class SingleServiceProvider(object service) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == service.GetType() ? service : null;
    }
}
