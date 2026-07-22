using HPD.Agent;
using HPD.Agent.Serialization;
using HPD.Events;
using System.Text.Json.Serialization;
using Xunit;

namespace CustomEventSourceGenerationFixtures.Serialization;

public sealed record GeneratedCustomProgressEvent(string Step, int Percent) : AgentEvent;

[EventType("CUSTOM_SOURCE_GEN_EVENT")]
public sealed record AttributeNamedCustomEvent(string Value) : AgentEvent;

public readonly record struct GeneratedCustomStructSample(
    string Stage,
    int Count,
    EventKind Kind = EventKind.Diagnostic,
    long SequenceNumber = 0,
    long TimestampNs = 0) : AgentStructEvent;

[EventType("CUSTOM_SOURCE_GEN_STRUCT_SAMPLE")]
public readonly record struct AttributeNamedStructSample(
    string Value,
    EventKind Kind = EventKind.Diagnostic,
    long SequenceNumber = 0,
    long TimestampNs = 0) : AgentStructEvent;

public readonly record struct ManuallyRegisteredStructSample(
    string Value,
    EventKind Kind = EventKind.Diagnostic,
    long SequenceNumber = 0,
    long TimestampNs = 0) : AgentStructEvent;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(ManuallyRegisteredStructSample))]
internal partial class ManualStructSampleJsonContext : JsonSerializerContext;

public class CustomEventSourceGenerationTests
{
    [Fact]
    public void SourceGeneratedCustomEvent_RoundTripsThroughAgentEventSerializer()
    {
        var json = AgentEventSerializer.ToJson(new GeneratedCustomProgressEvent("retrieval", 40));

        Assert.Contains("\"type\":\"GENERATED_CUSTOM_PROGRESS\"", json);
        Assert.Equal("GENERATED_CUSTOM_PROGRESS", AgentEventSerializer.GetEventTypeName(typeof(GeneratedCustomProgressEvent)));

        var roundTripped = Assert.IsType<GeneratedCustomProgressEvent>(
            AgentEventSerializer.FromJson(json));

        Assert.Equal("retrieval", roundTripped.Step);
        Assert.Equal(40, roundTripped.Percent);
    }

    [Fact]
    public void SourceGeneratedCustomEvent_UsesEventTypeAttribute()
    {
        var json = AgentEventSerializer.ToJson(new AttributeNamedCustomEvent("hello"));

        Assert.Contains("\"type\":\"CUSTOM_SOURCE_GEN_EVENT\"", json);
        Assert.Equal("CUSTOM_SOURCE_GEN_EVENT", AgentEventSerializer.GetEventTypeName(typeof(AttributeNamedCustomEvent)));

        var roundTripped = Assert.IsType<AttributeNamedCustomEvent>(
            AgentEventSerializer.FromJson(json));

        Assert.Equal("hello", roundTripped.Value);
    }

    [Fact]
    public void EventRegistration_SameTypeAndDiscriminator_IsIdempotentAndInspectable()
    {
        _ = AgentEventSerializer.ToJson(new GeneratedCustomProgressEvent("registration", 1));
        Assert.True(AgentEventSerializer.TryGetEventTypeRegistration(
            typeof(GeneratedCustomProgressEvent),
            out var existing));

        Parallel.For(0, 32, _ => AgentEventSerializer.RegisterEventType(
            typeof(GeneratedCustomProgressEvent),
            existing.Discriminator,
            existing.TypeInfo));

        Assert.True(AgentEventSerializer.TryGetEventTypeRegistration(
            typeof(GeneratedCustomProgressEvent),
            out var final));
        Assert.Equal(existing, final);
    }

    [Fact]
    public void EventRegistration_SameTypeWithDifferentDiscriminator_FailsWithoutMutation()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AgentEventSerializer.RegisterEventType(
                typeof(GeneratedCustomProgressEvent),
                "CONFLICTING_CUSTOM_PROGRESS"));

        Assert.Contains("already registered", exception.Message);
        Assert.Equal(
            "GENERATED_CUSTOM_PROGRESS",
            AgentEventSerializer.GetEventTypeName(typeof(GeneratedCustomProgressEvent)));
    }

    [Fact]
    public void EventRegistration_SameDiscriminatorWithDifferentType_FailsWithoutMutation()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AgentEventSerializer.RegisterEventType(
                typeof(AttributeNamedCustomEvent),
                "GENERATED_CUSTOM_PROGRESS"));

        Assert.Contains("already registered", exception.Message);
        Assert.Equal(
            "CUSTOM_SOURCE_GEN_EVENT",
            AgentEventSerializer.GetEventTypeName(typeof(AttributeNamedCustomEvent)));
    }

    [Fact]
    public void SourceGeneratedCustomStructEvent_RoundTripsThroughAgentStructEventSerializer()
    {
        var json = AgentStructEventSerializer.ToJson(new GeneratedCustomStructSample("audio", 12));

        Assert.Contains("\"type\":\"GENERATED_CUSTOM_STRUCT_SAMPLE\"", json);
        Assert.Equal("GENERATED_CUSTOM_STRUCT_SAMPLE", AgentStructEventSerializer.GetEventTypeName(typeof(GeneratedCustomStructSample)));

        var roundTripped = Assert.IsType<GeneratedCustomStructSample>(
            AgentStructEventSerializer.FromJson(json));

        Assert.Equal("audio", roundTripped.Stage);
        Assert.Equal(12, roundTripped.Count);
        Assert.Equal(EventKind.Diagnostic, roundTripped.Kind);
    }

    [Fact]
    public void SourceGeneratedCustomStructEvent_UsesEventTypeAttribute()
    {
        var json = AgentStructEventSerializer.ToJson(new AttributeNamedStructSample("hello"));

        Assert.Contains("\"type\":\"CUSTOM_SOURCE_GEN_STRUCT_SAMPLE\"", json);
        Assert.Equal("CUSTOM_SOURCE_GEN_STRUCT_SAMPLE", AgentStructEventSerializer.GetEventTypeName(typeof(AttributeNamedStructSample)));

        var roundTripped = Assert.IsType<AttributeNamedStructSample>(
            AgentStructEventSerializer.FromJson(json));

        Assert.Equal("hello", roundTripped.Value);
    }

    [Fact]
    public void ManualStructEventRegistration_CanProvideJsonTypeInfo()
    {
        AgentStructEventSerializer.RegisterEventType(
            typeof(ManuallyRegisteredStructSample),
            "MANUAL_STRUCT_SAMPLE",
            ManualStructSampleJsonContext.Default.ManuallyRegisteredStructSample);

        var json = AgentStructEventSerializer.ToJson(new ManuallyRegisteredStructSample("manual"));

        Assert.Contains("\"type\":\"MANUAL_STRUCT_SAMPLE\"", json);

        var roundTripped = Assert.IsType<ManuallyRegisteredStructSample>(
            AgentStructEventSerializer.FromJson(json));

        Assert.Equal("manual", roundTripped.Value);
    }
}
