using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Defines the common immutable envelope for a version 2 realtime client message.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BaseRealtimeJoinMessage), "join")]
[JsonDerivedType(typeof(BaseRealtimeLeaveMessage), "leave")]
[JsonDerivedType(typeof(BaseRealtimeHeartbeatMessage), "heartbeat")]
public abstract record BaseRealtimeClientMessage
{
    /// <summary>Gets the realtime protocol version.</summary>
    public int Protocol { get; init; } = 2;
    /// <summary>Gets the server-issued connection identifier.</summary>
    public required string ConnectionId { get; init; }
    /// <summary>Gets the server-issued connection epoch.</summary>
    public required string ConnectionEpoch { get; init; }
}

/// <summary>Requests one closed realtime channel.</summary>
public sealed record BaseRealtimeJoinMessage : BaseRealtimeClientMessage
{
    /// <summary>Gets the caller correlation reference.</summary>
    public required string Ref { get; init; }
    /// <summary>Gets the closed channel request.</summary>
    public required BaseRealtimeChannelRequest Channel { get; init; }
}

/// <summary>Leaves one active realtime channel.</summary>
public sealed record BaseRealtimeLeaveMessage : BaseRealtimeClientMessage
{
    /// <summary>Gets the joined channel reference.</summary>
    public required string Ref { get; init; }
}

/// <summary>Requests a heartbeat acknowledgement.</summary>
public sealed record BaseRealtimeHeartbeatMessage : BaseRealtimeClientMessage
{
    /// <summary>Gets the heartbeat identifier.</summary>
    public required string HeartbeatId { get; init; }
}

/// <summary>Defines the common immutable envelope for a version 2 realtime server message.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BaseRealtimeWelcomeMessage), "welcome")]
[JsonDerivedType(typeof(BaseRealtimeJoinedMessage), "joined")]
[JsonDerivedType(typeof(BaseRealtimeLiveRecordEventMessage), "liveRecordEvent")]
[JsonDerivedType(typeof(BaseRealtimeDurableRecordEventMessage), "durableRecordEvent")]
[JsonDerivedType(typeof(BaseRealtimeLiveQuerySnapshotMessage), "liveQuerySnapshot")]
[JsonDerivedType(typeof(BaseRealtimeDurableSubjectAuthorityChanged), "durableSubjectAuthorityChanged")]
[JsonDerivedType(typeof(BaseRealtimeLiveSubjectAuthorityChanged), "liveSubjectAuthorityChanged")]
[JsonDerivedType(typeof(BaseRealtimeLiveQuerySubjectAuthorityChanged), "liveQuerySubjectAuthorityChanged")]
[JsonDerivedType(typeof(BaseRealtimeHeartbeatAckMessage), "heartbeatAck")]
[JsonDerivedType(typeof(BaseRealtimeErrorMessage), "error")]
[JsonDerivedType(typeof(BaseRealtimeClosedMessage), "closed")]
public abstract record BaseRealtimeServerMessage
{
    /// <summary>Gets the realtime protocol version.</summary>
    public int Protocol { get; init; } = 2;
    /// <summary>Gets the server-issued connection identifier.</summary>
    public required string ConnectionId { get; init; }
    /// <summary>Gets the server-issued connection epoch.</summary>
    public required string ConnectionEpoch { get; init; }
}

/// <summary>Advertises the negotiated connection limits.</summary>
public sealed record BaseRealtimeWelcomeMessage : BaseRealtimeServerMessage
{
    /// <summary>Gets the required heartbeat interval.</summary>
    public required int HeartbeatIntervalMs { get; init; }
    /// <summary>Gets the maximum inbound frame size.</summary>
    public required int MaxInboundBytes { get; init; }
    /// <summary>Gets the maximum joined channels.</summary>
    public required int MaxChannels { get; init; }
}

/// <summary>Confirms one joined channel.</summary>
public sealed record BaseRealtimeJoinedMessage : BaseRealtimeServerMessage
{
    /// <summary>Gets the join reference.</summary>
    public required string Ref { get; init; }
    /// <summary>Gets the server-issued channel epoch.</summary>
    public required string ChannelEpoch { get; init; }
    /// <summary>Gets the exact delivery contract.</summary>
    public required string Delivery { get; init; }
}

