using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Represents a hpdbase realtime JSON serializer context.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    Converters =
    [
        typeof(LowerCamelJsonStringEnumConverter<BaseOperationKind>),
        typeof(LowerCamelJsonStringEnumConverter<RecordPayloadKind>)
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
[JsonSerializable(typeof(BaseRealtimeChannelRequest))]
[JsonSerializable(typeof(BaseRealtimeLiveQueryOperation))]
[JsonSerializable(typeof(BaseRealtimeEvent[]))]
[JsonSerializable(typeof(BaseRealtimeServerMessage[]))]
[JsonSerializable(typeof(BaseRealtimeDurableSubjectAuthorityChanged))]
[JsonSerializable(typeof(BaseRealtimeLiveSubjectAuthorityChanged))]
[JsonSerializable(typeof(BaseRealtimeLiveQuerySubjectAuthorityChanged))]
[JsonSerializable(typeof(BaseDependencyInvalidation))]
[JsonSerializable(typeof(BaseDependencyReference))]
[JsonSerializable(typeof(BaseDependencyReference[]))]
public partial class HPDBaseRealtimeJsonSerializerContext : JsonSerializerContext;
