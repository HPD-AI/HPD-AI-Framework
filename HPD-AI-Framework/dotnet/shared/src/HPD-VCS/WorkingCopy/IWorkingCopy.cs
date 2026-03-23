using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HPD.VCS.Core;

namespace HPD.VCS.WorkingCopy;

/// <summary>
/// Interface for working copy management functionality.
/// Provides a pluggable abstraction for different working copy implementations
/// while maintaining compatibility with the existing WorkingCopyState.
/// </summary>
public interface IWorkingCopy
{
    /// <summary>
    /// Gets the current file states tracked by this working copy.
    /// </summary>
    IReadOnlyDictionary<RepoPath, FileState> FileStates { get; }

    /// <summary>
    /// Gets the working copy root path.
    /// </summary>
    string WorkingCopyPath { get; }

    /// <summary>
    /// Gets the current tree ID representing the baseline state, if any.
    /// </summary>
    TreeId? CurrentTreeId { get; }

    /// <summary>
    /// Scans the working copy directory and updates file states.
    /// This performs a filesystem traversal to detect changes since the last scan.
    /// </summary>
    /// <returns>A task representing the asynchronous scan operation</returns>
    Task ScanWorkingCopyAsync();

    /// <summary>
    /// Creates a snapshot of the current working copy state and stores it in the object store.
    /// This captures all tracked files into immutable TreeData and FileContentData objects.
    /// </summary>
    /// <returns>The TreeId of the root tree representing the working copy snapshot</returns>
    Task<TreeId> CreateSnapshotAsync();

    /// <summary>
    /// Creates a snapshot of the working copy with detailed statistics and change tracking.
    /// Scans the filesystem, detects changes compared to the current tree state, and creates
    /// a new immutable snapshot with comprehensive statistics about what changed.
    /// </summary>
    /// <param name="options">Options controlling snapshot behavior</param>
    /// <param name="dryRun">If true, does not write new objects to the store, only computes changes</param>
    /// <returns>A tuple containing the new snapshot tree ID and detailed statistics</returns>
    Task<(TreeId newSnapshotTreeId, SnapshotStats stats)> SnapshotAsync(SnapshotOptions options, bool dryRun = false);

    /// <summary>
    /// Checks out the specified tree to the working directory, updating file states accordingly.
    /// This operation updates the physical working directory files and manages tracked file states post-checkout.
    /// </summary>
    /// <param name="targetTreeId">The TreeId of the commit/tree to check out</param>
    /// <param name="options">Options controlling checkout behavior</param>
    /// <returns>Statistics about the checkout operation</returns>
    Task<CheckoutStats> CheckoutAsync(TreeId targetTreeId, CheckoutOptions options);

    /// <summary>
    /// Updates the current tree ID and synchronizes file states to match the tree.
    /// Populates file states to match the tree, creating placeholders for missing files.
    /// </summary>
    /// <param name="newTreeId">The new tree ID to set as current</param>
    Task UpdateCurrentTreeIdAsync(TreeId newTreeId);

    /// <summary>
    /// Gets the file state for a specific path, or null if not tracked.
    /// </summary>
    /// <param name="repoPath">The repository-relative path</param>
    /// <returns>The file state or null if not found</returns>
    FileState? GetFileState(RepoPath repoPath);

    /// <summary>
    /// Updates the file state for a specific path.
    /// </summary>
    /// <param name="repoPath">The repository-relative path</param>
    /// <param name="fileState">The new file state</param>
    void UpdateFileState(RepoPath repoPath, FileState fileState);

    /// <summary>
    /// Removes tracking for a specific path (e.g., when a file is deleted).
    /// </summary>
    /// <param name="repoPath">The repository-relative path to stop tracking</param>
    void RemoveFileState(RepoPath repoPath);

    /// <summary>
    /// Gets all tracked file paths.
    /// </summary>
    /// <returns>An enumerable of all tracked repository paths</returns>
    IEnumerable<RepoPath> GetTrackedPaths();

    /// <summary>
    /// Replaces the internal tracked file states dictionary with the provided new states.
    /// This is called after a successful checkout has materialized files, using FileState objects 
    /// created from the on-disk mtime/size of those newly written/updated files.
    /// </summary>
    /// <param name="newStates">The new file states to replace the current tracked states</param>
    void ReplaceTrackedFileStates(Dictionary<RepoPath, FileState> newStates);
}
