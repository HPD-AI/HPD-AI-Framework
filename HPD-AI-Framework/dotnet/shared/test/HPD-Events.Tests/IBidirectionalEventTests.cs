using HPD.Events;
using HPD.Events.Core;

namespace HPD.Events.Tests;

/// <summary>
/// Tests for IBidirectionalEvent interface and implementations.
/// </summary>
public class IBidirectionalEventTests
{
    // Test event implementing IBidirectionalEvent
    private record TestBidirectionalEvent : Event, IBidirectionalEvent
    {
        public required string RequestId { get; init; }
        public required string SourceName { get; init; }
        public string? TestData { get; init; }

        public new EventKind Kind { get; init; } = EventKind.Control;
    }

    private record TestResponseEvent : Event, IBidirectionalEvent
    {
        public required string RequestId { get; init; }
        public required string SourceName { get; init; }
        public required bool Success { get; init; }

        public new EventKind Kind { get; init; } = EventKind.Control;
    }

    [Fact]
    public void IBidirectionalEvent_RequiresRequestId()
    {
        // Arrange & Act
        var evt = new TestBidirectionalEvent
        {
            RequestId = "test-123",
            SourceName = "TestSource"
        };

        // Assert
        Assert.Equal("test-123", evt.RequestId);
        Assert.NotNull(evt.RequestId);
    }

    [Fact]
    public void IBidirectionalEvent_RequiresSourceName()
    {
        // Arrange & Act
        var evt = new TestBidirectionalEvent
        {
            RequestId = "test-123",
            SourceName = "TestSource"
        };

        // Assert
        Assert.Equal("TestSource", evt.SourceName);
        Assert.NotNull(evt.SourceName);
    }

    [Fact]
    public void IBidirectionalEvent_InheritsFromEvent()
    {
        // Arrange & Act
        var evt = new TestBidirectionalEvent
        {
            RequestId = "test-123",
            SourceName = "TestSource"
        };

        // Assert
        Assert.IsAssignableFrom<Event>(evt);
        Assert.IsAssignableFrom<IBidirectionalEvent>(evt);
    }

    [Fact]
    public void IBidirectionalEvent_DefaultsToControlKind()
    {
        // Arrange & Act
        var evt = new TestBidirectionalEvent
        {
            RequestId = "test-123",
            SourceName = "TestSource"
        };

        // Assert
        Assert.Equal(EventKind.Control, evt.Kind);
    }

    [Fact]
    public async Task IBidirectionalEvent_CanBeUsedForRequestResponsePattern()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var requestId = Guid.NewGuid().ToString();

        var request = new TestBidirectionalEvent
        {
            RequestId = requestId,
            SourceName = "Requester",
            TestData = "Please process this"
        };

        using var subscription = coordinator.Subscribe<TestBidirectionalEvent>(evt =>
        {
            coordinator.Respond(evt.RequestId, new TestResponseEvent
            {
                RequestId = evt.RequestId,
                SourceName = "Responder",
                Success = true
            });
            return ValueTask.CompletedTask;
        });

        // Act
        var response = await coordinator.RequestAsync<TestBidirectionalEvent, TestResponseEvent>(
            request,
            timeout: TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal(requestId, response.RequestId);
        Assert.Equal("Responder", response.SourceName);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task IBidirectionalEvent_TimesOutWhenNoResponseReceived()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var requestId = Guid.NewGuid().ToString();

        var request = new TestBidirectionalEvent
        {
            RequestId = requestId,
            SourceName = "Requester"
        };

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await coordinator.RequestAsync<TestBidirectionalEvent, TestResponseEvent>(
                request,
                timeout: TimeSpan.FromMilliseconds(100),
                CancellationToken.None);
        });
    }

    [Fact]
    public void IBidirectionalEvent_CanCarryDomainSpecificData()
    {
        // Arrange & Act
        var evt = new TestBidirectionalEvent
        {
            RequestId = "test-123",
            SourceName = "TestSource",
            TestData = "Domain-specific payload"
        };

        // Assert
        Assert.Equal("Domain-specific payload", evt.TestData);
    }

    [Fact]
    public void IBidirectionalEvent_RecordEquality()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        var evt1 = new TestBidirectionalEvent
        {
            RequestId = "test-123",
            SourceName = "TestSource",
            TestData = "Data",
            Timestamp = timestamp
        };

        var evt2 = new TestBidirectionalEvent
        {
            RequestId = "test-123",
            SourceName = "TestSource",
            TestData = "Data",
            Timestamp = timestamp
        };

        var evt3 = new TestBidirectionalEvent
        {
            RequestId = "test-456",
            SourceName = "TestSource",
            TestData = "Data",
            Timestamp = timestamp
        };

        // Assert
        Assert.Equal(evt1, evt2);
        Assert.NotEqual(evt1, evt3);
    }
}
