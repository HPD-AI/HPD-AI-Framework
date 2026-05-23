using System.Threading.Channels;
using HPD.Events;
using HPD.Events.Core;

namespace HPD.Events.Tests;

public class StructEventTests
{
    private readonly record struct TestStructEvent(
        string Message,
        long TimestampNs = 0,
        long SequenceNumber = 0) : IStructEvent, ISequencedStructEvent<TestStructEvent>
    {
        public EventKind Kind => EventKind.Content;

        public TestStructEvent WithSequenceNumber(long sequenceNumber) =>
            this with { SequenceNumber = sequenceNumber };
    }

    private record TestClassEvent(string Message) : Event;

    [Fact]
    public void TryEmitStruct_ReturnsFalse_WhenNoSubscriberAccepts()
    {
        using var coordinator = new EventCoordinator();

        var accepted = coordinator.TryEmitStruct(new TestStructEvent("none"));

        Assert.False(accepted);
    }

    [Fact]
    public async Task TryEmitStruct_FansOutToAllSubscribers()
    {
        using var coordinator = new EventCoordinator();
        await using var first = coordinator.SubscribeStruct<TestStructEvent>();
        await using var second = coordinator.SubscribeStruct<TestStructEvent>();

        Assert.True(coordinator.TryEmitStruct(new TestStructEvent("frame")));

        Assert.Equal("frame", (await ReadOneAsync(first.Reader)).Message);
        Assert.Equal("frame", (await ReadOneAsync(second.Reader)).Message);
    }

    [Fact]
    public async Task CreateInbox_ReceivesStructEvents()
    {
        using var coordinator = new EventCoordinator();
        await using var inbox = coordinator.CreateInbox<TestStructEvent>();

        Assert.True(coordinator.TryEmitStruct(new TestStructEvent("owned")));

        Assert.Equal("owned", (await ReadOneAsync(inbox.Reader)).Message);
    }

    [Fact]
    public async Task CreateInbox_DefaultsToBackpressureWait()
    {
        using var coordinator = new EventCoordinator();
        await using var inbox = coordinator.CreateInbox<TestStructEvent>(
            new StructInboxOptions { Capacity = 1 });

        await coordinator.EmitStructAsync(new TestStructEvent("1"));
        var emitTask = coordinator.EmitStructAsync(new TestStructEvent("2")).AsTask();

        await Task.Delay(50);
        Assert.False(emitTask.IsCompleted);

        Assert.Equal("1", (await ReadOneAsync(inbox.Reader)).Message);

        await emitTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("2", (await ReadOneAsync(inbox.Reader)).Message);
    }

    [Fact]
    public async Task StructBusInterface_UsesNewNames()
    {
        using var coordinator = new EventCoordinator();
        IStructEventBus bus = coordinator;
        await using var inbox = bus.CreateInbox<TestStructEvent>();

        Assert.True(bus.TryEmit(new TestStructEvent("new-name")));

        Assert.Equal("new-name", (await ReadOneAsync(inbox.Reader)).Message);
    }

    [Fact]
    public async Task SubscribeStruct_DropOldest_KeepsNewestItems()
    {
        using var coordinator = new EventCoordinator();
        await using var subscription = coordinator.SubscribeStruct<TestStructEvent>(
            new StructSubscriptionOptions
            {
                Capacity = 2,
                FullMode = BoundedChannelFullMode.DropOldest
            });

        Assert.True(coordinator.TryEmitStruct(new TestStructEvent("1")));
        Assert.True(coordinator.TryEmitStruct(new TestStructEvent("2")));
        Assert.True(coordinator.TryEmitStruct(new TestStructEvent("3")));

        var events = await ReadManyAsync(subscription.Reader, 2);

        Assert.Equal(["2", "3"], events.Select(static evt => evt.Message));
    }

    [Fact]
    public async Task EmitStructAsync_WaitsForWaitSubscriberCapacity()
    {
        using var coordinator = new EventCoordinator();
        await using var subscription = coordinator.SubscribeStruct<TestStructEvent>(
            new StructSubscriptionOptions
            {
                Capacity = 1,
                FullMode = BoundedChannelFullMode.Wait
            });

        await coordinator.EmitStructAsync(new TestStructEvent("1"));
        var emitTask = coordinator.EmitStructAsync(new TestStructEvent("2")).AsTask();

        await Task.Delay(50);
        Assert.False(emitTask.IsCompleted);

        Assert.Equal("1", (await ReadOneAsync(subscription.Reader)).Message);

        await emitTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("2", (await ReadOneAsync(subscription.Reader)).Message);
    }

