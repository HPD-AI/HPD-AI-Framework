using System.IO.Abstractions;
using System.Timers;
using HPD.VCS.Core;
using HPD.VCS.Storage;

namespace HPD.VCS.WorkingCopy;

/// <summary>
/// Live working copy implementation that automatically tracks changes using FileSystemWatcher.
/// This implementation continuously monitors the file system and automatically updates the working copy 
/// state through a special amending working copy commit without requiring explicit snapshots.
/// </summary>
public class LiveWorkingCopy : IWorkingCopy, IDisposable
{    private readonly IFileSystem _fileSystem;
    private readonly IObjectStore _objectStore;
    private readonly string _workingCopyPath;
    private readonly Dictionary<RepoPath, FileState> _fileStates;
    private readonly FileSystemWatcher? _watcher;
    private readonly System.Timers.Timer _debounceTimer;
    private readonly object _lockObject = new object();
    
    private TreeId? _currentTreeId;
    private Dictionary<RepoPath, FileContentId>? _baselineFileContentIds; // Cache for baseline file content IDs
    private bool _isDirty = false;
    private bool _disposed = false;
    
    // Event-based communication to avoid circular dependency with Repository
    public event EventHandler<EventArgs>? WorkingCopyChanged;
    
    // Debounce timing configuration
    private const int DebounceDelayMs = 500; // Wait 500ms after last file change    /// <summary>
    /// Initializes a new instance of the LiveWorkingCopy class.
    /// </summary>
    /// <param name="fileSystem">The file system abstraction</param>
    /// <param name="objectStore">The object store for reading tree data</param>
    /// <param name="workingCopyPath">The path to the working copy directory</param>
    public LiveWorkingCopy(IFileSystem fileSystem, IObjectStore objectStore, string workingCopyPath)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _objectStore = objectStore ?? throw new ArgumentNullException(nameof(objectStore));
        _workingCopyPath = workingCopyPath ?? throw new ArgumentNullException(nameof(workingCopyPath));
        _fileStates = new Dictionary<RepoPath, FileState>();
        _currentTreeId = null;
        
        // Initialize FileSystemWatcher only if the path exists as a real directory
        // This allows the class to work with mock file systems in tests
        if (Directory.Exists(_workingCopyPath))
        {
            _watcher = new FileSystemWatcher(_workingCopyPath)
            {
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };
            
            // Hook up FileSystemWatcher events
            _watcher.Changed += OnFileSystemChanged;
            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemRenamed;
        }
        else
        {
            // For testing scenarios or when path doesn't exist, watcher is null
            _watcher = null;
        }
        
        // Exclude .hpd directory from watching
        // Note: FileSystemWatcher doesn't have built-in exclude patterns, so we'll filter in event handlers
        
