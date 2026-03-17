using HPD.Events;

namespace HPD.Events.Tests;

/// <summary>
/// Tests for IEventHandler&lt;TEvent&gt; interface.
/// </summary>
public class IEventHandlerTests
{
    // Test event
    private record TestEvent : Event
    {
        public required string Data { get; init; }
    }

    // Test handler implementation
    private class TestHandler : IEventHandler<TestEvent>
    {
        public List<TestEvent> HandledEvents { get; } = new();
        public List<DateTimeOffset> HandlingTimes { get; } = new();

        public bool ShouldProcess(TestEvent evt) => true;

        public Task OnEventAsync(TestEvent evt, CancellationToken cancellationToken = default)
        {
            HandledEvents.Add(evt);
            HandlingTimes.Add(DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }
    }

    // Blocking handler (simulates synchronous UI updates)
    private class BlockingHandler : IEventHandler<TestEvent>
    {
        public List<TestEvent> HandledEvents { get; } = new();
        public TimeSpan BlockingDuration { get; set; } = TimeSpan.FromMilliseconds(10);

        public bool ShouldProcess(TestEvent evt) => true;

        public async Task OnEventAsync(TestEvent evt, CancellationToken cancellationToken = default)
        {
            await Task.Delay(BlockingDuration, cancellationToken);
            HandledEvents.Add(evt);
        }
    }

    // Filtering handler
    private class FilteringHandler : IEventHandler<TestEvent>
    {
        public List<TestEvent> HandledEvents { get; } = new();

        public bool ShouldProcess(TestEvent evt)
        {
            return evt.Priority == EventPriority.Control;
        }

