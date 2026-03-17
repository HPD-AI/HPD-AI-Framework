namespace HPD.Agent;

/// <summary>
/// A scoped view of IContentStore for a single named folder.
/// All operations use the folder's pre-configured scope and tag.
/// </summary>
/// <remarks>
/// Obtain instances via ContentStoreExtensions.CreateFolder() or GetFolder().
/// IContentFolder pre-bakes the folder tag into every query/put/delete so callers
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
    /// Store content in this folder.
    /// Uses named upsert semantics when metadata.Name is provided.
    /// </summary>
    Task<string> PutAsync(
        string? scope,
        byte[] data,
        string contentType,
        ContentMetadata? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve content by name or ID from this folder.
    /// </summary>
    Task<ContentData?> GetAsync(
        string scope,
        string nameOrId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete content by name or ID from this folder.
    /// </summary>
    Task DeleteAsync(
        string scope,
        string nameOrId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List all content in this folder for the given scope.
    /// </summary>
    Task<IReadOnlyList<ContentInfo>> ListAsync(
        string scope,
        CancellationToken cancellationToken = default);
}
