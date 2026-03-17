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
    public void InterruptStream_RemovesStreamFromRegistry()
    {
        // Arrange
        var registry = new StreamRegistry();
        var handle = registry.BeginStream("stream-1");

        // Act
        registry.InterruptStream("stream-1");

        // Assert
        Assert.False(registry.IsActive("stream-1"));
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
    public void StreamHandle_Interrupt_MarksAsInterrupted()
    {
        // Arrange
        var registry = new StreamRegistry();
        var handle = registry.BeginStream("stream-1");

        // Act
        handle.Interrupt();

        // Assert - Stream stays in registry (marked interrupted) so EventCoordinator can check IsInterrupted
        Assert.True(registry.IsActive("stream-1"));
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
        // Stream stays in registry when interrupted via handle.Interrupt()
        Assert.True(registry.IsActive("stream-1"));
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

        var cts = new CancellationTokenSource(100);
        var results = await coordinator.ReadAllAsync(cts.Token)
            .Take(3) // Expect: before-interrupt TestEvent + EventDroppedEvent
            .ToListAsync();

        // Assert - First event should be received, second dropped (with EventDroppedEvent emitted)
        Assert.Equal(2, results.Count);
        Assert.Equal("before-interrupt", ((TestEvent)results[0]).Message);

        // EventDroppedEvent should be emitted for the dropped event
        var droppedEvent = Assert.IsType<EventDroppedEvent>(results[1]);
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

        var cts = new CancellationTokenSource(100);
        var results = await coordinator.ReadAllAsync(cts.Token)
            .Take(2)
            .ToListAsync();

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

        var cts = new CancellationTokenSource(100);
        var result = await coordinator.ReadAllAsync(cts.Token).FirstOrDefaultAsync();

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

        // Read all events
        var cts = new CancellationTokenSource(200);
        var results = await coordinator.ReadAllAsync(cts.Token).ToListAsync();

        // Assert
        var testEvents = results.OfType<TestEvent>().ToList();
        var droppedEvents = results.OfType<EventDroppedEvent>().ToList();

        Assert.Equal(6, testEvents.Count); // 5 before + 1 critical
        Assert.Equal(5, droppedEvents.Count); // 5 dropped events
        Assert.Equal(5, handle.EmittedCount); // Only interruptible events before interruption
        Assert.Equal(5, handle.DroppedCount); // 5 events dropped
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

        // Assert - Interrupted streams stay in registry, but are marked interrupted
        Assert.True(registry.IsActive("stream-1")); // Still in registry
        Assert.True(handle1.IsInterrupted); // But marked interrupted
        Assert.True(registry.IsActive("stream-2")); // Other stream unaffected
        Assert.False(handle2.IsInterrupted);
    }
}
