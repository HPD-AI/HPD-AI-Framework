using System.Text.Json;
using HPD.Base.Descriptors;
using HPD.Base.Events;
using HPD.Base.Health;
using HPD.Base.Realtime.Configuration;
using HPD.Base.Runtime.Descriptors;
using Microsoft.Extensions.Options;

namespace HPD.Base.Realtime.Descriptors;

internal sealed class BaseRealtimeDescriptorContributor : IBaseDescriptorContributor
{
    private readonly BaseRealtimeOptions _options;

    public BaseRealtimeDescriptorContributor(IOptions<BaseRealtimeOptions> options)
    {
        _options = options.Value;
    }

    public string Id => BaseRealtimeModuleIds.Module;

    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        foreach (var dto in DtoIds)
        {
            builder.AddDtoContract(new DtoContractDescriptor
            {
                Id = dto,
                ContractVersion = "1.0",
                JsonContextOwner = "HPD.Base.Realtime.Abstractions",
                Visibility = VisibilityLevel.Public
            });
        }

        builder.AddModule(new BaseModuleDescriptor
        {
            Id = BaseRealtimeModuleIds.Module,
            Name = "HPD.Base.Realtime",
            Kind = BaseModuleKind.Realtime,
            Version = "1.0.0",
            Status = _options.Enabled ? ModuleStatus.Installed : ModuleStatus.Disabled,
            Compatibility = new ModuleCompatibility { RequiresBaseContract = "1.0" },
            ContributedCapabilities = FeatureIds,
            ContributedDtoIds = DtoIds,
            ContributedRouteIds = [BaseRealtimeRouteIds.WebSocket],
            ContributedEventTypes = ["record.created", "record.patched", "record.updated", "record.deleted"],
            ContributedHealthRefIds = [HealthIds.Registration, HealthIds.EventStream],
            ContributedDiagnosticIds = [DiagnosticIds.Options, DiagnosticIds.StreamOpenFailures, DiagnosticIds.HPDEventsCoordinatorStats, DiagnosticIds.ConnectionStats],
            PublicConfig = PublicConfig(),
            Visibility = VisibilityLevel.Public
        });

        builder.AddHealthRef(new HealthRefDescriptor
        {
            Id = HealthIds.Registration,
            Scope = HealthScope.Module,
            TargetRef = BaseRealtimeModuleIds.Module,
            Visibility = VisibilityLevel.Public
        });
        builder.AddHealthRef(new HealthRefDescriptor
        {
            Id = HealthIds.EventStream,
            Scope = HealthScope.Dependency,
            TargetRef = "hpd.events",
            Visibility = VisibilityLevel.Admin
        });

        builder.AddDiagnosticRef(new DiagnosticRefDescriptor
        {
            Id = DiagnosticIds.Options,
            Visibility = VisibilityLevel.Admin
        });
        builder.AddDiagnosticRef(new DiagnosticRefDescriptor
        {
            Id = DiagnosticIds.StreamOpenFailures,
            Visibility = VisibilityLevel.Admin
        });
        builder.AddDiagnosticRef(new DiagnosticRefDescriptor
        {
            Id = DiagnosticIds.HPDEventsCoordinatorStats,
            Visibility = VisibilityLevel.Admin
        });
        builder.AddDiagnosticRef(new DiagnosticRefDescriptor
        {
            Id = DiagnosticIds.ConnectionStats,
            Visibility = VisibilityLevel.Admin
        });

        builder.AddEventType(new EventTypeDescriptor
        {
            Type = "base.recordMutation",
            EnvelopeVersion = BaseEventSchemaVersions.V1,
            SchemaId = BaseRealtimeDtoIds.Event,
            Visibility = VisibilityLevel.Public
        });

