using System.Text.Json.Serialization;
using HPD.Base.Serialization;

namespace HPD.Base.Realtime.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters =
    [
        typeof(LowerCamelJsonStringEnumConverter<BaseOperationKind>),
        typeof(LowerCamelJsonStringEnumConverter<VisibilityLevel>),
        typeof(LowerCamelJsonStringEnumConverter<HPD.Base.Events.EventResourceKind>),
        typeof(LowerCamelJsonStringEnumConverter<HPD.Base.Runtime.PrincipalAuthenticationState>),
        typeof(LowerCamelJsonStringEnumConverter<HPD.Base.Policy.AccessSubjectKind>),
        typeof(LowerCamelJsonStringEnumConverter<HPD.Base.Records.RecordPayloadKind>)
    ])]
[JsonSerializable(typeof(BaseRealtimeEvent))]
[JsonSerializable(typeof(BaseRealtimeRecordResource))]
[JsonSerializable(typeof(BaseRealtimeRecordSnapshot))]
[JsonSerializable(typeof(BaseRealtimePrincipalSummary))]
[JsonSerializable(typeof(BaseRealtimeChannelJoinRequest))]
[JsonSerializable(typeof(BaseRealtimeSubscribeRequest))]
[JsonSerializable(typeof(BaseRealtimeChannelJoinResult))]
[JsonSerializable(typeof(BaseRealtimeConnectionDescriptor))]
[JsonSerializable(typeof(BaseRealtimeChannelDescriptor))]
[JsonSerializable(typeof(BaseRealtimeError))]
[JsonSerializable(typeof(BaseRealtimeSnapshotOptions))]
[JsonSerializable(typeof(BaseRealtimeLimits))]
[JsonSerializable(typeof(BaseRealtimeClientMessage))]
[JsonSerializable(typeof(BaseRealtimeServerMessage))]
[JsonSerializable(typeof(BaseRealtimeEvent[]))]
[JsonSerializable(typeof(BaseRealtimeServerMessage[]))]
public partial class HPDBaseRealtimeJsonSerializerContext : JsonSerializerContext;
