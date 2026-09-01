using HPD.Agent;
using HPD.Agent.Serialization;
using HPD.Events;
using System.Text.Json.Serialization;

namespace CustomEventSourceGenerationFixtures.Serialization;

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
    public void SourceGeneratedCustomStructEvent_RoundTrips()
    {
        var json = AgentStructEventSerializer.ToJson(new GeneratedCustomStructSample("audio", 12));
        Assert.Contains("\"type\":\"GENERATED_CUSTOM_STRUCT_SAMPLE\"", json);
        var roundTripped = Assert.IsType<GeneratedCustomStructSample>(AgentStructEventSerializer.FromJson(json));
        Assert.Equal("audio", roundTripped.Stage);
        Assert.Equal(12, roundTripped.Count);
    }

    [Fact]
    public void StructEvent_UsesEventTypeAttribute()
    {
        var json = AgentStructEventSerializer.ToJson(new AttributeNamedStructSample("named"));
        Assert.Contains("\"type\":\"CUSTOM_SOURCE_GEN_STRUCT_SAMPLE\"", json);
        Assert.IsType<AttributeNamedStructSample>(AgentStructEventSerializer.FromJson(json));
    }

    [Fact]
    public void ExplicitStructRegistration_RemainsOnSeparateExportRail()
    {
        AgentStructEventSerializer.RegisterEventType(
            typeof(ManuallyRegisteredStructSample),
            "MANUAL_STRUCT_SAMPLE",
            ManualStructSampleJsonContext.Default.ManuallyRegisteredStructSample);
        var json = AgentStructEventSerializer.ToJson(new ManuallyRegisteredStructSample("manual"));
        Assert.IsType<ManuallyRegisteredStructSample>(AgentStructEventSerializer.FromJson(json));
    }
}
