using HPD.Events;
using HPD.Events.Core;

namespace HPD.Events.Tests;

/// <summary>
/// Tests for request-session contracts and lifecycle events.
/// </summary>
public class RequestSessionContractTests
{
    private record TestRequestEvent : Event, IRequestEvent
    {
        public required string RequestId { get; init; }
        public required string SourceName { get; init; }
        public string? TestData { get; init; }

        public new EventKind Kind { get; init; } = EventKind.Control;
    }

    private record TestResponseEvent : Event, IResponseEvent
    {
        public required string RequestId { get; init; }
        public required string SourceName { get; init; }
        public required bool Success { get; init; }

        public new EventKind Kind { get; init; } = EventKind.Control;
    }

    [Fact]
    public void IRequestEvent_RequiresRequestId()
    {
        // Arrange & Act
        var evt = new TestRequestEvent
        {
            RequestId = "test-123",
            SourceName = "TestSource"
        };

        // Assert
        Assert.Equal("test-123", evt.RequestId);
        Assert.NotNull(evt.RequestId);
    }

    [Fact]
    public void IRequestEvent_RequiresSourceName()
    {
        // Arrange & Act
        var evt = new TestRequestEvent
        {
            RequestId = "test-123",
            SourceName = "TestSource"
        };

        // Assert
        Assert.Equal("TestSource", evt.SourceName);
        Assert.NotNull(evt.SourceName);
    }

    [Fact]
    public void IRequestEvent_InheritsFromEvent()
    {
        // Arrange & Act
        var evt = new TestRequestEvent
        {
            RequestId = "test-123",
            SourceName = "TestSource"
        };

        // Assert
        Assert.IsAssignableFrom<Event>(evt);
        Assert.IsAssignableFrom<IRequestEvent>(evt);
    }

    [Fact]
    public void IRequestEvent_DefaultsToControlKind()
    {
        // Arrange & Act
        var evt = new TestRequestEvent
        {
            RequestId = "test-123",
            SourceName = "TestSource"
        };

        // Assert
        Assert.Equal(EventKind.Control, evt.Kind);
    }

    [Fact]
    public async Task RequestSession_CanBeUsedForRequestResponsePattern()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var requestId = Guid.NewGuid().ToString();
        var started = new TaskCompletionSource<RequestStartedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolved = new TaskCompletionSource<RequestResolvedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var lifecycleSubscription = coordinator.SubscribeAny(evt =>
        {
            if (evt is RequestStartedEvent startedEvent)
                started.TrySetResult(startedEvent);
            else if (evt is RequestResolvedEvent resolvedEvent)
                resolved.TrySetResult(resolvedEvent);

            return ValueTask.CompletedTask;
        });

        var request = new TestRequestEvent
        {
            RequestId = requestId,
            SourceName = "Requester",
            TestData = "Please process this"
        };

        using var subscription = coordinator.Subscribe<TestRequestEvent>(evt =>
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
        var response = await coordinator.RequestAsync<TestRequestEvent, TestResponseEvent>(
            request,
            timeout: TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal(requestId, response.RequestId);
        Assert.Equal("Responder", response.SourceName);
        Assert.True(response.Success);
        Assert.Equal(requestId, (await started.Task.WaitAsync(TimeSpan.FromSeconds(2))).RequestId);
        Assert.Equal(requestId, (await resolved.Task.WaitAsync(TimeSpan.FromSeconds(2))).RequestId);
    }

    [Fact]
    public async Task RequestSession_TimesOutWhenNoResponseReceived()
    {
        // Arrange
        var coordinator = new EventCoordinator();
        var requestId = Guid.NewGuid().ToString();
        var expired = new TaskCompletionSource<RequestExpiredEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = coordinator.Subscribe<RequestExpiredEvent>(evt =>
        {
            expired.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        var request = new TestRequestEvent
        {
            RequestId = requestId,
            SourceName = "Requester"
        };

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await coordinator.RequestAsync<TestRequestEvent, TestResponseEvent>(
                request,
                timeout: TimeSpan.FromMilliseconds(100),
                CancellationToken.None);
        });
        Assert.Equal(requestId, (await expired.Task.WaitAsync(TimeSpan.FromSeconds(2))).RequestId);
    }

    [Fact]
    public async Task Respond_ReturnsAlreadyResolvedForDuplicateResponse()
    {
        var coordinator = new EventCoordinator();
        var requestId = Guid.NewGuid().ToString();
        var request = new TestRequestEvent
        {
            RequestId = requestId,
            SourceName = "Requester"
        };

        var handle = coordinator.StartRequest<TestRequestEvent, TestResponseEvent>(request);
        var accepted = coordinator.Respond(new TestResponseEvent
        {
            RequestId = requestId,
            SourceName = "Responder",
            Success = true
        });
        var duplicate = coordinator.Respond(new TestResponseEvent
        {
            RequestId = requestId,
            SourceName = "Responder",
            Success = true
        });

        Assert.True(accepted.Accepted);
        Assert.Equal(RespondStatus.AlreadyResolved, duplicate.Status);
        Assert.True(((TestResponseEvent)await handle.Response).Success);
    }

    [Fact]
    public void IRequestEvent_CanCarryDomainSpecificData()
    {
        // Arrange & Act
        var evt = new TestRequestEvent
        {
            RequestId = "test-123",
            SourceName = "TestSource",
            TestData = "Domain-specific payload"
        };

        // Assert
        Assert.Equal("Domain-specific payload", evt.TestData);
    }

    [Fact]
    public void IRequestEvent_RecordEquality()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        var evt1 = new TestRequestEvent
        {
            RequestId = "test-123",
            SourceName = "TestSource",
            TestData = "Data",
            Timestamp = timestamp
        };

        var evt2 = new TestRequestEvent
        {
            RequestId = "test-123",
            SourceName = "TestSource",
            TestData = "Data",
            Timestamp = timestamp
        };

        var evt3 = new TestRequestEvent
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