    [Fact]
    public async Task StructEmitter_AssignsSequence_WhenSupported()
    {
        using var coordinator = new EventCoordinator();
        await using var subscription = coordinator.SubscribeStruct<TestStructEvent>();
        var emitter = coordinator.CreateStructEmitter<TestStructEvent>(
            new StructEmitterOptions<TestStructEvent> { AssignSequenceNumbers = true });

        Assert.True(emitter.TryEmit(new TestStructEvent("sequenced")));

        var evt = await ReadOneAsync(subscription.Reader);

        Assert.Equal(1, evt.SequenceNumber);
    }

    [Fact]
    public async Task StructEmitter_Filter_SkipsRejectedEvents()
    {
        using var coordinator = new EventCoordinator();
        await using var subscription = coordinator.SubscribeStruct<TestStructEvent>();
        var emitter = coordinator.CreateStructEmitter<TestStructEvent>(
            new StructEmitterOptions<TestStructEvent>
            {
                Filter = static evt => evt.Message == "allowed"
            });

        Assert.False(emitter.TryEmit(new TestStructEvent("blocked")));
        Assert.True(emitter.TryEmit(new TestStructEvent("allowed")));

        Assert.Equal("allowed", (await ReadOneAsync(subscription.Reader)).Message);
    }

    [Fact]
    public async Task StructSubscription_Dispose_RemovesSubscriber()
    {
        using var coordinator = new EventCoordinator();
        var subscription = coordinator.SubscribeStruct<TestStructEvent>();

        await subscription.DisposeAsync();

        Assert.False(coordinator.TryEmitStruct(new TestStructEvent("after-dispose")));
    }

    [Fact]
    public async Task SubscribeStructHandler_DispatchesWithoutRunAsync()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var handled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var handlerSubscription = coordinator.SubscribeStruct<TestStructEvent>(evt =>
        {
            handled.TrySetResult(evt.Message);
            return ValueTask.CompletedTask;
        });

        Assert.True(coordinator.TryEmitStruct(new TestStructEvent("handled")));

        Assert.Equal("handled", await handled.Task.WaitAsync(cts.Token));
    }

    [Fact]
    public async Task SubscribeStructHandler_DoesNotDispatchToClassSubscribeAny()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var sawClassEvent = false;

        using var anySubscription = coordinator.SubscribeAny(_ =>
        {
            sawClassEvent = true;
            return ValueTask.CompletedTask;
        });

        using var structSubscription = coordinator.SubscribeStruct<TestStructEvent>(_ => ValueTask.CompletedTask);
        Assert.True(coordinator.TryEmitStruct(new TestStructEvent("struct-only")));

        await Task.Delay(50, CancellationToken.None);

        Assert.False(sawClassEvent);
    }

    [Fact]
    public async Task StructEvents_DoNotBubbleToParent()
    {
        using var parent = new EventCoordinator();
        using var child = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var parentSawEvent = false;

        child.SetParent(parent);

        using var anySubscription = parent.SubscribeAny(_ =>
        {
            parentSawEvent = true;
            return ValueTask.CompletedTask;
        });

        await using var subscription = child.SubscribeStruct<TestStructEvent>();
        Assert.True(child.TryEmitStruct(new TestStructEvent("local")));
        Assert.Equal("local", (await ReadOneAsync(subscription.Reader)).Message);

        await Task.Delay(50, CancellationToken.None);

        Assert.False(parentSawEvent);
    }

    [Fact]
    public void SubscribeStruct_HandlerDispose_RemovesSubscriber()
    {
        using var coordinator = new EventCoordinator();

        var subscription = coordinator.SubscribeStruct<TestStructEvent>(_ => ValueTask.CompletedTask);
        subscription.Dispose();

        Assert.False(coordinator.TryEmitStruct(new TestStructEvent("removed")));
    }

    [Fact]
    public async Task StructAndClassHandlers_RunConcurrently()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var releaseClassHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var structHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var classSubscription = coordinator.Subscribe<TestClassEvent>(
            async _ => await releaseClassHandler.Task.WaitAsync(cts.Token));
        using var structSubscription = coordinator.SubscribeStruct<TestStructEvent>(_ =>
        {
            structHandled.TrySetResult();
            return ValueTask.CompletedTask;
        });

        coordinator.Emit(new TestClassEvent("slow"));
        Assert.True(coordinator.TryEmitStruct(new TestStructEvent("fast")));

        await structHandled.Task.WaitAsync(cts.Token);
        releaseClassHandler.SetResult();
    }

    private static async Task<TEvent> ReadOneAsync<TEvent>(
        ChannelReader<TEvent> reader,
        CancellationToken ct = default)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        return await reader.ReadAsync(linked.Token);
    }

    private static async Task<List<TEvent>> ReadManyAsync<TEvent>(
        ChannelReader<TEvent> reader,
        int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var events = new List<TEvent>(count);

        while (events.Count < count)
            events.Add(await reader.ReadAsync(timeout.Token));

        return events;
    }
}
