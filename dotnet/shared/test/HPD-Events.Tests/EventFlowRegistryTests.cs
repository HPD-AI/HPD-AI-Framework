using HPD.Events;
using HPD.Events.Core;
using System.Threading.Channels;

namespace HPD.Events.Tests;

/// <summary>
/// Tests for EventFlowRegistry and EventFlowHandle functionality.
/// </summary>
public class EventFlowRegistryTests
{
    // Test event type
    private record TestEvent(string Message) : Event;

    [Fact]
    public void BeginFlow_ReturnsEventFlowHandle()
    {
        // Arrange
        var registry = new EventFlowRegistry();

        // Act
        var handle = registry.BeginFlow("flow-1");

        // Assert
        Assert.NotNull(handle);
        Assert.Equal("flow-1", handle.EventFlowId);
    }

    [Fact]
    public void BeginFlow_EventFlowIsActive()
    {
        // Arrange
        var registry = new EventFlowRegistry();

        // Act
        var handle = registry.BeginFlow("flow-1");

        // Assert
        Assert.True(registry.IsActive("flow-1"));
    }

    [Fact]
    public void InterruptFlow_MarksEventFlowInterruptedButInactive()
    {
        // Arrange
        var registry = new EventFlowRegistry();
        var handle = registry.BeginFlow("flow-1");

        // Act
        registry.InterruptFlow("flow-1");

        // Assert
        Assert.False(registry.IsActive("flow-1"));
        var interrupted = Assert.IsType<EventFlowHandle>(registry.Get("flow-1"));
        Assert.True(interrupted.IsInterrupted);
    }

    [Fact]
    public void CompleteFlow_RemovesEventFlowFromRegistry()
    {
        // Arrange
        var registry = new EventFlowRegistry();
        var handle = registry.BeginFlow("flow-1");

        // Act
        registry.CompleteFlow("flow-1");

        // Assert
        Assert.False(registry.IsActive("flow-1"));
    }

    [Fact]
    public void EventFlowHandle_Interrupt_SetsIsInterrupted()
    {
        // Arrange
        var registry = new EventFlowRegistry();
        var handle = registry.BeginFlow("flow-1");

        // Act
        handle.Interrupt();

        // Assert
        Assert.True(handle.IsInterrupted);
    }

    [Fact]
    public void EventFlowHandle_Interrupt_MarksAsInterruptedButInactive()
    {
        // Arrange
        var registry = new EventFlowRegistry();
        var handle = registry.BeginFlow("flow-1");

        // Act
        handle.Interrupt();

        // Assert - EventFlow stays observable as an interrupted tombstone so EventCoordinator can check IsInterrupted
        Assert.False(registry.IsActive("flow-1"));
        Assert.Same(handle, registry.Get("flow-1"));
        Assert.True(handle.IsInterrupted);
        Assert.True(handle.IsCompleted);
    }

    [Fact]
    public void EventFlowHandle_Complete_RemovesFromRegistry()
    {
        // Arrange
        var registry = new EventFlowRegistry();
        var handle = registry.BeginFlow("flow-1");

        // Act
        handle.Complete();

        // Assert
        Assert.False(registry.IsActive("flow-1"));
    }

    [Fact]
    public void EventFlowHandle_Dispose_CompletesEventFlow()
    {
        // Arrange
        var registry = new EventFlowRegistry();
        var handle = registry.BeginFlow("flow-1");

        // Act
        handle.Dispose();

        // Assert
        Assert.False(registry.IsActive("flow-1"));
    }

    [Fact]
    public void EventFlowHandle_Dispose_AfterInterrupt_DoesNotThrow()
    {
        // Arrange
        var registry = new EventFlowRegistry();
        var handle = registry.BeginFlow("flow-1");
        handle.Interrupt();

        // Act & Assert (should not throw)
        handle.Dispose();
    }

    [Fact]
    public void EventFlowHandle_MultipleInterrupts_AreIdempotent()
    {
        // Arrange
        var registry = new EventFlowRegistry();
        var handle = registry.BeginFlow("flow-1");

        // Act
        handle.Interrupt();
        handle.Interrupt();
        handle.Interrupt();

        // Assert
        Assert.True(handle.IsInterrupted);
        Assert.False(registry.IsActive("flow-1"));
        Assert.Same(handle, registry.Get("flow-1"));
    }

    [Fact]
    public void EventFlowHandle_MultipleCompletes_AreIdempotent()
    {
        // Arrange
        var registry = new EventFlowRegistry();
        var handle = registry.BeginFlow("flow-1");

        // Act
        handle.Complete();
        handle.Complete();
        handle.Complete();

        // Assert
        Assert.False(registry.IsActive("flow-1"));
    }

    [Fact]
    public void EventFlowHandle_MultipleDisposes_AreIdempotent()
    {
        // Arrange
        var registry = new EventFlowRegistry();
        var handle = registry.BeginFlow("flow-1");

        // Act
        handle.Dispose();
        handle.Dispose();
        handle.Dispose();

        // Assert
        Assert.False(registry.IsActive("flow-1"));
    }

