namespace HPD.Agent;

/// <summary>
/// Unified content storage interface for the framework.
/// Provides stream-first versioned writes, OpenRead/Delete/Query operations with scope-based isolation.
/// </summary>
/// <remarks>
/// <para>
/// Scope is backend isolation, commonly a session id, an agent name, or null for global content.
/// Tags are generic metadata for filtering and policy. They do not define a public filesystem.
/// </para>
/// </remarks>
public interface IContentStore
{
    /// <summary>
    /// Write content in the given scope and return metadata for the stored item.
    /// </summary>
    /// <param name="scope">
    /// Scope identifier for isolation (e.g., agentName or sessionId).
    /// Pass null for global content visible to all agents.
    /// </param>
    /// <param name="data">Readable content stream.</param>
    /// <param name="metadata">Metadata describing the content, including content type.</param>
    /// <param name="options">Explicit write intent and conditional write options.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Metadata for the stored content</returns>
    Task<ContentInfo> WriteAsync(
        string? scope,
        Stream data,
        ContentMetadata metadata,
        ContentWriteOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Open content for reading by identifier within the given scope.
    /// Returns null if not found.
    /// </summary>
    /// <param name="scope">Scope identifier (e.g., agentName or sessionId). Pass null for global scope.</param>
    /// <param name="contentId">Content identifier returned by WriteAsync.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A readable stream, or null if not found. Caller owns disposal.</returns>
    Task<Stream?> OpenReadAsync(
        string? scope,
        string contentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a temporary provider-readable URI for content, when supported by the store.
    /// Returns null if this store cannot expose direct read URIs.
    /// </summary>
    Task<Uri?> CreateReadUriAsync(
        string? scope,
        string contentId,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve metadata by identifier within the given scope without opening the content.
    /// Returns null if not found.
    /// </summary>
    Task<ContentInfo?> StatAsync(
        string? scope,
        string contentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete content by identifier within the given scope.
    /// Idempotent - no-op if content doesn't exist.
    /// </summary>
    /// <param name="scope">Scope identifier (e.g., agentName or sessionId). Pass null for global scope.</param>
    /// <param name="contentId">Content identifier to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteAsync(
        string? scope,
        string contentId,
        ContentDeleteOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query content within the given scope with optional filters.
    /// Returns metadata only and never opens content streams.
    /// </summary>
    /// <param name="scope">Scope identifier (e.g., agentName or sessionId). Pass null to query across ALL scopes.</param>
    /// <param name="query">Optional query filters (null = return all content in scope)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of content metadata matching query within scope</returns>
    /// <remarks>
    /// <para><b>Performance Note:</b></para>
    /// <para>
    /// Query returns metadata only. Call OpenReadAsync to retrieve content.
    /// This enables efficient listing and filtering without loading all content into memory.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<ContentInfo>> QueryAsync(
        string? scope = null,
        ContentQuery? query = null,
        CancellationToken cancellationToken = default);
}

public sealed record ContentWriteOptions
{
    public ContentWriteMode Mode { get; init; } = ContentWriteMode.Create;

    public string? ContentId { get; init; }

    public string? IfMatchVersion { get; init; }

    public bool FailIfNameExists { get; init; }

    public IReadOnlyDictionary<string, string>? PolicyHints { get; init; }
}

public enum ContentWriteMode
{
    Create,
    ReplaceById,
    ReplaceByName,
    Append,
    Stage
}

public sealed record ContentDeleteOptions
{
    public string? IfMatchVersion { get; init; }
}

public sealed class ContentConflictException : Exception
{
    public ContentConflictException(
        string message,
        string? contentId = null,
        string? expectedVersion = null,
        string? actualVersion = null)
        : base(message)
    {
        ContentId = contentId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public string? ContentId { get; }

    public string? ExpectedVersion { get; }

    public string? ActualVersion { get; }
}

/// <summary>
/// Metadata provided when storing content.
/// </summary>
public record ContentMetadata
{
    /// <summary>
    /// MIME type (e.g., "image/jpeg", "text/plain").
    /// </summary>
    public string ContentType { get; init; } = "application/octet-stream";

    /// <summary>
    /// User-friendly name (e.g., "resume.pdf", "API Documentation").
    /// Defaults to contentId if not provided.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Description of content purpose/context.
    /// Helps humans and agents understand what this content is.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Who created this content (User, Agent, System).
    /// </summary>
    public ContentSource? Origin { get; init; }

    /// <summary>
    /// Arbitrary key-value tags for filtering/categorization.
    /// Examples: {"category": "knowledge"}, {"project": "alpha"}, {"priority": "high"}
    /// </summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }

    /// <summary>
    /// Original source path or URL (if content came from a file or web).
    /// </summary>
    public string? OriginalSource { get; init; }
}

/// <summary>
/// Metadata about stored content.
/// Returned by QueryAsync - does NOT include content bytes.
/// </summary>
public record ContentInfo
{
    /// <summary>Unique content identifier</summary>
    public required string Id { get; init; }

    /// <summary>Opaque store-specific version token used for conditional writes.</summary>
    public required string Version { get; init; }

    /// <summary>User-friendly name</summary>
    public required string Name { get; init; }

    /// <summary>MIME type (e.g., "image/jpeg", "text/plain")</summary>
    public required string ContentType { get; init; }

    /// <summary>Size in bytes</summary>
    public long SizeBytes { get; init; }

    /// <summary>When this content was created (UTC)</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>When this content was last modified (UTC)</summary>
    public DateTime? LastModified { get; init; }

    /// <summary>When this content was last accessed (UTC) - if tracked by store</summary>
    public DateTime? LastAccessed { get; init; }

    /// <summary>Description of content purpose</summary>
    public string? Description { get; init; }

    /// <summary>Who created this content</summary>
    public ContentSource Origin { get; init; }

    /// <summary>Arbitrary key-value tags</summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }

    /// <summary>Original source path or URL</summary>
    public string? OriginalSource { get; init; }

    /// <summary>
    /// Store-specific extended metadata.
    /// Examples:
    /// - Local content uploads: {"kind": "upload"} with a branch content scope
    /// - Runtime artifacts: {"kind": "artifact", "artifact-kind": "execute_command_output"}
    /// - StaticMemoryStore: {"extractedTextLength": "15234"}
    /// - DynamicMemoryStore: {"title": "Meeting Notes"}
    /// </summary>
    public IReadOnlyDictionary<string, object>? ExtendedMetadata { get; init; }

    public override string ToString() => Id;
}

/// <summary>
/// Indicates who created the content.
/// </summary>
public enum ContentSource
{
    /// <summary>Uploaded by the user</summary>
    User,

    /// <summary>Generated by the agent</summary>
    Agent,

    /// <summary>System-generated (transcriptions, extractions, etc.)</summary>
    System
}
