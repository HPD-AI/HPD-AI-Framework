namespace HPD.Base.Realtime;

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
}

public static class BaseRealtimeDtoIds
{
    public const string Event = "base.realtime.event";
    public const string RecordResource = "base.realtime.recordResource";
    public const string RecordSnapshot = "base.realtime.recordSnapshot";
    public const string PrincipalSummary = "base.realtime.principalSummary";
    public const string SubscribeRequest = "base.realtime.subscribeRequest";
    public const string ChannelJoinRequest = "base.realtime.channelJoinRequest";
    public const string ChannelJoinResult = "base.realtime.channelJoinResult";
    public const string ConnectionDescriptor = "base.realtime.connectionDescriptor";
    public const string ChannelDescriptor = "base.realtime.channelDescriptor";
    public const string SnapshotOptions = "base.realtime.snapshotOptions";
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
    public const string Connect = "connect";
    public const string Authenticate = "authenticate";
    public const string Join = "join";
    public const string Leave = "leave";
    public const string Heartbeat = "heartbeat";
    public const string Connected = "connected";
    public const string Joined = "joined";
    public const string Left = "left";
    public const string Event = "event";
    public const string System = "system";
    public const string Error = "error";
}

public static class BaseRealtimeErrorCodes
{
    public const string ProtocolInvalid = "base.realtime.protocol.invalid";
    public const string AuthRequired = "base.realtime.auth.required";
    public const string ChannelUnauthorized = "base.realtime.channel.unauthorized";
    public const string ChannelUnsupported = "base.realtime.channel.unsupported";
    public const string TooManyChannels = "base.realtime.tooManyChannels";
    public const string TooManyConnections = "base.realtime.tooManyConnections";
    public const string PayloadTooLarge = "base.realtime.payloadTooLarge";
    public const string HeartbeatTimeout = "base.realtime.heartbeatTimeout";
    public const string CapabilityUnavailable = "base.realtime.capabilityUnavailable";
    public const string Disabled = "base.realtime.disabled";
    public const string ResumeUnsupported = "base.realtime.resume.unsupported";
}
