using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Agent.Serialization;

namespace HPD.Agent.Tests.Serialization;

public sealed partial class AgentEventCodecTests
{
    private sealed record DurableCodecTestEvent(string Value) : AgentEvent;
    private sealed record LiveCodecTestEvent(string Value) : AgentEvent;
    private sealed record ConflictingCodecTestEvent(string Value) : AgentEvent;
    private sealed record ReservedCodecTestEvent([property: JsonPropertyName("type")] string TypeValue) : AgentEvent;

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false)]
    [JsonSerializable(typeof(DurableCodecTestEvent))]
    [JsonSerializable(typeof(LiveCodecTestEvent))]
    [JsonSerializable(typeof(ConflictingCodecTestEvent))]
    [JsonSerializable(typeof(ReservedCodecTestEvent))]
    private partial class CodecTestJsonContext : JsonSerializerContext;

    private static readonly AgentEventCodec CoreCodec = CoreAgentEventComposition.Instance.Codec;

    [Fact]
    public void CoreEvent_RoundTripsWithStableEnvelope()
    {
        var value = new ToolCallStartEvent("call-1", "Search", "message-1");
        var json = CoreCodec.Serialize(value);

        json.Should().Contain("\"version\":\"1.0\"");
        json.Should().Contain("\"type\":\"TOOL_CALL_START\"");
        CoreCodec.DeserializeEvent(json).Should().BeEquivalentTo(value);
    }

    [Fact]
    public void ThreadExecutionFinishedEvent_IsAScalarDurableFact()
    {
        var value = new ThreadExecutionFinishedEvent(
            "execution-1",
            "agent-1",
            ThreadExecutionOutcome.Succeeded,
            DateTimeOffset.Parse("2026-08-31T12:00:00Z"));

        var json = CoreCodec.Serialize(value);
        var roundTrip = CoreCodec.DeserializeEvent(json)
            .Should().BeOfType<ThreadExecutionFinishedEvent>().Subject;

        json.Should().NotContain("inputResult");
        json.Should().NotContain("turnResult");
        roundTrip.Should().BeEquivalentTo(value);
    }

    [Fact]
    public void ThreadExecutionFinishedEvent_SkipsRemovedLegacyInputResultWithoutHydratingNestedEvents()
    {
        var value = new ThreadExecutionFinishedEvent(
            "execution-1",
            "agent-1",
            ThreadExecutionOutcome.Succeeded,
            DateTimeOffset.Parse("2026-08-31T12:00:00Z"));
        var current = CoreCodec.Serialize(value);
        var legacy = current[..^1] +
            ",\"inputResult\":{\"type\":\"completed\",\"turnResult\":{\"events\":[{}]},\"threadExecutionId\":\"execution-1\"}}";

        CoreCodec.DeserializeEvent(legacy)
            .Should().BeEquivalentTo(value);
    }

    [Fact]
    public void Composition_IsDeterministicAcrossFragmentOrder()
    {
        var first = AgentEventComposition.Create([CoreAgentEventModule.Fragment, CreateTestFragment()]);
        var second = AgentEventComposition.Create([CreateTestFragment(), CoreAgentEventModule.Fragment]);

        first.Digest.Should().Be(second.Digest);
        first.Catalog.Events.Should().Equal(second.Catalog.Events);
    }

    [Fact]
    public void ExplicitCompositions_AreIsolated()
    {
        var core = CoreAgentEventComposition.Instance;
        var extended = AgentEventComposition.Create([CoreAgentEventModule.Fragment, CreateTestFragment()]);

        core.Codec.TryGetByType(typeof(DurableCodecTestEvent), out _).Should().BeFalse();
        extended.Codec.TryGetByType(typeof(DurableCodecTestEvent), out _).Should().BeTrue();
    }

    [Fact]
    public void IdenticalModuleRegistration_IsIdempotent()
    {
        var fragment = CreateTestFragment();
        var composition = AgentEventComposition.Create([fragment, fragment]);

        composition.Fragments.Should().ContainSingle();
        composition.Digest.Should().Be(AgentEventComposition.Create([fragment]).Digest);
    }

    [Fact]
    public void Composition_SnapshotsCallerOwnedEventLists()
    {
        var events = new List<AgentEventDescriptor>
        {
            Descriptor<DurableCodecTestEvent>("DURABLE_CODEC_TEST", AgentEventDurability.Durable, "test.mutable")
        };
        var composition = AgentEventComposition.Create([
            new AgentEventModuleFragment { ModuleId = "test.mutable", Events = events }
        ]);

        events.Clear();

        composition.Fragments.Single().Events.Should().ContainSingle();
        composition.Codec.TryGetByType(typeof(DurableCodecTestEvent), out _).Should().BeTrue();
    }

    [Fact]
    public void ConflictingModuleIdentity_FailsComposition()
    {
        var conflict = new AgentEventModuleFragment
        {
            ModuleId = "test.codec",
            Events = [Descriptor<ConflictingCodecTestEvent>("CONFLICT", AgentEventDurability.Durable, "test.codec")]
        };

        var action = () => AgentEventComposition.Create([CreateTestFragment(), conflict]);

        action.Should().Throw<InvalidOperationException>().WithMessage("*non-identical*");
    }

    [Fact]
    public void LiveOnlyEvent_CannotEnterDurableJournal()
    {
        var composition = AgentEventComposition.Create([CreateTestFragment()]);
        var action = () => composition.Codec.RequireDurable(new LiveCodecTestEvent("progress"));
        action.Should().Throw<LiveOnlyAgentEventAppendException>();
    }

    [Fact]
    public void DurableEvent_RoundTripsThroughExactGeneratedMetadata()
    {
        var composition = AgentEventComposition.Create([CreateTestFragment()]);
        var value = new DurableCodecTestEvent("done");
        var json = composition.Codec.Serialize(value);
        composition.Codec.DeserializeEvent(json).Should().BeEquivalentTo(value);
    }

    [Fact]
    public void UnknownDiscriminator_FailsClosedWithCodecDigest()
    {
        var action = () => CoreCodec.DeserializeEvent("{\"version\":\"1.0\",\"type\":\"MISSING_EVENT\"}");
        var exception = action.Should().Throw<UnknownAgentEventDiscriminatorException>().Which;
        exception.Discriminator.Should().Be("MISSING_EVENT");
        exception.CodecDigest.Should().Be(CoreCodec.Digest);
    }

    [Fact]
    public void DuplicateDiscriminator_FailsComposition()
    {
        var duplicate = new AgentEventModuleFragment
        {
            ModuleId = "test.conflict",
            Events =
            [
                Descriptor<DurableCodecTestEvent>("SAME", AgentEventDurability.Durable, "test.conflict"),
                Descriptor<ConflictingCodecTestEvent>("SAME", AgentEventDurability.Durable, "test.conflict")
            ]
        };

        var action = () => AgentEventComposition.Create([duplicate]);
        action.Should().Throw<InvalidOperationException>().WithMessage("*SAME*");
    }

    [Fact]
    public void DuplicateClrType_FailsComposition()
    {
        var duplicateType = new AgentEventModuleFragment
        {
            ModuleId = "test.other",
            Events = [Descriptor<DurableCodecTestEvent>("OTHER_DURABLE_TEST", AgentEventDurability.Durable, "test.other")]
        };

        var action = () => AgentEventComposition.Create([CreateTestFragment(), duplicateType]);

        action.Should().Throw<InvalidOperationException>().WithMessage("*claims both*");
    }

    [Fact]
    public void IncorrectJsonMetadata_FailsComposition()
    {
        var fragment = new AgentEventModuleFragment
        {
            ModuleId = "test.metadata",
            Events =
            [
                new AgentEventDescriptor
                {
                    EventType = typeof(DurableCodecTestEvent),
                    Discriminator = "BAD_METADATA",
                    JsonTypeInfo = CodecTestJsonContext.Default.LiveCodecTestEvent,
                    Durability = AgentEventDurability.Durable,
                    ModuleId = "test.metadata"
                }
            ]
        };

        var action = () => AgentEventComposition.Create([fragment]);

        action.Should().Throw<InvalidOperationException>().WithMessage("*cannot describe*");
    }

    [Fact]
    public void ReservedEnvelopeProperty_FailsComposition()
    {
        var fragment = new AgentEventModuleFragment
        {
            ModuleId = "test.reserved",
            Events = [Descriptor<ReservedCodecTestEvent>("RESERVED_TEST", AgentEventDurability.Durable, "test.reserved")]
        };
        var action = () => AgentEventComposition.Create([fragment]);
        action.Should().Throw<InvalidOperationException>().WithMessage("*reserved*");
    }

    [Fact]
    public void RemovedStaticSerializerAndConverter_AreAbsent()
    {
        typeof(AgentEvent).Assembly.GetType("HPD.Agent.Serialization.AgentEventSerializer").Should().BeNull();
        typeof(AgentEvent).Assembly.GetType("HPD.Agent.Serialization.AgentEventJsonConverter").Should().BeNull();
        typeof(AgentEvent).GetCustomAttributes(typeof(JsonConverterAttribute), false).Should().BeEmpty();
    }

    [Fact]
    public void CoreConcreteOutputEvents_HaveCheckedInDurableClassifications()
    {
        var classified = CoreAgentEventModule.Fragment.Events
            .Select(descriptor => descriptor.EventType)
            .ToHashSet();
        var concreteOutputs = typeof(AgentEvent).Assembly.GetTypes()
            .Where(type => typeof(AgentEvent).IsAssignableFrom(type))
            .Where(type => !typeof(AgentInputEvent).IsAssignableFrom(type))
            .Where(type => !type.IsAbstract && !type.ContainsGenericParameters)
            .ToArray();

        string.Join(", ", concreteOutputs.Except(classified).Select(type => type.FullName)).Should().BeEmpty();
        string.Join(", ", classified.Except(concreteOutputs).Select(type => type.FullName)).Should().BeEmpty();
        CoreAgentEventModule.Fragment.Events.Should().OnlyContain(descriptor =>
            descriptor.Durability == AgentEventDurability.Durable);
    }

    private static AgentEventModuleFragment CreateTestFragment() => new()
    {
        ModuleId = "test.codec",
        Events =
        [
            Descriptor<DurableCodecTestEvent>("DURABLE_CODEC_TEST", AgentEventDurability.Durable, "test.codec"),
            Descriptor<LiveCodecTestEvent>("LIVE_CODEC_TEST", AgentEventDurability.LiveOnly, "test.codec")
        ]
    };

    private static AgentEventDescriptor Descriptor<T>(
        string discriminator,
        AgentEventDurability durability,
        string moduleId)
        where T : AgentEvent => new()
    {
        EventType = typeof(T),
        Discriminator = discriminator,
        JsonTypeInfo = CodecTestJsonContext.Default.GetTypeInfo(typeof(T))!,
        Durability = durability,
        ModuleId = moduleId
    };
}
