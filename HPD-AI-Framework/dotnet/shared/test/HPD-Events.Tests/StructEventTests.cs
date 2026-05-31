using HPD.Events;
using HPD.Events.Struct;

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

    [Fact]
    public void Emit_ReturnsNoSubscribers_WhenRouteIsEmpty()
    {
        using var hub = new StructEventHub();
        var emitter = hub.Route<TestStructEvent>().CreateEmitter();

        var result = emitter.Emit(new TestStructEvent("none"));

        Assert.Equal(StructEventEmitStatus.NoSubscribers, result.Status);
        Assert.False(result.Accepted);
    }

    [Fact]
    public void Emit_FansOutToAllSubscribers()
    {
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>();
        using var first = route.Subscribe();
        using var second = route.Subscribe();
        var emitter = route.CreateEmitter();

        var result = emitter.Emit(new TestStructEvent("frame"));

        Assert.Equal(StructEventEmitStatus.Accepted, result.Status);
        Assert.Equal(2, result.AcceptedCount);
        Assert.True(first.TryRead(out var firstEvent));
        Assert.True(second.TryRead(out var secondEvent));
        Assert.Equal("frame", firstEvent.Message);
        Assert.Equal("frame", secondEvent.Message);
    }

    [Fact]
    public void CreateInbox_ReceivesStructEvents()
    {
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>();
        using var inbox = route.CreateInbox();
        var emitter = route.CreateEmitter();

        Assert.True(emitter.Emit(new TestStructEvent("owned")).Accepted);

        Assert.True(inbox.TryRead(out var evt));
        Assert.Equal("owned", evt.Message);
    }

    [Fact]
    public void CreateInbox_DefaultsToBackpressure()
    {
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>();
        using var inbox = route.CreateInbox(new StructEventInboxOptions { Capacity = 1 });
        var emitter = route.CreateEmitter();

        Assert.Equal(StructEventEmitStatus.Accepted, emitter.Emit(new TestStructEvent("1")).Status);
        Assert.Equal(StructEventEmitStatus.Backpressured, emitter.Emit(new TestStructEvent("2")).Status);

        Assert.True(inbox.TryRead(out var evt));
        Assert.Equal("1", evt.Message);
        Assert.Equal(StructEventEmitStatus.Accepted, emitter.Emit(new TestStructEvent("3")).Status);
    }

    [Fact]
    public void StructEventHub_RouteCachesByType()
    {
        using var bus = new StructEventHub();

        var first = bus.Route<TestStructEvent>();
        var second = bus.Route<TestStructEvent>();

        Assert.Same(first, second);
    }

    [Fact]
    public void StructEventHub_Route_AllowsEquivalentOptions()
    {
        using var bus = new StructEventHub();
        var first = bus.Route<TestStructEvent>(new StructEventRouteOptions
        {
            ConcurrencyMode = StructEventConcurrencyMode.MultiProducerMultiConsumer,
            StatsMode = StructEventStatsMode.Minimal
        });

        var second = bus.Route<TestStructEvent>(new StructEventRouteOptions
        {
            ConcurrencyMode = StructEventConcurrencyMode.MultiProducerMultiConsumer,
            StatsMode = StructEventStatsMode.Minimal
        });

        Assert.Same(first, second);
    }

    [Fact]
    public void StructEventHub_Route_ThrowsForConflictingOptions()
    {
        using var bus = new StructEventHub();
        _ = bus.Route<TestStructEvent>(new StructEventRouteOptions
        {
            StatsMode = StructEventStatsMode.Minimal
        });

        var error = Assert.Throws<InvalidOperationException>(() =>
            bus.Route<TestStructEvent>(new StructEventRouteOptions
            {
                StatsMode = StructEventStatsMode.None
            }));

        Assert.Contains(typeof(TestStructEvent).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CachedEmit_ToRingBufferSubscriber_AllocatesZeroBytesInSteadyState()
    {
        const int Iterations = 1024;
        using var bus = new StructEventHub();
        var route = bus.Route<NumericStructEvent>();
        using var subscription = route.Subscribe(new StructEventSubscriptionOptions
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
    public void CachedEmit_NoSubscribers_AllocatesZeroBytesInSteadyState()
    {
        const int Iterations = 1024;
        using var bus = new StructEventHub();
        var emitter = bus.Route<NumericStructEvent>().CreateEmitter();

        Assert.Equal(StructEventEmitStatus.NoSubscribers, emitter.Emit(new NumericStructEvent(-1)).Status);

        var matched = 0;
        var allocated = MeasureAllocatedBytes(() =>
        {
            for (var i = 0; i < Iterations; i++)
            {
                if (emitter.Emit(new NumericStructEvent(i)).Status == StructEventEmitStatus.NoSubscribers)
                    matched++;
            }
        });

        Assert.Equal(Iterations, matched);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(StructEventOverflowMode.DropOldest, StructEventEmitStatus.Accepted)]
    [InlineData(StructEventOverflowMode.DropNewest, StructEventEmitStatus.Dropped)]
    [InlineData(StructEventOverflowMode.Backpressure, StructEventEmitStatus.Backpressured)]
    [InlineData(StructEventOverflowMode.Reject, StructEventEmitStatus.Rejected)]
    public void CachedEmit_OverflowPath_AllocatesZeroBytesInSteadyState(
        StructEventOverflowMode overflowMode,
        StructEventEmitStatus expectedStatus)
    {
        const int Iterations = 1024;
        using var bus = new StructEventHub();
        var route = bus.Route<NumericStructEvent>();
        using var subscription = route.Subscribe(new StructEventSubscriptionOptions
        {
            Capacity = 1,
            OverflowMode = overflowMode
        });
        var emitter = route.CreateEmitter();

        Assert.Equal(StructEventEmitStatus.Accepted, emitter.Emit(new NumericStructEvent(-1)).Status);
        Assert.Equal(expectedStatus, emitter.Emit(new NumericStructEvent(-2)).Status);

        var matched = 0;
        var allocated = MeasureAllocatedBytes(() =>
        {
            for (var i = 0; i < Iterations; i++)
            {
                if (emitter.Emit(new NumericStructEvent(i)).Status == expectedStatus)
                    matched++;
            }
        });

        Assert.Equal(Iterations, matched);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void CachedBatchEmit_ToRingBufferSubscriber_AllocatesZeroBytesInSteadyState()
    {
        const int BatchSize = 128;
        const int Batches = 64;
        using var bus = new StructEventHub();
        var route = bus.Route<NumericStructEvent>();
        using var subscription = route.Subscribe(new StructEventSubscriptionOptions
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
    public void CachedTryRead_FromRingBufferSubscriber_AllocatesZeroBytesInSteadyState()
    {
        const int Count = 512;
        using var bus = new StructEventHub();
        var route = bus.Route<NumericStructEvent>();
        using var subscription = route.Subscribe(new StructEventSubscriptionOptions
        {
            Capacity = Count + 1
        });
        var emitter = route.CreateEmitter();
        for (var i = 0; i < Count; i++)
            Assert.True(emitter.Emit(new NumericStructEvent(i)).Accepted);

        var allocated = MeasureAllocatedBytes(() =>
        {
            for (var i = 0; i < Count; i++)
                Assert.True(subscription.TryRead(out _));
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void BatchDrain_FromRingBufferSubscriber_AllocatesZeroBytesInSteadyState()
    {
        const int Count = 512;
        using var bus = new StructEventHub();
        var route = bus.Route<NumericStructEvent>();
        using var subscription = route.Subscribe(new StructEventSubscriptionOptions
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
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>();
        using var subscription = route.Subscribe(new StructEventSubscriptionOptions
        {
            Capacity = 2,
            OverflowMode = StructEventOverflowMode.DropOldest
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
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>();
        using var subscription = route.Subscribe(new StructEventSubscriptionOptions
        {
            Capacity = 2,
            OverflowMode = StructEventOverflowMode.DropOldest
        });
        var emitter = route.CreateEmitter();

        Assert.True(emitter.Emit(new TestStructEvent("1")).Accepted);
        Assert.True(emitter.Emit(new TestStructEvent("2")).Accepted);
        var overwrite = emitter.Emit(new TestStructEvent("3"));

        Assert.Equal(StructEventEmitStatus.Accepted, overwrite.Status);
        Assert.Equal(1, overwrite.DroppedCount);

        var stats = route.GetStats();
        Assert.Equal(2, stats.CurrentQueued);
        Assert.Equal(1, stats.SubscriberDrops);

        var buffer = new TestStructEvent[2];
        Assert.Equal(2, subscription.TryReadBatch(buffer));
        Assert.Equal(0, route.GetStats().CurrentQueued);
    }

    [Fact]
    public void Emit_MixedFanout_ReturnsAcceptedWithDroppedCount_WhenAnySubscriberAccepts()
    {
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>();
        using var accepting = route.Subscribe(new StructEventSubscriptionOptions { Capacity = 2 });
        using var dropping = route.Subscribe(new StructEventSubscriptionOptions
        {
            Capacity = 1,
            OverflowMode = StructEventOverflowMode.DropNewest
        });
        var emitter = route.CreateEmitter();

        Assert.True(emitter.Emit(new TestStructEvent("1")).Accepted);

        var result = emitter.Emit(new TestStructEvent("2"));

        Assert.Equal(StructEventEmitStatus.Accepted, result.Status);
        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(1, result.DroppedCount);
        Assert.True(accepting.TryRead(out var first));
        Assert.Equal("1", first.Message);
        Assert.True(accepting.TryRead(out var second));
        Assert.Equal("2", second.Message);
    }

    [Fact]
    public void Emit_MixedFanout_ReturnsBackpressuredBeforeDropped_WhenNoneAccept()
    {
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>();
        using var dropping = route.Subscribe(new StructEventSubscriptionOptions
        {
            Capacity = 1,
            OverflowMode = StructEventOverflowMode.DropNewest
        });
        using var backpressured = route.Subscribe(new StructEventSubscriptionOptions
        {
            Capacity = 1,
            OverflowMode = StructEventOverflowMode.Backpressure
        });
        var emitter = route.CreateEmitter();

        Assert.True(emitter.Emit(new TestStructEvent("1")).Accepted);

        var result = emitter.Emit(new TestStructEvent("2"));

        Assert.Equal(StructEventEmitStatus.Backpressured, result.Status);
        Assert.Equal(0, result.AcceptedCount);
        Assert.Equal(1, result.DroppedCount);
    }

    [Fact]
    public void Subscribe_Reject_ReturnsRejected_WhenFull()
    {
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>();
        using var subscription = route.Subscribe(new StructEventSubscriptionOptions
        {
            Capacity = 1,
            OverflowMode = StructEventOverflowMode.Reject
        });
        var emitter = route.CreateEmitter();

        Assert.Equal(StructEventEmitStatus.Accepted, emitter.Emit(new TestStructEvent("1")).Status);
        Assert.Equal(StructEventEmitStatus.Rejected, emitter.Emit(new TestStructEvent("2")).Status);
    }

    [Fact]
    public void SequencedEmitter_AssignsSequence_WhenSupported()
    {
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>();
        using var subscription = route.Subscribe();
        var emitter = route.CreateSequencedEmitter();

        Assert.True(emitter.Emit(new TestStructEvent("sequenced")).Accepted);

        Assert.True(subscription.TryRead(out var evt));
        Assert.Equal(1, evt.SequenceNumber);
    }

    [Fact]
    public void CachedSequencedEmit_AllocatesZeroBytesInSteadyState()
    {
        const int Iterations = 1024;
        using var hub = new StructEventHub();
        var route = hub.Route<NumericStructEvent>();
        using var subscription = route.Subscribe(new StructEventSubscriptionOptions
        {
            Capacity = Iterations + 1
        });
        var emitter = route.CreateSequencedEmitter();

        Assert.True(emitter.Emit(new NumericStructEvent(-1)).Accepted);
        Assert.True(subscription.TryRead(out _));

        var allocated = MeasureAllocatedBytes(() =>
        {
            for (var i = 0; i < Iterations; i++)
                Assert.True(emitter.Emit(new NumericStructEvent(i)).Accepted);
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Emitter_Filter_SkipsRejectedEvents()
    {
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>();
        using var subscription = route.Subscribe();
        var emitter = route.CreateEmitter(new StructEventEmitterOptions<TestStructEvent>
        {
            Filter = static evt => evt.Message == "allowed"
        });

        Assert.Equal(StructEventEmitStatus.Filtered, emitter.Emit(new TestStructEvent("blocked")).Status);
        Assert.True(emitter.Emit(new TestStructEvent("allowed")).Accepted);

        Assert.True(subscription.TryRead(out var evt));
        Assert.Equal("allowed", evt.Message);
        Assert.False(subscription.TryRead(out _));
    }

    [Fact]
    public void Subscription_Dispose_RemovesSubscriber()
    {
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>();
        var subscription = route.Subscribe();
        subscription.Dispose();
        var emitter = route.CreateEmitter();

        Assert.Equal(StructEventEmitStatus.NoSubscribers, emitter.Emit(new TestStructEvent("after-dispose")).Status);
    }

    [Fact]
    public void Subscription_Dispose_IsIdempotent()
    {
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>();
        var subscription = route.Subscribe();

        subscription.Dispose();
        subscription.Dispose();

        Assert.Equal(0, route.GetStats().SubscriberCount);
    }

    [Fact]
    public void Inbox_Dispose_IsIdempotent()
    {
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>();
        var inbox = route.CreateInbox();

        inbox.Dispose();
        inbox.Dispose();

        Assert.Equal(0, route.GetStats().SubscriberCount);
    }

    [Fact]
    public void Subscription_Dispose_RemovesQueuedDepth()
    {
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>();
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
    public void Stats_TrackRouteActivity()
    {
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>();
        using var inbox = route.CreateInbox();
        var emitter = route.CreateEmitter(new StructEventEmitterOptions<TestStructEvent>
        {
            Filter = static evt => evt.Message != "filtered"
        });

        Assert.Equal(StructEventEmitStatus.Filtered, emitter.Emit(new TestStructEvent("filtered")).Status);
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

    [Fact]
    public void StatsModeNone_DoesNotTrackRouteCounters()
    {
        using var hub = new StructEventHub();
        var route = hub.Route<TestStructEvent>(new StructEventRouteOptions
        {
            StatsMode = StructEventStatsMode.None
        });
        using var subscription = route.Subscribe(new StructEventSubscriptionOptions
        {
            Capacity = 1,
            OverflowMode = StructEventOverflowMode.DropOldest
        });
        var emitter = route.CreateEmitter(new StructEventEmitterOptions<TestStructEvent>
        {
            Filter = static evt => evt.Message != "filtered"
        });

        Assert.Equal(StructEventEmitStatus.Filtered, emitter.Emit(new TestStructEvent("filtered")).Status);
        Assert.True(emitter.Emit(new TestStructEvent("1")).Accepted);
        var overwrite = emitter.Emit(new TestStructEvent("2"));
        Assert.Equal(StructEventEmitStatus.Accepted, overwrite.Status);
        Assert.Equal(1, overwrite.DroppedCount);

        Assert.True(subscription.TryRead(out var evt));
        Assert.Equal("2", evt.Message);

        var stats = route.GetStats();
        Assert.Equal(1, stats.SubscriberCount);
        Assert.Equal(0, stats.InboxCount);
        Assert.Equal(0, stats.CurrentQueued);
        Assert.Equal(0, stats.MaxQueued);
        Assert.Equal(0, stats.Emitted);
        Assert.Equal(0, stats.Accepted);
        Assert.Equal(0, stats.Dropped);
        Assert.Equal(0, stats.Filtered);
        Assert.Equal(0, stats.SubscriberWrites);
        Assert.Equal(0, stats.SubscriberDrops);
    }

    private static long MeasureAllocatedBytes(Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        action();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
