using HPD.Events;
using HPD.Events.Core;

namespace HPD.Events.Tests;

/// <summary>
/// Tests for StreamRegistry and StreamHandle functionality.
/// </summary>
public class StreamRegistryTests
{
    // Test event type
    private record TestEvent(string Message) : Event;

    [Fact]
    public void BeginStream_ReturnsStreamHandle()
    {
        // Arrange
        var registry = new StreamRegistry();

        // Act
        var handle = registry.BeginStream("stream-1");

        // Assert
        Assert.NotNull(handle);
        Assert.Equal("stream-1", handle.StreamId);
    }

    [Fact]
    public void BeginStream_StreamIsActive()
    {
        // Arrange
        var registry = new StreamRegistry();

        // Act
        var handle = registry.BeginStream("stream-1");

        // Assert
        Assert.True(registry.IsActive("stream-1"));
    }

    [Fact]
    public void InterruptStream_MarksStreamInterruptedButInactive()
    {
        // Arrange
        var registry = new StreamRegistry();
        var handle = registry.BeginStream("stream-1");

        // Act
        registry.InterruptStream("stream-1");

        // Assert
        Assert.False(registry.IsActive("stream-1"));
        var interrupted = Assert.IsType<StreamHandle>(registry.Get("stream-1"));
        Assert.True(interrupted.IsInterrupted);
    }

    [Fact]
    public void CompleteStream_RemovesStreamFromRegistry()
    {
        // Arrange
        var registry = new StreamRegistry();
        var handle = registry.BeginStream("stream-1");

        // Act
        registry.CompleteStream("stream-1");

        // Assert
        Assert.False(registry.IsActive("stream-1"));
    }

    [Fact]
    public void StreamHandle_Interrupt_SetsIsInterrupted()
    {
        // Arrange
        var registry = new StreamRegistry();
        var handle = registry.BeginStream("stream-1");

        // Act
        handle.Interrupt();

        // Assert
        Assert.True(handle.IsInterrupted);
    }

    [Fact]
    public void StreamHandle_Interrupt_MarksAsInterruptedButInactive()
    {
        // Arrange
        var registry = new StreamRegistry();
        var handle = registry.BeginStream("stream-1");

        // Act
        handle.Interrupt();

        // Assert - Stream stays observable as an interrupted tombstone so EventCoordinator can check IsInterrupted
        Assert.False(registry.IsActive("stream-1"));
        Assert.Same(handle, registry.Get("stream-1"));
        Assert.True(handle.IsInterrupted);
        Assert.True(handle.IsCompleted);
    }

    [Fact]
    public void StreamHandle_Complete_RemovesFromRegistry()
    {
        // Arrange
        var registry = new StreamRegistry();
        var handle = registry.BeginStream("stream-1");

        // Act
        handle.Complete();

        // Assert
        Assert.False(registry.IsActive("stream-1"));
    }

    [Fact]
    public void StreamHandle_Dispose_CompletesStream()
    {
        // Arrange
        var registry = new StreamRegistry();
        var handle = registry.BeginStream("stream-1");

        // Act
        handle.Dispose();

        // Assert
        Assert.False(registry.IsActive("stream-1"));
    }

    [Fact]
    public void StreamHandle_Dispose_AfterInterrupt_DoesNotThrow()
    {
        // Arrange
        var registry = new StreamRegistry();
        var handle = registry.BeginStream("stream-1");
        handle.Interrupt();

        // Act & Assert (should not throw)
        handle.Dispose();
    }

    [Fact]
    public void StreamHandle_MultipleInterrupts_AreIdempotent()
    {
        // Arrange
        var registry = new StreamRegistry();
        var handle = registry.BeginStream("stream-1");

        // Act
        handle.Interrupt();
        handle.Interrupt();
        handle.Interrupt();

        // Assert
        Assert.True(handle.IsInterrupted);
        Assert.False(registry.IsActive("stream-1"));
        Assert.Same(handle, registry.Get("stream-1"));
    }

    [Fact]
    public void StreamHandle_MultipleCompletes_AreIdempotent()
    {
        // Arrange
        var registry = new StreamRegistry();
        var handle = registry.BeginStream("stream-1");

        // Act
        handle.Complete();
        handle.Complete();
        handle.Complete();

        // Assert
        Assert.False(registry.IsActive("stream-1"));
    }

    [Fact]
    public void StreamHandle_MultipleDisposes_AreIdempotent()
    {
        // Arrange
        var registry = new StreamRegistry();
        var handle = registry.BeginStream("stream-1");

        // Act
        handle.Dispose();
        handle.Dispose();
        handle.Dispose();

        // Assert
        Assert.False(registry.IsActive("stream-1"));
    }

