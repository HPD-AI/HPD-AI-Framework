using System.Text.Json;
using HPD.Base;
using HPD.Base.Events;
using HPD.Base.Records;
using HPD.Base.Realtime;
using HPD.Base.Realtime.DependencyInjection;
using HPD.Base.Realtime.Serialization;
using HPD.Base.Runtime;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLogging();
services.AddHPDBaseRealtime();
using var provider = services.BuildServiceProvider();
_ = provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();

RoundTrip(Event(), HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeEvent);
RoundTrip(ClientJoin(), HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeClientMessage);
RoundTrip(ServerEvent(), HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeServerMessage);

var serialized = JsonSerializer.Serialize(ClientJoin(), HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeClientMessage);
if (!serialized.Contains("\"type\":\"join\"", StringComparison.Ordinal) ||
    !serialized.Contains("\"operation", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Realtime protocol JSON did not use the expected source-generated shape.");
}

static void RoundTrip<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
{
    var json = JsonSerializer.Serialize(value, typeInfo);
    var roundTrip = JsonSerializer.Deserialize(json, typeInfo);
    if (roundTrip is null)
        throw new InvalidOperationException($"Round trip failed for {typeof(T).FullName}.");
}

static BaseRealtimeClientMessage ClientJoin() => new()
{
    Type = BaseRealtimeProtocolTypes.Join,
    Ref = "1",
    Channel = "base:records:items",
    Config = new BaseRealtimeChannelJoinRequest
    {
        Kind = BaseRealtimeChannelKinds.RecordChanges,
        CollectionId = "items",
        Operations = [BaseOperationKind.Create],
        IncludeSnapshots = true
    }
};

static BaseRealtimeServerMessage ServerEvent() => new()
{
    Type = BaseRealtimeProtocolTypes.Event,
    Channel = "base:records:items",
    Event = Event()
};

static BaseRealtimeEvent Event() => new()
{
    EventId = "evt_1",
    Type = "record.created",
    SchemaVersion = BaseEventSchemaVersions.V1,
    OccurredAt = DateTimeOffset.UnixEpoch,
    Resource = new BaseRealtimeRecordResource
    {
        CollectionId = "items",
        RecordId = new RecordId("rec_1")
    },
    Operation = BaseOperationKind.Create,
    After = new BaseRealtimeRecordSnapshot
    {
        Payload = new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = new Dictionary<string, JsonElement>
            {
                ["title"] = Json("hello")
            }
        }
    }
};

static JsonElement Json(string value)
{
    using var document = JsonDocument.Parse($"\"{value}\"");
    return document.RootElement.Clone();
}
