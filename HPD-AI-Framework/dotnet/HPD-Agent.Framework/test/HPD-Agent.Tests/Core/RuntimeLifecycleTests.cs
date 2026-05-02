using System.Diagnostics;
using HPD.Agent.Tests.Infrastructure;
using HPD.Agent.ClientTools;
using HPD.Agent.Middleware;
using HPD.Events;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Core;

public class RuntimeLifecycleTests : AgentTestBase
{
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

    private readonly record struct TestStructFrame(
        int Value,
        long SequenceNumber = 0,
        long TimestampNs = 0) : IStructEvent
    {
        public EventKind Kind => EventKind.Content;
    }

    private sealed class RecordingObserver(List<string> order) : IAgentEventObserver
    {
        public bool ShouldProcess(AgentEvent evt) => evt is TextDeltaEvent;

        public Task OnEventAsync(AgentEvent evt, CancellationToken cancellationToken = default)
        {
            order.Add("observer");
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
            context.Emit(new PermissionRequestEvent(
                PermissionId,
                "PermissionWaitMiddleware",
                "RuntimeTool",
                "Approve runtime continuation",
                "call-1",
                null));

            var waitTask = context.WaitForResponseAsync<PermissionResponseEvent>(
                PermissionId,
                TimeSpan.FromSeconds(10));

            Started.TrySetResult();

            var response = await waitTask.ConfigureAwait(false);

            Completed.TrySetResult(response);
        }
    }

    [Fact]
    public async Task StartAsync_SetsIsRunning_StopAsyncClearsIt()
    {
        var agent = CreateAgent(client: new FakeChatClient());

        Assert.False(agent.IsRunning);

        await agent.StartAsync(TestCancellationToken);

        Assert.True(agent.IsRunning);

        await agent.StopAsync(TestCancellationToken);

        Assert.False(agent.IsRunning);
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

        await agent.StartAsync(TestCancellationToken);
        await agent.RunAsync("hello", cancellationToken: TestCancellationToken);

        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Contains(events, e => e is TextDeltaEvent text && text.Text.Contains("runtime response"));
        Assert.Contains(events, e => e is MessageTurnFinishedEvent);
    }

    [Fact]
    public async Task StopAsync_DrainsQueuedTurnBeforeReturning()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("drained response");

        var agent = CreateAgent(client: fakeClient);
        var events = new List<AgentEvent>();

