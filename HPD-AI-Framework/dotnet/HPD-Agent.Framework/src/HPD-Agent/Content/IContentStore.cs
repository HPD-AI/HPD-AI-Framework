namespace HPD.Agent;

/// <summary>
/// Unified content storage interface for the framework.
/// Provides Put/Get/Delete/Query operations with scope-based isolation.
/// </summary>
/// <remarks>
/// <para><b>V3 Design:</b></para>
/// <para>
/// One store, one interface. Scope and folder tags determine isolation:
/// </para>
/// <list type="bullet">
/// <item><b>scope = agentName, folder = /skills</b> — skill instruction documents</item>
/// <item><b>scope = agentName, folder = /knowledge</b> — agent knowledge base</item>
/// <item><b>scope = agentName, folder = /memory</b> — agent working memory</item>
/// <item><b>scope = sessionId, folder = /uploads</b> — user-uploaded files (ephemeral)</item>
/// <item><b>scope = sessionId, folder = /artifacts</b> — agent-generated outputs (ephemeral)</item>
/// </list>
/// <para>
/// Pass scope=null to QueryAsync to search across ALL scopes.
/// </para>
/// </remarks>
public interface IContentStore
{
    /// <summary>
    /// Store content in the given scope and return a unique identifier.
    /// </summary>
    /// <param name="scope">
    /// Scope identifier for isolation (e.g., agentName or sessionId).
    /// Pass null for global content visible to all agents.
    /// </param>
    /// <param name="data">Content bytes (binary or UTF-8 text)</param>
    /// <param name="contentType">MIME type (e.g., "image/jpeg", "text/plain")</param>
    /// <param name="metadata">Optional metadata (tags, origin, description, name, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Unique content identifier for later retrieval</returns>
    /// <remarks>
    /// <para><b>Named Upsert:</b></para>
    /// <para>
    /// When <see cref="ContentMetadata.Name"/> is provided, behaves as an upsert keyed on (scope, Name):
    /// same name + same content hash = no-op; same name + changed content = overwrite.
    /// When Name is null, always creates a new entry (unnamed insert — correct for uploads/artifacts).
    /// </para>
    /// </remarks>
    Task<string> PutAsync(
        string? scope,
        byte[] data,
        string contentType,
        ContentMetadata? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve content by identifier within the given scope.
    /// Returns null if not found.
    /// </summary>
    /// <param name="scope">Scope identifier (e.g., agentName or sessionId). Pass null for global scope.</param>
    /// <param name="contentId">Content identifier returned by PutAsync</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Content data, or null if not found</returns>
    Task<ContentData?> GetAsync(
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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query content within the given scope with optional filters.
    /// Returns metadata only (not full content bytes).
    /// </summary>
    /// <param name="scope">Scope identifier (e.g., agentName or sessionId). Pass null to query across ALL scopes.</param>
    /// <param name="query">Optional query filters (null = return all content in scope)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of content metadata matching query within scope</returns>
    /// <remarks>
    /// <para><b>Performance Note:</b></para>
    /// <para>
    /// Query returns metadata only. Call GetAsync to retrieve full content bytes.
    /// This enables efficient listing and filtering without loading all content into memory.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<ContentInfo>> QueryAsync(
        string? scope = null,
        ContentQuery? query = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Metadata provided when storing content.
/// All fields are optional - stores may compute defaults.
/// </summary>
public record ContentMetadata
{
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
    /// - IAssetStore: (none)
    /// - StaticMemoryStore: {"extractedTextLength": "15234"}
    /// - DynamicMemoryStore: {"title": "Meeting Notes"}
    /// - IInstructionDocumentStore: {"version": "3", "contentHash": "abc123"}
    /// </summary>
    public IReadOnlyDictionary<string, object>? ExtendedMetadata { get; init; }
}

/// <summary>
/// Full content with metadata.
/// Returned by GetAsync.
/// </summary>
public record ContentData
{
    /// <summary>Unique content identifier</summary>
    public required string Id { get; init; }

    /// <summary>Content bytes (binary or UTF-8 text)</summary>
    public required byte[] Data { get; init; }

    /// <summary>MIME type</summary>
    public required string ContentType { get; init; }

    /// <summary>Full metadata</summary>
    public required ContentInfo Info { get; init; }
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
