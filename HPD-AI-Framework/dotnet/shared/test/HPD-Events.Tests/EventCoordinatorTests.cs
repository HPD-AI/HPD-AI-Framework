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
    public async Task Emit_RoutesEventsToDeclaredChannels()
    {
        var coordinator = new EventCoordinator();

        coordinator.Emit(new TestStreamingEvent("streaming"));
        coordinator.Emit(new TestEvent("synchronous"));
        coordinator.Emit(new TestInteractiveEvent("interactive"));
        coordinator.Emit(new TestControlEvent("control"));

        Assert.Equal("streaming", Assert.IsType<TestStreamingEvent>(
            await ReadOneAsync(coordinator.ReadStreamingAsync())).Message);
        Assert.Equal("synchronous", Assert.IsType<TestEvent>(
            await ReadOneAsync(coordinator.ReadSynchronousAsync())).Message);
        Assert.Equal("interactive", Assert.IsType<TestInteractiveEvent>(
            await ReadOneAsync(coordinator.ReadInteractiveAsync())).Message);
        Assert.Equal("control", Assert.IsType<TestControlEvent>(
            await ReadOneAsync(coordinator.ReadControlAsync())).Message);
    }

    [Fact]
    public async Task LowLevelReaders_DoNotCrossReadChannels()
    {
        var coordinator = new EventCoordinator();
        coordinator.Emit(new TestStreamingEvent("streaming"));

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await ReadOneAsync(coordinator.ReadSynchronousAsync(cts.Token), cts.Token));
    }

    [Fact]
    public async Task SynchronousChannel_PreservesFifoOrder()
    {
        var coordinator = new EventCoordinator();
        coordinator.Emit(new TestEvent("1"));
        coordinator.Emit(new TestEvent("2"));
        coordinator.Emit(new TestEvent("3"));

        var events = await ReadManyAsync(coordinator.ReadSynchronousAsync(), 3);

        Assert.Equal(["1", "2", "3"], events.Cast<TestEvent>().Select(static evt => evt.Message));
    }

    [Fact]
    public async Task InteractiveChannel_PreservesFifoOrder()
    {
        var coordinator = new EventCoordinator();
        coordinator.Emit(new TestInteractiveEvent("1"));
        coordinator.Emit(new TestInteractiveEvent("2"));
        coordinator.Emit(new TestInteractiveEvent("3"));

        var events = await ReadManyAsync(coordinator.ReadInteractiveAsync(), 3);

        Assert.Equal(["1", "2", "3"], events.Cast<TestInteractiveEvent>().Select(static evt => evt.Message));
    }

    [Fact]
    public async Task ControlChannel_PreservesFifoOrder()
    {
        var coordinator = new EventCoordinator();
        coordinator.Emit(new TestControlEvent("1"));
        coordinator.Emit(new TestControlEvent("2"));
        coordinator.Emit(new TestControlEvent("3"));

        var events = await ReadManyAsync(coordinator.ReadControlAsync(), 3);

        Assert.Equal(["1", "2", "3"], events.Cast<TestControlEvent>().Select(static evt => evt.Message));
    }

    [Fact]
    public async Task RunAsync_DispatchesExactTypeHandler()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var handled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = coordinator
            .On<TestEvent>(evt =>
            {
                handled.TrySetResult(evt.Message);
                return ValueTask.CompletedTask;
            })
            .RunAsync(cts.Token);

        coordinator.Emit(new TestEvent("handled"));

        Assert.Equal("handled", await handled.Task.WaitAsync(cts.Token));
        await cts.CancelAsync();
        await runTask;
    }

    [Fact]
    public async Task RunAsync_DoesNotDispatchDerivedEventToBaseHandler()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var handled = false;

        var runTask = coordinator
            .On<BaseTestEvent>(_ =>
            {
                handled = true;
                return ValueTask.CompletedTask;
            })
            .RunAsync(cts.Token);

        coordinator.Emit(new DerivedTestEvent("derived"));

        await Task.Delay(50, CancellationToken.None);
        await cts.CancelAsync();
        await runTask;

        Assert.False(handled);
    }

    [Fact]
    public async Task OnAny_ReceivesEventsFromAllChannelsAfterExactHandlers()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var seen = new List<string>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = coordinator
            .On<TestEvent>(evt =>
            {
                seen.Add($"exact:{evt.Message}");
                return ValueTask.CompletedTask;
            })
            .OnAny(evt =>
            {
                seen.Add($"any:{evt.GetType().Name}");
                if (seen.Count == 5)
                    completed.TrySetResult();
                return ValueTask.CompletedTask;
            })
            .RunAsync(cts.Token);

        coordinator.Emit(new TestEvent("synchronous"));
        coordinator.Emit(new TestStreamingEvent("streaming"));
        coordinator.Emit(new TestInteractiveEvent("interactive"));
        coordinator.Emit(new TestControlEvent("control"));

        await completed.Task.WaitAsync(cts.Token);
        await cts.CancelAsync();
        await runTask;

        var exactIndex = seen.IndexOf("exact:synchronous");
        var anyIndex = seen.IndexOf("any:TestEvent");
        Assert.True(exactIndex >= 0);
        Assert.True(anyIndex > exactIndex);
        Assert.Contains("any:TestStreamingEvent", seen);
        Assert.Contains("any:TestInteractiveEvent", seen);
        Assert.Contains("any:TestControlEvent", seen);
    }

    [Fact]
    public async Task StreamingChannel_IsIsolatedFromSlowSynchronousHandler()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var releaseSynchronous = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var streamingHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = coordinator
            .On<TestEvent>(async _ => await releaseSynchronous.Task.WaitAsync(cts.Token))
            .On<TestStreamingEvent>(_ =>
            {
                streamingHandled.TrySetResult();
                return ValueTask.CompletedTask;
            })
            .RunAsync(cts.Token);

        coordinator.Emit(new TestEvent("slow"));
        coordinator.Emit(new TestStreamingEvent("fast"));

        await streamingHandled.Task.WaitAsync(cts.Token);
        releaseSynchronous.SetResult();
        await cts.CancelAsync();
        await runTask;
    }

    [Fact]
    public async Task GetStats_ReturnsChannelDepths()
    {
        var coordinator = new EventCoordinator();

        coordinator.Emit(new TestStreamingEvent("streaming"));
        coordinator.Emit(new TestEvent("synchronous"));
        coordinator.Emit(new TestInteractiveEvent("interactive"));
        coordinator.Emit(new TestControlEvent("control"));

        Assert.Equal(new EventCoordinatorStats(1, 1, 1, 1), coordinator.GetStats());

        _ = await ReadOneAsync(coordinator.ReadStreamingAsync());

        Assert.Equal(0, coordinator.GetStats().Streaming);
    }

    [Fact]
    public async Task StreamingChannel_DropsOldestWhenFullAndReportsControlDiagnostic()
    {
        var coordinator = new EventCoordinator();

        for (var i = 0; i < 300; i++)
            coordinator.Emit(new TestStreamingEvent(i.ToString()));

        var streamingEvents = await ReadManyAsync(coordinator.ReadStreamingAsync(), 256);
        var firstRemaining = Assert.IsType<TestStreamingEvent>(streamingEvents[0]);
        Assert.NotEqual("0", firstRemaining.Message);
        Assert.Equal("299", Assert.IsType<TestStreamingEvent>(streamingEvents[^1]).Message);

        var dropped = Assert.IsType<EventDroppedEvent>(await ReadOneAsync(coordinator.ReadControlAsync()));
        Assert.Equal(nameof(TestStreamingEvent), dropped.DroppedEventType);
    }

    [Fact]
    public void StreamingDropDiagnostic_DoesNotBlockWhenControlChannelIsFull()
    {
        var coordinator = new EventCoordinator();

        for (var i = 0; i < 64; i++)
            coordinator.Emit(new TestControlEvent(i.ToString()));

        for (var i = 0; i < 300; i++)
            coordinator.Emit(new TestStreamingEvent(i.ToString()));

        Assert.Equal(64, coordinator.GetStats().Control);
        Assert.Equal(256, coordinator.GetStats().Streaming);
    }

    [Fact]
    public void Emit_ThrowsImmediatelyWhenInteractiveChannelIsFull()
    {
        var coordinator = new EventCoordinator();

        for (var i = 0; i < 64; i++)
            coordinator.Emit(new TestInteractiveEvent(i.ToString()));

        var ex = Assert.Throws<InvalidOperationException>(
            () => coordinator.Emit(new TestInteractiveEvent("overflow")));

        Assert.Contains("Interactive", ex.Message);
        Assert.Contains("EmitAsync", ex.Message);
    }

    [Fact]
    public void Emit_ThrowsImmediatelyWhenControlChannelIsFull()
    {
        var coordinator = new EventCoordinator();

        for (var i = 0; i < 64; i++)
            coordinator.Emit(new TestControlEvent(i.ToString()));

        var ex = Assert.Throws<InvalidOperationException>(
            () => coordinator.Emit(new TestControlEvent("overflow")));

        Assert.Contains("Control", ex.Message);
        Assert.Contains("EmitAsync", ex.Message);
    }

    [Fact]
    public async Task EmitAsync_WaitsForInteractiveCapacity()
    {
        var coordinator = new EventCoordinator();

        for (var i = 0; i < 64; i++)
            coordinator.Emit(new TestInteractiveEvent(i.ToString()));

        var emitTask = coordinator.EmitAsync(new TestInteractiveEvent("after-capacity")).AsTask();
        await Task.Delay(50);

        Assert.False(emitTask.IsCompleted);

        _ = await ReadOneAsync(coordinator.ReadInteractiveAsync());

        await emitTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains(
            await ReadManyAsync(coordinator.ReadInteractiveAsync(), 64),
            evt => evt is TestInteractiveEvent { Message: "after-capacity" });
    }

    [Fact]
    public async Task EmitAsync_WaitsForControlCapacity()
    {
        var coordinator = new EventCoordinator();

        for (var i = 0; i < 64; i++)
            coordinator.Emit(new TestControlEvent(i.ToString()));

        var emitTask = coordinator.EmitAsync(new TestControlEvent("after-capacity")).AsTask();
        await Task.Delay(50);

        Assert.False(emitTask.IsCompleted);

        _ = await ReadOneAsync(coordinator.ReadControlAsync());

        await emitTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains(
            await ReadManyAsync(coordinator.ReadControlAsync(), 64),
            evt => evt is TestControlEvent { Message: "after-capacity" });
    }

    [Fact]
    public async Task EmitAsync_RespectsCancellationWhileWaitingForCapacity()
    {
        var coordinator = new EventCoordinator();

        for (var i = 0; i < 64; i++)
            coordinator.Emit(new TestControlEvent(i.ToString()));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await coordinator.EmitAsync(new TestControlEvent("cancelled"), cts.Token));
    }

    [Fact]
    public void SetParent_WithNullParent_ThrowsArgumentNullException()
    {
        var coordinator = new EventCoordinator();

        Assert.Throws<ArgumentNullException>(() => coordinator.SetParent(null!));
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
    public async Task SetParent_BubblesEventsToParent()
    {
        var parent = new EventCoordinator();
        var child = new EventCoordinator();
        child.SetParent(parent);

        child.Emit(new TestControlEvent("bubbled"));

        var result = await ReadOneAsync(parent.ReadControlAsync());

        Assert.Equal("bubbled", Assert.IsType<TestControlEvent>(result).Message);
        Assert.Equal(EventDirection.Upstream, result.Direction);
        Assert.Equal(EventChannel.Control, result.Channel);
    }

    [Fact]
    public async Task EventEnricher_EnrichesEventsBeforeEmission()
    {
        var coordinator = new EventCoordinator(
            eventEnricher: evt => evt with
            {
                Extensions = new Dictionary<string, object> { ["enriched"] = true }
            });

        coordinator.Emit(new TestEvent("test"));

        var result = await ReadOneAsync(coordinator.ReadSynchronousAsync());

        Assert.NotNull(result.Extensions);
        Assert.True((bool)result.Extensions["enriched"]);
    }

    [Fact]
    public async Task EventFilter_FiltersEvents()
    {
        var coordinator = new EventCoordinator(
            eventFilter: evt => evt is TestEvent { Message: "allowed" });

        coordinator.Emit(new TestEvent("allowed"));
        coordinator.Emit(new TestEvent("blocked"));

        var results = await ReadManyAsync(coordinator.ReadSynchronousAsync(), 1);

        Assert.Single(results);
        Assert.Equal("allowed", Assert.IsType<TestEvent>(results[0]).Message);
        Assert.Equal(0, coordinator.GetStats().Synchronous);
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
    public void SendResponse_IgnoresUnknownRequestId()
    {
        var coordinator = new EventCoordinator();

        coordinator.SendResponse("unknown", new TestEvent("response"));
    }

    [Fact]
    public async Task HandlerException_FaultsRunAsync()
    {
        using var coordinator = new EventCoordinator();

        var runTask = coordinator
            .On<TestEvent>(_ => throw new InvalidOperationException("handler failed"))
            .RunAsync();

        coordinator.Emit(new TestEvent("boom"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await runTask.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.Equal("handler failed", ex.Message);
    }

    [Fact]
    public async Task RunAsync_ExitsAfterDisposeCompletesWriters()
    {
        var coordinator = new EventCoordinator();
        var runTask = coordinator.RunAsync();

        coordinator.Dispose();

        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
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

    private static async Task<Event> ReadOneAsync(
        IAsyncEnumerable<Event> source,
        CancellationToken ct = default)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        await foreach (var evt in source.WithCancellation(linked.Token))
            return evt;

        throw new InvalidOperationException("No event was available.");
    }

    private static async Task<List<Event>> ReadManyAsync(
        IAsyncEnumerable<Event> source,
        int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var events = new List<Event>(count);

        await foreach (var evt in source.WithCancellation(timeout.Token))
        {
            events.Add(evt);
            if (events.Count == count)
                break;
        }

        return events;
    }
}
