using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using HPD.VCS.Core;
using HPD.VCS.Storage;

namespace HPD.VCS.WorkingCopy;

/// <summary>
/// Statistics about a working copy snapshot operation.
/// </summary>
public readonly record struct SnapshotStats(
    IReadOnlyList<RepoPath> UntrackedIgnoredFiles,
    IReadOnlyList<RepoPath> UntrackedKeptFiles,
    IReadOnlyList<RepoPath> NewFilesTracked,
    IReadOnlyList<RepoPath> ModifiedFiles,
    IReadOnlyList<RepoPath> DeletedFiles,
    IReadOnlyList<RepoPath> SkippedDueToLock
);

/// <summary>
/// Mutable builder for SnapshotStats to avoid struct copying during recursive operations.
/// </summary>
internal class SnapshotStatsBuilder
{
    public List<RepoPath> UntrackedIgnoredFiles { get; } = new();
    public List<RepoPath> UntrackedKeptFiles { get; } = new();
    public List<RepoPath> NewFilesTracked { get; } = new();
    public List<RepoPath> ModifiedFiles { get; } = new();
    public List<RepoPath> DeletedFiles { get; } = new();
    public List<RepoPath> SkippedDueToLock { get; } = new();

    public SnapshotStats ToStats() => new(
        UntrackedIgnoredFiles.AsReadOnly(),
        UntrackedKeptFiles.AsReadOnly(),
        NewFilesTracked.AsReadOnly(),
        ModifiedFiles.AsReadOnly(),
        DeletedFiles.AsReadOnly(),
        SkippedDueToLock.AsReadOnly()
    );
}

/// <summary>
/// Options for controlling working copy snapshot behavior.
/// </summary>
public record class SnapshotOptions
{
    /// <summary>
    /// A predicate function that determines whether a new file should be tracked.
    /// Default implementation tracks all files that aren't ignored.
    /// </summary>
    public Func<RepoPath, bool> ShouldTrackNewFileMatcher { get; init; } = _ => true;

    /// <summary>
    /// Maximum file size for new files to be automatically tracked (default: 8MB).
    /// Files larger than this require explicit tracking decisions.
    /// </summary>
    public long MaxNewFileSize { get; init; } = 8 * 1024 * 1024; // 8MB

    /// <summary>
    /// Minimum mtime granularity in milliseconds for change detection (default: 2000ms for FAT32 compatibility).
    /// Files modified within this window will use hash-based verification instead of mtime-only checking.
    /// </summary>
    public int MtimeGranularityMs { get; init; } = 2000; // 2 seconds for FAT32

    /// <summary>
    /// Whether to support nested ignore files (directory-level .gitignore/.hpdignore files).
    /// When enabled, each directory can have its own ignore rules that merge with parent rules.
    /// </summary>
    public bool SupportNestedIgnoreFiles { get; init; } = true;    /// <summary>
    /// Whether to enable Windows symlink support when running in Developer Mode.
    /// Requires elevated privileges or Developer Mode on Windows.
    /// </summary>
    public bool EnableWindowsSymlinks { get; init; } = true;

    /// <summary>
    /// When true, performs a dry-run that calculates statistics without writing objects to the store.
    /// This is useful for status operations that need change information without creating a snapshot.
    /// </summary>
    public bool DryRun { get; init; } = false;

    /// <summary>
    /// File size threshold above which streaming is used for content processing (default: 8MB).
    /// Large files are processed in chunks to avoid excessive memory usage.
    /// </summary>
    public long LargeFileThreshold { get; init; } = 8 * 1024 * 1024; // 8MB
}

/// <summary>
/// Manages the state of files in the working copy for change detection and snapshotting.
/// This class tracks file metadata, applies ignore rules, and creates immutable snapshots
/// of the working copy state into TreeData objects.
/// 
/// Inspired by jj's TreeState but simplified for initial implementation.
/// This is the explicit snapshot working copy implementation that requires manual snapshots.
/// </summary>
public class ExplicitSnapshotWorkingCopy : IWorkingCopy
{
    private readonly IFileSystem _fileSystem;
    private readonly IObjectStore _objectStore;
    private readonly string _workingCopyPath;
    private readonly Dictionary<RepoPath, FileState> _fileStates;
    private readonly IgnoreFile _ignoreRules;
    private TreeId? _currentTreeId;    private readonly ILogger<ExplicitSnapshotWorkingCopy> _logger;

    /// <summary>
    /// Initializes a new instance of the ExplicitSnapshotWorkingCopy class.
    /// </summary>
    /// <param name="fileSystem">The file system abstraction for testability</param>
    /// <param name="objectStore">The object store for persisting file content and trees</param>
    /// <param name="workingCopyPath">The root path of the working copy</param>
    /// <param name="ignoreRules">The ignore rules to apply when scanning the working copy</param>
    /// <param name="currentTreeId">The current tree ID representing the baseline state</param>
    /// <param name="logger">The logger instance for logging debug/info messages</param>
    public ExplicitSnapshotWorkingCopy(
        IFileSystem fileSystem,
        IObjectStore objectStore,
        string workingCopyPath,
        IgnoreFile? ignoreRules = null,
        TreeId? currentTreeId = null,
        ILogger<ExplicitSnapshotWorkingCopy>? logger = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _objectStore = objectStore ?? throw new ArgumentNullException(nameof(objectStore));
        _workingCopyPath = workingCopyPath ?? throw new ArgumentNullException(nameof(workingCopyPath));        _fileStates = new Dictionary<RepoPath, FileState>();
        _ignoreRules = ignoreRules ?? new IgnoreFile();
        _currentTreeId = currentTreeId;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ExplicitSnapshotWorkingCopy>.Instance;
    }    /// <summary>
         /// Gets the current file states tracked by this working copy.
         /// </summary>
    public IReadOnlyDictionary<RepoPath, FileState> FileStates => _fileStates;

    /// <summary>
    /// Gets the working copy root path.
    /// </summary>
    public string WorkingCopyPath => _workingCopyPath;

    /// <summary>
    /// Gets the current tree ID representing the baseline state, if any.
    /// </summary>
    public TreeId? CurrentTreeId => _currentTreeId;

    /// <summary>
    /// Scans the working copy directory and updates file states.
    /// This performs a filesystem traversal to detect changes since the last scan.
    /// </summary>
    /// <returns>A task representing the asynchronous scan operation</returns>
    public async Task ScanWorkingCopyAsync()
    {
        // Clear existing states - we'll rebuild from filesystem
        _fileStates.Clear();
        // Start recursive scan from working copy root
        await ScanDirectoryRecursiveAsync(RepoPath.Root, _workingCopyPath);
    }    /// <summary>
         /// Creates a snapshot of the current working copy state and stores it in the object store.
         /// This captures all tracked files into immutable TreeData and FileContentData objects.
         /// </summary>
         /// <returns>The TreeId of the root tree representing the working copy snapshot</returns>
    public async Task<TreeId> CreateSnapshotAsync()
    {
        // Create snapshot starting from the root
        return await CreateTreeSnapshotAsync(RepoPath.Root);
    }    /// <summary>
         /// Creates a snapshot of the working copy with detailed statistics and change tracking.
         /// Scans the filesystem, detects changes compared to the current tree state, and creates
         /// a new immutable snapshot with comprehensive statistics about what changed.
         /// </summary>
         /// <param name="options">Options controlling snapshot behavior</param>
         /// <param name="dryRun">If true, does not write new objects to the store, only computes changes</param>
         /// <returns>A tuple containing the new snapshot tree ID and detailed statistics</returns>
    public async Task<(TreeId newSnapshotTreeId, SnapshotStats stats)> SnapshotAsync(SnapshotOptions options, bool dryRun = false)    {
        // Set default DryRun from parameter if not set in options
        if (dryRun && !options.DryRun)
        {
            options = options with { DryRun = true };
        }

        // Initialize statistics builder
        var statsBuilder = new SnapshotStatsBuilder();

        // Build the previous tree state for comparison
        var previousFileStates = new Dictionary<RepoPath, FileState>(_fileStates);
        Dictionary<RepoPath, TreeEntry>? previousTreeEntries = null;

        if (_currentTreeId.HasValue)
        {
            previousTreeEntries = await BuildPreviousTreeEntriesAsync(_currentTreeId.Value);
        }        // Process the working copy recursively to build new snapshot
        var rootTreeId = await ProcessDirectoryAsync(
            RepoPath.Root,
            _workingCopyPath,
            previousFileStates,
            previousTreeEntries,
            options,
            statsBuilder);

        // Update the current tree ID atomically (only if not in dry-run mode)
        if (!options.DryRun)
        {
            _currentTreeId = rootTreeId;
        }

        return (rootTreeId, statsBuilder.ToStats());
    }