    [Fact]
    public void IsActive_ReturnsFalseForNonExistentStream()
    {
        // Arrange
        var registry = new StreamRegistry();

        // Act & Assert
        Assert.False(registry.IsActive("nonexistent"));
    }

    [Fact]
    public async Task EventCoordinator_DropsInterruptedStreamEvents()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var handle = coordinator.Streams.BeginStream("stream-1");

        // Act
        coordinator.Emit(new TestEvent("before-interrupt") { StreamId = "stream-1" });
        handle.Interrupt();
        coordinator.Emit(new TestEvent("after-interrupt") { StreamId = "stream-1" });

        var before = Assert.IsType<TestEvent>(await ReadOneAsync(coordinator.ReadSynchronousAsync()));
        var droppedEvent = Assert.IsType<EventDroppedEvent>(await ReadOneAsync(coordinator.ReadControlAsync()));

        // Assert - First event should be received, second dropped (with EventDroppedEvent emitted)
        Assert.Equal("before-interrupt", before.Message);

        // EventDroppedEvent should be emitted for the dropped event
        Assert.Equal("stream-1", droppedEvent.DroppedStreamId);
        Assert.Equal("TestEvent", droppedEvent.DroppedEventType);
        Assert.Equal(2, droppedEvent.DroppedSequenceNumber);
    }

    [Fact]
    public async Task EventCoordinator_DoesNotDropEventsWithCanInterruptFalse()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var handle = coordinator.Streams.BeginStream("stream-1");

        // Act
        coordinator.Emit(new TestEvent("before-interrupt") { StreamId = "stream-1" });
        handle.Interrupt();
        coordinator.Emit(new TestEvent("after-interrupt-critical") {
            StreamId = "stream-1",
            CanInterrupt = false // Critical event
        });

        var results = await ReadManyAsync(coordinator.ReadSynchronousAsync(), 2);

        // Assert - Both events should be received
        Assert.Equal(2, results.Count);
        Assert.Equal("before-interrupt", ((TestEvent)results[0]).Message);
        Assert.Equal("after-interrupt-critical", ((TestEvent)results[1]).Message);
    }

    [Fact]
    public async Task EventCoordinator_EventsWithoutStreamId_NotAffectedByInterruption()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var handle = coordinator.Streams.BeginStream("stream-1");

        // Act
        handle.Interrupt();
        coordinator.Emit(new TestEvent("no-stream-id")); // No StreamId

        var result = await ReadOneAsync(coordinator.ReadSynchronousAsync());

        // Assert - Event should be received
        Assert.NotNull(result);
        Assert.Equal("no-stream-id", ((TestEvent)result).Message);
    }

    [Fact]
    public async Task EventCoordinator_FullInterruptionFlow_TracksEmittedAndDroppedCounts()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var handle = coordinator.Streams.BeginStream("stream-1");

        // Act - Emit 5 events before interruption
        for (int i = 0; i < 5; i++)
        {
            coordinator.Emit(new TestEvent($"before-{i}") { StreamId = "stream-1" });
        }

        // Interrupt the stream
        handle.Interrupt();

        // Emit 5 more events after interruption (should be dropped)
        for (int i = 0; i < 5; i++)
        {
            coordinator.Emit(new TestEvent($"after-{i}") { StreamId = "stream-1" });
        }

        // Emit one critical event (CanInterrupt = false, should NOT be dropped)
        coordinator.Emit(new TestEvent("critical") { StreamId = "stream-1", CanInterrupt = false });

        var testEvents = await ReadManyAsync(coordinator.ReadSynchronousAsync(), 6);
        var droppedEvents = await ReadManyAsync(coordinator.ReadControlAsync(), 5);

        // Assert
        Assert.Equal(6, testEvents.Count); // 5 before + 1 critical
        Assert.Equal(5, droppedEvents.Count); // 5 dropped events
        Assert.Equal(5, handle.EmittedCount); // Only interruptible events before interruption
        Assert.Equal(5, handle.DroppedCount); // 5 events dropped
    }

    [Fact]
    public async Task CompletedStream_RemovesHandleAndDoesNotDropLaterEvents()
    {
        var coordinator = new EventCoordinator();
        var handle = coordinator.Streams.BeginStream("stream-1");

        coordinator.Emit(new TestEvent("before-complete") { StreamId = "stream-1" });
        handle.Complete();
        coordinator.Emit(new TestEvent("after-complete") { StreamId = "stream-1" });

        var events = await ReadManyAsync(coordinator.ReadSynchronousAsync(), 2);

        Assert.Null(coordinator.Streams.Get("stream-1"));
        Assert.Equal(["before-complete", "after-complete"], events.Cast<TestEvent>().Select(static evt => evt.Message));
    }

    [Fact]
    public async Task InterruptStream_DropsLaterEventsAndIncrementsDroppedCount()
    {
        var coordinator = new EventCoordinator();
        var handle = coordinator.Streams.BeginStream("stream-1");

        coordinator.Streams.InterruptStream("stream-1");
        coordinator.Emit(new TestEvent("after-interrupt") { StreamId = "stream-1" });

        var dropped = Assert.IsType<EventDroppedEvent>(await ReadOneAsync(coordinator.ReadControlAsync()));

        Assert.Equal("stream-1", dropped.DroppedStreamId);
        Assert.Equal(1, handle.DroppedCount);
    }

    [Fact]
    public async Task InterruptAll_DropsLaterEventsForAllInterruptedStreams()
    {
        var coordinator = new EventCoordinator();
        var handle1 = coordinator.Streams.BeginStream("stream-1");
        var handle2 = coordinator.Streams.BeginStream("stream-2");

        coordinator.Streams.InterruptAll();

        coordinator.Emit(new TestEvent("after-1") { StreamId = "stream-1" });
        coordinator.Emit(new TestEvent("after-2") { StreamId = "stream-2" });

        var droppedEvents = await ReadManyAsync(coordinator.ReadControlAsync(), 2);

        Assert.Equal(0, coordinator.Streams.ActiveCount);
        Assert.Equal(["stream-1", "stream-2"], droppedEvents.Cast<EventDroppedEvent>().Select(static evt => evt.DroppedStreamId));
        Assert.Equal(1, handle1.DroppedCount);
        Assert.Equal(1, handle2.DroppedCount);
    }

    [Fact]
    public async Task InterruptWhere_DropsOnlyMatchingStreams()
    {
        var coordinator = new EventCoordinator();
        var handle1 = coordinator.Streams.BeginStream("stream-1");
        var handle2 = coordinator.Streams.BeginStream("stream-2");

        coordinator.Streams.InterruptWhere(stream => stream.StreamId == "stream-1");

        coordinator.Emit(new TestEvent("after-1") { StreamId = "stream-1" });
        coordinator.Emit(new TestEvent("after-2") { StreamId = "stream-2" });

        var delivered = Assert.IsType<TestEvent>(await ReadOneAsync(coordinator.ReadSynchronousAsync()));
        var dropped = Assert.IsType<EventDroppedEvent>(await ReadOneAsync(coordinator.ReadControlAsync()));

        Assert.Equal("after-2", delivered.Message);
        Assert.Equal("stream-1", dropped.DroppedStreamId);
        Assert.True(handle1.IsInterrupted);
        Assert.False(handle2.IsInterrupted);
    }

    [Fact]
    public void Dispose_InterruptedStream_DoesNotClearTombstone()
    {
        var registry = new StreamRegistry();
        var handle = registry.BeginStream("stream-1");

        handle.Interrupt();
        handle.Dispose();

        Assert.Same(handle, registry.Get("stream-1"));
        Assert.False(registry.IsActive("stream-1"));
        Assert.True(handle.IsInterrupted);
    }

    [Fact]
    public void MultipleStreams_CanBeActiveSimultaneously()
    {
        // Arrange
        var registry = new StreamRegistry();

        // Act
        var handle1 = registry.BeginStream("stream-1");
        var handle2 = registry.BeginStream("stream-2");
        var handle3 = registry.BeginStream("stream-3");

        // Assert
        Assert.True(registry.IsActive("stream-1"));
        Assert.True(registry.IsActive("stream-2"));
        Assert.True(registry.IsActive("stream-3"));
    }

    [Fact]
    public void InterruptingOneStream_DoesNotAffectOthers()
    {
        // Arrange
        var registry = new StreamRegistry();
        var handle1 = registry.BeginStream("stream-1");
        var handle2 = registry.BeginStream("stream-2");

        // Act
        handle1.Interrupt();

        // Assert - Interrupted streams stay observable, but are not active
        Assert.False(registry.IsActive("stream-1"));
        Assert.Same(handle1, registry.Get("stream-1"));
        Assert.True(handle1.IsInterrupted); // But marked interrupted
        Assert.True(registry.IsActive("stream-2")); // Other stream unaffected
        Assert.False(handle2.IsInterrupted);
    }

    private static async Task<Event> ReadOneAsync(IAsyncEnumerable<Event> source)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await foreach (var evt in source.WithCancellation(timeout.Token))
            return evt;

        throw new InvalidOperationException("No event was available.");
    }

    private static async Task<List<Event>> ReadManyAsync(IAsyncEnumerable<Event> source, int count)
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
