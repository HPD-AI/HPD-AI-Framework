using System.Threading.Channels;
using HPD.Events;
using HPD.Events.Core;

namespace HPD.Events.Tests;

public class EventCoordinatorTests
{
    private record TestEvent(string Message) : Event;

    private record TestStreamingEvent(string Message) : Event
    {
        public override EventChannel Channel { get; init; } = EventChannel.Streaming;
    }

    private record TestInteractiveEvent(string Message) : Event
    {
        public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    }

    private record TestControlEvent(string Message) : Event
    {
        public override EventChannel Channel { get; init; } = EventChannel.Control;
        public override EventDirection Direction { get; init; } = EventDirection.Upstream;
    }

    private record BaseTestEvent(string Message) : Event;
    private record DerivedTestEvent(string Message) : BaseTestEvent(Message);

    [Fact]
    public void Emit_AssignsSequenceNumber()
    {
        var coordinator = new EventCoordinator();
        var evt = new TestEvent("test");

        coordinator.Emit(evt);

        Assert.Equal(1, evt.SequenceNumber);
    }

    [Fact]
    public void Emit_SequenceNumbers_AreMonotonicallyIncreasing()
    {
        var coordinator = new EventCoordinator();
        var evt1 = new TestEvent("first");
        var evt2 = new TestEvent("second");
        var evt3 = new TestEvent("third");

        coordinator.Emit(evt1);
        coordinator.Emit(evt2);
        coordinator.Emit(evt3);

        Assert.Equal(1, evt1.SequenceNumber);
        Assert.Equal(2, evt2.SequenceNumber);
        Assert.Equal(3, evt3.SequenceNumber);
    }

    [Fact]
    public async Task Subscribe_ReceivesEmittedEventWithoutRunAsync()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var handled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = coordinator.Subscribe<TestEvent>(evt =>
        {
            handled.TrySetResult(evt.Message);
            return ValueTask.CompletedTask;
        });

        coordinator.Emit(new TestEvent("handled"));

