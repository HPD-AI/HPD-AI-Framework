using HPD.Events;

namespace HPD.Events.Tests;

/// <summary>
/// Tests for IEventObserver&lt;TEvent&gt; interface.
/// </summary>
public class IEventObserverTests
{
    // Test event
    private record TestEvent : Event
    {
        public required string Data { get; init; }
    }

    // Test observer implementation
    private class TestObserver : IEventObserver<TestEvent>
    {
        public List<TestEvent> ReceivedEvents { get; } = new();
        public int ProcessedCount { get; private set; }

        public bool ShouldProcess(TestEvent evt) => true;

        public Task OnEventAsync(TestEvent evt, CancellationToken cancellationToken = default)
        {
            ReceivedEvents.Add(evt);
            ProcessedCount++;
            return Task.CompletedTask;
        }
    }

    // Filtering observer
    private class FilteringObserver : IEventObserver<TestEvent>
    {
        public List<TestEvent> ReceivedEvents { get; } = new();

        public bool ShouldProcess(TestEvent evt)
        {
            return evt.Data.StartsWith("important:");
        }

        public Task OnEventAsync(TestEvent evt, CancellationToken cancellationToken = default)
        {
            ReceivedEvents.Add(evt);
            return Task.CompletedTask;
        }
    }

    // Async observer
    private class AsyncObserver : IEventObserver<TestEvent>
    {
        public List<TestEvent> ReceivedEvents { get; } = new();
        public TimeSpan ProcessingDelay { get; set; } = TimeSpan.FromMilliseconds(10);

        public bool ShouldProcess(TestEvent evt) => true;

        public async Task OnEventAsync(TestEvent evt, CancellationToken cancellationToken = default)
        {
            await Task.Delay(ProcessingDelay, cancellationToken);
            ReceivedEvents.Add(evt);
        }
    }

    [Fact]
    public async Task IEventObserver_ReceivesEvents()
    {
        // Arrange
        var observer = new TestObserver();
        var evt = new TestEvent { Data = "test" };

        // Act
        await observer.OnEventAsync(evt);

        // Assert
        Assert.Single(observer.ReceivedEvents);
        Assert.Equal("test", observer.ReceivedEvents[0].Data);
        Assert.Equal(1, observer.ProcessedCount);
    }

    [Fact]
    public async Task IEventObserver_ProcessesMultipleEvents()
    {
        // Arrange
        var observer = new TestObserver();
        var events = new[]
        {
            new TestEvent { Data = "event1" },
            new TestEvent { Data = "event2" },
            new TestEvent { Data = "event3" }
        };

        // Act
        foreach (var evt in events)
        {
            await observer.OnEventAsync(evt);
        }

        // Assert
        Assert.Equal(3, observer.ReceivedEvents.Count);
        Assert.Equal(3, observer.ProcessedCount);
    }

    [Fact]
    public void IEventObserver_DefaultShouldProcessReturnsTrue()
    {
        // Arrange
        var observer = new TestObserver();
        var evt = new TestEvent { Data = "test" };

        // Act
        var shouldProcess = observer.ShouldProcess(evt);

        // Assert
        Assert.True(shouldProcess);
    }

    [Fact]
    public void IEventObserver_CanFilterEvents()
    {
        // Arrange
        var observer = new FilteringObserver();

        var importantEvt = new TestEvent { Data = "important:data" };
        var regularEvt = new TestEvent { Data = "regular data" };

        // Act & Assert
        Assert.True(observer.ShouldProcess(importantEvt));
        Assert.False(observer.ShouldProcess(regularEvt));
    }

    [Fact]
    public async Task IEventObserver_FiltersEventsBeforeProcessing()
    {
        // Arrange
        var observer = new FilteringObserver();
        var events = new[]
        {
            new TestEvent { Data = "important:event1" },
            new TestEvent { Data = "regular event" },
            new TestEvent { Data = "important:event2" }
        };

        // Act
        foreach (var evt in events)
        {
            if (observer.ShouldProcess(evt))
            {
                await observer.OnEventAsync(evt);
            }
        }

        // Assert
        Assert.Equal(2, observer.ReceivedEvents.Count);
        Assert.All(observer.ReceivedEvents, evt =>
            Assert.StartsWith("important:", evt.Data));
    }

    [Fact]
    public async Task IEventObserver_HandlesAsyncProcessing()
    {
        // Arrange
        var observer = new AsyncObserver { ProcessingDelay = TimeSpan.FromMilliseconds(50) };
        var evt = new TestEvent { Data = "async test" };

        // Act
        var startTime = DateTimeOffset.UtcNow;
        await observer.OnEventAsync(evt);
        var duration = DateTimeOffset.UtcNow - startTime;

        // Assert
        Assert.Single(observer.ReceivedEvents);
        Assert.True(duration >= TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task IEventObserver_SupportsCancellation()
    {
        // Arrange
        var observer = new AsyncObserver { ProcessingDelay = TimeSpan.FromSeconds(10) };
        var evt = new TestEvent { Data = "test" };
        var cts = new CancellationTokenSource();

        // Act
        var task = observer.OnEventAsync(evt, cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Assert
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
        Assert.Empty(observer.ReceivedEvents); // Event wasn't fully processed
    }

    [Fact]
    public async Task IEventObserver_MultipleObserversIndependent()
    {
        // Arrange
        var observer1 = new TestObserver();
        var observer2 = new TestObserver();
        var evt = new TestEvent { Data = "test" };

        // Act
        await observer1.OnEventAsync(evt);
        await observer2.OnEventAsync(evt);

        // Assert
        Assert.Single(observer1.ReceivedEvents);
        Assert.Single(observer2.ReceivedEvents);
        Assert.NotSame(observer1.ReceivedEvents, observer2.ReceivedEvents);
    }

    [Fact]
    public void IEventObserver_IsGenericOverEventType()
    {
        // Arrange
        var observer = new TestObserver();

        // Assert
        Assert.IsAssignableFrom<IEventObserver<TestEvent>>(observer);
    }
}
