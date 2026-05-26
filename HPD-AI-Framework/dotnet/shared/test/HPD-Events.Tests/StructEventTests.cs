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

    private readonly record struct NumericStructEvent(
        int Value,
        long TimestampNs = 0,
        long SequenceNumber = 0) : IStructEvent, ISequencedStructEvent<NumericStructEvent>
    {
        public EventKind Kind => EventKind.Content;

        public NumericStructEvent WithSequenceNumber(long sequenceNumber) =>
            this with { SequenceNumber = sequenceNumber };
    }

    private record TestClassEvent(string Message) : Event;

    [Fact]
    public void Emit_ReturnsNoSubscribers_WhenRouteIsEmpty()
    {
        using var coordinator = new EventCoordinator();
        var emitter = coordinator.LocalStructs.Route<TestStructEvent>().CreateEmitter();

        var result = emitter.Emit(new TestStructEvent("none"));

        Assert.Equal(LocalStructEmitStatus.NoSubscribers, result.Status);
        Assert.False(result.Accepted);
    }

    [Fact]
    public void Emit_FansOutToAllSubscribers()
    {
        using var coordinator = new EventCoordinator();
        var route = coordinator.LocalStructs.Route<TestStructEvent>();
        using var first = route.Subscribe();
        using var second = route.Subscribe();
        var emitter = route.CreateEmitter();

        var result = emitter.Emit(new TestStructEvent("frame"));

        Assert.Equal(LocalStructEmitStatus.Accepted, result.Status);
        Assert.Equal(2, result.AcceptedCount);
        Assert.True(first.TryRead(out var firstEvent));
        Assert.True(second.TryRead(out var secondEvent));
        Assert.Equal("frame", firstEvent.Message);
        Assert.Equal("frame", secondEvent.Message);
    }

    [Fact]
    public void CreateInbox_ReceivesStructEvents()
    {
        using var coordinator = new EventCoordinator();
        var route = coordinator.LocalStructs.Route<TestStructEvent>();
        using var inbox = route.CreateInbox();
        var emitter = route.CreateEmitter();

        Assert.True(emitter.Emit(new TestStructEvent("owned")).Accepted);

        Assert.True(inbox.TryRead(out var evt));
        Assert.Equal("owned", evt.Message);
    }

    [Fact]
    public void CreateInbox_DefaultsToBackpressure()
    {
        using var coordinator = new EventCoordinator();
        var route = coordinator.LocalStructs.Route<TestStructEvent>();
        using var inbox = route.CreateInbox(new LocalStructInboxOptions { Capacity = 1 });
        var emitter = route.CreateEmitter();

        Assert.Equal(LocalStructEmitStatus.Accepted, emitter.Emit(new TestStructEvent("1")).Status);
        Assert.Equal(LocalStructEmitStatus.Backpressured, emitter.Emit(new TestStructEvent("2")).Status);

        Assert.True(inbox.TryRead(out var evt));
        Assert.Equal("1", evt.Message);
        Assert.Equal(LocalStructEmitStatus.Accepted, emitter.Emit(new TestStructEvent("3")).Status);
    }

    [Fact]
    public void LocalStructBus_RouteCachesByType()
    {
        using var bus = new LocalStructEventBus();

        var first = bus.Route<TestStructEvent>();
        var second = bus.Route<TestStructEvent>();

        Assert.Same(first, second);
    }

    [Fact]
    public void CachedEmit_ToRingBufferSubscriber_AllocatesZeroBytesInSteadyState()
    {
        const int Iterations = 1024;
        using var bus = new LocalStructEventBus();
        var route = bus.Route<NumericStructEvent>();
        using var subscription = route.Subscribe(new LocalStructSubscriptionOptions
        {
            Capacity = Iterations + 1
        });
        var emitter = route.CreateEmitter();

        Assert.True(emitter.Emit(new NumericStructEvent(-1)).Accepted);
        Assert.True(subscription.TryRead(out _));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < Iterations; i++)
            emitter.Emit(new NumericStructEvent(i));

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void CachedBatchEmit_ToRingBufferSubscriber_AllocatesZeroBytesInSteadyState()
    {
        const int BatchSize = 128;
        const int Batches = 64;
        using var bus = new LocalStructEventBus();
        var route = bus.Route<NumericStructEvent>();
        using var subscription = route.Subscribe(new LocalStructSubscriptionOptions
        {
            Capacity = (BatchSize * Batches) + BatchSize
        });
        var emitter = route.CreateEmitter();
        var events = new NumericStructEvent[BatchSize];
        for (var i = 0; i < events.Length; i++)
            events[i] = new NumericStructEvent(i);

        Assert.Equal(BatchSize, emitter.EmitBatch(events).AcceptedEvents);
        var drain = new NumericStructEvent[BatchSize];
        Assert.Equal(BatchSize, subscription.TryReadBatch(drain));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < Batches; i++)
            emitter.EmitBatch(events);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void BatchDrain_FromRingBufferSubscriber_AllocatesZeroBytesInSteadyState()
    {
        const int Count = 512;
        using var bus = new LocalStructEventBus();
        var route = bus.Route<NumericStructEvent>();
        using var subscription = route.Subscribe(new LocalStructSubscriptionOptions
        {
            Capacity = Count + 1
        });
        var emitter = route.CreateEmitter();
        for (var i = 0; i < Count; i++)
            Assert.True(emitter.Emit(new NumericStructEvent(i)).Accepted);
        var destination = new NumericStructEvent[Count];

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var read = subscription.TryReadBatch(destination);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(Count, read);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Subscribe_DropOldest_KeepsNewestItems()
    {
        using var coordinator = new EventCoordinator();
        var route = coordinator.LocalStructs.Route<TestStructEvent>();
        using var subscription = route.Subscribe(new LocalStructSubscriptionOptions
        {
            Capacity = 2,
            FullMode = LocalStructFullMode.DropOldest
        });
        var emitter = route.CreateEmitter();

        Assert.True(emitter.Emit(new TestStructEvent("1")).Accepted);
        Assert.True(emitter.Emit(new TestStructEvent("2")).Accepted);
        Assert.True(emitter.Emit(new TestStructEvent("3")).Accepted);

        var buffer = new TestStructEvent[2];
        Assert.Equal(2, subscription.TryReadBatch(buffer));
        Assert.Equal(["2", "3"], buffer.Select(static evt => evt.Message));
    }

    [Fact]
    public void Subscribe_DropOldest_TracksDepthAndSubscriberDrops()
    {
        using var coordinator = new EventCoordinator();
        var route = coordinator.LocalStructs.Route<TestStructEvent>();
        using var subscription = route.Subscribe(new LocalStructSubscriptionOptions
        {
            Capacity = 2,
            FullMode = LocalStructFullMode.DropOldest
        });
        var emitter = route.CreateEmitter();

        Assert.True(emitter.Emit(new TestStructEvent("1")).Accepted);
        Assert.True(emitter.Emit(new TestStructEvent("2")).Accepted);
        var overwrite = emitter.Emit(new TestStructEvent("3"));

        Assert.Equal(LocalStructEmitStatus.Accepted, overwrite.Status);
        Assert.Equal(1, overwrite.DroppedCount);

        var stats = route.GetStats();
        Assert.Equal(2, stats.CurrentQueued);
        Assert.Equal(1, stats.SubscriberDrops);

        var buffer = new TestStructEvent[2];
        Assert.Equal(2, subscription.TryReadBatch(buffer));
        Assert.Equal(0, route.GetStats().CurrentQueued);
    }

    [Fact]
    public void Subscribe_Reject_ReturnsRejected_WhenFull()
    {
        using var coordinator = new EventCoordinator();
        var route = coordinator.LocalStructs.Route<TestStructEvent>();
        using var subscription = route.Subscribe(new LocalStructSubscriptionOptions
        {
            Capacity = 1,
            FullMode = LocalStructFullMode.Reject
        });
        var emitter = route.CreateEmitter();

        Assert.Equal(LocalStructEmitStatus.Accepted, emitter.Emit(new TestStructEvent("1")).Status);
        Assert.Equal(LocalStructEmitStatus.Rejected, emitter.Emit(new TestStructEvent("2")).Status);
    }

    [Fact]
    public void SequencedEmitter_AssignsSequence_WhenSupported()
    {
        using var coordinator = new EventCoordinator();
        var route = coordinator.LocalStructs.Route<TestStructEvent>();
        using var subscription = route.Subscribe();
        var emitter = route.CreateSequencedEmitter();

        Assert.True(emitter.Emit(new TestStructEvent("sequenced")).Accepted);

        Assert.True(subscription.TryRead(out var evt));
        Assert.Equal(1, evt.SequenceNumber);
    }

    [Fact]
    public void Emitter_Filter_SkipsRejectedEvents()
    {
        using var coordinator = new EventCoordinator();
        var route = coordinator.LocalStructs.Route<TestStructEvent>();
        using var subscription = route.Subscribe();
        var emitter = route.CreateEmitter(new LocalStructEmitterOptions<TestStructEvent>
        {
            Filter = static evt => evt.Message == "allowed"
        });

        Assert.Equal(LocalStructEmitStatus.Filtered, emitter.Emit(new TestStructEvent("blocked")).Status);
        Assert.True(emitter.Emit(new TestStructEvent("allowed")).Accepted);

        Assert.True(subscription.TryRead(out var evt));
        Assert.Equal("allowed", evt.Message);
        Assert.False(subscription.TryRead(out _));
    }

    [Fact]
    public void Subscription_Dispose_RemovesSubscriber()
    {
        using var coordinator = new EventCoordinator();
        var route = coordinator.LocalStructs.Route<TestStructEvent>();
        var subscription = route.Subscribe();
        subscription.Dispose();
        var emitter = route.CreateEmitter();

        Assert.Equal(LocalStructEmitStatus.NoSubscribers, emitter.Emit(new TestStructEvent("after-dispose")).Status);
    }

    [Fact]
    public void Subscription_Dispose_RemovesQueuedDepth()
    {
        using var coordinator = new EventCoordinator();
        var route = coordinator.LocalStructs.Route<TestStructEvent>();
        var subscription = route.Subscribe();
        var emitter = route.CreateEmitter();

        Assert.True(emitter.Emit(new TestStructEvent("queued")).Accepted);
        Assert.Equal(1, route.GetStats().CurrentQueued);

        subscription.Dispose();

        var stats = route.GetStats();
        Assert.Equal(0, stats.SubscriberCount);
        Assert.Equal(0, stats.CurrentQueued);
    }

    [Fact]
    public void Observe_DispatchesSynchronously()
    {
        using var coordinator = new EventCoordinator();
        var route = coordinator.LocalStructs.Route<TestStructEvent>();
        var handled = "";
        using var observer = route.Observe(evt =>
        {
            handled = evt.Message;
            return ValueTask.CompletedTask;
        });
        var emitter = route.CreateEmitter();

        Assert.True(emitter.Emit(new TestStructEvent("handled")).Accepted);

        Assert.Equal("handled", handled);
    }

    [Fact]
    public async Task StructObserver_DoesNotDispatchToClassSubscribeAny()
    {
        using var coordinator = new EventCoordinator();
        var sawClassEvent = false;

        using var anySubscription = coordinator.SubscribeAny(_ =>
        {
            sawClassEvent = true;
            return ValueTask.CompletedTask;
        });

        var route = coordinator.LocalStructs.Route<TestStructEvent>();
        using var observer = route.Observe(_ => ValueTask.CompletedTask);
        Assert.True(route.CreateEmitter().Emit(new TestStructEvent("struct-only")).Accepted);

        await Task.Delay(50, CancellationToken.None);

        Assert.False(sawClassEvent);
    }

    [Fact]
    public async Task StructEvents_DoNotBubbleToParent()
    {
        using var parent = new EventCoordinator();
        using var child = new EventCoordinator();
        var parentSawEvent = false;

        child.SetParent(parent);

        using var anySubscription = parent.SubscribeAny(_ =>
        {
            parentSawEvent = true;
            return ValueTask.CompletedTask;
        });

        var route = child.LocalStructs.Route<TestStructEvent>();
        using var subscription = route.Subscribe();
        Assert.True(route.CreateEmitter().Emit(new TestStructEvent("local")).Accepted);
        Assert.True(subscription.TryRead(out var evt));
        Assert.Equal("local", evt.Message);

        await Task.Delay(50, CancellationToken.None);

        Assert.False(parentSawEvent);
    }

    [Fact]
    public void Observe_Dispose_RemovesSubscriber()
    {
        using var coordinator = new EventCoordinator();
        var route = coordinator.LocalStructs.Route<TestStructEvent>();

        var observer = route.Observe(_ => ValueTask.CompletedTask);
        observer.Dispose();

        Assert.Equal(LocalStructEmitStatus.NoSubscribers, route.CreateEmitter().Emit(new TestStructEvent("removed")).Status);
    }

    [Fact]
    public void Observe_HandlerFault_RemovesOnlyFaultedObserver()
    {
        using var coordinator = new EventCoordinator();
        var route = coordinator.LocalStructs.Route<TestStructEvent>();
        var healthyCount = 0;

        using var healthy = route.Observe(_ =>
        {
            healthyCount++;
            return ValueTask.CompletedTask;
        });
        using var faulty = route.Observe(_ => throw new InvalidOperationException("boom"));

        var first = route.CreateEmitter().Emit(new TestStructEvent("first"));
        var second = route.CreateEmitter().Emit(new TestStructEvent("second"));

        Assert.Equal(LocalStructEmitStatus.Accepted, first.Status);
        Assert.Equal(LocalStructEmitStatus.Accepted, second.Status);
        Assert.Equal(2, healthyCount);
        Assert.Equal(1, route.GetStats().ObserverCount);
    }

    [Fact]
    public async Task StructAndClassHandlers_DoNotBlockEachOther()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var releaseClassHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var structHandled = false;

        using var classSubscription = coordinator.Subscribe<TestClassEvent>(
            async _ => await releaseClassHandler.Task.WaitAsync(cts.Token));
        using var observer = coordinator.LocalStructs.Route<TestStructEvent>().Observe(_ =>
        {
            structHandled = true;
            return ValueTask.CompletedTask;
        });

        coordinator.Emit(new TestClassEvent("slow"));
        Assert.True(coordinator.LocalStructs.Route<TestStructEvent>().CreateEmitter().Emit(new TestStructEvent("fast")).Accepted);

        Assert.True(structHandled);
        releaseClassHandler.SetResult();
        await Task.Delay(10, cts.Token);
    }

    [Fact]
    public void Stats_TrackRouteActivity()
    {
        using var coordinator = new EventCoordinator();
        var route = coordinator.LocalStructs.Route<TestStructEvent>();
        using var inbox = route.CreateInbox();
        var emitter = route.CreateEmitter(new LocalStructEmitterOptions<TestStructEvent>
        {
            Filter = static evt => evt.Message != "filtered"
        });

        Assert.Equal(LocalStructEmitStatus.Filtered, emitter.Emit(new TestStructEvent("filtered")).Status);
        Assert.True(emitter.Emit(new TestStructEvent("accepted")).Accepted);
        Assert.True(inbox.TryRead(out _));

        var stats = route.GetStats();
        Assert.Equal(1, stats.SubscriberCount);
        Assert.Equal(1, stats.InboxCount);
        Assert.Equal(1, stats.Emitted);
        Assert.Equal(1, stats.Accepted);
        Assert.Equal(1, stats.Filtered);
        Assert.Equal(0, stats.CurrentQueued);
    }
}