        public Task OnEventAsync(TestEvent evt, CancellationToken cancellationToken = default)
        {
            HandledEvents.Add(evt);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task IEventHandler_HandlesEvents()
    {
        // Arrange
        var handler = new TestHandler();
        var evt = new TestEvent { Data = "test" };

        // Act
        await handler.OnEventAsync(evt);

        // Assert
        Assert.Single(handler.HandledEvents);
        Assert.Equal("test", handler.HandledEvents[0].Data);
    }

    [Fact]
    public async Task IEventHandler_PreservesEventOrder()
    {
        // Arrange
        var handler = new TestHandler();
        var events = new[]
        {
            new TestEvent { Data = "first" },
            new TestEvent { Data = "second" },
            new TestEvent { Data = "third" }
        };

        // Act
        foreach (var evt in events)
        {
            await handler.OnEventAsync(evt);
        }

        // Assert
        Assert.Equal(3, handler.HandledEvents.Count);
        Assert.Equal("first", handler.HandledEvents[0].Data);
        Assert.Equal("second", handler.HandledEvents[1].Data);
        Assert.Equal("third", handler.HandledEvents[2].Data);
    }

    [Fact]
    public async Task IEventHandler_BlocksUntilComplete()
    {
        // Arrange
        var handler = new BlockingHandler { BlockingDuration = TimeSpan.FromMilliseconds(100) };
        var evt = new TestEvent { Data = "test" };

        // Act
        var startTime = DateTimeOffset.UtcNow;
        await handler.OnEventAsync(evt);
        var duration = DateTimeOffset.UtcNow - startTime;

        // Assert — allow 20 ms of timer jitter below the nominal delay
        Assert.True(duration >= TimeSpan.FromMilliseconds(80),
            "Handler should block until processing completes");
        Assert.Single(handler.HandledEvents);
    }

    [Fact]
    public async Task IEventHandler_ProcessesSequentially()
    {
        // Arrange
        var handler = new BlockingHandler { BlockingDuration = TimeSpan.FromMilliseconds(50) };
        var events = new[]
        {
            new TestEvent { Data = "event1" },
            new TestEvent { Data = "event2" }
        };

        // Act
        var startTime = DateTimeOffset.UtcNow;
        foreach (var evt in events)
        {
            await handler.OnEventAsync(evt);
        }
        var duration = DateTimeOffset.UtcNow - startTime;

        // Assert — two 50 ms delays; allow 20 ms total jitter below the nominal sum
        Assert.True(duration >= TimeSpan.FromMilliseconds(80),
            "Sequential processing should take sum of durations");
        Assert.Equal(2, handler.HandledEvents.Count);
    }

    [Fact]
    public void IEventHandler_DefaultShouldProcessReturnsTrue()
    {
        // Arrange
        var handler = new TestHandler();
        var evt = new TestEvent { Data = "test" };

        // Act
        var shouldProcess = handler.ShouldProcess(evt);

        // Assert
        Assert.True(shouldProcess);
    }

    [Fact]
    public void IEventHandler_CanFilterByPriority()
    {
        // Arrange
        var handler = new FilteringHandler();

        var controlEvt = new TestEvent { Data = "control", Priority = EventPriority.Control };
        var normalEvt = new TestEvent { Data = "normal", Priority = EventPriority.Normal };

        // Act & Assert
        Assert.True(handler.ShouldProcess(controlEvt));
        Assert.False(handler.ShouldProcess(normalEvt));
    }

    [Fact]
    public async Task IEventHandler_FiltersBeforeHandling()
    {
        // Arrange
        var handler = new FilteringHandler();
        var events = new[]
        {
            new TestEvent { Data = "control1", Priority = EventPriority.Control },
            new TestEvent { Data = "normal", Priority = EventPriority.Normal },
            new TestEvent { Data = "control2", Priority = EventPriority.Control }
        };

        // Act
        foreach (var evt in events)
        {
            if (handler.ShouldProcess(evt))
            {
                await handler.OnEventAsync(evt);
            }
        }

        // Assert
        Assert.Equal(2, handler.HandledEvents.Count);
        Assert.All(handler.HandledEvents, evt =>
            Assert.Equal(EventPriority.Control, evt.Priority));
    }

    [Fact]
    public async Task IEventHandler_SupportsCancellation()
    {
        // Arrange
        var handler = new BlockingHandler { BlockingDuration = TimeSpan.FromSeconds(10) };
        var evt = new TestEvent { Data = "test" };
        var cts = new CancellationTokenSource();

        // Act
        var task = handler.OnEventAsync(evt, cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Assert
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
        Assert.Empty(handler.HandledEvents);
    }

    [Fact]
    public async Task IEventHandler_GuaranteesOrdering()
    {
        // Arrange
        var handler = new TestHandler();
        var events = Enumerable.Range(1, 100)
            .Select(i => new TestEvent { Data = $"event{i}" })
            .ToArray();

        // Act
        foreach (var evt in events)
        {
            await handler.OnEventAsync(evt);
        }

        // Assert
        Assert.Equal(100, handler.HandledEvents.Count);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal($"event{i + 1}", handler.HandledEvents[i].Data);
        }
    }

    [Fact]
    public async Task IEventHandler_TimestampsShowSequentialProcessing()
    {
        // Arrange
        var handler = new TestHandler();
        var events = new[]
        {
            new TestEvent { Data = "event1" },
            new TestEvent { Data = "event2" },
            new TestEvent { Data = "event3" }
        };

        // Act
        foreach (var evt in events)
        {
            await handler.OnEventAsync(evt);
        }

        // Assert
        Assert.Equal(3, handler.HandlingTimes.Count);
        Assert.True(handler.HandlingTimes[1] >= handler.HandlingTimes[0]);
        Assert.True(handler.HandlingTimes[2] >= handler.HandlingTimes[1]);
    }

    [Fact]
    public void IEventHandler_DifferentFromObserver()
    {
        // This test documents the conceptual difference:
        // - Handler: Synchronous, ordered, blocks stream
        // - Observer: Fire-and-forget, async, non-blocking

        // Arrange
        var handler = new BlockingHandler { BlockingDuration = TimeSpan.FromMilliseconds(100) };

        // Assert
        Assert.IsAssignableFrom<IEventHandler<TestEvent>>(handler);
        // Handler guarantees ordering by blocking
    }
}