        // Initialize debounce timer
        _debounceTimer = new System.Timers.Timer(DebounceDelayMs)
        {
            AutoReset = false // One-shot timer
        };
        _debounceTimer.Elapsed += OnDebounceTimerElapsed;
    }

    /// <summary>
    /// Gets the current file states in the working copy.
    /// </summary>
    public IReadOnlyDictionary<RepoPath, FileState> FileStates => _fileStates.AsReadOnly();

    /// <summary>
    /// Gets the path to the working copy directory.
    /// </summary>
    public string WorkingCopyPath => _workingCopyPath;

    /// <summary>
    /// Gets the current tree ID that the working copy is based on.
    /// </summary>
    public TreeId? CurrentTreeId => _currentTreeId;

    /// <summary>
    /// Scans the working copy directory for changes and updates the file states.
    /// In live mode, this automatically tracks all changes without explicit snapshotting.
    /// </summary>
    /// <returns>A task representing the asynchronous scan operation</returns>
    public async Task ScanWorkingCopyAsync()
    {
        // In live mode, always scan for the latest changes
        var scannedFiles = new Dictionary<RepoPath, FileState>();
        
        await ScanDirectoryRecursiveAsync(_workingCopyPath, RepoPath.Root, scannedFiles);
        
        // Update the file states with the latest scan results
        _fileStates.Clear();
        foreach (var kvp in scannedFiles)
        {
            _fileStates[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Creates a snapshot of the current working copy state and stores it in the object store.
    /// This captures all tracked files into immutable TreeData and FileContentData objects.
    /// </summary>
    /// <returns>The TreeId of the root tree representing the working copy snapshot</returns>
    public async Task<TreeId> CreateSnapshotAsync()
    {
        // Always scan for the latest changes in live mode
        await ScanWorkingCopyAsync();
        
        // Create a snapshot with default options
        var defaultOptions = new SnapshotOptions();
        var (treeId, _) = await SnapshotAsync(defaultOptions);
        return treeId;
    }

    /// <summary>
    /// Creates a snapshot of the current working copy state and returns the tree ID.
    /// In live mode, this immediately captures the current state without requiring explicit tracking.
    /// </summary>
    /// <param name="options">Options controlling the snapshot behavior</param>
    /// <returns>A tuple containing the tree ID and snapshot statistics</returns>
    public async Task<(TreeId TreeId, SnapshotStats Stats)> CreateSnapshotAsync(SnapshotOptions options)
    {
        // Always scan for the latest changes in live mode
        await ScanWorkingCopyAsync();
        
        // Create the snapshot with the current state
        return await SnapshotAsync(options);
    }    /// <summary>
    /// Creates a snapshot from the current file states.
    /// In live mode, this reads from the already-up-to-date working copy commit instead of scanning disk.
    /// </summary>
    /// <param name="options">Options controlling the snapshot behavior</param>
    /// <param name="dryRun">If true, does not write new objects to the store, only computes changes</param>
    /// <returns>A tuple containing the new tree ID and snapshot statistics</returns>
    public async Task<(TreeId newSnapshotTreeId, SnapshotStats stats)> SnapshotAsync(SnapshotOptions options, bool dryRun = false)
    {
        // In live mode, we should read from the already-up-to-date working copy commit
        // instead of scanning the disk, as per the specification
        if (_currentTreeId.HasValue)
        {
            // Return the current tree ID since it should already be up-to-date
            // In live mode, the tree is continuously updated by the file watcher
            var emptyStats = new SnapshotStats(
                UntrackedIgnoredFiles: Array.Empty<RepoPath>(),
                UntrackedKeptFiles: Array.Empty<RepoPath>(),
                NewFilesTracked: Array.Empty<RepoPath>(),
                ModifiedFiles: Array.Empty<RepoPath>(),
                DeletedFiles: Array.Empty<RepoPath>(),
                SkippedDueToLock: Array.Empty<RepoPath>()
            );
            return (_currentTreeId.Value, emptyStats);
        }

        // Fallback for test scenarios or when no current tree is available
        Dictionary<RepoPath, FileState>? previousFileStates = null;
        if (_watcher == null)
        {
            // Save current file states before scanning
            previousFileStates = new Dictionary<RepoPath, FileState>(_fileStates);
            await ScanWorkingCopyAsync();
        }
        
        var treeEntries = new List<TreeEntry>();
        var newFilesTracked = new List<RepoPath>();
        var filesModified = new List<RepoPath>();
        var filesRemoved = new List<RepoPath>();

        // Check for deleted files if we have previous state to compare with
        if (previousFileStates != null)
        {
            foreach (var previousPath in previousFileStates.Keys)
            {
                if (!_fileStates.ContainsKey(previousPath))
                {
                    // File was in previous state but not current state - it was deleted
                    filesRemoved.Add(previousPath);
                }
            }
        }        // Process all current file states
        foreach (var kvp in _fileStates)
        {
            var repoPath = kvp.Key;
            var fileState = kvp.Value;
            var fullPath = _fileSystem.Path.Combine(_workingCopyPath, repoPath.ToString());
            
            // Check if file exists on disk
            if (_fileSystem.File.Exists(fullPath))
            {
                // Read file content and create blob
                var content = await _fileSystem.File.ReadAllBytesAsync(fullPath);
                var fileContentData = new FileContentData(content);
                
                FileContentId contentId;
                if (dryRun || options.DryRun)
                {
                    // In dry run mode, compute ID without writing to store
                    contentId = ObjectHasher.ComputeFileContentId(fileContentData);
                }
                else
                {
                    // Write to object store
                    contentId = await _objectStore.WriteFileContentAsync(fileContentData);
                }
                
                // Get the filename component for the tree entry
                var fileName = repoPath.FileName();
                if (fileName != null)
                {
                    treeEntries.Add(new TreeEntry(
                        fileName.Value,
                        TreeEntryType.File,
                        new ObjectIdBase(contentId.HashValue.ToArray())
                    ));
                      // Check if file is actually modified by comparing with baseline
                    var currentFileContentId = ObjectHasher.ComputeFileContentId(fileContentData);
                    var baselineContentId = GetBaselineFileContentId(repoPath);
                    
                    if (baselineContentId == null)
                    {
                        // File doesn't exist in baseline - in live mode, treat as modified
                        filesModified.Add(repoPath);
                    }
                    else if (!currentFileContentId.Equals(baselineContentId.Value))
                    {
                        // File content differs from baseline - it's modified
                        filesModified.Add(repoPath);
                    }
                    // If content IDs match, file is unchanged (don't add to any list)
                }
            }
            else
            {
                // File doesn't exist on disk, consider it removed
                filesRemoved.Add(repoPath);
            }
        }

        // Create the tree data and write it to the object store
        var treeData = new TreeData(treeEntries);
        TreeId treeId;
        
        if (dryRun || options.DryRun)
        {
            // In dry run mode, compute tree ID without writing to store
            treeId = ObjectHasher.ComputeTreeId(treeData);
        }
        else
        {
            // Write to object store
            treeId = await _objectStore.WriteTreeAsync(treeData);
        }

        var stats = new SnapshotStats(
            UntrackedIgnoredFiles: Array.Empty<RepoPath>(),
            UntrackedKeptFiles: Array.Empty<RepoPath>(),
            NewFilesTracked: newFilesTracked.AsReadOnly(),
            ModifiedFiles: filesModified.AsReadOnly(),
            DeletedFiles: filesRemoved.AsReadOnly(),
            SkippedDueToLock: Array.Empty<RepoPath>()
        );

        return (treeId, stats);
    }   
     /// <summary>
    /// Updates the current tree ID that the working copy is based on.
    /// </summary>
    /// <param name="treeId">The new tree ID</param>
    /// <returns>A task representing the asynchronous update operation</returns>
    public async Task UpdateCurrentTreeIdAsync(TreeId treeId)
    {
        _currentTreeId = treeId;

        // Clear existing file states and baseline cache
        _fileStates.Clear();
        _baselineFileContentIds = new Dictionary<RepoPath, FileContentId>();

        // Populate from the tree and baseline cache
        await PopulateFileStatesFromTreeAsync(treeId, RepoPath.Root);

        // Also scan the working copy to detect any additional changes
        await ScanWorkingCopyAsync();
    }    /// <summary>
    /// Amends the current working copy commit with a new description.
    /// This method is called by the Repository when in live working copy mode to update
    /// the working copy commit's description without creating a new commit.
    /// </summary>
    /// <param name="newDescription">The new description for the working copy commit</param>
    /// <param name="settings">User settings for the amend operation</param>
    /// <returns>A task representing the asynchronous amend operation</returns>
    public async Task AmendCommitAsync(string newDescription, UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(newDescription);
        ArgumentNullException.ThrowIfNull(settings);

        // In live working copy mode, we need to ensure the working copy state is current
        // before the Repository amends the commit
        await ScanWorkingCopyAsync();
        
        // Create an up-to-date snapshot to ensure the commit has the latest tree
        var snapshotOptions = new SnapshotOptions();
        var (currentTreeId, _) = await SnapshotAsync(snapshotOptions, dryRun: false);
        
        // Update our current tree to match
        _currentTreeId = currentTreeId;
        
        // The actual commit amendment (description change) is handled by the Repository's
        // DescribeAsync method, which calls this method to ensure the working copy is current
        // before performing the amendment
    }/// <summary>
    /// Amends the working copy commit by performing a targeted snapshot and creating a new Operation.
    /// This is the method called by the debounced file watcher events as specified in the requirements.
    /// </summary>
    /// <returns>A task representing the asynchronous amend operation</returns>
    public async Task AmendWorkingCopyCommitAsync()
    {
        // Perform targeted snapshot to capture current file system state
        await ScanWorkingCopyAsync();
        
        // Create a new tree from current file states
        var snapshotOptions = new SnapshotOptions();
        var (newTreeId, _) = await SnapshotAsync(snapshotOptions, dryRun: false);
        
        // Update our current tree ID to reflect the new state
        _currentTreeId = newTreeId;
        
        // Notify the Repository that the working copy has changed
        // The Repository will handle the actual commit amendment and operation creation
        WorkingCopyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Checks out files from the target tree to the working copy.
    /// </summary>
    /// <param name="targetTreeId">The tree ID to check out</param>
    /// <param name="options">Options controlling the checkout behavior</param>
    /// <returns>Statistics about the checkout operation</returns>
    public async Task<CheckoutStats> CheckoutAsync(TreeId targetTreeId, CheckoutOptions options)
    {
        var checkoutStats = new CheckoutStats(0, 0, 0, 0, 0);
        
        // Read the target tree
        var targetTreeData = await _objectStore.ReadTreeAsync(targetTreeId);
        if (targetTreeData == null)
        {
            throw new InvalidOperationException($"Target tree {targetTreeId} not found in object store");
        }

        // Update files based on the target tree
        foreach (var entry in targetTreeData.Value.Entries)
        {
            if (entry.Type == TreeEntryType.File)
            {
                var fileName = entry.Name;
                var repoPath = new RepoPath(fileName);
                var fullPath = _fileSystem.Path.Combine(_workingCopyPath, repoPath.ToString());
                
                // Convert ObjectId to FileContentId
                var fileContentId = new FileContentId(entry.ObjectId.HashValue.ToArray());
                var fileContentData = await _objectStore.ReadFileContentAsync(fileContentId);
                
                if (fileContentData.HasValue)
                {
                    // Ensure directory exists
                    var directory = _fileSystem.Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(directory) && !_fileSystem.Directory.Exists(directory))
                    {
                        _fileSystem.Directory.CreateDirectory(directory);
                    }
                    
                    await _fileSystem.File.WriteAllBytesAsync(fullPath, fileContentData.Value.Content.ToArray());
                    checkoutStats = checkoutStats with { FilesUpdated = checkoutStats.FilesUpdated + 1 };
                }
            }
        }

        // Update the current tree ID and rescan
        _currentTreeId = targetTreeId;
        await ScanWorkingCopyAsync();

        return checkoutStats;
    }

    /// <summary>
    /// Gets the file state for the specified repository path.
    /// </summary>
    /// <param name="path">The repository path</param>
    /// <returns>The file state, or null if the path is not tracked</returns>
    public FileState? GetFileState(RepoPath path)
    {
        return _fileStates.TryGetValue(path, out var state) ? state : null;
    }

    /// <summary>
    /// Updates the file state for the specified repository path.
    /// In live mode, this is typically called automatically during scanning.
    /// </summary>
    /// <param name="path">The repository path</param>
    /// <param name="state">The new file state</param>
    public void UpdateFileState(RepoPath path, FileState state)
    {
        _fileStates[path] = state;
    }

    /// <summary>
    /// Removes the file state for the specified repository path.
    /// </summary>
    /// <param name="path">The repository path</param>
    public void RemoveFileState(RepoPath path)
    {
        _fileStates.Remove(path);
    }

    /// <summary>
    /// Gets all currently tracked repository paths.
    /// </summary>
    /// <returns>A collection of tracked repository paths</returns>
    public IEnumerable<RepoPath> GetTrackedPaths()
    {
        return _fileStates.Keys;
    }

    /// <summary>
    /// Replaces all tracked file states with the provided states.
    /// </summary>
    /// <param name="newFileStates">The new file states to use</param>
    public void ReplaceTrackedFileStates(Dictionary<RepoPath, FileState> newFileStates)
    {
        _fileStates.Clear();
        foreach (var kvp in newFileStates)
        {
            _fileStates[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Recursively scans a directory for files and updates the file states.
    /// </summary>
    /// <param name="currentPath">The current file system path being scanned</param>
    /// <param name="currentRepoPath">The current repository path being scanned</param>
    /// <param name="scannedFiles">Dictionary to collect scanned file states</param>
    /// <returns>A task representing the asynchronous scan operation</returns>
    private async Task ScanDirectoryRecursiveAsync(string currentPath, RepoPath currentRepoPath, Dictionary<RepoPath, FileState> scannedFiles)
    {
        if (!_fileSystem.Directory.Exists(currentPath))
        {
            return;
        }

        // Skip .hpd directory
        var hpdPath = _fileSystem.Path.Combine(_workingCopyPath, ".hpd");
        if (currentPath.StartsWith(hpdPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Scan files in current directory
        foreach (var filePath in _fileSystem.Directory.GetFiles(currentPath))
        {
            var fileName = _fileSystem.Path.GetFileName(filePath);
            var fileRepoPath = currentRepoPath.Join(fileName);
            
            // Get file info for metadata
            var fileInfo = _fileSystem.FileInfo.New(filePath);
            var fileType = FileType.NormalFile; // Simplified for now - could add symlink detection
            var mTime = fileInfo.LastWriteTimeUtc;
            var size = fileInfo.Length;
            
            // Create file state with current metadata
            var fileState = new FileState(fileType, mTime, size);
            scannedFiles[fileRepoPath] = fileState;
        }

        // Recursively scan subdirectories
        foreach (var subdirPath in _fileSystem.Directory.GetDirectories(currentPath))
        {
            var subdirName = _fileSystem.Path.GetFileName(subdirPath);
            var subdirRepoPath = currentRepoPath.Join(subdirName);
            await ScanDirectoryRecursiveAsync(subdirPath, subdirRepoPath, scannedFiles);
        }
    }    /// <summary>
    /// Populates _fileStates to match the given tree, recursing into subdirectories.
    /// For files missing on disk, creates a placeholder FileState.
    /// </summary>
    private async Task PopulateFileStatesFromTreeAsync(TreeId treeId, RepoPath currentPath)
    {
        var treeData = await _objectStore.ReadTreeAsync(treeId);
        if (!treeData.HasValue)
        {
            return;
        }

        foreach (var entry in treeData.Value.Entries)
        {
            var entryPath = currentPath.IsRoot ?
                new RepoPath(entry.Name) :
                new RepoPath(currentPath.Components.Append(entry.Name));

            if (entry.Type == TreeEntryType.Directory)
            {
                var subTreeId = new TreeId(entry.ObjectId.HashValue.ToArray());
                await PopulateFileStatesFromTreeAsync(subTreeId, entryPath);
            }
            else if (entry.Type == TreeEntryType.File)
            {
                var diskPath = _fileSystem.Path.Combine(_workingCopyPath, entryPath.ToString());

                FileState fileState;
                if (_fileSystem.File.Exists(diskPath))
                {
                    var fileInfo = _fileSystem.FileInfo.New(diskPath);
                    var fileType = FileType.NormalFile; // Simplified for now
                    var mTime = fileInfo.LastWriteTimeUtc;
                    var size = fileInfo.Length;
                    fileState = new FileState(fileType, mTime, size, isPlaceholder: false);
                }
                else
                {
                    // Create placeholder for missing files
                    fileState = FileState.Placeholder();
                }
                _fileStates[entryPath] = fileState;
                
                // Also store the baseline file content ID for change detection
                if (_baselineFileContentIds != null)
                {
                    var fileContentId = new FileContentId(entry.ObjectId.HashValue.ToArray());
                    _baselineFileContentIds[entryPath] = fileContentId;
                }
            }
        }
    }    /// <summary>
    /// Handles the debounce timer elapsed event to trigger lazy amend operations.
    /// This is called after the debounce delay when file system changes have settled.
    /// </summary>
    private async void OnDebounceTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            lock (_lockObject)
            {
                if (_disposed || !_isDirty)
                {
                    return;
                }
                
                // Reset dirty flag since we're processing the changes
                _isDirty = false;
            }

            // Perform lazy amend operation as specified in requirements
            await AmendWorkingCopyCommitAsync();
        }
        catch (Exception ex)
        {
            // In a real implementation, we'd use proper logging
            Console.WriteLine($"Error during debounced file system update: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles file system change events (Created, Changed, Deleted).
    /// This method filters out .hpd directory changes and debounces rapid changes.
    /// </summary>
    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Skip changes in .hpd directory
            var hpdPath = _fileSystem.Path.Combine(_workingCopyPath, ".hpd");
            if (e.FullPath.StartsWith(hpdPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lock (_lockObject)
            {
                if (_disposed)
                {
                    return;
                }

                // Mark as dirty and restart debounce timer
                _isDirty = true;
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }
        catch (Exception ex)
        {
            // Log error but don't let it crash the application
            Console.WriteLine($"Error handling file system change event: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles file system rename events.
    /// This method filters out .hpd directory changes and debounces rapid changes.
    /// </summary>
    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        try
        {
            // Skip changes in .hpd directory for both old and new paths
            var hpdPath = _fileSystem.Path.Combine(_workingCopyPath, ".hpd");
            if (e.FullPath.StartsWith(hpdPath, StringComparison.OrdinalIgnoreCase) ||
                e.OldFullPath.StartsWith(hpdPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lock (_lockObject)
            {
                if (_disposed)
                {
                    return;
                }

                // Mark as dirty and restart debounce timer
                _isDirty = true;
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }
        catch (Exception ex)
        {
            // Log error but don't let it crash the application
            Console.WriteLine($"Error handling file system rename event: {ex.Message}");
        }
    }

    /// <summary>
    /// Disposes of the resources used by the LiveWorkingCopy.
    /// This stops the FileSystemWatcher and debounce timer to prevent resource leaks.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lockObject)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Stop and dispose of the debounce timer
            _debounceTimer?.Stop();
            _debounceTimer?.Dispose();

            // Stop and dispose of the file system watcher
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
        }
    }

    /// <summary>
    /// Gets the baseline file content ID for a given repository path.
    /// This represents the file content in the current tree that the working copy is based on.
    /// </summary>
    /// <param name="path">The repository path</param>
    /// <returns>The file content ID from the baseline tree, or null if the file doesn't exist in the baseline</returns>
    private FileContentId? GetBaselineFileContentId(RepoPath path)
    {
        return _baselineFileContentIds?.TryGetValue(path, out var contentId) == true ? contentId : null;
    }
}
