namespace HPD.Agent;

/// <summary>
/// A scoped view of IContentStore for a single named folder.
/// All operations use the folder's pre-configured scope and tag.
/// </summary>
/// <remarks>
/// Obtain instances via ContentStoreExtensions.CreateFolder() or GetFolder().
/// IContentFolder pre-bakes the folder tag into every query/write/delete so callers
/// don't have to construct tags manually.
/// </remarks>
public interface IContentFolder
{
    /// <summary>Folder name (e.g., "knowledge").</summary>
    string Name { get; }

    /// <summary>Folder path as seen by agents (e.g., "/knowledge").</summary>
    string Path { get; }

    /// <summary>Folder options (description, permissions, tags).</summary>
    FolderOptions Options { get; }

    /// <summary>
    /// Write content in this folder with explicit write intent.
    /// </summary>
    Task<ContentInfo> WriteAsync(
        string? scope,
        Stream data,
        ContentMetadata metadata,
        ContentWriteOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Open content by name or ID from this folder.
    /// </summary>
    Task<Stream?> OpenReadAsync(
        string scope,
        string nameOrId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve content metadata by name or ID from this folder.
    /// </summary>
    Task<ContentInfo?> StatAsync(
        string scope,
        string nameOrId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete content by name or ID from this folder.
    /// </summary>
    Task DeleteAsync(
        string scope,
        string nameOrId,
        ContentDeleteOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List all content in this folder for the given scope.
    /// </summary>
    Task<IReadOnlyList<ContentInfo>> ListAsync(
        string scope,
        CancellationToken cancellationToken = default);
}
