namespace HPD.Agent;

/// <summary>
/// Options for creating a content store folder.
/// </summary>
public record FolderOptions
{
    /// <summary>
    /// REQUIRED: Human-readable description of the folder's purpose.
    /// This description is shown to the agent in the FolderDiscoveryMiddleware context injection,
    /// helping the agent understand what the folder contains without reading files.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Access permissions for this folder.
    /// Controls which content_* tools the agent can use on this folder.
    /// </summary>
    public ContentPermissions Permissions { get; init; } = ContentPermissions.ReadWrite;

    /// <summary>
    /// Arbitrary tags attached to all content in this folder.
    /// Useful for additional categorization and filtering.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }
}

/// <summary>
/// Permissions for a content store folder.
/// </summary>
[Flags]
public enum ContentPermissions
{
    /// <summary>No access.</summary>
    None = 0,
    /// <summary>Allow reading (content_read, content_list, content_glob, content_stat, content_tree).</summary>
    Read = 1,
    /// <summary>Allow writing (content_write).</summary>
    Write = 2,
    /// <summary>Allow deletion (content_delete).</summary>
    Delete = 4,
    /// <summary>Allow reading and writing (default for user-facing folders).</summary>
    ReadWrite = Read | Write,
    /// <summary>Full access.</summary>
    Full = Read | Write | Delete
}

/// <summary>
/// Metadata about a registered folder in a content store.
/// </summary>
public record FolderInfo
{
    /// <summary>Folder name (e.g., "knowledge").</summary>
    public required string Name { get; init; }

    /// <summary>Folder path as seen by agents (e.g., "/knowledge").</summary>
    public required string Path { get; init; }

    /// <summary>Human-readable description shown to the agent.</summary>
    public required string Description { get; init; }

    /// <summary>Access permissions for this folder.</summary>
    public required ContentPermissions Permissions { get; init; }

    /// <summary>
    /// Scope label: "agent" for agent-scoped folders, "session" for session-scoped folders.
    /// </summary>
    public required string Scope { get; init; }
}