        using var subscription = agent.SubscribeAny(evt =>
        {
            events.Add(evt);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(TestCancellationToken);
        await agent.RunAsync("hello", cancellationToken: TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.False(agent.IsRunning);
        Assert.Contains(events, e => e is TextDeltaEvent text && text.Text.Contains("drained response"));
        Assert.Contains(events, e => e is MessageTurnFinishedEvent);
    }

    [Fact]
    public async Task StartedAgent_QueuesSameBranchInputsInOrder()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("response one");
        fakeClient.EnqueueTextResponse("response two");

        var agent = CreateAgent(client: fakeClient);
        var session = new global::HPD.Agent.Session("session-1");
        var branch = new Branch("session-1");

        await agent.StartAsync(TestCancellationToken);

        await agent.RunAsync(new UserMessagesInputEvent([new ChatMessage(ChatRole.User, "question one")])
        {
            Session = session,
            Branch = branch
        }, TestCancellationToken);

        await agent.RunAsync(new UserMessagesInputEvent([new ChatMessage(ChatRole.User, "question two")])
        {
            Session = session,
            Branch = branch
        }, TestCancellationToken);

        await agent.StopAsync(TestCancellationToken);

        Assert.Contains(branch.Messages, m => m.Text == "question one");
        Assert.Contains(branch.Messages, m => m.Text == "response one");
        Assert.Contains(branch.Messages, m => m.Text == "question two");
        Assert.Contains(branch.Messages, m => m.Text == "response two");

        var texts = branch.Messages.Select(m => m.Text).ToList();
        Assert.True(texts.IndexOf("question one") < texts.IndexOf("response one"));
        Assert.True(texts.IndexOf("response one") < texts.IndexOf("question two"));
        Assert.True(texts.IndexOf("question two") < texts.IndexOf("response two"));
    }

    [Fact]
    public async Task InterruptionRequest_CancelsActiveRuntimeTurn_WhenNoStreamId()
    {
        var blockingClient = new BlockingChatClient();
        var agent = CreateAgent(client: blockingClient);
        var interruptions = new List<InterruptionRequestEvent>();

        using var subscription = agent.Subscribe<InterruptionRequestEvent>(evt =>
        {
            interruptions.Add(evt);
        });

        await agent.StartAsync(TestCancellationToken);
        await agent.RunAsync("block", cancellationToken: TestCancellationToken);

        await blockingClient.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        await agent.RunAsync(new InterruptionRequestEvent(
            StreamId: null,
            Reason: "stop",
            Source: InterruptionSource.User), TestCancellationToken);

        await blockingClient.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.Single(interruptions);
        Assert.Equal("stop", interruptions[0].Reason);
    }

    [Fact]
    public async Task RunAsync_StructEvent_DispatchesOnStructHandlers()
    {
        var agent = CreateAgent(client: new FakeChatClient());
        var received = new TaskCompletionSource<TestStructFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

        agent.OnStruct<TestStructFrame>(frame =>
        {
            received.TrySetResult(frame);
            return ValueTask.CompletedTask;
        });

        await agent.RunAsync(new TestStructFrame(42), TestCancellationToken);

        var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.Equal(42, frame.Value);
    }

    [Fact]
    public async Task OnStruct_ReceivesCoordinatorStructEventsWithoutCoordinatorPump()
    {
        var agent = CreateAgent(client: new FakeChatClient());
        var received = new TaskCompletionSource<TestStructFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

        agent.OnStruct<TestStructFrame>(frame =>
        {
            received.TrySetResult(frame);
            return ValueTask.CompletedTask;
        });

        await agent.EventCoordinator.EmitStructAsync(new TestStructFrame(7), TestCancellationToken);

        var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.Equal(7, frame.Value);
    }

    [Fact]
    public async Task StartedAgent_RunAsync_StructEvent_DispatchesOnStructHandlers()
    {
        var agent = CreateAgent(client: new FakeChatClient());
        var received = new TaskCompletionSource<TestStructFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

        agent.OnStruct<TestStructFrame>(frame =>
        {
            received.TrySetResult(frame);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync(TestCancellationToken);
        await agent.RunAsync(new TestStructFrame(99), TestCancellationToken);

        var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        Assert.Equal(99, frame.Value);

        await agent.StopAsync(TestCancellationToken);
    }

    [Fact]
    public async Task OutputHandlers_RunTypedBeforeOnAny_InRegistrationOrder()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("ordered");

        var agent = CreateAgent(client: fakeClient);
        var order = new List<string>();

        agent
            .On<TextDeltaEvent>(_ =>
            {
                order.Add("typed-1");
                return ValueTask.CompletedTask;
            })
            .On<TextDeltaEvent>(_ =>
            {
                order.Add("typed-2");
                return ValueTask.CompletedTask;
            })
            .OnAny(evt =>
            {
                if (evt is TextDeltaEvent)
                    order.Add("any-1");

                return ValueTask.CompletedTask;
            })
            .OnAny(evt =>
            {
                if (evt is TextDeltaEvent)
                    order.Add("any-2");

                return ValueTask.CompletedTask;
            });

        await agent.RunAsync("hello", cancellationToken: TestCancellationToken);

        Assert.Equal(["typed-1", "typed-2", "any-1", "any-2"], order);
    }

    [Fact]
    public async Task OnHandlers_RunTypedBeforeOnAny_ThenObservers()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("ordered");

        var order = new List<string>();
        var observer = new RecordingObserver(order);

        var agent = await new AgentBuilder(DefaultConfig(), new TestProviderRegistry(fakeClient))
            .WithObserver(observer)
            .WithCircuitBreaker(5)
            .WithErrorTracking(maxConsecutiveErrors: 3)
            .BuildAsync(TestCancellationToken);

        agent
            .On<TextDeltaEvent>(_ =>
            {
                order.Add("typed-1");
                return ValueTask.CompletedTask;
            })
            .On<TextDeltaEvent>(_ =>
            {
                order.Add("typed-2");
                return ValueTask.CompletedTask;
            })
            .OnAny(evt =>
            {
                if (evt is TextDeltaEvent)
                    order.Add("any-1");

                return ValueTask.CompletedTask;
            })
            .OnAny(evt =>
            {
                if (evt is TextDeltaEvent)
                    order.Add("any-2");

                return ValueTask.CompletedTask;
            });

        await agent.RunAsync("hello", cancellationToken: TestCancellationToken);
        await agent.FlushObserversAsync(TestCancellationToken);

        Assert.Equal(["typed-1", "typed-2", "any-1", "any-2", "observer"], order);
    }

    [Fact]
    public async Task OutputHandler_Exception_DoesNotStopLaterHandlersOrRun()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("still completes");

        var agent = CreateAgent(client: fakeClient);
        var laterHandlerCalled = false;
        var finished = false;

        agent
            .On<TextDeltaEvent>((Action<TextDeltaEvent>)(_ => throw new InvalidOperationException("handler failed")))
            .On<TextDeltaEvent>(_ =>
            {
                laterHandlerCalled = true;
            })
            .On<MessageTurnFinishedEvent>(_ =>
            {
                finished = true;
            });

        await agent.RunAsync("hello", cancellationToken: TestCancellationToken);

        Assert.True(laterHandlerCalled);
        Assert.True(finished);
    }

