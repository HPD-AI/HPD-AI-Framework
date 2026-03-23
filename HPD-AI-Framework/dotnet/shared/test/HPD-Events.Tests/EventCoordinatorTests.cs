using HPD.Events;
using HPD.Events.Core;

namespace HPD.Events.Tests;

/// <summary>
/// Tests for EventCoordinator functionality.
/// </summary>
public class EventCoordinatorTests
{
    // Test event types
    private record TestEvent(string Message) : Event;

    // Helper to create control priority events
    private static TestEvent CreateControlEvent(string message) =>
        new TestEvent(message) { Priority = EventPriority.Control };

    // Helper to create immediate priority events
    private static TestEvent CreateImmediateEvent(string message) =>
        new TestEvent(message) { Priority = EventPriority.Immediate };

    // Legacy test event types (kept for backward compatibility with existing tests)
    private record TestControlEvent(string Message) : Event
    {
        public new EventPriority Priority { get; init; } = EventPriority.Control;
    }
    private record TestImmediateEvent(string Message) : Event
    {
        public new EventPriority Priority { get; init; } = EventPriority.Immediate;
    }

    [Fact]
    public void Emit_AssignsSequenceNumber()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var evt = new TestEvent("test");

        // Act
        coordinator.Emit(evt);

        // Assert
        Assert.Equal(1, evt.SequenceNumber);
    }

    [Fact]
    public void Emit_SequenceNumbers_AreMonotonicallyIncreasing()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var evt1 = new TestEvent("first");
        var evt2 = new TestEvent("second");
        var evt3 = new TestEvent("third");

        // Act
        coordinator.Emit(evt1);
        coordinator.Emit(evt2);
        coordinator.Emit(evt3);

        // Assert
        Assert.Equal(1, evt1.SequenceNumber);
        Assert.Equal(2, evt2.SequenceNumber);
        Assert.Equal(3, evt3.SequenceNumber);
    }

    [Fact]
    public async Task ReadAllAsync_ReturnsEmittedEvents()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var evt = new TestEvent("test");

        // Act
        coordinator.Emit(evt);
        var cts = new CancellationTokenSource(100);

        var result = await coordinator.ReadAllAsync(cts.Token).FirstOrDefaultAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TestEvent>(result);
        Assert.Equal("test", ((TestEvent)result).Message);
    }

    [Fact]
    public async Task ReadAllAsync_HandlesMultiplePriorities()
    {
        // Arrange
        var coordinator = new EventCoordinator();

        // Act - Emit events with different priorities
        coordinator.Emit(new TestEvent("normal"));
        coordinator.Emit(new TestImmediateEvent("immediate"));

        var cts = new CancellationTokenSource(1000);
        var results = await coordinator.ReadAllAsync(cts.Token)
            .Take(2)
            .ToListAsync();

        // Assert - Both events should be received
        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e is TestEvent);
        Assert.Contains(results, e => e is TestImmediateEvent);
    }

    [Fact]
    public async Task ReadAllAsync_HandlesControlEvents()
    {
        // Arrange
        var coordinator = new EventCoordinator();

        // Act - Emit normal then control
        coordinator.Emit(new TestEvent("normal"));
        coordinator.Emit(new TestControlEvent("control"));

        var cts = new CancellationTokenSource(1000);
        var results = await coordinator.ReadAllAsync(cts.Token)
            .Take(2)
            .ToListAsync();

        // Assert - Both events should be received
        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e is TestEvent);
        Assert.Contains(results, e => e is TestControlEvent);
    }

    [Fact]
    public async Task PriorityEvents_ImmediateProcessedBeforeNormal()
    {
        // Arrange
        var coordinator = new EventCoordinator();

        // Emit 100 normal events first
        for (int i = 0; i < 100; i++)
        {
            coordinator.Emit(new TestEvent($"text{i}") { Priority = EventPriority.Normal });
        }

        // Then emit 1 immediate priority event
        var urgentEvent = CreateImmediateEvent("urgent");
        coordinator.Emit(urgentEvent);

        // Act - Read first event
        var cts = new CancellationTokenSource(1000);
        var firstEvent = await coordinator.ReadAllAsync(cts.Token).FirstOrDefaultAsync();

        // Assert - First event should be the immediate priority event (bypasses 100 normal events)
        Assert.NotNull(firstEvent);
        Assert.Equal(EventPriority.Immediate, firstEvent.Priority);
        Assert.Equal("urgent", ((TestEvent)firstEvent).Message);
    }

    [Fact]
    public async Task PriorityEvents_ControlProcessedBeforeNormal()
    {
        // Arrange
        var coordinator = new EventCoordinator();

        // Emit 50 normal events first
        for (int i = 0; i < 50; i++)
        {
            coordinator.Emit(new TestEvent($"text{i}") { Priority = EventPriority.Normal });
        }

        // Emit control priority event
        var controlEvent = CreateControlEvent("control-message");
        coordinator.Emit(controlEvent);

        // Act - Read first event
        var cts = new CancellationTokenSource(1000);
        var firstEvent = await coordinator.ReadAllAsync(cts.Token).FirstOrDefaultAsync();

        // Assert - First event should be the control event (bypasses 50 normal events)
        Assert.NotNull(firstEvent);
        Assert.Equal(EventPriority.Control, firstEvent.Priority);
        Assert.Equal("control-message", ((TestEvent)firstEvent).Message);
    }

    [Fact]
    public async Task PriorityEvents_NormalAndBackgroundInFIFOOrder()
    {
        // Arrange
        var coordinator = new EventCoordinator();

        // Both Normal and Background go to the standard channel
        // They are read in FIFO order within the same channel
        coordinator.Emit(new TestEvent("background-first") { Priority = EventPriority.Background });
        coordinator.Emit(new TestEvent("normal-second") { Priority = EventPriority.Normal });

        // Act - Read events in order
        var cts = new CancellationTokenSource(1000);
        var events = await coordinator.ReadAllAsync(cts.Token)
            .Take(2)
            .ToListAsync();

        // Assert - Events come in FIFO order since both are in standard channel
        Assert.Equal(2, events.Count);
        // Background was emitted first, so it comes first
        Assert.Equal("background-first", ((TestEvent)events[0]).Message);
        Assert.Equal("normal-second", ((TestEvent)events[1]).Message);
    }

    [Fact]
    public void SetParent_WithNullParent_ThrowsArgumentNullException()
    {
        // Arrange
        var coordinator = new EventCoordinator();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => coordinator.SetParent(null!));
    }

    [Fact]
    public void SetParent_WithSelfReference_ThrowsInvalidOperationException()
    {
        // Arrange
        var coordinator = new EventCoordinator();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => coordinator.SetParent(coordinator));
        Assert.Contains("Cannot set coordinator as its own parent", ex.Message);
        Assert.Contains("infinite loop", ex.Message);
    }

    [Fact]
    public void SetParent_WithTwoNodeCycle_ThrowsInvalidOperationException()
    {
        // Arrange
        var coordinatorA = new EventCoordinator();
        var coordinatorB = new EventCoordinator();

        coordinatorA.SetParent(coordinatorB);

        // Act & Assert
        // Trying to set B's parent to A would create cycle: A -> B -> A
        var ex = Assert.Throws<InvalidOperationException>(() => coordinatorB.SetParent(coordinatorA));
        Assert.Contains("Cannot set parent: this would create a cycle", ex.Message);
    }

    [Fact]
    public void SetParent_WithThreeNodeCycle_ThrowsInvalidOperationException()
    {
        // Arrange
        var coordinatorA = new EventCoordinator();
        var coordinatorB = new EventCoordinator();
        var coordinatorC = new EventCoordinator();

        coordinatorA.SetParent(coordinatorB);
        coordinatorB.SetParent(coordinatorC);

        // Act & Assert
        // Trying to set C's parent to A would create cycle: A -> B -> C -> A
        var ex = Assert.Throws<InvalidOperationException>(() => coordinatorC.SetParent(coordinatorA));
        Assert.Contains("Cannot set parent: this would create a cycle", ex.Message);
    }

    [Fact]
    public void SetParent_WithValidChain_Succeeds()
    {
        // Arrange
        var root = new EventCoordinator();
        var middle = new EventCoordinator();
        var leaf = new EventCoordinator();

        // Act
        middle.SetParent(root);
        leaf.SetParent(middle);

        // Assert
        // If we got here without exception, the chain is valid
        Assert.True(true);
    }

    [Fact]
    public void SetParent_CanChangeParent_WhenNoCycleCreated()
    {
        // Arrange
        var root1 = new EventCoordinator();
        var root2 = new EventCoordinator();
        var child = new EventCoordinator();

        // Act
        child.SetParent(root1);  // First parent
        child.SetParent(root2);  // Change to different parent

        // Assert
        // If we got here without exception, changing parent worked
        Assert.True(true);
    }

    [Fact]
    public void SetParent_WithComplexChain_DetectsCycleCorrectly()
    {
        // Arrange
        // Create chain: A -> B -> C -> D
        var coordinatorA = new EventCoordinator();
        var coordinatorB = new EventCoordinator();
        var coordinatorC = new EventCoordinator();
        var coordinatorD = new EventCoordinator();

        coordinatorA.SetParent(coordinatorB);
        coordinatorB.SetParent(coordinatorC);
        coordinatorC.SetParent(coordinatorD);

        // Act & Assert
        // Trying to set D's parent to B would create cycle: B -> C -> D -> B
        var ex = Assert.Throws<InvalidOperationException>(() => coordinatorD.SetParent(coordinatorB));
        Assert.Contains("Cannot set parent: this would create a cycle", ex.Message);
    }

    [Fact]
    public async Task SetParent_BubblesEventsToParent()
    {
        // Arrange
        var parent = new EventCoordinator();
        var child = new EventCoordinator();
        child.SetParent(parent);

        // Act
        child.Emit(new TestEvent("bubbled"));

        var cts = new CancellationTokenSource(100);
        var result = await parent.ReadAllAsync(cts.Token).FirstOrDefaultAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TestEvent>(result);
        Assert.Equal("bubbled", ((TestEvent)result).Message);
    }

    [Fact]
    public async Task EventBubbling_WithSetParent_BubblesCorrectly()
    {
        // Arrange
        var root = new EventCoordinator();
        var middle = new EventCoordinator();
        var leaf = new EventCoordinator();

        middle.SetParent(root);
        leaf.SetParent(middle);

        var testEvent = new TestEvent("Test event");

        // Act
        leaf.Emit(testEvent);

        // Assert - Event should appear in all three coordinators (CRITICAL: including middle!)
        var cts = new CancellationTokenSource(100);

        var leafEvt = await leaf.ReadAllAsync(cts.Token).FirstOrDefaultAsync();
        Assert.NotNull(leafEvt);
        Assert.IsType<TestEvent>(leafEvt);
        Assert.Equal("Test event", ((TestEvent)leafEvt).Message);

        var middleEvt = await middle.ReadAllAsync(cts.Token).FirstOrDefaultAsync();
        Assert.NotNull(middleEvt);
        Assert.IsType<TestEvent>(middleEvt);
        Assert.Equal("Test event", ((TestEvent)middleEvt).Message);  // THIS IS THE KEY TEST - middle sees it!

        var rootEvt = await root.ReadAllAsync(cts.Token).FirstOrDefaultAsync();
        Assert.NotNull(rootEvt);
        Assert.IsType<TestEvent>(rootEvt);
        Assert.Equal("Test event", ((TestEvent)rootEvt).Message);
    }

    [Fact]
    public async Task EventBubbling_MultiLevel_AllCoordinatorsReceiveEvents()
    {
        // Arrange - Create 3-level hierarchy
        var orchestrator = new EventCoordinator();
        var middle = new EventCoordinator();
        var worker = new EventCoordinator();

        middle.SetParent(orchestrator);
        worker.SetParent(middle);

        var testEvent = new TestEvent("Multi-level test");

        var receivedEvents = new List<(string coordinator, Event evt)>();

        // Act - Worker emits event
        worker.Emit(testEvent);

        var cts = new CancellationTokenSource(100);

        // Assert - Verify event reached all three levels
        var workerEvt = await worker.ReadAllAsync(cts.Token).FirstOrDefaultAsync();
        if (workerEvt != null)
            receivedEvents.Add(("Worker", workerEvt));

        var middleEvt = await middle.ReadAllAsync(cts.Token).FirstOrDefaultAsync();
        if (middleEvt != null)
            receivedEvents.Add(("Middle", middleEvt));

        var orchEvt = await orchestrator.ReadAllAsync(cts.Token).FirstOrDefaultAsync();
        if (orchEvt != null)
            receivedEvents.Add(("Orchestrator", orchEvt));

        // All three should have received the event
        Assert.Equal(3, receivedEvents.Count);
        Assert.Contains(receivedEvents, e => e.coordinator == "Worker");
        Assert.Contains(receivedEvents, e => e.coordinator == "Middle");  // 🔥 KEY: Middle sees it!
        Assert.Contains(receivedEvents, e => e.coordinator == "Orchestrator");
    }

    [Fact]
    public async Task EventBubbling_WithoutSetParent_DoesNotBubble()
    {
        // Arrange - Create coordinators WITHOUT linking them
        var coordinator1 = new EventCoordinator();
        var coordinator2 = new EventCoordinator();

        var testEvent = new TestEvent("No bubbling test");

        // Act - Emit on coordinator1
        coordinator1.Emit(testEvent);

        var cts = new CancellationTokenSource(100);

        // Assert - Only coordinator1 should receive the event
        var evt1 = await coordinator1.ReadAllAsync(cts.Token).FirstOrDefaultAsync();
        Assert.NotNull(evt1);
        Assert.IsType<TestEvent>(evt1);

        // coordinator2 should NOT receive the event (no parent relationship)
        var evt2 = await coordinator2.ReadAllAsync(cts.Token).FirstOrDefaultAsync();
        Assert.Null(evt2);
    }

    [Fact]
    public async Task EventEnricher_EnrichesEventsBeforeEmission()
    {
        // Arrange
        var coordinator = new EventCoordinator(
            eventEnricher: evt => evt with {
                Extensions = new Dictionary<string, object> { ["enriched"] = true }
            });

        // Act
        coordinator.Emit(new TestEvent("test"));

        var cts = new CancellationTokenSource(100);
        var result = await coordinator.ReadAllAsync(cts.Token).FirstOrDefaultAsync();

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Extensions);
        Assert.True((bool)result.Extensions["enriched"]);
    }

    [Fact]
    public async Task EventFilter_FiltersEvents()
    {
        // Arrange
        var coordinator = new EventCoordinator(
            eventFilter: evt => evt is TestEvent te && te.Message == "allowed");

        // Act
        coordinator.Emit(new TestEvent("allowed"));
        coordinator.Emit(new TestEvent("blocked"));

        var cts = new CancellationTokenSource(100);
        var results = await coordinator.ReadAllAsync(cts.Token)
            .Take(2)
            .ToListAsync();

        // Assert - Only one event should pass filter
        Assert.Single(results);
        Assert.Equal("allowed", ((TestEvent)results[0]).Message);
    }

    [Fact]
    public async Task WaitForResponseAsync_ReturnsResponse()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var requestId = "test-request";

        // Act
        var responseTask = coordinator.WaitForResponseAsync<TestEvent>(
            requestId,
            TimeSpan.FromSeconds(5));

        coordinator.SendResponse(requestId, new TestEvent("response"));

        var result = await responseTask;

        // Assert
        Assert.NotNull(result);
        Assert.Equal("response", result.Message);
    }

    [Fact]
    public async Task WaitForResponseAsync_ThrowsTimeoutException()
    {
        // Arrange
        var coordinator = new EventCoordinator();

        // Act & Assert
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
        // Arrange
        var coordinator = new EventCoordinator();
        var requestId = "test-request";

        // Act
        var responseTask = coordinator.WaitForResponseAsync<TestControlEvent>(
            requestId,
            TimeSpan.FromSeconds(5));

        coordinator.SendResponse(requestId, new TestEvent("wrong-type"));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await responseTask);
    }

    [Fact]
    public void EmitUpstream_EmitsToUpstreamChannel()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var evt = new TestEvent("upstream");

        // Act & Assert (should not throw)
        coordinator.EmitUpstream(evt);
        Assert.Equal(1, evt.SequenceNumber);
    }

    [Fact]
    public async Task EmitUpstream_BubblesToParent()
    {
        // Arrange
        var parent = new EventCoordinator();
        var child = new EventCoordinator();
        child.SetParent(parent);

        // Act
        child.EmitUpstream(new TestEvent("upstream-bubbled"));

        var cts = new CancellationTokenSource(100);
        var result = await parent.ReadAllAsync(cts.Token).FirstOrDefaultAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("upstream-bubbled", ((TestEvent)result).Message);
    }

    [Fact]
    public void Dispose_CompletesChannels()
    {
        // Arrange
        var coordinator = new EventCoordinator();

        // Act
        coordinator.Dispose();

        // Assert - Should throw ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => coordinator.Emit(new TestEvent("test")));
    }

    [Fact]
    public async Task StreamRegistry_IsAccessible()
    {
        // Arrange
        var coordinator = new EventCoordinator();

        // Act
        var registry = coordinator.Streams;

        // Assert
        Assert.NotNull(registry);
        Assert.IsAssignableFrom<IStreamRegistry>(registry);
    }
}