/// <summary>Delivers one live at-most-once record event.</summary>
public sealed record BaseRealtimeLiveRecordEventMessage : BaseRealtimeServerMessage
{
    /// <summary>Gets the join reference.</summary>
    public required string Ref { get; init; }
    /// <summary>Gets the active channel epoch.</summary>
    public required string ChannelEpoch { get; init; }
    /// <summary>Gets the authorized record event.</summary>
    public required BaseRealtimeEvent Event { get; init; }
}

/// <summary>Delivers one durable at-least-once record event.</summary>
public sealed record BaseRealtimeDurableRecordEventMessage : BaseRealtimeServerMessage
{
    /// <summary>Gets the join reference.</summary>
    public required string Ref { get; init; }
    /// <summary>Gets the active channel epoch.</summary>
    public required string ChannelEpoch { get; init; }
    /// <summary>Gets the authorized record event.</summary>
    public required BaseRealtimeEvent Event { get; init; }
    /// <summary>Gets the opaque durable cursor.</summary>
    public required string Cursor { get; init; }
}

/// <summary>Delivers one complete dependency-driven live-query replacement.</summary>
public sealed record BaseRealtimeLiveQuerySnapshotMessage : BaseRealtimeServerMessage
{
    /// <summary>Gets the join reference.</summary>
    public required string Ref { get; init; }
    /// <summary>Gets the active channel epoch.</summary>
    public required string ChannelEpoch { get; init; }
    /// <summary>Gets the epoch-local decimal version.</summary>
    public required string Version { get; init; }
    /// <summary>Gets whether the value is initial or rerun.</summary>
    public required string Source { get; init; }
    /// <summary>Gets the typed operation value in its declared JSON contract.</summary>
    public required JsonElement Value { get; init; }
}

/// <summary>Delivers one durable cursor-bound exported-subject authority invalidation.</summary>
public sealed record BaseRealtimeDurableSubjectAuthorityChanged : BaseRealtimeServerMessage
{
    /// <summary>Gets the join reference.</summary>
    public required string Ref { get; init; }
    /// <summary>Gets the active channel epoch.</summary>
    public required string ChannelEpoch { get; init; }
    /// <summary>Gets the exported contract ID.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the canonical positive state generation.</summary>
    public required string StateGeneration { get; init; }
    /// <summary>Gets the protected durable cursor.</summary>
    public required string Cursor { get; init; }
}

/// <summary>Delivers one live record-channel exported-subject authority invalidation.</summary>
public sealed record BaseRealtimeLiveSubjectAuthorityChanged : BaseRealtimeServerMessage
{
    /// <summary>Gets the join reference.</summary>
    public required string Ref { get; init; }
    /// <summary>Gets the active channel epoch.</summary>
    public required string ChannelEpoch { get; init; }
    /// <summary>Gets the exported contract ID.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the canonical positive state generation.</summary>
    public required string StateGeneration { get; init; }
}

/// <summary>Delivers one live-query exported-subject authority invalidation before replacement.</summary>
public sealed record BaseRealtimeLiveQuerySubjectAuthorityChanged : BaseRealtimeServerMessage
{
    /// <summary>Gets the join reference.</summary>
    public required string Ref { get; init; }
    /// <summary>Gets the active channel epoch.</summary>
    public required string ChannelEpoch { get; init; }
    /// <summary>Gets the exported contract ID.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the canonical positive state generation.</summary>
    public required string StateGeneration { get; init; }
}

/// <summary>Acknowledges one heartbeat.</summary>
public sealed record BaseRealtimeHeartbeatAckMessage : BaseRealtimeServerMessage
{
    /// <summary>Gets the acknowledged heartbeat identifier.</summary>
    public required string HeartbeatId { get; init; }
}

/// <summary>Reports one bounded protocol or channel failure.</summary>
public sealed record BaseRealtimeErrorMessage : BaseRealtimeServerMessage
{
    /// <summary>Gets the related reference, when one exists.</summary>
    public string? Ref { get; init; }
    /// <summary>Gets the related channel epoch, when one exists.</summary>
    public string? ChannelEpoch { get; init; }
    /// <summary>Gets whether the related scope is terminal.</summary>
    public required bool Terminal { get; init; }
    /// <summary>Gets the safe failure.</summary>
    public required BaseRealtimeError Error { get; init; }
}

/// <summary>Reports a terminal connection closure.</summary>
public sealed record BaseRealtimeClosedMessage : BaseRealtimeServerMessage
{
    /// <summary>Gets the stable close code.</summary>
    public required string Code { get; init; }
    /// <summary>Gets whether reconnecting may succeed.</summary>
    public required bool Retryable { get; init; }
}
