using System.Text.Json.Serialization;
using HPD.Agent;

namespace HPD.Agent.Middleware;

/// <summary>
/// Emitted when DataContent is successfully uploaded to session-owned content storage.
/// </summary>
/// <remarks>
/// This event indicates framework-managed local or ephemeral storage was used.
/// The provider boundary materializes the stored reference before model delivery.
/// </remarks>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("CONTENT_UPLOADED")]
public record ContentUploadedEvent(
    /// <summary>The unique identifier assigned to the uploaded content.</summary>
    string ContentId,
    /// <summary>The MIME type of the uploaded content.</summary>
    string MediaType,
    /// <summary>The size of the uploaded content in bytes.</summary>
    int SizeBytes) : AgentEvent;

/// <summary>
/// Emitted when IContentStore upload fails.
/// </summary>
/// <remarks>
/// In Auto mode, the middleware will attempt to fall back to HostedFileClient.
/// In Local mode, original DataContent is kept.
/// </remarks>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("CONTENT_UPLOAD_FAILED")]
public record ContentUploadFailedEvent(
    /// <summary>Error message describing why the upload failed.</summary>
    string ErrorMessage) : AgentEvent, IErrorEvent
{
    [JsonIgnore]
    public Exception? Exception => null;
}

/// <summary>
/// Emitted when DataContent is successfully uploaded via provider's HostedFileClient
/// and transformed to HostedFileContent.
/// </summary>
/// <remarks>
/// This event indicates provider-native file storage was used (e.g., OpenAI Files API).
/// The HostedFileContent is directly compatible with LLM providers and requires no resolver.
/// </remarks>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("HOSTED_FILE_UPLOADED")]
public record HostedFileUploadedEvent(
    /// <summary>The provider-specific file identifier.</summary>
    string FileId,
    /// <summary>The MIME type of the uploaded file.</summary>
    string MediaType,
    /// <summary>The size of the uploaded file in bytes (may be null if provider doesn't report).</summary>
    int? SizeBytes) : AgentEvent;

/// <summary>
/// Emitted when HostedFileClient upload fails.
/// </summary>
/// <remarks>
/// In Auto mode, the middleware will fall back to IContentStore.
/// In Hosted mode, original DataContent is kept and an error is logged.
/// </remarks>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("HOSTED_FILE_UPLOAD_FAILED")]
public record HostedFileUploadFailedEvent(
    /// <summary>Error message describing why the hosted upload failed.</summary>
    string ErrorMessage) : AgentEvent, IErrorEvent
{
    [JsonIgnore]
    public Exception? Exception => null;
}

/// <summary>
/// Describes how an internal content reference was resolved.
/// </summary>
public enum ContentReferenceResolutionKind
{
    /// <summary>Resolved to provider-readable UriContent.</summary>
    DirectUri,

    /// <summary>Resolved by uploading the content stream to a hosted file client.</summary>
    HostedFile,

    /// <summary>Resolved by buffering the stream into DataContent.</summary>
    BufferedData
}

/// <summary>
/// Emitted when a stored content attachment is materialized at the provider boundary.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("CONTENT_REFERENCE_RESOLVED")]
public record ContentReferenceResolvedEvent(
    /// <summary>The synthetic stored-content event address; never sent to a provider.</summary>
    Uri ContentUri,
    /// <summary>The resolution shape selected for provider dispatch.</summary>
    ContentReferenceResolutionKind ResolutionKind,
    /// <summary>The MIME type of the resolved content.</summary>
    string MediaType,
    /// <summary>The size of the resolved content in bytes.</summary>
    long? SizeBytes) : AgentEvent;

/// <summary>
/// Emitted when a stored content attachment cannot be materialized.
/// </summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("CONTENT_REFERENCE_RESOLUTION_FAILED")]
public record ContentReferenceResolutionFailedEvent(
    /// <summary>The synthetic stored-content event address that failed to materialize.</summary>
    Uri ContentUri,
    /// <summary>Error message describing why resolution failed.</summary>
    string ErrorMessage) : AgentEvent, IErrorEvent
{
    [JsonIgnore]
    public Exception? Exception => null;
}
