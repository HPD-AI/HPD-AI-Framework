namespace HPD.Agent;

/// <summary>
/// Identifies an explicit content-store isolation scope.
/// </summary>
/// <param name="Value">The non-empty, store-independent scope value.</param>
public readonly record struct ContentScope(string Value)
{
    /// <summary>Gets the explicit global content scope.</summary>
    public static ContentScope Global { get; } = new("global");

    /// <summary>Creates and validates a content scope.</summary>
    /// <param name="value">The scope value.</param>
    /// <returns>A validated content scope.</returns>
    public static ContentScope Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new ContentScope(value);
    }

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Addresses one content item and optionally constrains the exact snapshot to open.
/// </summary>
/// <param name="Scope">The explicit isolation scope.</param>
/// <param name="ContentId">The non-empty content identifier.</param>
/// <param name="Version">An optional opaque expected-version token.</param>
/// <param name="Sha256">An optional lowercase hexadecimal expected content digest.</param>
public readonly record struct ContentAddress(
    ContentScope Scope,
    string ContentId,
    string? Version = null,
    string? Sha256 = null)
{
    /// <summary>Creates and validates a content address.</summary>
    /// <param name="scope">The explicit isolation scope.</param>
    /// <param name="contentId">The content identifier.</param>
    /// <param name="version">An optional expected version.</param>
    /// <param name="sha256">An optional expected SHA-256 digest.</param>
    /// <returns>A validated content address.</returns>
    public static ContentAddress Create(
        ContentScope scope,
        string contentId,
        string? version = null,
        string? sha256 = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        return new ContentAddress(scope, contentId, version, sha256);
    }
}

/// <summary>
/// Owns an opened content stream and metadata describing those exact bytes.
/// </summary>
public sealed class ContentReadResult : IAsyncDisposable
{
    /// <summary>Gets the readable content stream owned by this result.</summary>
    public required Stream Content { get; init; }

    /// <summary>Gets metadata for the exact returned snapshot.</summary>
    public required ContentInfo Info { get; init; }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>
/// Unified content storage interface for the framework.
/// Provides stream-first versioned writes and atomic snapshot reads with explicit scope isolation.
/// </summary>
/// <remarks>
/// <para>
/// Scope is backend isolation, commonly a session id or an agent name. Global content uses
/// <see cref="ContentScope.Global"/> explicitly.
/// Tags are generic metadata for filtering and policy. They do not define a public filesystem.
/// </para>
/// </remarks>
public interface IContentStore
{
    /// <summary>
    /// Write content in the given scope and return metadata for the stored item.
    /// </summary>
     /// <param name="scope">
    /// Explicit scope identifier for isolation (for example, an agent or session scope).
    /// Use <see cref="ContentScope.Global"/> for global content.
    /// </param>
    /// <param name="data">Readable content stream.</param>
    /// <param name="metadata">Metadata describing the content, including content type.</param>
    /// <param name="options">Explicit write intent and conditional write options.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Metadata for the stored content</returns>
    ValueTask<ContentInfo> WriteAsync(
        ContentScope scope,
        Stream data,
        ContentMetadata metadata,
        ContentWriteOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically opens content and returns metadata describing the exact returned bytes.
    /// </summary>
    /// <param name="address">The content address and optional exact version/hash constraints.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An owned atomic stream-and-metadata snapshot, or null if not found.</returns>
    ValueTask<ContentReadResult?> OpenReadAsync(
        ContentAddress address,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a temporary provider-readable URI for content, when supported by the store.
    /// Returns null if this store cannot expose direct read URIs.
    /// </summary>
    ValueTask<Uri?> CreateReadUriAsync(
        ContentAddress address,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve metadata by identifier within the given scope without opening the content.
    /// Returns null if not found.
    /// </summary>
    ValueTask<ContentInfo?> StatAsync(
        ContentAddress address,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete content by identifier within the given scope.
    /// Idempotent - no-op if content doesn't exist.
    /// </summary>
    /// <param name="address">The content address and optional exact version/hash constraints.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    ValueTask DeleteAsync(
        ContentAddress address,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query content within the given scope with optional filters.
    /// Returns metadata only and never opens content streams.
    /// </summary>
    /// <param name="scope">The one explicit scope to query.</param>
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
    ValueTask<IReadOnlyList<ContentInfo>> QueryAsync(
        ContentScope scope,
        ContentQuery? query = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Controls content creation, replacement, and conditional mutation.</summary>
public sealed record ContentWriteOptions
{
    /// <summary>Gets the explicit write operation.</summary>
    public ContentWriteMode Mode { get; init; } = ContentWriteMode.Create;

    /// <summary>Gets the target identifier for ID-based replacement or append.</summary>
    public string? ContentId { get; init; }

    /// <summary>Gets the opaque expected version required for a conditional mutation.</summary>
    public string? IfMatchVersion { get; init; }

    /// <summary>Gets whether creation fails when the logical name is already indexed.</summary>
    public bool FailIfNameExists { get; init; }

    /// <summary>Gets optional backend-specific policy hints.</summary>
    public IReadOnlyDictionary<string, string>? PolicyHints { get; init; }
}

/// <summary>Specifies the content mutation performed by a write.</summary>
public enum ContentWriteMode
{
    /// <summary>Creates a new content identifier.</summary>
    Create,
    /// <summary>Replaces the content selected by <see cref="ContentWriteOptions.ContentId"/>.</summary>
    ReplaceById,
    /// <summary>Replaces the content selected by its scoped logical name.</summary>
    ReplaceByName,
    /// <summary>Appends to an existing ID/name or creates it when absent.</summary>
    Append
}

/// <summary>Reports an expected version or digest that does not match current content.</summary>
public sealed class ContentConflictException : Exception
{
    /// <summary>Creates a content conflict.</summary>
    /// <param name="message">The conflict description.</param>
    /// <param name="contentId">The affected content identifier.</param>
    /// <param name="expectedVersion">The expected version or digest.</param>
    /// <param name="actualVersion">The actual version or digest.</param>
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

    /// <summary>Gets the affected content identifier.</summary>
    public string? ContentId { get; }

    /// <summary>Gets the expected version or digest.</summary>
    public string? ExpectedVersion { get; }

    /// <summary>Gets the actual version or digest.</summary>
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
    /// <summary>Gets the exact address, version, and digest of this content snapshot.</summary>
    public required ContentAddress Address { get; init; }

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
    /// - Local content uploads: {"kind": "upload"} with a thread content scope
    /// - Runtime artifacts: {"kind": "artifact", "artifact-kind": "execute_command_output"}
    /// - StaticMemoryStore: {"extractedTextLength": "15234"}
    /// - DynamicMemoryStore: {"title": "Meeting Notes"}
    /// </summary>
    public IReadOnlyDictionary<string, object>? ExtendedMetadata { get; init; }

    public override string ToString() => Address.ContentId;
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
