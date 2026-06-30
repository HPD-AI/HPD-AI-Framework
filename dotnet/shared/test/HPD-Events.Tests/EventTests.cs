using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Events;

namespace HPD.Events.Tests;

public class EventTests
{
    private record TestEvent : Event;

    private record TestLifecycleEvent : Event
    {
        public override EventKind Kind { get; init; } = EventKind.Lifecycle;
    }

    private record TestStreamingEvent : Event
    {
        public override EventChannel Channel { get; init; } = EventChannel.Streaming;
    }

    [Fact]
    public void Event_HasAutomaticTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var evt = new TestEvent();
        var after = DateTimeOffset.UtcNow;

        Assert.True(evt.Timestamp >= before && evt.Timestamp <= after);
    }

    [Fact]
    public void Event_Defaults_AreSafe()
    {
        var evt = new TestEvent();

        Assert.Equal(EventKind.Content, evt.Kind);
        Assert.Equal(EventChannel.Synchronous, evt.Channel);
        Assert.Equal(EventDirection.Downstream, evt.Direction);
        Assert.Equal(0, evt.SequenceNumber);
        Assert.Equal(0, evt.ExchangeTimestampNs);
        Assert.True(evt.CanInterrupt);
        Assert.Null(evt.EventFlowId);
    }

    [Fact]
    public void Event_CanOverrideKind()
    {
        var evt = new TestLifecycleEvent();

        Assert.Equal(EventKind.Lifecycle, evt.Kind);
    }

    [Fact]
    public void Event_CanOverrideChannel()
    {
        var evt = new TestStreamingEvent();

        Assert.Equal(EventChannel.Streaming, evt.Channel);
    }

    [Fact]
    public void Event_CanSetDirection()
    {
        var evt = new TestEvent { Direction = EventDirection.Upstream };

        Assert.Equal(EventDirection.Upstream, evt.Direction);
    }

    [Fact]
    public void Event_CanSetEventFlowMetadata()
    {
        var evt = new TestEvent
        {
            EventFlowId = "flow-123",
            CanInterrupt = false
        };

        Assert.Equal("flow-123", evt.EventFlowId);
        Assert.False(evt.CanInterrupt);
    }

    [Fact]
    public void Event_DoesNotExposeExtensionsProperty()
    {
        Assert.Null(typeof(Event).GetProperty("Extensions"));
    }

    [Fact]
    public void Event_SequenceNumber_CanBeSet()
    {
        var evt = new TestEvent { SequenceNumber = 123 };

        Assert.Equal(123, evt.SequenceNumber);
    }

    [Fact]
    public void Event_ExchangeTimestampNs_CanBeSet()
    {
        var evt = new TestEvent { ExchangeTimestampNs = 123456789 };

        Assert.Equal(123456789, evt.ExchangeTimestampNs);
    }

    [Fact]
    public void Event_RecordEquality_WorksCorrectly()
    {
        var timestamp1 = DateTimeOffset.UtcNow;
        var timestamp2 = timestamp1.AddSeconds(1);
        var evt1 = new TestEvent { Timestamp = timestamp1 };
        var evt2 = new TestEvent { Timestamp = timestamp2 };

        Assert.NotEqual(evt1, evt2);
    }

    [Fact]
    public void Event_WithSyntax_CreatesNewInstance()
    {
        var evt1 = new TestEvent { EventFlowId = "flow-1" };

        var evt2 = evt1 with { EventFlowId = "flow-2" };

        Assert.Equal("flow-1", evt1.EventFlowId);
        Assert.Equal("flow-2", evt2.EventFlowId);
    }

    [Fact]
    public void TimeEvent_UsesSynchronousLifecycleClassification()
    {
        var evt = new TimeEvent
        {
            TimerName = "timer",
            TriggerTime = DateTimeOffset.UtcNow
        };

        Assert.Equal(EventChannel.Synchronous, evt.Channel);
        Assert.Equal(EventKind.Lifecycle, evt.Kind);
    }

    [Fact]
    public void EventDroppedEvent_UsesControlDiagnosticClassification()
    {
        Event evt = new EventDroppedEvent("stream", "TestEvent", 42);

        Assert.Equal(EventChannel.Control, evt.Channel);
        Assert.Equal(EventKind.Diagnostic, evt.Kind);
    }

    [Fact]
    public void FixedEventClassification_RoundTrips_WithSourceGeneratedJsonMetadata()
    {
        var evt = new EventSourceGenerationTestEvent { Name = "fixed" };

        var json = JsonSerializer.Serialize(
            evt,
            EventTestsJsonSerializerContext.Default.EventSourceGenerationTestEvent);

        var deserialized = JsonSerializer.Deserialize(
            json,
            EventTestsJsonSerializerContext.Default.EventSourceGenerationTestEvent);

        Assert.NotNull(deserialized);
        Assert.Equal("fixed", deserialized.Name);
        Assert.Equal(EventKind.Lifecycle, deserialized.Kind);
        Assert.Equal(EventChannel.Control, deserialized.Channel);
    }

    [Fact]
    public void EventAnnotationValue_Constructors_SetExpectedScalar()
    {
        var text = EventAnnotationValue.FromString("alpha");
        var integer = EventAnnotationValue.FromInteger(42);
        var number = EventAnnotationValue.FromNumber(3.14);
        var boolean = EventAnnotationValue.FromBoolean(true);

        Assert.Equal(EventAnnotationValueKind.String, text.Kind);
        Assert.Equal("alpha", text.String);
        Assert.Equal(EventAnnotationValueKind.Integer, integer.Kind);
        Assert.Equal(42, integer.Integer);
        Assert.Equal(EventAnnotationValueKind.Number, number.Kind);
        Assert.Equal(3.14, number.Number);
        Assert.Equal(EventAnnotationValueKind.Boolean, boolean.Kind);
        Assert.True(boolean.Boolean);
    }

    [Fact]
    public void AnnotatedEvent_RoundTrips_WithSourceGeneratedJsonMetadata()
    {
        var evt = new AnnotatedSourceGenerationTestEvent
        {
            Name = "annotated",
            Annotations =
            [
                new EventAnnotation
                {
                    Key = "tenant",
                    Value = EventAnnotationValue.FromString("hpd"),
                    Visibility = EventAnnotationVisibility.Internal
                },
                new EventAnnotation
                {
                    Key = "display",
                    Value = EventAnnotationValue.FromBoolean(true),
                    Visibility = EventAnnotationVisibility.Public
                }
            ]
        };

        var json = JsonSerializer.Serialize(
            evt,
            EventTestsJsonSerializerContext.Default.AnnotatedSourceGenerationTestEvent);

        var deserialized = JsonSerializer.Deserialize(
            json,
            EventTestsJsonSerializerContext.Default.AnnotatedSourceGenerationTestEvent);

        Assert.NotNull(deserialized);
        Assert.Equal("annotated", deserialized.Name);
        Assert.Equal(2, deserialized.Annotations.Count);
        Assert.Equal("tenant", deserialized.Annotations[0].Key);
        Assert.Equal("hpd", deserialized.Annotations[0].Value.String);
        Assert.Equal(EventAnnotationVisibility.Internal, deserialized.Annotations[0].Visibility);
        Assert.True(deserialized.Annotations[1].Value.Boolean);
        Assert.Equal(EventAnnotationVisibility.Public, deserialized.Annotations[1].Visibility);
    }
}

internal sealed record EventSourceGenerationTestEvent : Event
{
    public required string Name { get; init; }

    public override EventKind Kind => EventKind.Lifecycle;

    public override EventChannel Channel => EventChannel.Control;
}

internal sealed record AnnotatedSourceGenerationTestEvent : Event, IAnnotatedEvent
{
    public required string Name { get; init; }

    public IReadOnlyList<EventAnnotation> Annotations { get; init; } = [];
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(EventSourceGenerationTestEvent))]
[JsonSerializable(typeof(AnnotatedSourceGenerationTestEvent))]
internal sealed partial class EventTestsJsonSerializerContext : JsonSerializerContext;
