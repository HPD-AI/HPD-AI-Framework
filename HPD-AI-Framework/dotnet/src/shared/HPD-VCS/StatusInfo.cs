using System.Collections.Generic;
using HPD.VCS.Core;

namespace HPD.VCS;

/// <summary>
/// Represents the status of the working copy, showing files that have been added, modified, deleted, or are untracked.
/// This provides a user-friendly view of working copy changes compared to the current tracked state.
/// </summary>
public readonly record struct StatusInfo(
    /// <summary>
    /// Files that exist in the working copy but are not tracked by the repository.
    /// These files would be added if a commit operation is performed.
    /// </summary>
    IReadOnlyList<RepoPath> UntrackedFiles,
    
    /// <summary>
    /// Files that are tracked and have been modified since the last commit.
    /// These changes would be included in the next commit.
    /// </summary>
    IReadOnlyList<RepoPath> ModifiedFiles,
    
    /// <summary>
    /// Files that were previously tracked but have been deleted from the working copy.
    /// These deletions would be recorded in the next commit.
    /// </summary>
    IReadOnlyList<RepoPath> DeletedFiles,
    
    /// <summary>
    /// New files that have been added to tracking and would be included in the next commit.
    /// </summary>
    IReadOnlyList<RepoPath> NewFilesTracked,
    
    /// <summary>
    /// Files that were ignored during the status check due to being locked or inaccessible.
    /// These files could not be processed and their actual status is unknown.
    /// </summary>
    IReadOnlyList<RepoPath> SkippedFiles,
    
    /// <summary>
    /// Files that exist in the working copy but are ignored by ignore rules.
    /// These files are not tracked and would not be included in commits.
    /// </summary>
    IReadOnlyList<RepoPath> IgnoredFiles
)
{
    /// <summary>
    /// Gets a value indicating whether the working copy has any changes.
    /// A clean working copy has no untracked, modified, deleted, or newly tracked files.
    /// </summary>
    public bool IsClean => UntrackedFiles.Count == 0 && 
                          ModifiedFiles.Count == 0 && 
                          DeletedFiles.Count == 0 && 
                          NewFilesTracked.Count == 0;

    /// <summary>
    /// Gets the total number of files with changes (excluding ignored and skipped files).
    /// </summary>
    public int TotalChangedFiles => UntrackedFiles.Count + ModifiedFiles.Count + DeletedFiles.Count + NewFilesTracked.Count;
}
