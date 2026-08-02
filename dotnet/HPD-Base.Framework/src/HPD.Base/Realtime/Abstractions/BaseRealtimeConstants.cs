namespace HPD.Base;

/// <summary>Represents a base realtime module IDs.</summary>
public static class BaseRealtimeModuleIds
{
    /// <summary>Provides the module value.</summary>
    public const string Module = "hpd.base.realtime";
}

/// <summary>Represents a base realtime feature IDs.</summary>
public static class BaseRealtimeFeatureIds
{
    /// <summary>Provides the channels value.</summary>
    public const string Channels = "base.realtime.channels";
    /// <summary>Provides the record changes value.</summary>
    public const string RecordChanges = "base.realtime.recordChanges";
    /// <summary>Provides the web socket transport value.</summary>
    public const string WebSocketTransport = "base.realtime.transport.websocket";
    /// <summary>Provides the private channels value.</summary>
    public const string PrivateChannels = "base.realtime.privateChannels";
    /// <summary>Provides the policy per event value.</summary>
    public const string PolicyPerEvent = "base.realtime.policyPerEvent";
    /// <summary>Provides the redacted projection value.</summary>
    public const string RedactedProjection = "base.realtime.redactedProjection";
    /// <summary>Provides the durable replay value.</summary>
    public const string DurableReplay = "base.realtime.durableReplay";
}

/// <summary>Represents a base realtime DTO IDs.</summary>
public static class BaseRealtimeDtoIds
{
    /// <summary>Provides the event value.</summary>
    public const string Event = "base.realtime.event";
    /// <summary>Provides the record resource value.</summary>
    public const string RecordResource = "base.realtime.recordResource";
    /// <summary>Provides the record snapshot value.</summary>
    public const string RecordSnapshot = "base.realtime.recordSnapshot";
    /// <summary>Provides the channel join request value.</summary>
    public const string ChannelJoinRequest = "base.realtime.channelJoinRequest";
    /// <summary>Provides the channel join result value.</summary>
    public const string ChannelJoinResult = "base.realtime.channelJoinResult";
    /// <summary>Provides the error value.</summary>
    public const string Error = "base.realtime.error";
    /// <summary>Provides the client message value.</summary>
    public const string ClientMessage = "base.realtime.protocol.clientMessage";
    /// <summary>Provides the server message value.</summary>
    public const string ServerMessage = "base.realtime.protocol.serverMessage";
}

/// <summary>Represents a base realtime route IDs.</summary>
public static class BaseRealtimeRouteIds
{
    /// <summary>Provides the web socket value.</summary>
    public const string WebSocket = "base.realtime.websocket";
}

/// <summary>Represents a base realtime routes.</summary>
public static class BaseRealtimeRoutes
{
    /// <summary>Provides the web socket value.</summary>
    public const string WebSocket = "/base/realtime/v1/socket";
}

/// <summary>Represents a base realtime channel kinds.</summary>
public static class BaseRealtimeChannelKinds
{
    /// <summary>Provides the record changes value.</summary>
    public const string RecordChanges = "base.record_changes";
}

/// <summary>Represents a base realtime protocol types.</summary>
public static class BaseRealtimeProtocolTypes
{
    /// <summary>Provides the join value.</summary>
    public const string Join = "join";
    /// <summary>Provides the leave value.</summary>
    public const string Leave = "leave";
    /// <summary>Provides the heartbeat value.</summary>
    public const string Heartbeat = "heartbeat";
    /// <summary>Provides the connected value.</summary>
    public const string Connected = "connected";
    /// <summary>Provides the joined value.</summary>
    public const string Joined = "joined";
    /// <summary>Provides the left value.</summary>
    public const string Left = "left";
    /// <summary>Provides the event value.</summary>
    public const string Event = "event";
    /// <summary>Provides the error value.</summary>
    public const string Error = "error";
}

/// <summary>Represents a base realtime error codes.</summary>
public static class BaseRealtimeErrorCodes
{
    /// <summary>Provides the protocol invalid value.</summary>
    public const string ProtocolInvalid = "base.realtime.protocol.invalid";
    /// <summary>Provides the auth required value.</summary>
    public const string AuthRequired = "base.realtime.auth.required";
    /// <summary>Provides the channel unauthorized value.</summary>
    public const string ChannelUnauthorized = "base.realtime.channel.unauthorized";
    /// <summary>Provides the channel unsupported value.</summary>
    public const string ChannelUnsupported = "base.realtime.channel.unsupported";
    /// <summary>Identifies an attempt to join a channel name already active on the connection.</summary>
    public const string ChannelAlreadyJoined = "base.realtime.channel.alreadyJoined";
    /// <summary>Identifies a channel join rejected by the per-connection fixed-window limit.</summary>
    public const string JoinRateLimited = "base.realtime.join.rateLimited";
    /// <summary>Identifies a channel terminated because its consumer could not keep pace.</summary>
    public const string ConsumerSlow = "base.realtime.consumer.slow";
    /// <summary>Provides the too many channels value.</summary>
    public const string TooManyChannels = "base.realtime.tooManyChannels";
    /// <summary>Provides the too many connections value.</summary>
    public const string TooManyConnections = "base.realtime.tooManyConnections";
    /// <summary>Provides the payload too large value.</summary>
    public const string PayloadTooLarge = "base.realtime.payloadTooLarge";
    /// <summary>Identifies a channel terminated because safe dependency invalidation could not be produced.</summary>
    public const string DependencyInvalidationFailed = "base.realtime.dependencyInvalidationFailed";
    /// <summary>Identifies a channel terminated because an event could not be projected safely.</summary>
    public const string ProjectionFailed = "base.realtime.projectionFailed";
    /// <summary>Identifies a connection closed after its receive-idle limit elapsed.</summary>
    public const string ConnectionIdleTimeout = "base.realtime.connection.idleTimeout";
    /// <summary>Provides the capability unavailable value.</summary>
    public const string CapabilityUnavailable = "base.realtime.capabilityUnavailable";
    /// <summary>Provides the disabled value.</summary>
    public const string Disabled = "base.realtime.disabled";
    /// <summary>Provides the cursor invalid value.</summary>
    public const string CursorInvalid = "base.realtime.cursor.invalid";
    /// <summary>Provides the cursor scope mismatch value.</summary>
    public const string CursorScopeMismatch = "base.realtime.cursor.scopeMismatch";
    /// <summary>Provides the cursor expired value.</summary>
    public const string CursorExpired = "base.realtime.cursor.expired";
    /// <summary>Provides the cursor version unsupported value.</summary>
    public const string CursorVersionUnsupported = "base.realtime.cursor.versionUnsupported";
    /// <summary>Provides the durable collection required value.</summary>
    public const string DurableCollectionRequired = "base.realtime.durable.collectionRequired";
}
