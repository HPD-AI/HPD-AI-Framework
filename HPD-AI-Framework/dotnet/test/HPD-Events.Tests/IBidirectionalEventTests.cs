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

        // Act - Emit request and wait for response in background
        var responseTask = Task.Run(async () =>
        {
            coordinator.Emit(request);
            return await coordinator.WaitForResponseAsync<TestResponseEvent>(
                requestId,
                timeout: TimeSpan.FromSeconds(2),
                CancellationToken.None
            );
        });

        // Simulate handler receiving request and sending response
        await Task.Delay(100); // Let request emit
        coordinator.SendResponse(requestId, new TestResponseEvent
        {
            RequestId = requestId,
            SourceName = "Responder",
            Success = true
        });

        var response = await responseTask;

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

        coordinator.Emit(request);

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await coordinator.WaitForResponseAsync<TestResponseEvent>(
                requestId,
                timeout: TimeSpan.FromMilliseconds(100),
                CancellationToken.None
            );
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