        Assert.Equal("handled", await handled.Task.WaitAsync(cts.Token));
    }

    [Fact]
    public async Task SubscribeAny_ReceivesEventsFromAllChannelsWithoutRunAsync()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var seen = new List<string>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = coordinator.SubscribeAny(evt =>
        {
            lock (seen)
            {
                seen.Add(evt.GetType().Name);
                if (seen.Count == 4)
                    completed.TrySetResult();
            }

            return ValueTask.CompletedTask;
        });

        coordinator.Emit(new TestEvent("synchronous"));
        coordinator.Emit(new TestStreamingEvent("streaming"));
        coordinator.Emit(new TestInteractiveEvent("interactive"));
        coordinator.Emit(new TestControlEvent("control"));

        await completed.Task.WaitAsync(cts.Token);

        Assert.Contains(nameof(TestEvent), seen);
        Assert.Contains(nameof(TestStreamingEvent), seen);
        Assert.Contains(nameof(TestInteractiveEvent), seen);
        Assert.Contains(nameof(TestControlEvent), seen);
    }

    [Fact]
    public async Task Subscribe_BaseTypeReceivesDerivedEventByDefault()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var handled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = coordinator.Subscribe<BaseTestEvent>(evt =>
        {
            handled.TrySetResult(evt.Message);
            return ValueTask.CompletedTask;
        });

        coordinator.Emit(new DerivedTestEvent("derived"));

        Assert.Equal("derived", await handled.Task.WaitAsync(cts.Token));
    }

    [Fact]
    public async Task Subscribe_ExactTypeOptionDoesNotReceiveDerivedEvent()
    {
        using var coordinator = new EventCoordinator();
        var handled = false;

        using var subscription = coordinator.Subscribe<BaseTestEvent>(
            _ =>
            {
                handled = true;
                return ValueTask.CompletedTask;
            },
            new EventSubscriptionOptions { IncludeDerivedTypes = false });

        coordinator.Emit(new DerivedTestEvent("derived"));
        await Task.Delay(50);

        Assert.False(handled);
    }

    [Fact]
    public async Task MultipleSubscribers_AllReceiveSameClassEvent()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var first = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var sub1 = coordinator.Subscribe<TestEvent>(evt =>
        {
            first.TrySetResult(evt.Message);
            return ValueTask.CompletedTask;
        });
        using var sub2 = coordinator.Subscribe<TestEvent>(evt =>
        {
            second.TrySetResult(evt.Message);
            return ValueTask.CompletedTask;
        });

        coordinator.Emit(new TestEvent("fan-out"));

        Assert.Equal("fan-out", await first.Task.WaitAsync(cts.Token));
        Assert.Equal("fan-out", await second.Task.WaitAsync(cts.Token));
    }

    [Fact]
    public async Task TypedSubscriberAndStreamSubscriber_BothReceiveSameEvent()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var handled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var stream = coordinator.SubscribeStream<TestEvent>();

        using var subscription = coordinator.Subscribe<TestEvent>(evt =>
        {
            handled.TrySetResult(evt.Message);
            return ValueTask.CompletedTask;
        });

        coordinator.Emit(new TestEvent("both"));

        Assert.Equal("both", await handled.Task.WaitAsync(cts.Token));
        Assert.Equal("both", (await ReadOneAsync(stream.Reader, cts.Token)).Message);
    }

    [Fact]
    public async Task ChannelSubscriber_ReceivesOnlyMatchingChannelWithoutStealing()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var streaming = coordinator.SubscribeChannel(EventChannel.Streaming);
        var handled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = coordinator.Subscribe<TestStreamingEvent>(evt =>
        {
            handled.TrySetResult(evt.Message);
            return ValueTask.CompletedTask;
        });

        coordinator.Emit(new TestEvent("sync"));
        coordinator.Emit(new TestStreamingEvent("stream"));

        Assert.Equal("stream", Assert.IsType<TestStreamingEvent>(
            await ReadOneAsync(streaming.Reader, cts.Token)).Message);
        Assert.Equal("stream", await handled.Task.WaitAsync(cts.Token));
        Assert.False(await WaitToReadSafelyAsync(streaming.Reader));
    }

    [Fact]
    public async Task SlowSubscriber_DoesNotBlockUnrelatedSubscriber()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fast = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var slowSub = coordinator.Subscribe<TestEvent>(
            async _ => await release.Task.WaitAsync(cts.Token));
        using var fastSub = coordinator.Subscribe<TestStreamingEvent>(_ =>
        {
            fast.TrySetResult();
            return ValueTask.CompletedTask;
        });

        coordinator.Emit(new TestEvent("slow"));
        coordinator.Emit(new TestStreamingEvent("fast"));

        await fast.Task.WaitAsync(cts.Token);
        release.SetResult();
    }

    [Fact]
    public async Task EmitAsync_WaitsForFullModeWaitSubscriber()
    {
        using var coordinator = new EventCoordinator();
        await using var stream = coordinator.SubscribeStream<TestEvent>(
            new EventSubscriptionOptions
            {
                Capacity = 1,
                FullMode = BoundedChannelFullMode.Wait
            });

        await coordinator.EmitAsync(new TestEvent("first"));
        var emitTask = coordinator.EmitAsync(new TestEvent("second")).AsTask();
        await Task.Delay(50);

        Assert.False(emitTask.IsCompleted);

        Assert.Equal("first", (await ReadOneAsync(stream.Reader)).Message);
        await emitTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("second", (await ReadOneAsync(stream.Reader)).Message);
    }

    [Fact]
    public async Task HandlerException_FaultsOnlyThatSubscriptionAndEmitsDiagnostic()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var diagnostics = coordinator.SubscribeStream<EventSubscriberFaultedEvent>();
        var later = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var throwing = coordinator.Subscribe<TestEvent>(
            _ => throw new InvalidOperationException("handler failed"));
        using var remaining = coordinator.Subscribe<TestEvent>(evt =>
        {
            later.TrySetResult(evt.Message);
            return ValueTask.CompletedTask;
        });

        coordinator.Emit(new TestEvent("boom"));

        var diagnostic = await ReadOneAsync(diagnostics.Reader, cts.Token);
        Assert.Equal(nameof(InvalidOperationException), diagnostic.ErrorType);
        Assert.Equal("handler failed", diagnostic.ErrorMessage);
        Assert.Equal("boom", await later.Task.WaitAsync(cts.Token));
    }

    [Fact]
    public async Task SetParent_BubblesEventsToParentSubscribersWithoutChangingSequence()
    {
        using var parent = new EventCoordinator();
        using var child = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        child.SetParent(parent);
        await using var parentStream = parent.SubscribeStream<TestControlEvent>();

        var evt = new TestControlEvent("bubbled");
        child.Emit(evt);

        var result = await ReadOneAsync(parentStream.Reader, cts.Token);

        Assert.Same(evt, result);
        Assert.Equal(1, result.SequenceNumber);
        Assert.Equal(EventDirection.Upstream, result.Direction);
        Assert.Equal(EventChannel.Control, result.Channel);
    }

    [Fact]
    public async Task SetParent_ParentEnricherAppliesToBubbledEventsWithoutChangingSequence()
    {
        using var parent = new EventCoordinator(
            eventEnricher: evt => evt with
            {
                Extensions = new Dictionary<string, object> { ["parent"] = true }
            });
        using var child = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        child.SetParent(parent);
        await using var parentStream = parent.SubscribeStream<TestControlEvent>();

        child.Emit(new TestControlEvent("bubbled"));

        var result = await ReadOneAsync(parentStream.Reader, cts.Token);

        Assert.Equal(1, result.SequenceNumber);
        Assert.NotNull(result.Extensions);
        Assert.True((bool)result.Extensions["parent"]);
    }

    [Fact]
    public async Task SetParent_ParentFilterAppliesToBubbledEvents()
    {
        using var parent = new EventCoordinator(
            eventFilter: evt => evt is TestControlEvent { Message: "allowed" });
        using var child = new EventCoordinator();
        child.SetParent(parent);
        await using var parentStream = parent.SubscribeStream<TestControlEvent>();

        child.Emit(new TestControlEvent("blocked"));
        child.Emit(new TestControlEvent("allowed"));

        Assert.Equal("allowed", (await ReadOneAsync(parentStream.Reader)).Message);
        Assert.False(await WaitToReadSafelyAsync(parentStream.Reader));
    }

    [Fact]
    public async Task EventEnricher_EnrichesEventsBeforeEmission()
    {
        using var coordinator = new EventCoordinator(
            eventEnricher: evt => evt with
            {
                Extensions = new Dictionary<string, object> { ["enriched"] = true }
            });
        await using var stream = coordinator.SubscribeStream<TestEvent>();

        coordinator.Emit(new TestEvent("test"));

        var result = await ReadOneAsync(stream.Reader);

        Assert.NotNull(result.Extensions);
        Assert.True((bool)result.Extensions["enriched"]);
    }

    [Fact]
    public async Task EventFilter_FiltersEvents()
    {
        using var coordinator = new EventCoordinator(
            eventFilter: evt => evt is TestEvent { Message: "allowed" });
        await using var stream = coordinator.SubscribeStream<TestEvent>();

        coordinator.Emit(new TestEvent("allowed"));
        coordinator.Emit(new TestEvent("blocked"));

        Assert.Equal("allowed", (await ReadOneAsync(stream.Reader)).Message);
        Assert.False(await WaitToReadSafelyAsync(stream.Reader));
    }

    [Fact]
    public async Task WaitForResponseAsync_ReturnsResponse()
    {
        var coordinator = new EventCoordinator();
        var requestId = "test-request";

        var responseTask = coordinator.WaitForResponseAsync<TestEvent>(
            requestId,
            TimeSpan.FromSeconds(5));

        coordinator.SendResponse(requestId, new TestEvent("response"));

        var result = await responseTask;

        Assert.Equal("response", result.Message);
    }

    [Fact]
    public async Task WaitForResponseAsync_ThrowsTimeoutException()
    {
        var coordinator = new EventCoordinator();

        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await coordinator.WaitForResponseAsync<TestEvent>(
                "missing-request",
                TimeSpan.FromMilliseconds(50));
        });
    }

    [Fact]
    public async Task WaitForResponseAsync_ThrowsOnTypeMismatch()
    {
        var coordinator = new EventCoordinator();
        var requestId = "test-request";

        var responseTask = coordinator.WaitForResponseAsync<TestControlEvent>(
            requestId,
            TimeSpan.FromSeconds(5));

        coordinator.SendResponse(requestId, new TestEvent("wrong-type"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await responseTask);
    }

    [Fact]
    public async Task WaitForResponseAsync_RejectsDuplicateRequestId()
    {
        var coordinator = new EventCoordinator();
        var requestId = "duplicate-request";

        var first = coordinator.WaitForResponseAsync<TestEvent>(
            requestId,
            TimeSpan.FromSeconds(5));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.WaitForResponseAsync<TestEvent>(
                requestId,
                TimeSpan.FromSeconds(5)));

        Assert.Contains("Duplicate request ID", ex.Message);

        coordinator.SendResponse(requestId, new TestEvent("response"));
        Assert.Equal("response", (await first).Message);
    }

    [Fact]
    public async Task SendResponse_RoutesToChildWaiter()
    {
        var parent = new EventCoordinator();
        var child = new EventCoordinator();
        child.SetParent(parent);

        var responseTask = child.WaitForResponseAsync<TestEvent>(
            "child-request",
            TimeSpan.FromSeconds(5));

        parent.SendResponse("child-request", new TestEvent("from-parent"));

        var response = await responseTask;
        Assert.Equal("from-parent", response.Message);
    }

    [Fact]
    public async Task SendResponse_ThrowsWhenRequestIdIsAmbiguousInHierarchy()
    {
        var parent = new EventCoordinator();
        var left = new EventCoordinator();
        var right = new EventCoordinator();
        left.SetParent(parent);
        right.SetParent(parent);

        var leftWait = left.WaitForResponseAsync<TestEvent>(
            "ambiguous-request",
            TimeSpan.FromSeconds(5));
        var rightWait = right.WaitForResponseAsync<TestEvent>(
            "ambiguous-request",
            TimeSpan.FromSeconds(5));

        var ex = Assert.Throws<InvalidOperationException>(
            () => parent.SendResponse("ambiguous-request", new TestEvent("response")));

        Assert.Contains("Multiple pending response waiters", ex.Message);

        left.SendResponse("ambiguous-request", new TestEvent("left-response"));
        right.SendResponse("ambiguous-request", new TestEvent("right-response"));

        Assert.Equal("left-response", (await leftWait).Message);
        Assert.Equal("right-response", (await rightWait).Message);
    }

    [Fact]
    public void SetParent_WithSelfReference_ThrowsInvalidOperationException()
    {
        var coordinator = new EventCoordinator();

        var ex = Assert.Throws<InvalidOperationException>(() => coordinator.SetParent(coordinator));

        Assert.Contains("Cannot set coordinator as its own parent", ex.Message);
    }

    [Fact]
    public void SetParent_WithCycle_ThrowsInvalidOperationException()
    {
        var coordinatorA = new EventCoordinator();
        var coordinatorB = new EventCoordinator();
        var coordinatorC = new EventCoordinator();

        coordinatorA.SetParent(coordinatorB);
        coordinatorB.SetParent(coordinatorC);

        var ex = Assert.Throws<InvalidOperationException>(() => coordinatorC.SetParent(coordinatorA));

        Assert.Contains("Cannot set parent: this would create a cycle", ex.Message);
    }

    [Fact]
    public async Task Subscribe_Dispose_RemovesHandler()
    {
        using var coordinator = new EventCoordinator();
        var handled = false;

        var subscription = coordinator.Subscribe<TestEvent>(_ =>
        {
            handled = true;
            return ValueTask.CompletedTask;
        });
        subscription.Dispose();

        coordinator.Emit(new TestEvent("removed"));
        await Task.Delay(50);

        Assert.False(handled);
    }

    [Fact]
    public void Dispose_PreventsFurtherEmission()
    {
        var coordinator = new EventCoordinator();

        coordinator.Dispose();

        Assert.Throws<ObjectDisposedException>(() => coordinator.Emit(new TestEvent("test")));
    }

    [Fact]
    public void StreamRegistry_IsAccessible()
    {
        var coordinator = new EventCoordinator();

        Assert.IsAssignableFrom<IStreamRegistry>(coordinator.Streams);
    }

    [Fact]
    public async Task GetStats_ReportsSubscriberHealth()
    {
        using var coordinator = new EventCoordinator();
        await using var stream = coordinator.SubscribeStream<TestEvent>();
        using var handler = coordinator.SubscribeAny(_ => ValueTask.CompletedTask);

        coordinator.Emit(new TestEvent("queued"));

        var stats = coordinator.GetStats();

        Assert.Equal(2, stats.SubscriberCount);
        Assert.Equal(1, stats.StreamSubscriberCount);
        Assert.True(stats.TotalQueued >= 1);
        Assert.True(stats.MaxSubscriberDepth >= 1);
    }

    private static async Task<TEvent> ReadOneAsync<TEvent>(
        ChannelReader<TEvent> reader,
        CancellationToken ct = default)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        return await reader.ReadAsync(linked.Token);
    }

    private static async Task<bool> WaitToReadSafelyAsync<TEvent>(ChannelReader<TEvent> reader)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try
        {
            return await reader.WaitToReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