    /// <summary>
    /// Checks if a file appears to be clean (unchanged) based on its file state.
    /// </summary>
    /// <param name="repoPath">The repository-relative path of the file</param>
    /// <param name="previousFileState">The previous file state to compare against</param>
    /// <returns>True if the file appears unchanged based on metadata</returns>
    public bool IsFileClean(RepoPath repoPath, FileState previousFileState)
    {
        if (!_fileStates.TryGetValue(repoPath, out var currentState))
        {
            // File doesn't exist in current state, so it's been deleted
            return false;
        }

        return currentState.IsClean(previousFileState);
    }

    /// <summary>
    /// Gets the file state for a specific path, or null if not tracked.
    /// </summary>
    /// <param name="repoPath">The repository-relative path</param>
    /// <returns>The file state or null if not found</returns>
    public FileState? GetFileState(RepoPath repoPath)
    {
        return _fileStates.TryGetValue(repoPath, out var state) ? state : null;
    }

    /// <summary>
    /// Updates the file state for a specific path.
    /// </summary>
    /// <param name="repoPath">The repository-relative path</param>
    /// <param name="fileState">The new file state</param>
    public void UpdateFileState(RepoPath repoPath, FileState fileState)
    {
        _fileStates[repoPath] = fileState;
    }

    /// <summary>
    /// Removes tracking for a specific path (e.g., when a file is deleted).
    /// </summary>
    /// <param name="repoPath">The repository-relative path to stop tracking</param>
    public void RemoveFileState(RepoPath repoPath)
    {
        _fileStates.Remove(repoPath);
    }

    /// <summary>
    /// Gets all tracked file paths.
    /// </summary>
    /// <returns>An enumerable of all tracked repository paths</returns>
    public IEnumerable<RepoPath> GetTrackedPaths()
    {
        return _fileStates.Keys;
    }