    [Fact]
    public async Task On_UserTextInputEvent_IsOutputOnly_DoesNotHandleInput()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("processed");

        var agent = CreateAgent(client: fakeClient);
        var inputHandlerCalls = 0;
        var textOutputSeen = false;

        agent
            .On<UserTextInputEvent>(_ => inputHandlerCalls++)
            .On<TextDeltaEvent>(_ => textOutputSeen = true);

        await agent.RunAsync(new UserTextInputEvent("hello"), TestCancellationToken);

        Assert.Equal(0, inputHandlerCalls);
        Assert.True(textOutputSeen);
    }

    [Fact]
    public async Task RunAsync_UnsupportedAgentEvent_ThrowsNotSupported()
    {
        var agent = CreateAgent(client: new FakeChatClient());

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            agent.RunAsync(new TextDeltaEvent("not input", "m1"), TestCancellationToken));

        Assert.Contains(nameof(TextDeltaEvent), ex.Message);
        Assert.Contains("cannot be used as agent input", ex.Message);
    }

    [Fact]
    public async Task RunAsync_ResponseEvents_WithNoActiveWaiter_AreNoOps()
    {
        var agent = CreateAgent(client: new FakeChatClient());

        await agent.RunAsync(new PermissionResponseEvent("perm-1", "source", true), TestCancellationToken);
        await agent.RunAsync(new ContinuationResponseEvent("cont-1", "source", true), TestCancellationToken);
        await agent.RunAsync(new ClarificationResponseEvent("clar-1", "source", "question?", "answer"), TestCancellationToken);
        await agent.RunAsync(new ClientToolInvokeResponseEvent("client-tool-1", "done"), TestCancellationToken);
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

        await agent.StartAsync(TestCancellationToken);
        await agent.RunAsync("needs approval", cancellationToken: TestCancellationToken);

        await middleware.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);

        await agent.RunAsync(new PermissionResponseEvent(
            PermissionWaitMiddleware.PermissionId,
            "PermissionWaitMiddleware",
            Approved: true), TestCancellationToken);

        var response = await middleware.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestCancellationToken);
        await agent.StopAsync(TestCancellationToken);

        Assert.True(response.Approved);
        Assert.Single(fakeClient.CapturedRequests);
    }

    [Fact]
    public async Task InterruptionRequest_WithStreamId_InterruptsOnlyMatchingStream()
    {
        var agent = CreateAgent(client: new FakeChatClient());
        var stream1 = agent.EventCoordinator.Streams.Create("stream-1");
        var stream2 = agent.EventCoordinator.Streams.Create("stream-2");
        var interruptions = new List<InterruptionRequestEvent>();

        using var subscription = agent.Subscribe<InterruptionRequestEvent>(interruptions.Add);

        await agent.RunAsync(new InterruptionRequestEvent(
            StreamId: "stream-1",
            Reason: "targeted stop",
            Source: InterruptionSource.User), TestCancellationToken);

        Assert.True(stream1.IsInterrupted);
        Assert.False(stream2.IsInterrupted);
        Assert.Single(interruptions);
        Assert.Equal("stream-1", interruptions[0].StreamId);
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

        agent
            .On<TextDeltaEvent>((Action<TextDeltaEvent>)(_ => actionCalled = true))
            .OnAny((Func<AgentEvent, Task>)(evt =>
            {
                if (evt is TextDeltaEvent)
                    taskCalled = true;

                return Task.CompletedTask;
            }));

        await agent.RunAsync("hello", cancellationToken: TestCancellationToken);

        Assert.True(actionCalled);
        Assert.True(taskCalled);
    }
}