        builder.AddCapabilities(new CapabilityDescriptor
        {
            DescriptorVersion = "1.0",
            RuntimeId = BaseRealtimeModuleIds.Module,
            Families =
            [
                new CapabilityFamilyDescriptor
                {
                    FamilyId = "base.realtime",
                    FamilyVersion = "1.0",
                    Status = _options.Enabled ? CapabilityStatus.Available : CapabilityStatus.Disabled,
                    OwnerModuleId = BaseRealtimeModuleIds.Module,
                    Visibility = VisibilityLevel.Public,
                    Features =
                    [
                        Feature(BaseRealtimeFeatureIds.Channels),
                        Feature(BaseRealtimeFeatureIds.RecordChanges),
                        Feature(BaseRealtimeFeatureIds.WebSocketTransport),
                        Feature(BaseRealtimeFeatureIds.PrivateChannels),
                        Feature(BaseRealtimeFeatureIds.PolicyPerEvent),
                        Feature(BaseRealtimeFeatureIds.RedactedProjection),
                        Feature(BaseRealtimeFeatureIds.DurableReplay)
                    ],
                    Limits =
                    [
                        Limit("maxConnections", _options.Limits.MaxConnections),
                        Limit("maxChannelsPerConnection", _options.Limits.MaxChannelsPerConnection),
                        Limit("streamCapacity", _options.Limits.StreamCapacity),
                        Limit("outboundCapacity", _options.Limits.OutboundCapacity),
                        Limit("maxMessageBytes", _options.Limits.MaxMessageBytes),
                        Limit("maxPayloadBytes", _options.Limits.MaxPayloadBytes),
                        Limit("receiveIdleTimeoutSeconds", _options.Limits.ReceiveIdleTimeoutSeconds),
                        Limit("sendTimeoutSeconds", _options.Limits.SendTimeoutSeconds),
                        Limit("maxJoinsPerSecond", _options.Limits.MaxJoinsPerSecond),
                        Limit("replayBatchSize", _options.Limits.ReplayBatchSize),
                        Limit("cursorLifetimeSeconds", _options.Limits.CursorLifetimeSeconds),
                    ]
                }
            ]
        });
    }

    private CapabilityFeatureDescriptor Feature(string featureId) => new()
    {
        FeatureId = featureId,
        Version = "1.0",
        Status = FeatureAvailable(featureId) ? CapabilityStatus.Available : CapabilityStatus.Disabled,
        SupportLevel = SupportLevel.Optional,
        Scope = CapabilityScope.Runtime,
        Constraints = new CapabilityConstraintSet
        {
            Realtime = new RealtimeCapabilityConstraints
            {
                Subscribe = _options.Enabled,
                MaxSubscriptions = _options.Limits.MaxChannelsPerConnection,
                FeatureIds = FeatureIds,
                Extensions = Extensions()
            }
        },
        HealthRef = HealthIds.Registration,
        DiagnosticRefs = [DiagnosticIds.Options, DiagnosticIds.StreamOpenFailures],
        RouteRefs = [BaseRealtimeRouteIds.WebSocket],
        Visibility = VisibilityLevel.Public
    };

    private static CapabilityLimitDescriptor Limit(string id, int value) => new()
    {
        Name = id,
        Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Unit = "count"
    };

    private Dictionary<string, JsonElement> PublicConfig() => new()
    {
        ["transport"] = JsonString("websocket"),
        ["route"] = JsonString(BaseRealtimeRoutes.WebSocket),
        ["replayable"] = JsonBoolean(DurableConfigured),
        ["resumable"] = JsonBoolean(DurableConfigured),
        ["durable"] = JsonBoolean(DurableConfigured),
        ["durableRequiresTransactionalJournal"] = JsonTrue(),
        ["liveQuery"] = JsonFalse()
    };

    private Dictionary<string, JsonElement> Extensions() => new(PublicConfig())
    {
        ["eventTypes"] = JsonArray(["base.recordMutation"]),
        ["recordMutationEventsOnly"] = JsonTrue(),
        ["backpressure"] = JsonString(_options.Backpressure.ToString())
    };

    private static JsonElement JsonFalse() => JsonDocument.Parse("false").RootElement.Clone();
    private static JsonElement JsonTrue() => JsonDocument.Parse("true").RootElement.Clone();
    private static JsonElement JsonBoolean(bool value) => value ? JsonTrue() : JsonFalse();

    private bool DurableConfigured =>
        _options.Enabled && !string.IsNullOrWhiteSpace(_options.CursorSigningKey);

    private bool FeatureAvailable(string featureId) =>
        _options.Enabled
        && (featureId != BaseRealtimeFeatureIds.DurableReplay || DurableConfigured);

    private static JsonElement JsonString(string value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStringValue(value);
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private static JsonElement JsonArray(string[] values)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var value in values)
                writer.WriteStringValue(value);
            writer.WriteEndArray();
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    internal static class HealthIds
    {
        public const string Registration = "hpd.base.realtime.registration";
        public const string EventStream = "hpd.base.realtime.hpdEventsStream";
    }

    internal static class DiagnosticIds
    {
        public const string Options = "hpd.base.realtime.options";
        public const string StreamOpenFailures = "hpd.base.realtime.streamOpenFailures";
        public const string HPDEventsCoordinatorStats = "hpd.base.realtime.hpdEventsCoordinatorStats";
        public const string ConnectionStats = "hpd.base.realtime.connectionStats";
    }

    private static readonly string[] FeatureIds =
    [
        BaseRealtimeFeatureIds.Channels,
        BaseRealtimeFeatureIds.RecordChanges,
        BaseRealtimeFeatureIds.WebSocketTransport,
        BaseRealtimeFeatureIds.PrivateChannels,
        BaseRealtimeFeatureIds.PolicyPerEvent,
        BaseRealtimeFeatureIds.RedactedProjection,
        BaseRealtimeFeatureIds.DurableReplay
    ];

    private static readonly string[] DtoIds =
    [
        BaseRealtimeDtoIds.Event,
        BaseRealtimeDtoIds.RecordResource,
        BaseRealtimeDtoIds.RecordSnapshot,
        BaseRealtimeDtoIds.ChannelJoinRequest,
        BaseRealtimeDtoIds.ChannelJoinResult,
        BaseRealtimeDtoIds.Error,
        BaseRealtimeDtoIds.ClientMessage,
        BaseRealtimeDtoIds.ServerMessage
    ];
}