    [Fact]
    public void IsActive_ReturnsFalseForNonExistentEventFlow()
    {
        // Arrange
        var registry = new EventFlowRegistry();

        // Act & Assert
        Assert.False(registry.IsActive("nonexistent-flow"));
    }

    [Fact]
    public async Task EventCoordinator_DropsInterruptedEventFlowEvents()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var handle = coordinator.EventFlows.BeginFlow("flow-1");
        await using var events = coordinator.CreateInbox<TestEvent>();
        await using var diagnostics = coordinator.CreateInbox<EventDroppedEvent>();

        // Act
        coordinator.Emit(new TestEvent("before-interrupt") { EventFlowId = "flow-1" });
        handle.Interrupt();
        coordinator.Emit(new TestEvent("after-interrupt") { EventFlowId = "flow-1" });

        var before = await ReadOneAsync(events.Reader);
        var droppedEvent = await ReadOneAsync(diagnostics.Reader);

        // Assert - First event should be received, second dropped (with EventDroppedEvent emitted)
        Assert.Equal("before-interrupt", before.Message);

        // EventDroppedEvent should be emitted for the dropped event
        Assert.Equal("flow-1", droppedEvent.DroppedEventFlowId);
        Assert.Equal("TestEvent", droppedEvent.DroppedEventType);
        Assert.Equal(2, droppedEvent.DroppedSequenceNumber);
    }

    [Fact]
    public async Task EventCoordinator_DoesNotDropEventsWithCanInterruptFalse()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var handle = coordinator.EventFlows.BeginFlow("flow-1");
        await using var events = coordinator.CreateInbox<TestEvent>();

        // Act
        coordinator.Emit(new TestEvent("before-interrupt") { EventFlowId = "flow-1" });
        handle.Interrupt();
        coordinator.Emit(new TestEvent("after-interrupt-critical") {
            EventFlowId = "flow-1",
            CanInterrupt = false // Critical event
        });

        var results = await ReadManyAsync(events.Reader, 2);

        // Assert - Both events should be received
        Assert.Equal(2, results.Count);
        Assert.Equal("before-interrupt", results[0].Message);
        Assert.Equal("after-interrupt-critical", results[1].Message);
    }

    [Fact]
    public async Task EventCoordinator_EventsWithoutEventFlowId_NotAffectedByInterruption()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var handle = coordinator.EventFlows.BeginFlow("flow-1");
        await using var events = coordinator.CreateInbox<TestEvent>();

        // Act
        handle.Interrupt();
        coordinator.Emit(new TestEvent("no-event flow-id")); // No EventFlowId

        var result = await ReadOneAsync(events.Reader);

        // Assert - Event should be received
        Assert.NotNull(result);
        Assert.Equal("no-event flow-id", result.Message);
    }

    [Fact]
    public async Task EventCoordinator_FullInterruptionFlow_TracksEmittedAndDroppedCounts()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var handle = coordinator.EventFlows.BeginFlow("flow-1");
        await using var events = coordinator.CreateInbox<TestEvent>();
        await using var diagnostics = coordinator.CreateInbox<EventDroppedEvent>();

        // Act - Emit 5 events before interruption
        for (int i = 0; i < 5; i++)
        {
            coordinator.Emit(new TestEvent($"before-{i}") { EventFlowId = "flow-1" });
        }

        // Interrupt the event flow
        handle.Interrupt();

        // Emit 5 more events after interruption (should be dropped)
        for (int i = 0; i < 5; i++)
        {
            coordinator.Emit(new TestEvent($"after-{i}") { EventFlowId = "flow-1" });
        }

        // Emit one critical event (CanInterrupt = false, should NOT be dropped)
        coordinator.Emit(new TestEvent("critical") { EventFlowId = "flow-1", CanInterrupt = false });

        var testEvents = await ReadManyAsync(events.Reader, 6);
        var droppedEvents = await ReadManyAsync(diagnostics.Reader, 5);

        // Assert
        Assert.Equal(6, testEvents.Count); // 5 before + 1 critical
        Assert.Equal(5, droppedEvents.Count); // 5 dropped events
        Assert.Equal(5, handle.EmittedCount); // Only interruptible events before interruption
        Assert.Equal(5, handle.DroppedCount); // 5 events dropped
    }

    [Fact]
    public async Task CompletedEventFlow_RemovesHandleAndDoesNotDropLaterEvents()
    {
        var coordinator = new EventCoordinator();
        var handle = coordinator.EventFlows.BeginFlow("flow-1");
        await using var events = coordinator.CreateInbox<TestEvent>();

        coordinator.Emit(new TestEvent("before-complete") { EventFlowId = "flow-1" });
        handle.Complete();
        coordinator.Emit(new TestEvent("after-complete") { EventFlowId = "flow-1" });

        var received = await ReadManyAsync(events.Reader, 2);

        Assert.Null(coordinator.EventFlows.Get("flow-1"));
        Assert.Equal(["before-complete", "after-complete"], received.Select(static evt => evt.Message));
    }

    [Fact]
    public async Task InterruptFlow_DropsLaterEventsAndIncrementsDroppedCount()
    {
        var coordinator = new EventCoordinator();
        var handle = coordinator.EventFlows.BeginFlow("flow-1");
        await using var diagnostics = coordinator.CreateInbox<EventDroppedEvent>();

        coordinator.EventFlows.InterruptFlow("flow-1");
        coordinator.Emit(new TestEvent("after-interrupt") { EventFlowId = "flow-1" });

        var dropped = await ReadOneAsync(diagnostics.Reader);

        Assert.Equal("flow-1", dropped.DroppedEventFlowId);
        Assert.Equal(1, handle.DroppedCount);
    }

    [Fact]
    public async Task InterruptAll_DropsLaterEventsForAllInterruptedEventFlows()
    {
        var coordinator = new EventCoordinator();
        var handle1 = coordinator.EventFlows.BeginFlow("flow-1");
        var handle2 = coordinator.EventFlows.BeginFlow("flow-2");
        await using var diagnostics = coordinator.CreateInbox<EventDroppedEvent>();

        coordinator.EventFlows.InterruptAll();

        coordinator.Emit(new TestEvent("after-1") { EventFlowId = "flow-1" });
        coordinator.Emit(new TestEvent("after-2") { EventFlowId = "flow-2" });

        var droppedEvents = await ReadManyAsync(diagnostics.Reader, 2);

        Assert.Equal(0, coordinator.EventFlows.ActiveCount);
        Assert.Equal(["flow-1", "flow-2"], droppedEvents.Select(static evt => evt.DroppedEventFlowId));
        Assert.Equal(1, handle1.DroppedCount);
        Assert.Equal(1, handle2.DroppedCount);
    }

    [Fact]
    public async Task InterruptWhere_DropsOnlyMatchingEventFlows()
    {
        var coordinator = new EventCoordinator();
        var handle1 = coordinator.EventFlows.BeginFlow("flow-1");
        var handle2 = coordinator.EventFlows.BeginFlow("flow-2");
        await using var events = coordinator.CreateInbox<TestEvent>();
        await using var diagnostics = coordinator.CreateInbox<EventDroppedEvent>();

        coordinator.EventFlows.InterruptWhere(flow => flow.EventFlowId == "flow-1");

        coordinator.Emit(new TestEvent("after-1") { EventFlowId = "flow-1" });
        coordinator.Emit(new TestEvent("after-2") { EventFlowId = "flow-2" });

        var delivered = await ReadOneAsync(events.Reader);
        var dropped = await ReadOneAsync(diagnostics.Reader);

        Assert.Equal("after-2", delivered.Message);
        Assert.Equal("flow-1", dropped.DroppedEventFlowId);
        Assert.True(handle1.IsInterrupted);
        Assert.False(handle2.IsInterrupted);
    }

    [Fact]
    public void Dispose_InterruptedEventFlow_DoesNotClearTombstone()
    {
        var registry = new EventFlowRegistry();
        var handle = registry.BeginFlow("flow-1");

        handle.Interrupt();
        handle.Dispose();

        Assert.Same(handle, registry.Get("flow-1"));
        Assert.False(registry.IsActive("flow-1"));
        Assert.True(handle.IsInterrupted);
    }

    [Fact]
    public void MultipleEventFlows_CanBeActiveSimultaneously()
    {
        // Arrange
        var registry = new EventFlowRegistry();

        // Act
        var handle1 = registry.BeginFlow("flow-1");
        var handle2 = registry.BeginFlow("flow-2");
        var handle3 = registry.BeginFlow("flow-3");

        // Assert
        Assert.True(registry.IsActive("flow-1"));
        Assert.True(registry.IsActive("flow-2"));
        Assert.True(registry.IsActive("flow-3"));
    }

    [Fact]
    public void InterruptingOneEventFlow_DoesNotAffectOthers()
    {
        // Arrange
        var registry = new EventFlowRegistry();
        var handle1 = registry.BeginFlow("flow-1");
        var handle2 = registry.BeginFlow("flow-2");

        // Act
        handle1.Interrupt();

        // Assert - Interrupted event flows stay observable, but are not active
        Assert.False(registry.IsActive("flow-1"));
        Assert.Same(handle1, registry.Get("flow-1"));
        Assert.True(handle1.IsInterrupted); // But marked interrupted
        Assert.True(registry.IsActive("flow-2")); // Other event flow unaffected
        Assert.False(handle2.IsInterrupted);
    }

    private static async Task<TEvent> ReadOneAsync<TEvent>(ChannelReader<TEvent> reader)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        return await reader.ReadAsync(timeout.Token);
    }

    private static async Task<List<TEvent>> ReadManyAsync<TEvent>(ChannelReader<TEvent> reader, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var events = new List<TEvent>(count);

        while (events.Count < count)
        {
            var evt = await reader.ReadAsync(timeout.Token);
            events.Add(evt);
        }

        return events;
    }
}
