using System.Text.Json.Serialization;

namespace HPD.Agent;

/// <summary>Reports successful native tool-output projection and storage.</summary>
[Serialization.DurableEvent]
[Serialization.EventType("TOOL_OUTPUT_PROJECTED")]
public sealed record ToolOutputProjectedEvent(
    string? FunctionName,
    string CallId,
    string ContentId,
    string ContentKind,
    int PartCount,
    long BytesRetained,
    ContentStorePersistenceCapability StorageCapability) : AgentEvent;

/// <summary>Reports a native tool-output projection or storage failure.</summary>
[Serialization.DurableEvent]
[Serialization.EventType("TOOL_OUTPUT_PROJECTION_FAILED")]
public sealed record ToolOutputProjectionFailedEvent(
    string? FunctionName,
    string CallId,
    string? PartName,
    string Code,
    string ErrorMessage) : AgentEvent, IErrorEvent
{
    /// <inheritdoc />
    [JsonIgnore]
    public Exception? Exception => null;
}

/// <summary>Reports finalization of one streaming tool-output capture.</summary>
[Serialization.DurableEvent]
[Serialization.EventType("CONTENT_CAPTURE_FINALIZED")]
public sealed record ContentCaptureFinalizedEvent(
    string? FunctionName,
    string CallId,
    string PartName,
    string MediaType,
    long SizeBytes) : AgentEvent;

/// <summary>Reports one model content read.</summary>
[Serialization.DurableEvent]
[Serialization.EventType("CONTENT_READ")]
public sealed record ContentReadEvent(
    string ContentId,
    string? ContentVersion,
    string PartName,
    string Encoding,
    int BytesReturned) : AgentEvent;

/// <summary>Reports successful deletion of one session-owned content scope.</summary>
[Serialization.DurableEvent]
[Serialization.EventType("CONTENT_SCOPE_CLEANED")]
public sealed record ContentScopeCleanedEvent : AgentEvent
{
    /// <summary>Gets the deleted content scope.</summary>
    public required string Scope { get; init; }
}
