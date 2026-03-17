using HPD.VCS.Core;

namespace HPD.VCS;

/// <summary>
/// Represents the status of the working copy compared to the current HEAD commit.
/// This is the result of a dry-run status check that shows what has changed
/// without creating any new objects in the object store.
/// </summary>
public readonly record struct WorkingCopyStatus(
    IReadOnlyList<RepoPath> UntrackedFiles,
    IReadOnlyList<RepoPath> ModifiedFiles,
    IReadOnlyList<RepoPath> AddedFiles,
    IReadOnlyList<RepoPath> RemovedFiles,
    IReadOnlyList<RepoPath>? ignoredFiles = null,
    IReadOnlyList<RepoPath>? skippedFiles = null
)
{
    /// <summary>
    /// Gets whether the working copy is clean (no changes).
    /// </summary>
    public bool IsClean => 
        UntrackedFiles.Count == 0 && 
        ModifiedFiles.Count == 0 && 
        AddedFiles.Count == 0 && 
        RemovedFiles.Count == 0;

    /// <summary>
    /// Gets the total number of changes in the working copy.
    /// </summary>
    public int TotalChanges => 
        UntrackedFiles.Count + 
        ModifiedFiles.Count + 
        AddedFiles.Count + 
        RemovedFiles.Count;

    // Compatibility properties for existing test code
    /// <summary>
    /// Alias for TotalChanges to maintain compatibility with existing code.
    /// </summary>
    public int TotalChangedFiles => TotalChanges;

    /// <summary>
    /// Alias for RemovedFiles to maintain compatibility with existing code.
    /// </summary>
    public IReadOnlyList<RepoPath> DeletedFiles => RemovedFiles;

    /// <summary>
    /// Alias for AddedFiles to maintain compatibility with existing code.
    /// </summary>
    public IReadOnlyList<RepoPath> NewFilesTracked => AddedFiles;    /// <summary>
    /// Gets ignored files, or empty list if not provided.
    /// </summary>
    public IReadOnlyList<RepoPath> IgnoredFiles => ignoredFiles ?? new List<RepoPath>().AsReadOnly();

    /// <summary>
    /// Gets skipped files, or empty list if not provided.
    /// </summary>
    public IReadOnlyList<RepoPath> SkippedFiles => skippedFiles ?? new List<RepoPath>().AsReadOnly();
}
