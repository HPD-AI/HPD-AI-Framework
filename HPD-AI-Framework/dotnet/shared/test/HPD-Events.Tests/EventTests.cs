using HPD.Events;

namespace HPD.Events.Tests;

/// <summary>
/// Tests for Event base class functionality.
/// </summary>
public class EventTests
{
    // Test event types
    private record TestEvent : Event;
    private record TestLifecycleEvent : Event
    {
        public EventKind Kind { get; init; } = EventKind.Lifecycle;
    }

    [Fact]
    public void Event_HasAutomaticTimestamp()
    {
        // Arrange & Act
        var before = DateTimeOffset.UtcNow;
        var evt = new TestEvent();
        var after = DateTimeOffset.UtcNow;

        // Assert
        Assert.True(evt.Timestamp >= before && evt.Timestamp <= after,
            "Event timestamp should be set automatically to current UTC time");
    }

    [Fact]
    public void Event_DefaultKind_IsContent()
    {
        // Arrange & Act
        var evt = new TestEvent();

        // Assert
        Assert.Equal(EventKind.Content, evt.Kind);
    }

    [Fact]
    public void Event_DefaultPriority_IsNormal()
    {
        // Arrange & Act
        var evt = new TestEvent();

        // Assert
        Assert.Equal(EventPriority.Normal, evt.Priority);
    }

    [Fact]
    public void Event_DefaultDirection_IsDownstream()
    {
        // Arrange & Act
        var evt = new TestEvent();

        // Assert
        Assert.Equal(EventDirection.Downstream, evt.Direction);
    }

    [Fact]
    public void Event_DefaultCanInterrupt_IsTrue()
    {
        // Arrange & Act
        var evt = new TestEvent();

        // Assert
        Assert.True(evt.CanInterrupt);
    }

    [Fact]
    public void Event_SequenceNumber_DefaultsToZero()
    {
        // Arrange & Act
        var evt = new TestEvent();

        // Assert
        Assert.Equal(0, evt.SequenceNumber);
    }

    [Fact]
    public void Event_StreamId_DefaultsToNull()
    {
        // Arrange & Act
        var evt = new TestEvent();

        // Assert
        Assert.Null(evt.StreamId);
    }

    [Fact]
    public void Event_Extensions_DefaultsToNull()
    {
        // Arrange & Act
        var evt = new TestEvent();

        // Assert
        Assert.Null(evt.Extensions);
    }

    [Fact]
    public void Event_CanOverrideKind()
    {
        // Arrange & Act
        var evt = new TestLifecycleEvent();

        // Assert
        Assert.Equal(EventKind.Lifecycle, evt.Kind);
    }

    [Fact]
    public void Event_CanSetPriority()
    {
        // Arrange & Act
        var evt = new TestEvent { Priority = EventPriority.Immediate };

        // Assert
        Assert.Equal(EventPriority.Immediate, evt.Priority);
    }

    [Fact]
    public void Event_CanSetDirection()
    {
        // Arrange & Act
        var evt = new TestEvent { Direction = EventDirection.Upstream };

        // Assert
        Assert.Equal(EventDirection.Upstream, evt.Direction);
    }

    [Fact]
    public void Event_CanSetStreamId()
    {
        // Arrange & Act
        var evt = new TestEvent { StreamId = "stream-123" };

        // Assert
        Assert.Equal("stream-123", evt.StreamId);
    }

    [Fact]
    public void Event_CanSetCanInterrupt()
    {
        // Arrange & Act
        var evt = new TestEvent { CanInterrupt = false };

        // Assert
        Assert.False(evt.CanInterrupt);
    }

    [Fact]
    public void Event_CanSetExtensions()
    {
        // Arrange
        var extensions = new Dictionary<string, object>
        {
            ["key1"] = "value1",
            ["key2"] = 42
        };

        // Act
        var evt = new TestEvent { Extensions = extensions };

        // Assert
        Assert.NotNull(evt.Extensions);
        Assert.Equal("value1", evt.Extensions["key1"]);
        Assert.Equal(42, evt.Extensions["key2"]);
    }

    [Fact]
    public void Event_SequenceNumber_CanBeSet()
    {
        // Arrange & Act
        var evt = new TestEvent { SequenceNumber = 123 };

        // Assert
        Assert.Equal(123, evt.SequenceNumber);
    }

    [Fact]
    public void Event_RecordEquality_WorksCorrectly()
    {
        // Arrange
        var timestamp1 = DateTimeOffset.UtcNow;
        var timestamp2 = timestamp1.AddSeconds(1);
        var evt1 = new TestEvent { Timestamp = timestamp1 };
        var evt2 = new TestEvent { Timestamp = timestamp2 };

        // Act & Assert
        Assert.NotEqual(evt1, evt2); // Different timestamps
    }

    [Fact]
    public void Event_WithSyntax_CreatesNewInstance()
    {
        // Arrange
        var evt1 = new TestEvent { StreamId = "stream-1" };

        // Act
        var evt2 = evt1 with { StreamId = "stream-2" };

        // Assert
        Assert.Equal("stream-1", evt1.StreamId);
        Assert.Equal("stream-2", evt2.StreamId);
    }
}
