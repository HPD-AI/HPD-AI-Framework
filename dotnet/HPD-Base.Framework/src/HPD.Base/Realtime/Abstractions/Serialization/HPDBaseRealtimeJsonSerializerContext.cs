using System.Text.Json.Serialization;
using HPD.Base.Serialization;
using HPD.Base.Dependencies;

namespace HPD.Base.Realtime.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    Converters =
    [
        typeof(LowerCamelJsonStringEnumConverter<BaseOperationKind>),
        typeof(LowerCamelJsonStringEnumConverter<HPD.Base.Records.RecordPayloadKind>)
    ])]
[JsonSerializable(typeof(BaseRealtimeEvent))]
[JsonSerializable(typeof(BaseRealtimeRecordResource))]
[JsonSerializable(typeof(BaseRealtimeRecordSnapshot))]
[JsonSerializable(typeof(BaseRealtimeChannelJoinRequest))]
[JsonSerializable(typeof(BaseRealtimeChannelJoinResult))]
[JsonSerializable(typeof(BaseRealtimeError))]
[JsonSerializable(typeof(BaseRealtimeLimits))]
[JsonSerializable(typeof(BaseRealtimeClientMessage))]
[JsonSerializable(typeof(BaseRealtimeServerMessage))]
[JsonSerializable(typeof(BaseRealtimeEvent[]))]
[JsonSerializable(typeof(BaseRealtimeServerMessage[]))]
[JsonSerializable(typeof(BaseDependencyInvalidation))]
[JsonSerializable(typeof(BaseDependencyReference))]
[JsonSerializable(typeof(BaseDependencyReference[]))]
public partial class HPDBaseRealtimeJsonSerializerContext : JsonSerializerContext;
