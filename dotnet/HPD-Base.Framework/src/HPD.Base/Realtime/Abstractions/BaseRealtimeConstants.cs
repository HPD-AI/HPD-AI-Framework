namespace HPD.Base;

public static class BaseRealtimeModuleIds
{
    public const string Module = "hpd.base.realtime";
}

public static class BaseRealtimeFeatureIds
{
    public const string Channels = "base.realtime.channels";
    public const string RecordChanges = "base.realtime.recordChanges";
    public const string WebSocketTransport = "base.realtime.transport.websocket";
    public const string PrivateChannels = "base.realtime.privateChannels";
    public const string PolicyPerEvent = "base.realtime.policyPerEvent";
    public const string RedactedProjection = "base.realtime.redactedProjection";
    public const string DurableReplay = "base.realtime.durableReplay";
}

public static class BaseRealtimeDtoIds
{
    public const string Event = "base.realtime.event";
    public const string RecordResource = "base.realtime.recordResource";
    public const string RecordSnapshot = "base.realtime.recordSnapshot";
    public const string ChannelJoinRequest = "base.realtime.channelJoinRequest";
    public const string ChannelJoinResult = "base.realtime.channelJoinResult";
    public const string Error = "base.realtime.error";
    public const string ClientMessage = "base.realtime.protocol.clientMessage";
    public const string ServerMessage = "base.realtime.protocol.serverMessage";
}

public static class BaseRealtimeRouteIds
{
    public const string WebSocket = "base.realtime.websocket";
}

public static class BaseRealtimeRoutes
{
    public const string WebSocket = "/base/realtime/v1/socket";
}

public static class BaseRealtimeChannelKinds
{
    public const string RecordChanges = "base.record_changes";
}

public static class BaseRealtimeProtocolTypes
{
    public const string Join = "join";
    public const string Leave = "leave";
    public const string Heartbeat = "heartbeat";
    public const string Connected = "connected";
    public const string Joined = "joined";
    public const string Left = "left";
    public const string Event = "event";
    public const string Error = "error";
}

public static class BaseRealtimeErrorCodes
{
    public const string ProtocolInvalid = "base.realtime.protocol.invalid";
    public const string AuthRequired = "base.realtime.auth.required";
    public const string ChannelUnauthorized = "base.realtime.channel.unauthorized";
    public const string ChannelUnsupported = "base.realtime.channel.unsupported";
    /// <summary>Identifies an attempt to join a channel name already active on the connection.</summary>
    public const string ChannelAlreadyJoined = "base.realtime.channel.alreadyJoined";
    /// <summary>Identifies a channel join rejected by the per-connection fixed-window limit.</summary>
    public const string JoinRateLimited = "base.realtime.join.rateLimited";
    /// <summary>Identifies a channel terminated because its consumer could not keep pace.</summary>
    public const string ConsumerSlow = "base.realtime.consumer.slow";
    public const string TooManyChannels = "base.realtime.tooManyChannels";
    public const string TooManyConnections = "base.realtime.tooManyConnections";
    public const string PayloadTooLarge = "base.realtime.payloadTooLarge";
    /// <summary>Identifies a channel terminated because safe dependency invalidation could not be produced.</summary>
    public const string DependencyInvalidationFailed = "base.realtime.dependencyInvalidationFailed";
    /// <summary>Identifies a channel terminated because an event could not be projected safely.</summary>
    public const string ProjectionFailed = "base.realtime.projectionFailed";
    /// <summary>Identifies a connection closed after its receive-idle limit elapsed.</summary>
    public const string ConnectionIdleTimeout = "base.realtime.connection.idleTimeout";
    public const string CapabilityUnavailable = "base.realtime.capabilityUnavailable";
    public const string Disabled = "base.realtime.disabled";
    public const string CursorInvalid = "base.realtime.cursor.invalid";
    public const string CursorScopeMismatch = "base.realtime.cursor.scopeMismatch";
    public const string CursorExpired = "base.realtime.cursor.expired";
    public const string CursorVersionUnsupported = "base.realtime.cursor.versionUnsupported";
    public const string DurableCollectionRequired = "base.realtime.durable.collectionRequired";
}