    /// <summary>
    /// Recursively scans a directory and updates file states.
    /// </summary>
    private async Task ScanDirectoryRecursiveAsync(RepoPath repoDir, string diskPath)
    {
        if (!_fileSystem.Directory.Exists(diskPath))
        {
            return;
        }

        try
        {
            // Get all entries in this directory
            var entries = _fileSystem.Directory.GetFileSystemEntries(diskPath);

            foreach (var entryPath in entries)
            {
                var entryName = _fileSystem.Path.GetFileName(entryPath);

                // Skip invalid path components (e.g., empty names)
                if (string.IsNullOrWhiteSpace(entryName))
                {
                    continue;
                }

                // Create repo path for this entry
                var repoPath = repoDir.IsRoot ?
                    new RepoPath(new RepoPathComponent(entryName)) :
                    new RepoPath(repoDir.Components.Append(new RepoPathComponent(entryName)));                // Check if this path should be ignored
                var isDirectory = _fileSystem.Directory.Exists(entryPath);
                if (_ignoreRules.IsMatch(repoPath, isDirectory) || IsVcsMetadataPath(repoPath))
                {
                    continue;
                }

                if (isDirectory)
                {
                    // Recursively scan subdirectory
                    await ScanDirectoryRecursiveAsync(repoPath, entryPath);
                }
                else if (_fileSystem.File.Exists(entryPath))
                {
                    // Process file
                    await ProcessFileAsync(repoPath, entryPath);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or
                                        System.IO.DirectoryNotFoundException or
                                        System.IO.IOException)
        {
            // Log and continue - don't fail the entire scan for one problematic directory
            // In a real implementation, you might want to use a proper logging framework
            Console.WriteLine($"Warning: Could not scan directory {diskPath}: {ex.Message}");
        }
    }    /// <summary>
         /// Processes a single file and updates its file state.
         /// </summary>
    private Task ProcessFileAsync(RepoPath repoPath, string diskPath)
    {
        try
        {
            var fileInfo = _fileSystem.FileInfo.New(diskPath);

            // Determine file type
            var fileType = DetermineFileType(diskPath);

            // Get file metadata
            var mTime = fileInfo.LastWriteTimeUtc;
            var size = fileType == FileType.Symlink ?
                GetSymlinkTargetSize(diskPath) :
                fileInfo.Length;

            // Create file state
            var fileState = new FileState(fileType, mTime, size);

            // Store in our tracking dictionary
            _fileStates[repoPath] = fileState;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or
                                        System.IO.FileNotFoundException or
                                        System.IO.IOException)
        {
            // Log and continue - don't fail for one problematic file
            Console.WriteLine($"Warning: Could not process file {diskPath}: {ex.Message}");
        }

        return Task.CompletedTask;
    }    /// <summary>
         /// Determines the file type (normal file vs symlink).
         /// </summary>
    private FileType DetermineFileType(string diskPath, SnapshotOptions? options = null)
    {
        // Use enhanced Windows detection if enabled
        if (options?.EnableWindowsSymlinks == true && OperatingSystem.IsWindows())
        {
            return DetermineWindowsFileType(diskPath);
        }

        // Fallback to basic detection
        try
        {
            var attributes = _fileSystem.File.GetAttributes(diskPath);
            if ((attributes & System.IO.FileAttributes.ReparsePoint) != 0)
            {
                return FileType.Symlink;
            }
        }
        catch
        {
            // If we can't determine attributes, assume normal file
        }

        return FileType.NormalFile;
    }

    /// <summary>
    /// Enhanced Windows-specific file type detection with comprehensive reparse point analysis.
    /// Properly distinguishes symbolic links from other reparse point types like junction points.
    /// </summary>
    private FileType DetermineWindowsFileType(string diskPath)
    {
        try
        {
            var attributes = _fileSystem.File.GetAttributes(diskPath);
            if ((attributes & System.IO.FileAttributes.ReparsePoint) == 0)
            {
                return FileType.NormalFile;
            }

            // For reparse points, we need to determine the specific type
            // Check if it's a symbolic link by attempting to read the link target
            var fileInfo = _fileSystem.FileInfo.New(diskPath);

            // In .NET, LinkTarget property indicates a symbolic link
            // If LinkTarget is not null, it's a symbolic link
            if (!string.IsNullOrEmpty(fileInfo.LinkTarget))
            {
                return FileType.Symlink;
            }

            // If it's a reparse point but not a symbolic link, treat as normal file
            // This handles junction points, mount points, and other reparse point types
            return FileType.NormalFile;
        }
        catch
        {
            // If we can't determine the type, assume normal file
            return FileType.NormalFile;
        }
    }

    /// <summary>
    /// Gets the size of a symlink target string.
    /// </summary>
    private long GetSymlinkTargetSize(string symlinkPath)
    {
        try
        {
            // For symlinks, we want the size of the target path string
            var target = _fileSystem.FileInfo.New(symlinkPath).LinkTarget;
            return target?.Length ?? 0;
        }
        catch
        {
            // If we can't read the symlink target, return 0
            return 0;
        }
    }

    /// <summary>
    /// Creates a tree snapshot for a specific directory and all its contents.
    /// </summary>
    private async Task<TreeId> CreateTreeSnapshotAsync(RepoPath repoDir)
    {
        var entries = new List<TreeEntry>();

        // Find all files and subdirectories in this directory
        var childPaths = _fileStates.Keys
            .Where(path => IsDirectChildOf(path, repoDir))
            .ToList();

        // Group by whether they're files or directories
        var filesInDir = new List<RepoPath>();
        var dirsInDir = new HashSet<string>();

        foreach (var childPath in childPaths)
        {
            var lastComponent = childPath.FileName();
            if (lastComponent != null)
            {
                var childDiskPath = GetDiskPath(childPath);
                if (_fileSystem.Directory.Exists(childDiskPath))
                {
                    dirsInDir.Add(lastComponent.Value);
                }
                else
                {
                    filesInDir.Add(childPath);
                }
            }
        }        // Create tree entries for files
        foreach (var filePath in filesInDir)
        {
            var fileName = filePath.FileName();
            if (fileName == null) continue; // Skip files without valid names

            var fileContentId = await CreateFileContentSnapshotAsync(filePath);

            entries.Add(new TreeEntry(
                fileName.Value, // Use .Value to get the non-nullable RepoPathComponent
                TreeEntryType.File,
                new ObjectIdBase(fileContentId.HashValue.ToArray())
            ));
        }        // Create tree entries for subdirectories
        foreach (var dirName in dirsInDir)
        {
            var subDirPath = repoDir.IsRoot ?
                new RepoPath(new RepoPathComponent(dirName)) :
                new RepoPath(repoDir.Components.Append(new RepoPathComponent(dirName)));

            var subTreeId = await CreateTreeSnapshotAsync(subDirPath);

            entries.Add(new TreeEntry(
                new RepoPathComponent(dirName),
                TreeEntryType.Directory,
                new ObjectIdBase(subTreeId.HashValue.ToArray())
            ));
        }

        // Create and store the tree
        var treeData = new TreeData(entries);
        return await _objectStore.WriteTreeAsync(treeData);
    }    /// <summary>
    /// Creates a file content snapshot for a specific file.
    /// </summary>
    private async Task<FileContentId> CreateFileContentSnapshotAsync(RepoPath filePath)
    {
        var diskPath = GetDiskPath(filePath);

        byte[] content;
        if (_fileStates.TryGetValue(filePath, out var fileState) && fileState.Type == FileType.Symlink)
        {
            // For symlinks, store the target path as content
            var target = _fileSystem.FileInfo.New(diskPath).LinkTarget ?? "";
            content = System.Text.Encoding.UTF8.GetBytes(target);
        }
        else
        {
            // For normal files, read the file content
            content = await _fileSystem.File.ReadAllBytesAsync(diskPath);
        }

        var fileContentData = new FileContentData(content);
        return await _objectStore.WriteFileContentAsync(fileContentData);
    }

    /// <summary>
    /// Checks if a path is a direct child of a directory.
    /// </summary>
    private static bool IsDirectChildOf(RepoPath childPath, RepoPath parentDir)
    {
        if (parentDir.IsRoot)
        {
            return childPath.Components.Length == 1;
        }

        return childPath.Components.Length == parentDir.Components.Length + 1 &&
               childPath.Components.Take(parentDir.Components.Length).SequenceEqual(parentDir.Components);
    }    /// <summary>
         /// Converts a repository path to a disk path.
         /// </summary>
    private string GetDiskPath(RepoPath repoPath)
    {
        if (repoPath.IsRoot)
        {
            return _workingCopyPath;
        }

        var pathParts = new[] { _workingCopyPath }.Concat(repoPath.Components.Select(c => c.Value));
        return _fileSystem.Path.Combine(pathParts.ToArray());
    }    /// <summary>
         /// Builds a dictionary of previous tree entries for comparison during snapshotting.
         /// </summary>
    private async Task<Dictionary<RepoPath, TreeEntry>> BuildPreviousTreeEntriesAsync(TreeId treeId)
    {
        var result = new Dictionary<RepoPath, TreeEntry>();
        await BuildPreviousTreeEntriesRecursiveAsync(treeId, RepoPath.Root, result);
        return result;
    }

    /// <summary>
    /// Recursively builds the previous tree entries dictionary.
    /// </summary>
    private async Task BuildPreviousTreeEntriesRecursiveAsync(TreeId treeId, RepoPath currentPath, Dictionary<RepoPath, TreeEntry> result)
    {
        var treeData = await _objectStore.ReadTreeAsync(treeId);
        if (!treeData.HasValue) return;

        foreach (var entry in treeData.Value.Entries)
        {
            var entryPath = currentPath.IsRoot ?
                new RepoPath(entry.Name) :
                new RepoPath(currentPath.Components.Append(entry.Name));

            result[entryPath] = entry; if (entry.Type == TreeEntryType.Directory)
            {
                var subTreeId = ToTreeId(entry.ObjectId);
                await BuildPreviousTreeEntriesRecursiveAsync(subTreeId, entryPath, result);
            }
        }
    }    /// <summary>
         /// Recursively processes a directory and builds a tree snapshot with change statistics.
         /// </summary>
    private async Task<TreeId> ProcessDirectoryAsync(
        RepoPath repoDir,
        string diskPath,
        Dictionary<RepoPath, FileState> previousFileStates,
        Dictionary<RepoPath, TreeEntry>? previousTreeEntries,
        SnapshotOptions options,
        SnapshotStatsBuilder statsBuilder)
    {
        var entries = new List<TreeEntry>();

        if (!_fileSystem.Directory.Exists(diskPath))
        {
            return await _objectStore.WriteTreeAsync(new TreeData(entries));
        }

        try
        {
            // Load nested ignore rules if enabled
            var effectiveIgnoreRules = _ignoreRules;
            if (options.SupportNestedIgnoreFiles)
            {
                effectiveIgnoreRules = await LoadNestedIgnoreRulesAsync(repoDir, diskPath, _ignoreRules);
            }

            // Get all entries in this directory
            var diskEntries = _fileSystem.Directory.GetFileSystemEntries(diskPath);

            // Process files and directories
            var processedNames = new HashSet<string>();

            foreach (var entryPath in diskEntries)
            {
                var entryName = _fileSystem.Path.GetFileName(entryPath);

                if (string.IsNullOrWhiteSpace(entryName))
                    continue;

                processedNames.Add(entryName);

                var repoPath = repoDir.IsRoot ?
                    new RepoPath(new RepoPathComponent(entryName)) :
                    new RepoPath(repoDir.Components.Append(new RepoPathComponent(entryName)));

                var isDirectory = _fileSystem.Directory.Exists(entryPath);                // Check if this path should be ignored
                if (effectiveIgnoreRules.IsMatch(repoPath, isDirectory) || IsVcsMetadataPath(repoPath))
                {
                    statsBuilder.UntrackedIgnoredFiles.Add(repoPath);
                    continue;
                }
                if (isDirectory)
                {
                    // Process subdirectory
                    var subTreeId = await ProcessDirectoryAsync(
                        repoPath, entryPath, previousFileStates, previousTreeEntries, options, statsBuilder);

                    entries.Add(new TreeEntry(
                        new RepoPathComponent(entryName),
                        TreeEntryType.Directory,
                        ToObjectIdBase(subTreeId)
                    ));
                }
                else if (_fileSystem.File.Exists(entryPath))
                {
                    // Process file
                    var fileContentId = await ProcessFileWithStatsAsync(
                        repoPath, entryPath, previousFileStates, previousTreeEntries, options, statsBuilder);

                    if (fileContentId.HasValue)
                    {
                        entries.Add(new TreeEntry(
                            new RepoPathComponent(entryName),
                            TreeEntryType.File,
                            ToObjectIdBase(fileContentId.Value)
                        ));
                    }
                }
            }            // Check for deleted files (existed in previous but not on disk now)            // Check for deleted files (existed in previous but not on disk now)
            if (previousTreeEntries != null)
            {
                var candidatesForDeletion = previousTreeEntries.Keys
                    .Where(path => IsDirectChildOf(path, repoDir))
                    .ToList();
                
                var deletedInThisDir = candidatesForDeletion
                    .Where(path => !processedNames.Contains(path.FileName()?.Value ?? ""))
                    .ToList();

                foreach (var deletedPath in deletedInThisDir)
                {
                    statsBuilder.DeletedFiles.Add(deletedPath);
                    // Remove from tracked file states since it's deleted (only when not in dry-run mode)
                    if (!options.DryRun)
                    {
                        _fileStates.Remove(deletedPath);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or
                                        System.IO.DirectoryNotFoundException or
                                        System.IO.IOException)
        {
            Console.WriteLine($"Warning: Could not process directory {diskPath}: {ex.Message}");
        }        var treeData = new TreeData(entries);
        TreeId treeId;
        
        if (options.DryRun)
        {
            // In dry-run mode, generate a placeholder tree ID without writing to the store
            treeId = ObjectHasher.ComputeTreeId(treeData);
        }
        else
        {
            treeId = await _objectStore.WriteTreeAsync(treeData);
        }
        
        return treeId;
    }/// <summary>
     /// Processes a file and returns its content ID with updated statistics.
     /// </summary>
    private async Task<FileContentId?> ProcessFileWithStatsAsync(
        RepoPath repoPath,
        string diskPath,
        Dictionary<RepoPath, FileState> previousFileStates,
        Dictionary<RepoPath, TreeEntry>? previousTreeEntries,
        SnapshotOptions options,
        SnapshotStatsBuilder statsBuilder)
    {
        try
        {
            // Read file metadata
            var fileInfo = _fileSystem.FileInfo.New(diskPath);
            var fileType = DetermineFileType(diskPath, options);
            var mTime = fileInfo.LastWriteTimeUtc;
            var size = fileType == FileType.Symlink ?
                GetSymlinkTargetSize(diskPath) :
                fileInfo.Length;

            var currentFileState = new FileState(fileType, mTime, size); var wasTracked = previousFileStates.ContainsKey(repoPath);
            var hadPreviousTreeEntry = previousTreeEntries?.ContainsKey(repoPath) ?? false;            // Check if file is locked (simple check - in real implementation might be more sophisticated)
            if (IsFileLocked(diskPath))
            {
                statsBuilder.SkippedDueToLock.Add(repoPath);
                return null;
            }// Determine if this is a new file - a file is new if it wasn't in the previous tree
            if (!hadPreviousTreeEntry)
            {                if (size > options.MaxNewFileSize)
                {
                    // Large new file - skip automatic tracking
                    statsBuilder.UntrackedKeptFiles.Add(repoPath);
                    return null;
                }
                if (options.ShouldTrackNewFileMatcher(repoPath))
                {                    // Don't count ignore files as new tracked files when nested ignore support is enabled
                    if (!IsIgnoreFile(repoPath, options))
                    {
                        statsBuilder.NewFilesTracked.Add(repoPath);
                    }
                }                else
                {
                    statsBuilder.UntrackedKeptFiles.Add(repoPath);
                    return null;
                }
            }            // File was in previous tree - check if it was modified
            else
            {                // Try to get previous file state for comparison
                if (previousFileStates.TryGetValue(repoPath, out var previousState))
                {
                    // Module 7.4: Check for conflict resolution detection
                    if (previousState.ActiveConflictId.HasValue)
                    {
                        // This file has an active conflict - any edit is considered a resolution
                        _logger.LogDebug("Detected conflict resolution for file {RepoPath} with conflict {ConflictId}", 
                            repoPath, previousState.ActiveConflictId.Value.ToShortHexString());
                          // Read the on-disk content as the resolved version
                        var resolvedContent = await ReadFileOrSymlinkTargetOptimizedAsync(repoPath, diskPath, size, options);
                        var resolvedFileContentData = new FileContentData(resolvedContent);
                        var resolvedContentId = options.DryRun 
                            ? ObjectHasher.ComputeFileContentId(resolvedFileContentData)
                            : await _objectStore.WriteFileContentAsync(resolvedFileContentData);
                          // Create a new FileState with ActiveConflictId = null (conflict resolved)
                        var resolvedFileState = new FileState(fileType, mTime, size, isPlaceholder: false, activeConflictId: null);
                        _fileStates[repoPath] = resolvedFileState;
                        
                        statsBuilder.ModifiedFiles.Add(repoPath);
                        _logger.LogDebug("Conflict resolved for file {RepoPath}, created content {ContentId}", 
                            repoPath, resolvedContentId.ToShortHexString());
                        
                        return resolvedContentId;
                    }
                      var isClean = await IsFileCleanWithHashFallbackAsync(repoPath, diskPath, currentFileState, previousState, options, previousTreeEntries);
                    if (!isClean)
                    {
                        statsBuilder.ModifiedFiles.Add(repoPath);
                    }
                }

                // File was in tree but we don't have previous file state - treat as clean
                // This can happen when _fileStates wasn't properly populated from the tree
                else
                {
                }
            }            // Update file state tracking (preserve any existing ActiveConflictId if not resolved)
            // Only update file states when not in dry-run mode to avoid affecting subsequent status checks
            if (!options.DryRun)
            {
                var preservedActiveConflictId = previousFileStates.TryGetValue(repoPath, out var existingState) 
                    ? existingState.ActiveConflictId 
                    : null;
                _fileStates[repoPath] = new FileState(fileType, mTime, size, isPlaceholder: false, activeConflictId: preservedActiveConflictId);
            }// Read and store file content with large file optimization
            var content = await ReadFileOrSymlinkTargetOptimizedAsync(repoPath, diskPath, size, options);
            var fileContentData = new FileContentData(content);
            var contentId = options.DryRun
                ? ObjectHasher.ComputeFileContentId(fileContentData)
                : await _objectStore.WriteFileContentAsync(fileContentData);

            return contentId;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or
                                        System.IO.FileNotFoundException or
                                        System.IO.IOException)
        {
            Console.WriteLine($"Warning: Could not process file {diskPath}: {ex.Message}");
            return null;
        }
    }    /// <summary>
         /// Reads file content or symlink target, handling different file types appropriately.
         /// </summary>
    private async Task<byte[]> ReadFileOrSymlinkTargetAsync(RepoPath repoPath, string diskPath)
    {
        if (_fileStates.TryGetValue(repoPath, out var fileState) && fileState.Type == FileType.Symlink)
        {
            // For symlinks, store the target path as content
            var target = _fileSystem.FileInfo.New(diskPath).LinkTarget ?? "";
            return System.Text.Encoding.UTF8.GetBytes(target);
        }
        else
        {
            // For normal files, read the file content
            return await _fileSystem.File.ReadAllBytesAsync(diskPath);
        }
    }

    /// <summary>
    /// Optimized file reading with streaming support for large files.
    /// </summary>
    private async Task<byte[]> ReadFileOrSymlinkTargetOptimizedAsync(RepoPath repoPath, string diskPath, long fileSize, SnapshotOptions options)
    {
        if (_fileStates.TryGetValue(repoPath, out var fileState) && fileState.Type == FileType.Symlink)
        {
            // For symlinks, store the target path as content
            var target = _fileSystem.FileInfo.New(diskPath).LinkTarget ?? "";
            return System.Text.Encoding.UTF8.GetBytes(target);
        }
        else if (fileSize > options.LargeFileThreshold)
        {
            // Use streaming for large files to avoid memory pressure
            using var stream = _fileSystem.FileStream.New(diskPath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
            using var memoryStream = new System.IO.MemoryStream();

            var buffer = new byte[64 * 1024]; // 64KB chunks
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await memoryStream.WriteAsync(buffer, 0, bytesRead);
            }

            return memoryStream.ToArray();
        }
        else
        {
            // For normal-sized files, read the file content directly
            return await _fileSystem.File.ReadAllBytesAsync(diskPath);
        }
    }    /// <summary>
         /// Enhanced file cleanliness check with hash-based verification for files modified within mtime granularity window.
         /// </summary>
    private async Task<bool> IsFileCleanWithHashFallbackAsync(RepoPath repoPath, string diskPath, FileState currentState, FileState previousState, SnapshotOptions options, Dictionary<RepoPath, TreeEntry>? previousTreeEntries)
    {
        // First, do the basic metadata check
        if (currentState.Size != previousState.Size || currentState.Type != previousState.Type)
        {
            return false;
        }

        var timeDifferenceMs = Math.Abs((currentState.MTimeUtc - previousState.MTimeUtc).TotalMilliseconds);        // If times are exactly equal, file is clean (common case for unchanged files)
        if (timeDifferenceMs == 0)
        {
            return true;
        }

        // If the modification time difference is within granularity window, do hash verification
        if (timeDifferenceMs <= options.MtimeGranularityMs)
        {
            try
            {
                // Read current file content and compute hash
                var currentContent = await ReadFileOrSymlinkTargetOptimizedAsync(repoPath, diskPath, currentState.Size, options);
                var currentHash = ComputeContentHash(currentContent);

                // For this simplified implementation, we need to compare against previous content
                // In a real implementation, we'd cache content hashes in FileState
                // For now, we'll re-read the previous content from the object store if we can find it
                if (previousTreeEntries?.TryGetValue(repoPath, out var previousEntry) == true)
                {
                    var previousContentData = await _objectStore.ReadFileContentAsync(ToFileContentId(previousEntry.ObjectId));
                    if (previousContentData.HasValue)
                    {
                        var previousHash = ComputeContentHash(previousContentData.Value.Content.ToArray());
                        return currentHash == previousHash;
                    }
                }

                // If we can't get previous content, fall back to assuming it changed
                return false;
            }
            catch
            {
                // If we can't read the file or compute hash, assume it changed
                return false;
            }
        }

        // Use regular metadata-based check
        return currentState.IsClean(previousState);
    }

    /// <summary>
    /// Computes a content hash for change detection purposes.
    /// </summary>
    private string ComputeContentHash(byte[] content)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hashBytes = sha1.ComputeHash(content);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Simple check to determine if a file is locked or in use.
    /// In a real implementation, this would be more sophisticated.
    /// </summary>
    private bool IsFileLocked(string diskPath)
    {
        try
        {
            using var stream = _fileSystem.FileStream.New(diskPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None);
            return false;
        }
        catch (System.IO.IOException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }    /// <summary>
         /// Checks if a path should be ignored as VCS metadata.
         /// </summary>
    private bool IsVcsMetadataPath(RepoPath repoPath)
    {
        // Ignore common VCS metadata directories
        if (repoPath.Components.Length > 0)
        {
            var name = repoPath.Components[0].Value;
            return name == ".vcs" || name == ".git" || name == ".hg" || name == ".svn" || name == ".hpd";
        }
        return false;
    }

    /// <summary>
    /// Loads nested ignore rules for a directory, merging with parent rules.
    /// </summary>
    private async Task<IgnoreFile> LoadNestedIgnoreRulesAsync(RepoPath repoDir, string diskPath, IgnoreFile parentRules)
    {
        var allRules = new List<IgnoreRule>(parentRules.Rules);

        // Check for .gitignore or .hpdignore in this directory
        var ignoreFileNames = new[] { ".gitignore", ".hpdignore" };

        foreach (var ignoreFileName in ignoreFileNames)
        {
            var ignoreFilePath = _fileSystem.Path.Combine(diskPath, ignoreFileName);

            if (_fileSystem.File.Exists(ignoreFilePath))
            {
                try
                {
                    var lines = await _fileSystem.File.ReadAllLinesAsync(ignoreFilePath);

                    foreach (var line in lines)
                    {
                        var trimmedLine = line.Trim();

                        // Skip empty lines and comments
                        if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith('#'))
                            continue;

                        // Create ignore rule relative to this directory
                        var rule = new IgnoreRule(trimmedLine, repoDir);
                        allRules.Add(rule);
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or
                                                System.IO.FileNotFoundException or
                                                System.IO.IOException)
                {
                    Console.WriteLine($"Warning: Could not read ignore file {ignoreFilePath}: {ex.Message}");
                }
            }
        }

        return new IgnoreFile(allRules);
    }

    /// <summary>
    /// Safely converts a TreeEntry ObjectId to a strongly-typed TreeId.
    /// Provides type safety for tree ID conversions.
    /// </summary>
    private static TreeId ToTreeId(ObjectIdBase objectId)
    {
        return new TreeId(objectId.HashValue.ToArray());
    }

    /// <summary>
    /// Safely converts a TreeEntry ObjectId to a strongly-typed FileContentId.
    /// Provides type safety for file content ID conversions.
    /// </summary>
    private static FileContentId ToFileContentId(ObjectIdBase objectId)
    {
        return new FileContentId(objectId.HashValue.ToArray());
    }

    /// <summary>
    /// Safely converts a TreeEntry ObjectId to a strongly-typed ConflictId.
    /// Provides type safety for conflict ID conversions.
    /// </summary>
    private static ConflictId ToConflictId(ObjectIdBase objectId)
    {
        return new ConflictId(objectId.HashValue.ToArray());
    }

    /// <summary>
    /// Safely converts a strongly-typed ID to ObjectIdBase for tree entries.
    /// Provides type safety for reverse conversions.
    /// </summary>
    private static ObjectIdBase ToObjectIdBase<T>(T typedId) where T : IObjectId
    {
        return new ObjectIdBase(typedId.HashValue.ToArray());
    }



    public async Task UpdateCurrentTreeIdAsync(TreeId newTreeId)
    {
        _currentTreeId = newTreeId;
        // Populate _fileStates to match the tree, creating placeholders for missing files
        _fileStates.Clear();
        await PopulateFileStatesFromTreeAsync(newTreeId, RepoPath.Root);

    }

    /// <summary>
    /// Populates _fileStates to match the given tree, recursing into subdirectories.
    /// For files missing on disk, creates a placeholder FileState.
    /// </summary>
    private async Task PopulateFileStatesFromTreeAsync(TreeId treeId, RepoPath currentPath)
    {
        var treeData = await _objectStore.ReadTreeAsync(treeId);
        if (!treeData.HasValue)
        {
            _logger.LogDebug("No tree data found for TreeId {TreeId} at path {Path}", treeId, currentPath);
            return;
        }

        _logger.LogDebug("Processing tree {TreeId} at path {Path} with {EntryCount} entries", treeId, currentPath, treeData.Value.Entries.Count);

        foreach (var entry in treeData.Value.Entries)
        {
            var entryPath = currentPath.IsRoot ?
                new RepoPath(entry.Name) :
                new RepoPath(currentPath.Components.Append(entry.Name));

            _logger.LogDebug("Processing entry {EntryName} at path {EntryPath}", entry.Name.Value, entryPath);

            if (entry.Type == TreeEntryType.Directory)
            {
                var subTreeId = ToTreeId(entry.ObjectId);
                await PopulateFileStatesFromTreeAsync(subTreeId, entryPath);
            }
            else if (entry.Type == TreeEntryType.File)
            {
                var diskPath = GetDiskPath(entryPath);
                _logger.LogDebug("File entry {EntryPath} -> disk path {DiskPath}", entryPath, diskPath);
                _logger.LogDebug("File exists: {Exists}", _fileSystem.File.Exists(diskPath));

                FileState fileState;
                if (_fileSystem.File.Exists(diskPath))
                {
                    var fileInfo = _fileSystem.FileInfo.New(diskPath);
                    var fileType = DetermineFileType(diskPath);
                    var mTime = fileInfo.LastWriteTimeUtc;
                    var size = fileType == FileType.Symlink ? GetSymlinkTargetSize(diskPath) : fileInfo.Length;
                    fileState = new FileState(fileType, mTime, size, isPlaceholder: false);
                    _logger.LogDebug("Created normal FileState for {EntryPath}: type={FileType}, size={Size}", entryPath, fileType, size);
                }
                else
                {
                    // Use explicit placeholder
                    fileState = FileState.Placeholder();
                    _logger.LogDebug("Created placeholder FileState for {EntryPath}", entryPath);
                }
                _fileStates[entryPath] = fileState;
                _logger.LogDebug("Added FileState to _fileStates. Total count: {Count}", _fileStates.Count);
                _logger.LogDebug("Key added to dictionary: '{EntryPath}' (Components: [{Components}])",
                    entryPath,
                    string.Join(", ", entryPath.Components.Select(c => $"'{c.Value}'")));
            }
        }
        _logger.LogDebug("Final _fileStates contents:");
        foreach (var kvp in _fileStates)
        {
            _logger.LogDebug("  Key: '{Key}' (Components: [{Components}])",
                kvp.Key,
                string.Join(", ", kvp.Key.Components.Select(c => $"'{c.Value}'")));
        }
    }    /// <summary>
         /// Determines if the given path represents an ignore file that should be excluded from tracking statistics.
         /// </summary>
         /// <param name="repoPath">The repository path to check</param>
         /// <param name="options">The snapshot options</param>
         /// <returns>True if this is an ignore file and nested ignore support is enabled</returns>
    private static bool IsIgnoreFile(RepoPath repoPath, SnapshotOptions options)
    {
        if (!options.SupportNestedIgnoreFiles)
            return false;

        if (repoPath.Components.Length == 0)
            return false;

        var fileName = repoPath.Components[repoPath.Components.Length - 1].Value;
        return string.Equals(fileName, ".gitignore", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, ".hpdignore", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Replaces the internal tracked file states dictionary with the provided new states.
    /// This is called after a successful checkout has materialized files, using FileState objects 
    /// created from the on-disk mtime/size of those newly written/updated files.
    /// </summary>
    /// <param name="newStates">The new file states to replace the current tracked states</param>
    public void ReplaceTrackedFileStates(Dictionary<RepoPath, FileState> newStates)
    {
        ArgumentNullException.ThrowIfNull(newStates);

        _fileStates.Clear();
        foreach (var kvp in newStates)
        {
            _fileStates[kvp.Key] = kvp.Value;
        }
    }    /// <summary>
    /// Checks out the specified tree to the working directory, updating file states accordingly.
    /// This operation updates the physical working directory files and manages tracked file states post-checkout.
    /// </summary>
    /// <param name="targetTreeId">The TreeId of the commit/tree to check out</param>
    /// <param name="options">Options controlling checkout behavior</param>
    /// <returns>Statistics about the checkout operation</returns>
    public async Task<CheckoutStats> CheckoutAsync(TreeId targetTreeId, CheckoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _logger.LogDebug("CheckoutAsync - targetTreeId: {TargetTreeId}", targetTreeId);

        // Step 1: Read target tree data
        TreeData? targetTreeData = await _objectStore.ReadTreeAsync(targetTreeId);
        _logger.LogDebug("Target tree data - entries count: {Count}", targetTreeData?.Entries.Count ?? 0);

        // Step 2: Read current working copy tree data
        TreeData? currentWcTreeData = null;
        if (_currentTreeId.HasValue)
        {
            currentWcTreeData = await _objectStore.ReadTreeAsync(_currentTreeId.Value);
            _logger.LogDebug("Current WC tree data - entries count: {Count}", currentWcTreeData?.Entries.Count ?? 0);
        }
        else
        {
            _logger.LogDebug("No current tree ID set");
        }

        // Step 3: Initialize statistics builder
        var statsBuilder = new CheckoutStatsBuilder();

        // Step 4: Prepare new tracked file states for post-checkout
        var newTrackedFileStatesForPostCheckout = new Dictionary<RepoPath, FileState>();

        // Step 5: Call recursive helper to update directory
        await UpdateDirectoryRecursiveAsync(
            RepoPath.Root,
            _workingCopyPath,
            currentWcTreeData,
            targetTreeData,
            options,
            statsBuilder,
            newTrackedFileStatesForPostCheckout);        // Step 6: Handle skipped files
        // For V1, we don't treat skipped files as a fatal error
        // The caller can check the returned stats to see if there were conflicts
        // Only critical errors during the checkout process should cause exceptions
        
        // Step 7: Update tracked file states and current tree ID
        // We update state even if some files were skipped, as the checkout partially succeeded
        ReplaceTrackedFileStates(newTrackedFileStatesForPostCheckout);
        _currentTreeId = targetTreeId;

        // Step 8: Return statistics
        return statsBuilder.ToImmutableStats();
    }    /// <summary>
    /// Recursively updates a directory during checkout, handling additions, deletions, and modifications.
    /// </summary>
    private async Task UpdateDirectoryRecursiveAsync(
        RepoPath currentRepoDir,
        string currentDiskDir,
        TreeData? oldDirTreeData,
        TreeData? newDirTreeData,
        CheckoutOptions options,
        CheckoutStatsBuilder stats,
        Dictionary<RepoPath, FileState> newTrackedFileStates)
    {
        _logger.LogDebug("UpdateDirectoryRecursiveAsync - currentRepoDir: {RepoDir}, currentDiskDir: {DiskDir}", currentRepoDir, currentDiskDir);
        _logger.LogDebug("Old entries: {OldCount}, New entries: {NewCount}", oldDirTreeData?.Entries.Count ?? 0, newDirTreeData?.Entries.Count ?? 0);

        // Step 1: Create directory if it doesn't exist and new tree has entries
        if (newDirTreeData?.Entries.Count > 0 && !_fileSystem.Directory.Exists(currentDiskDir))
        {
            _fileSystem.Directory.CreateDirectory(currentDiskDir);
        }

        // Step 2: Get entry lists
        var oldEntries = oldDirTreeData?.Entries ?? Enumerable.Empty<TreeEntry>();
        var newEntries = newDirTreeData?.Entries ?? Enumerable.Empty<TreeEntry>();        // Step 3: Create dictionaries for efficient lookup
        var oldEntriesDict = oldEntries.ToDictionary(e => e.Name.Value, e => e);
        var newEntriesDict = newEntries.ToDictionary(e => e.Name.Value, e => e);

        // Get all entry names (union of old, new, and existing files on disk)
        var allEntryNames = oldEntriesDict.Keys.Union(newEntriesDict.Keys).ToHashSet();
        
        // Also include files that exist in the working directory but aren't in either tree
        // This ensures we properly delete files that should no longer exist after checkout
        if (_fileSystem.Directory.Exists(currentDiskDir))
        {
            try
            {
                var existingFiles = _fileSystem.Directory.EnumerateFileSystemEntries(currentDiskDir, "*", SearchOption.TopDirectoryOnly)
                    .Select(path => _fileSystem.Path.GetFileName(path))
                    .Where(name => !string.IsNullOrEmpty(name));
                
                foreach (var fileName in existingFiles)
                {
                    allEntryNames.Add(fileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enumerate existing files in directory: {Directory}", currentDiskDir);
            }
        }// Step 4: Process each entry
        foreach (var entryName in allEntryNames)
        {
            _logger.LogDebug("Processing entry: {EntryName}", entryName);
            var hasOldEntry = oldEntriesDict.TryGetValue(entryName, out var oldEntry);
            var hasNewEntry = newEntriesDict.TryGetValue(entryName, out var newEntry);
            _logger.LogDebug("hasOldEntry: {HasOld}, hasNewEntry: {HasNew}", hasOldEntry, hasNewEntry);

            var entryRepoPath = currentRepoDir.IsRoot ?
                new RepoPath(new RepoPathComponent(entryName)) :
                new RepoPath(currentRepoDir.Components.Append(new RepoPathComponent(entryName)));

            var entryDiskPath = _fileSystem.Path.Combine(currentDiskDir, entryName);
            _logger.LogDebug("entryRepoPath: {RepoPath}, entryDiskPath: {DiskPath}", entryRepoPath, entryDiskPath);

            if (!hasOldEntry && hasNewEntry)
            {
                // Addition: entry present only in new tree
                await HandleAdditionAsync(entryRepoPath, entryDiskPath, newEntry!, options, stats, newTrackedFileStates);
            }
            else if (hasOldEntry && !hasNewEntry)
            {
                // Deletion: entry present only in old tree
                await HandleDeletionAsync(entryRepoPath, entryDiskPath, oldEntry!, stats);
            }
            else if (hasOldEntry && hasNewEntry)
            {
                // Potential modification: entry present in both trees
                await HandleModificationAsync(entryRepoPath, entryDiskPath, oldEntry!, newEntry!, options, stats, newTrackedFileStates);
            }
        }

        // Step 5: Clean up empty directory if it has no entries in new tree
        if ((newDirTreeData?.Entries.Count ?? 0) == 0 && _fileSystem.Directory.Exists(currentDiskDir) && !currentRepoDir.IsRoot)
        {
            try
            {
                // Only delete if empty
                if (!_fileSystem.Directory.EnumerateFileSystemEntries(currentDiskDir).Any())
                {
                    _fileSystem.Directory.Delete(currentDiskDir);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore failures to delete directories - they might have untracked files
            }
        }
    }    /// <summary>
    /// Handles addition of a new entry during checkout.
    /// </summary>
    private async Task HandleAdditionAsync(
        RepoPath entryRepoPath,
        string entryDiskPath,
        TreeEntry newEntry,
        CheckoutOptions options,
        CheckoutStatsBuilder stats,
        Dictionary<RepoPath, FileState> newTrackedFileStates)
    {
        // Check for untracked conflicts: if the path exists on disk but was not tracked
        // in the old tree, we should skip it to avoid overwriting untracked content
        bool hasUntrackedConflict = _fileSystem.File.Exists(entryDiskPath) || _fileSystem.Directory.Exists(entryDiskPath);
        
        if (hasUntrackedConflict)
        {
            // Skip this entry due to untracked conflict
            _logger.LogDebug("Skipping addition of {RepoPath} due to untracked conflict at {DiskPath}", entryRepoPath, entryDiskPath);
            stats.FilesSkipped++;
            return;
        }
        if (newEntry.Type == TreeEntryType.Directory)
        {
            // Create directory and recurse
            _fileSystem.Directory.CreateDirectory(entryDiskPath);

            var subTreeId = ToTreeId(newEntry.ObjectId);
            var subTreeData = await _objectStore.ReadTreeAsync(subTreeId);

            await UpdateDirectoryRecursiveAsync(
                entryRepoPath,
                entryDiskPath,
                null,
                subTreeData,
                options,
                stats,
                newTrackedFileStates);

            // For directories, we don't typically track them as individual file states
            // since they are implicitly created when files are added within them
        }        else if (newEntry.Type == TreeEntryType.File)
        {
            // Materialize file content
            var fileContentId = ToFileContentId(newEntry.ObjectId);
            var fileContentData = await _objectStore.ReadFileContentAsync(fileContentId);

            if (!fileContentData.HasValue)
            {
                stats.FilesSkipped++;
                return;
            }

            // Write file atomically
            await WriteFileAtomicallyAsync(entryDiskPath, fileContentData.Value.Content.ToArray());

            // Create file state from newly written file
            var fileInfo = _fileSystem.FileInfo.New(entryDiskPath);
            var fileState = new FileState(FileType.NormalFile, fileInfo.LastWriteTimeUtc, fileInfo.Length);
            newTrackedFileStates[entryRepoPath] = fileState;

            stats.FilesAdded++;
        }        else if (newEntry.Type == TreeEntryType.Conflict)
        {
            // Materialize conflict to disk with conflict markers
            var conflictId = ToConflictId(newEntry.ObjectId);
            await MaterializeConflictAsync(entryDiskPath, conflictId, options, stats);

            // Create file state with ActiveConflictId to track this as a materialized conflict
            var fileInfo = _fileSystem.FileInfo.New(entryDiskPath);
            var fileState = new FileState(FileType.NormalFile, fileInfo.LastWriteTimeUtc, fileInfo.Length, false, conflictId);
            newTrackedFileStates[entryRepoPath] = fileState;

            _logger.LogDebug("Materialized conflict {ConflictId} at {DiskPath} with ActiveConflictId", conflictId, entryDiskPath);
        }
        // Note: Symlink handling would go here in a more complete implementation
        // For V1, we're focusing on regular files and directories
    }    /// <summary>
         /// Handles deletion of an entry during checkout.
         /// </summary>
    private Task HandleDeletionAsync(
        RepoPath entryRepoPath,
        string entryDiskPath,
        TreeEntry oldEntry,
        CheckoutStatsBuilder stats)
    {
        if (oldEntry.Type == TreeEntryType.Directory)
        {
            // Recursively delete directory contents first
            if (_fileSystem.Directory.Exists(entryDiskPath))
            {
                try
                {
                    _fileSystem.Directory.Delete(entryDiskPath, recursive: true);
                    stats.FilesRemoved++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // If we can't delete the directory, mark as skipped
                    stats.FilesSkipped++;
                }
            }
        }
        else if (oldEntry.Type == TreeEntryType.File)
        {
            // Delete file
            if (_fileSystem.File.Exists(entryDiskPath))
            {
                try
                {
                    _fileSystem.File.Delete(entryDiskPath);
                    stats.FilesRemoved++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // If we can't delete the file, mark as skipped
                    stats.FilesSkipped++;
                }
            }
        }

        return Task.CompletedTask;
    }    /// <summary>
    /// Handles modification of an entry during checkout.
    /// </summary>
    private async Task HandleModificationAsync(
        RepoPath entryRepoPath,
        string entryDiskPath,
        TreeEntry oldEntry,
        TreeEntry newEntry,
        CheckoutOptions options,
        CheckoutStatsBuilder stats,
        Dictionary<RepoPath, FileState> newTrackedFileStates)
    {
        _logger.LogDebug("HandleModificationAsync - {RepoPath}", entryRepoPath);
        _logger.LogDebug("Old ObjectId: {OldObj}, New ObjectId: {NewObj}", oldEntry.ObjectId, newEntry.ObjectId);
        _logger.LogDebug("Old Type: {OldType}, New Type: {NewType}", oldEntry.Type, newEntry.Type);
        _logger.LogDebug("ObjectIds equal: {Equal}", newEntry.ObjectId.Equals(oldEntry.ObjectId));

        // If same object ID and type, no change needed
        if (newEntry.ObjectId.Equals(oldEntry.ObjectId) && newEntry.Type == oldEntry.Type)
        {
            _logger.LogDebug("No change needed for {RepoPath} - ObjectIds and types are equal", entryRepoPath);
            if (newEntry.Type == TreeEntryType.Directory)
            {
                // Still need to recurse for directory to handle potential changes inside
                var subTreeId = ToTreeId(newEntry.ObjectId);
                var oldSubTreeId = ToTreeId(oldEntry.ObjectId);
                var newSubTreeData = await _objectStore.ReadTreeAsync(subTreeId);
                var oldSubTreeData = await _objectStore.ReadTreeAsync(oldSubTreeId);

                await UpdateDirectoryRecursiveAsync(
                    entryRepoPath,
                    entryDiskPath,
                    oldSubTreeData,
                    newSubTreeData,
                    options,
                    stats, newTrackedFileStates);

                // Directories are not tracked as FileState objects - only files are tracked
            }            else if (newEntry.Type == TreeEntryType.File)
            {
                // File unchanged, just record existing state
                if (_fileSystem.File.Exists(entryDiskPath))
                {
                    var fileInfo = _fileSystem.FileInfo.New(entryDiskPath);
                    var fileState = new FileState(FileType.NormalFile, fileInfo.LastWriteTimeUtc, fileInfo.Length);
                    newTrackedFileStates[entryRepoPath] = fileState;
                }
            }
            else if (newEntry.Type == TreeEntryType.Conflict)
            {
                // Conflict unchanged, preserve existing state with ActiveConflictId
                if (_fileSystem.File.Exists(entryDiskPath))
                {
                    var conflictId = ToConflictId(newEntry.ObjectId);
                    var fileInfo = _fileSystem.FileInfo.New(entryDiskPath);
                    var fileState = new FileState(FileType.NormalFile, fileInfo.LastWriteTimeUtc, fileInfo.Length, false, conflictId);
                    newTrackedFileStates[entryRepoPath] = fileState;
                    
                    _logger.LogDebug("Preserving unchanged conflict {ConflictId} at {RepoPath}", conflictId, entryRepoPath);
                }
            }
            return;
        }        // Handle type changes (e.g., file -> directory, directory -> file)
        if (oldEntry.Type != newEntry.Type)
        {
            // Delete old first
            await HandleDeletionAsync(entryRepoPath, entryDiskPath, oldEntry, stats);
            // Then add new
            await HandleAdditionAsync(entryRepoPath, entryDiskPath, newEntry, options, stats, newTrackedFileStates);
            return;
        }        // Same type but different content
        if (newEntry.Type == TreeEntryType.File && oldEntry.Type == TreeEntryType.File)
        {            // Update file content
            var fileContentId = ToFileContentId(newEntry.ObjectId);
            _logger.LogDebug("Trying to read file content for {RepoPath}, FileContentId: {FileContentId}", entryRepoPath, fileContentId);
            var fileContentData = await _objectStore.ReadFileContentAsync(fileContentId);
            _logger.LogDebug("File content data HasValue: {HasValue}", fileContentData.HasValue);

            if (!fileContentData.HasValue)
            {
                _logger.LogDebug("File content not found, skipping {RepoPath}", entryRepoPath);
                stats.FilesSkipped++;
                return;
            }

            // Write file atomically
            var contentToWrite = fileContentData.Value.Content.ToArray();
            string preview = System.Text.Encoding.UTF8.GetString(contentToWrite).Length > 50
                ? System.Text.Encoding.UTF8.GetString(contentToWrite).Substring(0, 50)
                : System.Text.Encoding.UTF8.GetString(contentToWrite);
            _logger.LogDebug("Writing file {DiskPath}, content length: {Length}, first 50 chars: {Preview}", entryDiskPath, contentToWrite.Length, preview);
            await WriteFileAtomicallyAsync(entryDiskPath, contentToWrite);

            // Create file state from updated file
            var fileInfo = _fileSystem.FileInfo.New(entryDiskPath);
            var fileState = new FileState(FileType.NormalFile, fileInfo.LastWriteTimeUtc, fileInfo.Length);
            newTrackedFileStates[entryRepoPath] = fileState;

            stats.FilesUpdated++;
        }
        else if (newEntry.Type == TreeEntryType.Conflict && oldEntry.Type == TreeEntryType.File)
        {
            // File becoming a conflict - materialize conflict markers
            var conflictId = ToConflictId(newEntry.ObjectId);
            await MaterializeConflictAsync(entryDiskPath, conflictId, options, stats);

            // Create file state with ActiveConflictId to track this as a materialized conflict
            var fileInfo = _fileSystem.FileInfo.New(entryDiskPath);
            var fileState = new FileState(FileType.NormalFile, fileInfo.LastWriteTimeUtc, fileInfo.Length, false, conflictId);
            newTrackedFileStates[entryRepoPath] = fileState;

            _logger.LogDebug("File {RepoPath} became conflict {ConflictId} with ActiveConflictId", entryRepoPath, conflictId);
        }
        else if (newEntry.Type == TreeEntryType.Conflict && oldEntry.Type == TreeEntryType.Conflict)
        {
            // Conflict to different conflict - update conflict markers
            var newConflictId = ToConflictId(newEntry.ObjectId);
            var oldConflictId = ToConflictId(oldEntry.ObjectId);
            
            if (!newConflictId.Equals(oldConflictId))
            {
                await MaterializeConflictAsync(entryDiskPath, newConflictId, options, stats);

                // Create file state with new ActiveConflictId
                var fileInfo = _fileSystem.FileInfo.New(entryDiskPath);
                var fileState = new FileState(FileType.NormalFile, fileInfo.LastWriteTimeUtc, fileInfo.Length, false, newConflictId);
                newTrackedFileStates[entryRepoPath] = fileState;

                _logger.LogDebug("Conflict {RepoPath} updated from {OldConflictId} to {NewConflictId}", entryRepoPath, oldConflictId, newConflictId);
            }
            else
            {
                // Same conflict ID, just preserve existing state
                if (_fileSystem.File.Exists(entryDiskPath))
                {
                    var fileInfo = _fileSystem.FileInfo.New(entryDiskPath);
                    var fileState = new FileState(FileType.NormalFile, fileInfo.LastWriteTimeUtc, fileInfo.Length, false, oldConflictId);
                    newTrackedFileStates[entryRepoPath] = fileState;
                }
            }
        }
        else if (newEntry.Type == TreeEntryType.Directory && oldEntry.Type == TreeEntryType.Directory)
        {
            // Directory content changed - recurse
            var newSubTreeId = ToTreeId(newEntry.ObjectId);
            var oldSubTreeId = ToTreeId(oldEntry.ObjectId);
            var newSubTreeData = await _objectStore.ReadTreeAsync(newSubTreeId);
            var oldSubTreeData = await _objectStore.ReadTreeAsync(oldSubTreeId);

            await UpdateDirectoryRecursiveAsync(
                entryRepoPath,
                entryDiskPath,
                oldSubTreeData,
                newSubTreeData,
                options,
                stats, newTrackedFileStates);

            // Directories are not tracked as FileState objects - only files are tracked
        }
    }    /// <summary>
    /// Writes file content atomically using a temporary file and atomic move.
    /// </summary>
    private async Task WriteFileAtomicallyAsync(string targetPath, byte[] content)
    {
        var tempPath = targetPath + ".tmp." + Guid.NewGuid().ToString("N")[..8];
        _logger.LogDebug("WriteFileAtomicallyAsync - target: {Target}, temp: {Temp}, content length: {Length}", targetPath, tempPath, content.Length);

        try
        {
            // Write to temporary file first
            await _fileSystem.File.WriteAllBytesAsync(tempPath, content);
            _logger.LogDebug("Wrote to temp file: {Temp}", tempPath);

            // Atomic move to final location, overwriting if exists
            _fileSystem.File.Move(tempPath, targetPath, overwrite: true);
            _logger.LogDebug("Moved to final location: {Target}", targetPath);
        }
        catch
        {
            // Clean up temp file if something went wrong
            if (_fileSystem.File.Exists(tempPath))
            {
                try
                {
                    _fileSystem.File.Delete(tempPath);
                }
                catch
                {
                    // Ignore cleanup failures
                }
            }
            throw;
        }
    }

    /// <summary>
    /// Materializes a conflict to disk by creating conflict markers in the file content.
    /// This creates a merged file with conflict markers showing the different versions.
    /// </summary>
    private async Task MaterializeConflictAsync(
        string targetPath,
        ConflictId conflictId,
        CheckoutOptions options,
        CheckoutStatsBuilder stats)
    {
        _logger.LogDebug("MaterializeConflictAsync - target: {Target}, conflict: {ConflictId}", targetPath, conflictId);

        try
        {
            // Read conflict data from object store
            var conflictData = await _objectStore.ReadConflictAsync(conflictId);
            if (!conflictData.HasValue)
            {
                _logger.LogWarning("Could not read conflict data for {ConflictId}", conflictId);
                stats.FilesSkipped++;
                return;
            }            var conflict = conflictData.Value;
            _logger.LogDebug("Conflict has {Count} adds, removes base: {HasBase}", 
                conflict.ConflictedMerge.Adds.Count, conflict.ConflictedMerge.Removes.Any());            // Check if this is a binary file by examining the content of the adds
            // For simplicity, we'll check the first few bytes of each version
            bool isBinaryFile = false;
            foreach (var add in conflict.ConflictedMerge.Adds)
            {
                if (add.HasValue)
                {
                    var addContentData = await _objectStore.ReadFileContentAsync(ToFileContentId(add.Value.ObjectId));
                    if (addContentData.HasValue && IsBinaryContent(addContentData.Value.Content.ToArray().AsSpan()))
                    {
                        isBinaryFile = true;
                        break;
                    }
                }
            }byte[] materializedContent;
            if (isBinaryFile)
            {
                // For binary files, we can't create meaningful text conflict markers
                // Instead, use the first available version or create a placeholder
                _logger.LogDebug("Binary file conflict detected, using first available version");
                
                if (conflict.ConflictedMerge.Adds.Any(add => add.HasValue))
                {
                    var firstAdd = conflict.ConflictedMerge.Adds.First(add => add.HasValue)!.Value;
                    var contentData = await _objectStore.ReadFileContentAsync(ToFileContentId(firstAdd.ObjectId));
                    materializedContent = contentData.HasValue ? contentData.Value.Content.ToArray() : 
                        System.Text.Encoding.UTF8.GetBytes($"<<< BINARY FILE CONFLICT: {conflictId.ToShortHexString()} >>>");
                }
                else
                {
                    materializedContent = System.Text.Encoding.UTF8.GetBytes($"<<< BINARY FILE CONFLICT: {conflictId.ToShortHexString()} >>>");
                }
            }
            else
            {
                // Create text conflict markers
                materializedContent = await CreateConflictMarkersAsync(conflict);
            }

            // Write the materialized conflict to disk
            await WriteFileAtomicallyAsync(targetPath, materializedContent);
            stats.ConflictsMaterialized++;
            
            _logger.LogDebug("Successfully materialized conflict {ConflictId} to {Target}", conflictId, targetPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to materialize conflict {ConflictId} to {Target}", conflictId, targetPath);
            stats.FilesSkipped++;
        }
    }    /// <summary>
    /// Creates conflict markers for text files by merging different versions with conflict syntax.
    /// </summary>
    private async Task<byte[]> CreateConflictMarkersAsync(ConflictData conflict)
    {
        var sb = new System.Text.StringBuilder();
        
        // If there's a base version (common ancestor), show it first
        if (conflict.ConflictedMerge.Removes.Any(remove => remove.HasValue))
        {
            var baseRemove = conflict.ConflictedMerge.Removes.First(remove => remove.HasValue)!.Value;
            var baseContent = await _objectStore.ReadFileContentAsync(ToFileContentId(baseRemove.ObjectId));
            if (baseContent.HasValue)
            {
                sb.AppendLine("<<<<<<< BASE");
                sb.Append(System.Text.Encoding.UTF8.GetString(baseContent.Value.Content.ToArray()));
                if (!sb.ToString().EndsWith("\n"))
                    sb.AppendLine();
                sb.AppendLine("=======");
            }
        }

        // Add each conflicting version
        var addsList = conflict.ConflictedMerge.Adds.Where(add => add.HasValue).Select(add => add!.Value).ToList();
        for (int i = 0; i < addsList.Count; i++)
        {
            var add = addsList[i];
            var addContentData = await _objectStore.ReadFileContentAsync(ToFileContentId(add.ObjectId));
            if (addContentData.HasValue)
            {
                if (i > 0)
                {
                    sb.AppendLine("|||||||");
                }
                
                sb.AppendLine($"<<<<<<< VERSION {i + 1}");
                sb.Append(System.Text.Encoding.UTF8.GetString(addContentData.Value.Content.ToArray()));
                if (!sb.ToString().EndsWith("\n"))
                    sb.AppendLine();
                
                if (i < addsList.Count - 1)
                {
                    sb.AppendLine("=======");
                }
            }
        }
        
        sb.AppendLine(">>>>>>>");

        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Detects if content appears to be binary by checking for null bytes and non-printable characters.
    /// </summary>
    private static bool IsBinaryContent(ReadOnlySpan<byte> content)
    {
        // Check first 8KB or entire content if smaller
        var sampleSize = Math.Min(content.Length, 8192);
        var sample = content[..sampleSize];
        
        // Look for null bytes (strong indicator of binary content)
        for (int i = 0; i < sample.Length; i++)
        {
            if (sample[i] == 0)
                return true;
        }

        // Check ratio of non-printable ASCII characters
        int nonPrintableCount = 0;
        for (int i = 0; i < sample.Length; i++)
        {
            byte b = sample[i];
            // Consider characters outside printable ASCII range (32-126) plus common whitespace (9,10,13)
            if (b < 32 && b != 9 && b != 10 && b != 13)
            {
                nonPrintableCount++;
            }
        }

        // If more than 30% non-printable, consider it binary
        return nonPrintableCount > (sample.Length * 0.3);
    }
}