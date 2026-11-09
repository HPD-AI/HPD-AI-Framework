using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;

namespace HPD.Agent.Tests.Serialization;

public sealed partial class AgentEventCodecTests
{
    private sealed record DurableCodecTestEvent(string Value) : AgentEvent;
    private sealed record LiveCodecTestEvent(string Value) : AgentEvent;
    private sealed record ConflictingCodecTestEvent(string Value) : AgentEvent;
    private sealed record ReservedCodecTestEvent([property: JsonPropertyName("type")] string TypeValue) : AgentEvent;
    private sealed record ShapeEvent(string First, string Second) : AgentEvent;
    private sealed record GoldenEventCatalog(
        string Version,
        string Digest,
        string BehaviorDigest,
        int EventCount,
        IReadOnlyList<GoldenEventContract> Events);
    private sealed record GoldenEventContract(
        string Discriminator,
        string ContractDigest,
        string Durability,
        bool RequiresDurable,
        string ModuleId);

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false)]
    [JsonSerializable(typeof(DurableCodecTestEvent))]
    [JsonSerializable(typeof(LiveCodecTestEvent))]
    [JsonSerializable(typeof(ConflictingCodecTestEvent))]
    [JsonSerializable(typeof(ReservedCodecTestEvent))]
    [JsonSerializable(typeof(ShapeEvent))]
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
    public void CoreEvent_RejectsMissingOrUnsupportedEnvelopeVersion()
    {
        var json = CoreCodec.Serialize(new ToolCallStartEvent("call-1", "Search", "message-1"));
        var invalidEnvelopes = new[]
        {
            json.Replace("\"version\":\"1.0\",", string.Empty, StringComparison.Ordinal),
            json.Replace("\"version\":\"1.0\"", "\"version\":\"2.0\"", StringComparison.Ordinal),
            json.Replace("\"version\":\"1.0\"", "\"version\":null", StringComparison.Ordinal)
        };

        foreach (var invalid in invalidEnvelopes)
        {
            var action = () => CoreCodec.DeserializeEvent(invalid);
            action.Should().Throw<JsonException>().WithMessage("*version '1.0'*");
        }
    }

    [Fact]
    public void ComputedErrorConveniences_AreNotSerialized()
    {
        var value = new MessageTurnErrorEvent(
            "turn-1",
            "rate limit exceeded",
            MessageTurnUsageSummary.Empty,
            new HttpRequestException("rate limit exceeded", null, System.Net.HttpStatusCode.TooManyRequests));

        var json = CoreCodec.Serialize(value);

        json.Should().NotContain("\"errorCode\"");
        json.Should().NotContain("\"isModelNotFound\"");
        json.Should().NotContain("\"isRetryable\"");
        json.Should().Contain("\"errorMessage\":\"rate limit exceeded\"");
    }

    [Fact]
    public void MiddlewareStateSnapshotEvent_RoundTripsCanonicalAttribution()
    {
        var timestamp = DateTimeOffset.Parse("2026-09-03T09:17:55Z");
        using var state = JsonDocument.Parse("{\"expandedContainers\":[]}");
        var value = new MiddlewareStateSnapshotEvent(
            "agent", 1, "before_model_call", null, null, null, 1,
            [new MiddlewareStateEntrySnapshot(
                "state", "State", "State", StateScope.Thread, false, 1,
                state.RootElement.Clone(), null, false)])
        {
            SessionId = "session-1",
            ThreadId = "main",
            ThreadSequenceNumber = 11,
            Timestamp = timestamp
        };

        var roundTrip = CoreCodec.DeserializeEvent(CoreCodec.Serialize(value));

        roundTrip.SessionId.Should().Be("session-1");
        roundTrip.ThreadId.Should().Be("main");
        roundTrip.ThreadSequenceNumber.Should().Be(11);
        roundTrip.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void PositionalTimestampEvent_PreservesSuppliedTimestamp()
    {
        var timestamp = DateTimeOffset.Parse("2026-10-31T12:34:56Z");
        var value = new CollapsedToolsVisibleEvent(
            "agent",
            3,
            ["search"],
            [],
            [],
            1,
            timestamp);

        value.Timestamp.Should().Be(timestamp);
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
    public void DurabilityPolicy_ChangesBehaviorWithoutChangingStructuralCatalog()
    {
        var original = CoreAgentEventComposition.Instance;
        var effective = original.WithDurabilityPolicy(
            AgentEventDurabilityPolicy.Empty.Set<StateSnapshotEvent>(AgentEventDurability.Durable));

        effective.Digest.Should().Be(original.Digest);
        effective.BehaviorDigest.Should().NotBe(original.BehaviorDigest);
        effective.Codec.TryGetByType(typeof(StateSnapshotEvent), out var descriptor).Should().BeTrue();
        descriptor.Durability.Should().Be(AgentEventDurability.Durable);
    }

    [Fact]
    public void DurabilityPolicy_CanDemoteOverridableDurableEvents()
    {
        var composition = AgentEventComposition.Create([CreateTestFragment()]);
        var effective = composition.WithDurabilityPolicy(
            AgentEventDurabilityPolicy.Empty.Set<DurableCodecTestEvent>(AgentEventDurability.LiveOnly));

        effective.Codec.TryGetByType(typeof(DurableCodecTestEvent), out var descriptor).Should().BeTrue();
        descriptor.Durability.Should().Be(AgentEventDurability.LiveOnly);
    }

    [Fact]
    public void DurabilityPolicy_CannotDemoteRequiredDurableEvents()
    {
        var action = () => CoreAgentEventComposition.Instance.WithDurabilityPolicy(
            AgentEventDurabilityPolicy.Empty.Set<PermissionRequestEvent>(AgentEventDurability.LiveOnly));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*PERMISSION_REQUEST*required-durable*");
    }

    [Fact]
    public void AuditAndSubAgentInvocationLifecycle_RemainOverridableDurable()
    {
        foreach (var type in new[]
                 {
                     typeof(FunctionInvocationAuditedEvent),
                     typeof(SubAgentInvocationStartedEvent),
                     typeof(SubAgentInvocationCompletedEvent),
                     typeof(SubAgentInvocationFailedEvent),
                     typeof(SubAgentInvocationCancelledEvent)
                 })
        {
            CoreAgentEventComposition.Instance.Codec.TryGetByType(type, out var descriptor).Should().BeTrue();
            descriptor.Durability.Should().Be(AgentEventDurability.Durable);
            descriptor.RequiresDurable.Should().BeFalse();
        }
    }

    [Fact]
    public void DurabilityPolicy_RejectsUnknownOverrides()
    {
        var byDiscriminator = () => CoreAgentEventComposition.Instance.WithDurabilityPolicy(
            AgentEventDurabilityPolicy.Empty.Set("UNKNOWN_EVENT", AgentEventDurability.Durable));
        var byType = () => CoreAgentEventComposition.Instance.WithDurabilityPolicy(
            AgentEventDurabilityPolicy.Empty.Set<DurableCodecTestEvent>(AgentEventDurability.Durable));

        byDiscriminator.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown discriminator 'UNKNOWN_EVENT'*");
        byType.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown event type*DurableCodecTestEvent*");
    }

    [Fact]
    public void CoreCatalog_ExposesJsonContractDigests()
    {
        var entries = CoreAgentEventComposition.Instance.Catalog.Events;

        entries.Should().OnlyContain(entry =>
            entry.ContractDigest.Length == 64 &&
            entry.ContractDigest.All(static value => char.IsAsciiHexDigitLower(value)));
    }

    [Fact]
    public void StructuralDigest_ChangesWhenSerializedPropertyShapeChanges()
    {
        var original = AgentEventComposition.Create([
            ShapeFragment(CodecTestJsonContext.Default.GetTypeInfo(typeof(ShapeEvent))!)
        ]);
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    static typeInfo =>
                    {
                        if (typeInfo.Type == typeof(ShapeEvent))
                            typeInfo.Properties.RemoveAt(0);
                    }
                }
            }
        };
        var changed = AgentEventComposition.Create([
            ShapeFragment(options.GetTypeInfo(typeof(ShapeEvent))!)
        ]);

        original.Digest.Should().NotBe(changed.Digest);

        static AgentEventModuleFragment ShapeFragment(JsonTypeInfo typeInfo) => new()
        {
            ModuleId = "test.shape",
            Events =
            [
                new AgentEventDescriptor
                {
                    EventType = typeof(ShapeEvent),
                    Discriminator = "SHAPE_EVENT",
                    JsonTypeInfo = typeInfo,
                    Durability = AgentEventDurability.LiveOnly,
                    ModuleId = "test.shape"
                }
            ]
        };
    }

    [Fact]
    public void CoreEventContracts_MatchGoldenManifest()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Serialization",
            "CoreEventContracts.golden.json");
        var expected = JsonSerializer.Deserialize<GoldenEventCatalog>(File.ReadAllText(path));
        var composition = CoreAgentEventComposition.Instance;
        var actual = new GoldenEventCatalog(
            "1.0",
            composition.Digest,
            composition.BehaviorDigest,
            composition.Catalog.Events.Count,
            composition.Catalog.Events.Select(entry =>
            {
                composition.Codec.TryGetByDiscriminator(entry.Discriminator, out var descriptor)
                    .Should().BeTrue();
                return new GoldenEventContract(
                    entry.Discriminator,
                    entry.ContractDigest,
                    entry.Durability.ToString(),
                    descriptor.RequiresDurable,
                    entry.ModuleId);
            }).ToArray());
        expected.Should().BeEquivalentTo(actual, options => options.WithStrictOrdering());
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
    public void CoreConcreteOutputEvents_HaveCheckedInDurabilityClassifications()
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

        var expectedLiveOnly = new HashSet<Type>
        {
            typeof(AgentCapabilityRefreshStartedEvent),
            typeof(AgentCompletionEvent),
            typeof(AgentDecisionEvent),
            typeof(AgentOperationNotificationSuppressedEvent),
            typeof(AgentOperationTombstoneEvictedEvent),
            typeof(AgentTurnStartedEvent),
            typeof(CircuitBreakerTriggeredEvent),
            typeof(CollapsedToolsVisibleEvent),
            typeof(CollapsingStateEvent),
            typeof(ContainerExpandedEvent),
            typeof(ContentCaptureFinalizedEvent),
            typeof(ContentReadEvent),
            typeof(ContentReferenceResolutionFailedEvent),
            typeof(ContentReferenceResolvedEvent),
            typeof(ContentScopeCleanedEvent),
            typeof(ContentUploadedEvent),
            typeof(ContentUploadFailedEvent),
            typeof(DeltaSendingActivatedEvent),
            typeof(DocumentProcessedEvent),
            typeof(EventDroppedEvent),
            typeof(FunctionRetryEvent),
            typeof(HostedFileUploadedEvent),
            typeof(HostedFileUploadFailedEvent),
            typeof(InternalMessagePreparedEvent),
            typeof(InternalParallelToolExecutionEvent),
            typeof(IterationStartEvent),
            typeof(MiddlewareStateChangedEvent),
            typeof(MiddlewareStateSnapshotEvent),
            typeof(ModelCallRetryEvent),
            typeof(NestedAgentInvokedEvent),
            typeof(OperationExecutionOwnerCleanupFailedEvent),
            typeof(ProviderValuationObservationEvent),
            typeof(RequestEventProcessedEvent),
            typeof(RuntimeAgentLiveEvent),
            typeof(SkillActivatedEvent),
            typeof(SkillActivationFailedEvent),
            typeof(SkillActivationStartedEvent),
            typeof(SkillResourceReadCompletedEvent),
            typeof(SkillResourceReadFailedEvent),
            typeof(SkillResourceReadStartedEvent),
            typeof(SkillScriptCompletedEvent),
            typeof(SkillScriptFailedEvent),
            typeof(SkillScriptStartedEvent),
            typeof(SkillScriptTimedOutEvent),
            typeof(StateSnapshotEvent),
            typeof(StructuredOutputCompleteEvent),
            typeof(StructuredOutputPartialEvent),
            typeof(StructuredOutputStartEvent),
            typeof(ToolOutputProjectedEvent),
            typeof(ToolOutputProjectionFailedEvent)
        };

        CoreAgentEventModule.Fragment.Events.Should().OnlyContain(descriptor =>
            descriptor.Durability == (expectedLiveOnly.Contains(descriptor.EventType)
                ? AgentEventDurability.LiveOnly
                : AgentEventDurability.Durable),
            "the checked-in event catalog distinguishes canonical recovery facts from live diagnostics");
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
