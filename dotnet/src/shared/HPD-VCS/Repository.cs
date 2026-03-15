using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using HPD.VCS.Core;
using HPD.VCS.Graphing;
using HPD.VCS.Storage;
using HPD.VCS.WorkingCopy;
using HPD.VCS.Configuration;
using Microsoft.Extensions.Logging;

namespace HPD.VCS;

/// <summary>
/// Repository class that manages the VCS state, stores, and operations.
/// This is the main entry point for repository operations, handling initialization,
/// loading, and coordinating between the object store, operation store, and working copy.
/// </summary>
public partial class Repository : IDisposable
{
    private readonly string _repoPath;
    private readonly IObjectStore _objectStore;
    private readonly IOperationStore _operationStore;
    private readonly IOperationHeadStore _operationHeadStore;
    private readonly IWorkingCopy _workingCopyState;
    private readonly IFileSystem _fileSystem;
    private readonly IIndex _index;
    private readonly string _lockFilePath;
    private OperationId _currentOperationId;
    private ViewData _currentViewData;
    private CommitData? _currentCommitData;
    private bool _disposed = false;

    /// <summary>
    /// Private constructor used by static factory methods.
    /// </summary>
    private Repository(
        string repoPath,
        IObjectStore objectStore,
        IOperationStore operationStore,
        IOperationHeadStore operationHeadStore,
        IWorkingCopy workingCopyState,
        IFileSystem fileSystem,
        IIndex index,
        OperationId currentOperationId,
        ViewData currentViewData)
    {
        _repoPath = repoPath ?? throw new ArgumentNullException(nameof(repoPath));
        _objectStore = objectStore ?? throw new ArgumentNullException(nameof(objectStore));
        _operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
        _operationHeadStore = operationHeadStore ?? throw new ArgumentNullException(nameof(operationHeadStore));
        _workingCopyState = workingCopyState ?? throw new ArgumentNullException(nameof(workingCopyState));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _lockFilePath = Path.Combine(repoPath, ".hpd", ".lock");
        _currentOperationId = currentOperationId;
        _currentViewData = currentViewData;
    }

    /// <summary>
    /// Gets the repository path.
    /// </summary>
    public string RepoPath => _repoPath;

    /// <summary>
    /// Gets the current operation ID (head of operation log).
    /// </summary>
    public OperationId CurrentOperationId => _currentOperationId;

    /// <summary>
    /// Gets the current view data (repository state).
    /// </summary>
    public ViewData CurrentViewData => _currentViewData;

    /// <summary>
    /// Gets the object store for reading/writing commits, trees, and file content.
    /// </summary>
    public IObjectStore ObjectStore => _objectStore;    /// <summary>
    /// Gets the operation store for reading/writing operations and views.
    /// </summary>
    public IOperationStore OperationStore => _operationStore;

    /// <summary>
    /// Gets the working copy state manager.
    /// </summary>
    public IWorkingCopy WorkingCopyState => _workingCopyState;

    /// <summary>
    /// Gets the user settings for this repository instance (for transaction support).
    /// </summary>
    public UserSettings? UserSettings { get; private set; }

    /// <summary>
    /// Gets the operation head store for transaction operations.
    /// </summary>
    public IOperationHeadStore OperationHeadStore => _operationHeadStore;

    /// <summary>
    /// Starts a new transaction for performing complex repository operations atomically.
    /// </summary>
    /// <param name="settings">User settings for the transaction operations</param>
    /// <returns>A new Transaction instance</returns>
    public Transaction StartTransaction(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        UserSettings = settings; // Store for future use
        return new Transaction(this, settings);
    }

    /// <summary>
    /// Initializes a new repository at the specified path.
    /// Creates the .hpd directory structure and initial empty state.
    /// </summary>
    /// <param name="repoPath">Path where the repository should be created</param>
    /// <param name="userSettings">User settings for the initial commit</param>
    /// <param name="fileSystem">File system abstraction (optional, defaults to real filesystem)</param>
    /// <returns>A new Repository instance</returns>
    public static async Task<Repository> InitializeAsync(
        string repoPath, 
        UserSettings userSettings, 
        IFileSystem? fileSystem = null)
    {
        ArgumentNullException.ThrowIfNull(repoPath);
        ArgumentNullException.ThrowIfNull(userSettings);
        
        fileSystem ??= new FileSystem();

        // Create .hpd directory structure
        var vcsDir = Path.Combine(repoPath, ".hpd");
        var objectStoreDir = Path.Combine(vcsDir, "store");
        var operationStoreDir = Path.Combine(vcsDir, "operations");
          // Check if repository already exists
        if (fileSystem.Directory.Exists(vcsDir))
        {
            throw new InvalidOperationException($"Repository already exists at {repoPath}");
        }
        
        fileSystem.Directory.CreateDirectory(vcsDir);
        fileSystem.Directory.CreateDirectory(objectStoreDir);
        fileSystem.Directory.CreateDirectory(operationStoreDir);        // Initialize stores
        var objectStore = new FileSystemObjectStore(fileSystem, objectStoreDir);
        var operationStore = new FileSystemOperationStore(fileSystem, operationStoreDir);
        var operationHeadStore = new FileSystemOperationHeadStore(fileSystem, operationStoreDir);        // Create default configuration and initialize working copy state
        var configManager = new ConfigurationManager(fileSystem, repoPath);
        await configManager.CreateDefaultConfigAsync();
          var config = await configManager.ReadConfigAsync();
        if (config.WorkingCopy?.Mode == null)
        {
            throw new InvalidOperationException("Configuration file is missing working copy mode settings.");
        }
        var workingCopyMode = config.WorkingCopy.Mode.ToWorkingCopyMode();
        var workingCopyState = CreateWorkingCopy(workingCopyMode, fileSystem, objectStore, repoPath);

        // Create initial empty tree
        var emptyTreeData = new TreeData(new List<TreeEntry>());
        var emptyTreeId = await objectStore.WriteTreeAsync(emptyTreeData);

        // Generate a root change ID for the initial commit
        var rootChangeId = SimpleContentHashable.CreateChangeId("initial-commit");

        // Create initial commit
        var signature = userSettings.GetSignature();
        var initialCommitData = new CommitData(
            rootTreeId: emptyTreeId,
            parentIds: new List<CommitId>(),
            associatedChangeId: rootChangeId,
            author: signature,
            committer: signature,
            description: "Initial commit"  // Fixed: was "Initialize repository"
        );

        var initialCommitId = await objectStore.WriteCommitAsync(initialCommitData);        // Create initial view with the commit as the default workspace and head
        var initialViewData = new ViewData(
            workspaceCommitIds: new Dictionary<string, CommitId> { { "default", initialCommitId } },
            headCommitIds: new List<CommitId> { initialCommitId },
            branches: new Dictionary<string, CommitId>()
        );

        var initialViewId = await operationStore.WriteViewAsync(initialViewData);

        // Create initial operation
        var now = DateTimeOffset.UtcNow;
        var initialOperationMetadata = new OperationMetadata(
            startTime: now,
            endTime: now.AddMilliseconds(10),
            description: "Initialize repository",
            username: userSettings.GetUsername(),
            hostname: userSettings.GetHostname(),
            tags: new Dictionary<string, string> { { "type", "init" } }
        );

        var initialOperationData = new OperationData(
            associatedViewId: initialViewId,
            parentOperationIds: new List<OperationId>(),
            metadata: initialOperationMetadata
        );

        var initialOperationId = await operationStore.WriteOperationAsync(initialOperationData);        // Set the operation head
        await operationHeadStore.UpdateHeadOperationIdsAsync(new List<OperationId>(), initialOperationId);        // Update the working copy state with the initial commit's tree ID
        // This ensures files are properly categorized as untracked rather than new tracked files
        await workingCopyState.UpdateCurrentTreeIdAsync(emptyTreeId);        // Initialize and build the index
        var index = new InMemoryIndex();
        await index.BuildIndexAsync(objectStore, new[] { initialCommitId });

        return new Repository(
            repoPath: repoPath,
            objectStore: objectStore,
            operationStore: operationStore,
            operationHeadStore: operationHeadStore,
            workingCopyState: workingCopyState,
            fileSystem: fileSystem,
            index: index,
            currentOperationId: initialOperationId,
            currentViewData: initialViewData
        );
    }

    /// <summary>
    /// Loads an existing repository from the specified path.
    /// Reads the current operation head and associated view data.
    /// </summary>
    /// <param name="repoPath">Path to the repository root</param>
    /// <param name="fileSystem">File system abstraction (optional, defaults to real filesystem)</param>
    /// <returns>A Repository instance for the existing repository</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when the repository directory doesn't exist</exception>
    /// <exception cref="InvalidOperationException">Thrown when the repository is in an invalid state</exception>
    public static async Task<Repository> LoadAsync(string repoPath, IFileSystem? fileSystem = null)
    {
        ArgumentNullException.ThrowIfNull(repoPath);
        
        fileSystem ??= new FileSystem();

        var vcsDir = Path.Combine(repoPath, ".hpd");
        if (!fileSystem.Directory.Exists(vcsDir))
        {
            throw new DirectoryNotFoundException($"Repository not found at {repoPath}. Use Repository.InitializeAsync() to create a new repository.");
        }

        var objectStoreDir = Path.Combine(vcsDir, "store");
        var operationStoreDir = Path.Combine(vcsDir, "operations");        // Initialize stores
        var objectStore = new FileSystemObjectStore(fileSystem, objectStoreDir);        var operationStore = new FileSystemOperationStore(fileSystem, operationStoreDir);
        var operationHeadStore = new FileSystemOperationHeadStore(fileSystem, operationStoreDir);
          // Load configuration and initialize appropriate working copy state
        var configManager = new ConfigurationManager(fileSystem, repoPath);
        var config = await configManager.ReadConfigAsync();
        if (config.WorkingCopy?.Mode == null)
        {
            throw new InvalidOperationException("Configuration file is missing working copy mode settings.");
        }
        var workingCopyMode = config.WorkingCopy.Mode.ToWorkingCopyMode();
        var workingCopyState = CreateWorkingCopy(workingCopyMode, fileSystem, objectStore, repoPath);

        // Load current operation head
        var headOperationIds = await operationHeadStore.GetHeadOperationIdsAsync();
        if (headOperationIds.Count == 0)
        {
            throw new InvalidOperationException("Repository has no operation heads. The repository may be corrupted.");
        }

        if (headOperationIds.Count > 1)
        {
            throw new InvalidOperationException($"Repository has multiple operation heads ({headOperationIds.Count}). Merge conflicts need to be resolved.");
        }

        var currentOperationId = headOperationIds[0];

        // Load the operation and associated view
        var operationData = await operationStore.ReadOperationAsync(currentOperationId);
        if (operationData == null)
        {
            throw new InvalidOperationException($"Failed to read operation {currentOperationId}. The repository may be corrupted.");
        }        var viewData = await operationStore.ReadViewAsync(operationData.Value.AssociatedViewId);
        if (viewData == null)
        {
            throw new InvalidOperationException($"Failed to read view {operationData.Value.AssociatedViewId}. The repository may be corrupted.");
        }        // Update the working copy state with the current workspace commit's tree ID
        // This ensures files are properly categorized as untracked rather than new tracked files
        if (viewData.Value.WorkspaceCommitIds.TryGetValue("default", out var workspaceCommitId))
        {
            var commitData = await objectStore.ReadCommitAsync(workspaceCommitId);
            if (commitData.HasValue)
            {
                await workingCopyState.UpdateCurrentTreeIdAsync(commitData.Value.RootTreeId);
            }
        }        // For live working copy mode, ensure we have a working copy commit
        var currentViewData = viewData.Value;
        var finalOperationId = currentOperationId;
        if (workingCopyMode == WorkingCopyMode.Live && !currentViewData.WorkingCopyId.HasValue)
        {
            // Create initial working copy commit for live mode
            var result = await CreateInitialWorkingCopyCommitAsync(
                objectStore, operationStore, operationHeadStore, currentViewData, currentOperationId);
            currentViewData = result.ViewData;
            finalOperationId = result.OperationId;
        }

        // Initialize and build the index with all commits reachable from the current view
        var index = new InMemoryIndex();
        var allCommitIds = new List<CommitId>();
        allCommitIds.AddRange(currentViewData.HeadCommitIds);
        allCommitIds.AddRange(currentViewData.WorkspaceCommitIds.Values);
        if (currentViewData.WorkingCopyId.HasValue)
        {
            allCommitIds.Add(currentViewData.WorkingCopyId.Value);
        }
        if (allCommitIds.Count > 0)
        {
            await index.BuildIndexAsync(objectStore, allCommitIds);
        }        return new Repository(
            repoPath: repoPath,
            objectStore: objectStore,
            operationStore: operationStore,
            operationHeadStore: operationHeadStore,
            workingCopyState: workingCopyState,
            fileSystem: fileSystem,
            index: index,
            currentOperationId: finalOperationId,
            currentViewData: currentViewData
        );
    }    /// <summary>
    /// Creates a new commit with the current working copy state.
    /// Updates the repository state and operation log.
    /// </summary>
    /// <param name="message">Commit message</param>
    /// <param name="settings">User settings for the commit signature</param>
    /// <param name="snapshotOptions">Options for controlling the snapshot behavior</param>
    /// <returns>The ID of the created commit, or null if no changes were detected</returns>    
    public async Task<CommitId?> CommitAsync(string message, UserSettings settings, SnapshotOptions snapshotOptions)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(snapshotOptions);        // Check that we're not in live working copy mode
        if (_workingCopyState is LiveWorkingCopy)
        {
            throw new InvalidOperationException("CommitAsync is not available in live working copy mode. Use NewAsync instead.");
        }

        using (FileLock.Acquire(_fileSystem, _lockFilePath))
        {
            var now = DateTimeOffset.UtcNow;            // Call _workingCopyState.SnapshotAsync(snapshotOptions) to get (newTreeId, snapshotStats)
            var (newTreeId, snapshotStats) = await _workingCopyState.SnapshotAsync(snapshotOptions);

            // Get oldWorkspaceCommitId from _currentViewData.WorkspaceCommitIds["default"] (V1: "default" workspace)
            CommitId? oldWorkspaceCommitId = null;
            if (_currentViewData.WorkspaceCommitIds.TryGetValue("default", out var workspaceCommitId))
            {
                oldWorkspaceCommitId = workspaceCommitId;
            }

            // Read oldCommitData from _objectStore.ReadCommitAsync(oldWorkspaceCommitId) if it exists
            CommitData? oldCommitData = null;
            if (oldWorkspaceCommitId.HasValue)
            {
                var commitDataResult = await _objectStore.ReadCommitAsync(oldWorkspaceCommitId.Value);
                if (commitDataResult.HasValue)
                {
                    oldCommitData = commitDataResult.Value;
                }
            }

            // Check for changes: if newTreeId == oldCommitData.RootTreeId && string.IsNullOrWhiteSpace(message), return null
            if (oldCommitData.HasValue && 
                newTreeId.Equals(oldCommitData.Value.RootTreeId) && 
                string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            // Create CommitData with ParentIds: [oldWorkspaceCommitId], new AssociatedChangeId (using Guid.NewGuid() for seed)
            var parentIds = new List<CommitId>();
            if (oldWorkspaceCommitId.HasValue)
            {
                parentIds.Add(oldWorkspaceCommitId.Value);
            }

            // Generate change ID using Guid.NewGuid() for seed
            var changeContent = $"{message}\n{Guid.NewGuid()}\n{settings.GetUsername()}";
            var changeId = SimpleContentHashable.CreateChangeId(changeContent);

            // Create commit with Author/Committer from settings.GetSignature()
            var signature = settings.GetSignature();
            var commitData = new CommitData(
                rootTreeId: newTreeId,
                parentIds: parentIds,
                associatedChangeId: changeId,
                author: signature,
                committer: signature,
                description: message
            );

            var newCommitId = await _objectStore.WriteCommitAsync(commitData);

            // Create ViewData newViewData:
            // WorkspaceCommitIds: Copy _currentViewData.WorkspaceCommitIds, update "default" to newCommitId.
            var newWorkspaceCommits = new Dictionary<string, CommitId>(_currentViewData.WorkspaceCommitIds)
            {
                ["default"] = newCommitId
            };

            // HeadCommitIds: Create new list. If oldWorkspaceCommitId was in _currentViewData.HeadCommitIds, 
            // new list is [newCommitId] + other old heads. Else, _currentViewData.HeadCommitIds + newCommitId.
            // Then filter newHeadCommitIds to remove any commit that is now an ancestor of another commit.
            var newHeadCommits = new List<CommitId>();
            if (oldWorkspaceCommitId.HasValue && _currentViewData.HeadCommitIds.Contains(oldWorkspaceCommitId.Value))
            {
                // Replace oldWorkspaceCommitId with newCommitId and keep other heads
                newHeadCommits.Add(newCommitId);
                foreach (var headId in _currentViewData.HeadCommitIds)
                {
                    if (!headId.Equals(oldWorkspaceCommitId.Value))
                    {
                        newHeadCommits.Add(headId);
                    }
                }
            }
            else
            {
                // Add newCommitId to existing heads
                newHeadCommits.AddRange(_currentViewData.HeadCommitIds);
                newHeadCommits.Add(newCommitId);
            }            // Filter heads to remove any commit that is now an ancestor of another commit
            // For V1 implementation, we'll keep a simple filter - remove exact duplicates and basic checks
            newHeadCommits = await FilterHeadsAsync(newHeadCommits);

            // Handle branch auto-advancement: move any branches that were pointing at the old commit
            var newBranches = new Dictionary<string, CommitId>(_currentViewData.Branches);
            if (oldWorkspaceCommitId.HasValue)
            {
                // Find all branches that were pointing to the old commit and advance them
                var branchesToAdvance = _currentViewData.Branches
                    .Where(kvp => kvp.Value.Equals(oldWorkspaceCommitId.Value))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var branchName in branchesToAdvance)
                {
                    newBranches[branchName] = newCommitId;
                }
            }

            var newViewData = new ViewData(newWorkspaceCommits, newHeadCommits, newBranches);
            var newViewId = await _operationStore.WriteViewAsync(newViewData);

            // opMeta: Timestamps, user/host from settings, description $"commit: {first line of message}".
            var firstLineOfMessage = message.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var operationMetadata = new OperationMetadata(
                startTime: now,
                endTime: DateTimeOffset.UtcNow,
                description: $"commit: {firstLineOfMessage}",
                username: settings.GetUsername(),
                hostname: settings.GetHostname(),
                tags: new Dictionary<string, string> { { "type", "commit" } }
            );

            // newOperationData: newViewId, [_currentOperationId] as parent, opMeta.
            var newOperationData = new OperationData(
                associatedViewId: newViewId,
                parentOperationIds: new List<OperationId> { _currentOperationId },
                metadata: operationMetadata
            );

            var newOperationId = await _operationStore.WriteOperationAsync(newOperationData);

            // await _operationHeadStore.UpdateHeadOperationIdsAsync(new List<OperationId> { _currentOperationId }, newOperationId);
            await _operationHeadStore.UpdateHeadOperationIdsAsync(new List<OperationId> { _currentOperationId }, newOperationId);

            // Update instance fields: _currentOperationId, _currentViewData, _currentCommitData (read the newCommitData back or use the one created).
            _currentOperationId = newOperationId;
            _currentViewData = newViewData;
            
            // Read back the commit data or use the one we created
            var newCommitData = await _objectStore.ReadCommitAsync(newCommitId);
            if (newCommitData.HasValue)
            {
                _currentCommitData = newCommitData.Value;
            }
            else
            {
                _currentCommitData = commitData;
            }

            // Update the working copy state to reflect the new tree
            await _workingCopyState.UpdateCurrentTreeIdAsync(newTreeId);

            // Return newCommitId.
            return newCommitId;
        }
    }    /// <summary>
    /// Checks out the specified commit to the working directory.
    /// This is the main checkout command that updates both the working copy and repository metadata.
    /// </summary>
    /// <param name="targetCommitId">The commit ID to check out</param>
    /// <param name="options">Options controlling checkout behavior</param>
    /// <param name="userSettings">User settings for the operation metadata</param>
    /// <returns>Statistics about the checkout operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
    /// <exception cref="InvalidOperationException">Thrown when the target commit doesn't exist or checkout fails</exception>
    public async Task<CheckoutStats> CheckoutAsync(CommitId targetCommitId, CheckoutOptions options, UserSettings userSettings)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(userSettings);

        using (FileLock.Acquire(_fileSystem, _lockFilePath))
        {
            var now = DateTimeOffset.UtcNow;

            // Step 1: Read target commit data and validate it exists
            var targetCommitData = await _objectStore.ReadCommitAsync(targetCommitId);
            if (!targetCommitData.HasValue)
            {
                throw new InvalidOperationException($"Target commit {targetCommitId} does not exist in the object store.");
            }            // Step 2: Check if already at the target commit (optimization)
            if (_currentViewData.WorkspaceCommitIds.TryGetValue("default", out var currentWorkspaceCommitId) &&
                currentWorkspaceCommitId.Equals(targetCommitId))
            {
                // Already at target commit, return empty stats
                return new CheckoutStats(0, 0, 0, 0, 0);
            }

            // Step 3: Update the working copy using WorkingCopyState.CheckoutAsync
            CheckoutStats checkoutStats;
            try
            {
                checkoutStats = await _workingCopyState.CheckoutAsync(targetCommitData.Value.RootTreeId, options);
                
                // For V1, we proceed with metadata updates even if some files were skipped
                // The caller can check checkoutStats.FilesSkipped to see if there were conflicts
                // Only fail if WorkingCopyState.CheckoutAsync threw an exception indicating a critical failure
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to checkout working copy: {ex.Message}", ex);
            }

            // Step 4: Update repository metadata - create new view data
            var newWorkspaceCommits = new Dictionary<string, CommitId>(_currentViewData.WorkspaceCommitIds)
            {
                ["default"] = targetCommitId
            };

            // Step 5: Update heads - add target commit if not already present, then filter
            var newHeadCommits = new List<CommitId>(_currentViewData.HeadCommitIds);
            if (!newHeadCommits.Contains(targetCommitId))
            {
                newHeadCommits.Add(targetCommitId);
            }            // Filter heads to remove any commit that is now an ancestor of another commit
            newHeadCommits = await FilterHeadsAsync(newHeadCommits);

            var newViewData = new ViewData(newWorkspaceCommits, newHeadCommits, _currentViewData.Branches);
            var newViewId = await _operationStore.WriteViewAsync(newViewData);

            // Step 6: Create operation metadata and data
            var operationMetadata = new OperationMetadata(
                startTime: now,
                endTime: DateTimeOffset.UtcNow,
                description: $"checkout: {targetCommitId}",
                username: userSettings.GetUsername(),
                hostname: userSettings.GetHostname(),
                tags: new Dictionary<string, string> { { "type", "checkout" } }
            );

            var newOperationData = new OperationData(
                associatedViewId: newViewId,
                parentOperationIds: new List<OperationId> { _currentOperationId },
                metadata: operationMetadata
            );

            var newOperationId = await _operationStore.WriteOperationAsync(newOperationData);

            // Step 7: Update operation head - first get the current head to ensure we have the right expected value
            var currentHeadOperationIds = await _operationHeadStore.GetHeadOperationIdsAsync();
            if (currentHeadOperationIds.Count != 1)
            {
                throw new InvalidOperationException($"Expected exactly one operation head, but found {currentHeadOperationIds.Count}");
            }
            
            await _operationHeadStore.UpdateHeadOperationIdsAsync(currentHeadOperationIds, newOperationId);

            // Step 8: Update instance fields
            _currentOperationId = newOperationId;
            _currentViewData = newViewData;
            _currentCommitData = targetCommitData.Value;

            return checkoutStats;
        }
    }

    /// <summary>
    /// Filters a list of commit IDs to remove any that are ancestors of other commits in the list.
    /// This is an enhanced version that supports async operations for future extensibility.
    /// For V1 implementation, this performs basic filtering and deduplication.
    /// </summary>
    /// <param name="commitIds">List of commit IDs to filter</param>
    /// <returns>Filtered list with ancestors removed</returns>
    /// <remarks>
    /// In a full implementation, this would use IIndex.IsAncestor to filter out ancestor commits.
    /// For now, we implement basic deduplication and simple checks.
    /// </remarks>
    private async Task<List<CommitId>> FilterHeadsAsync(List<CommitId> commitIds)
    {
        // For V1, we'll do a simple deduplication
        // In a full implementation, we would check for ancestor relationships using the object store
        var uniqueCommits = new HashSet<CommitId>(commitIds);
        var filteredList = uniqueCommits.ToList();

        // TODO: For future versions, implement proper ancestor checking:
        // For each pair of commits (A, B), if A is an ancestor of B, remove A from the list
        // This would require walking the commit graph using _objectStore.ReadCommitAsync
        
        return await Task.FromResult(filteredList);
    }

    /// <summary>
    /// Gets the commit history starting from the current head.
    /// </summary>
    /// <param name="maxCount">Maximum number of commits to return (optional)</param>
    /// <returns>List of commits in reverse chronological order</returns>
    public async Task<List<(CommitId Id, CommitData Data)>> GetCommitHistoryAsync(int? maxCount = null)
    {
        var history = new List<(CommitId Id, CommitData Data)>();
        var visited = new HashSet<CommitId>();

        // Start from default workspace commit if it exists
        if (!_currentViewData.WorkspaceCommitIds.TryGetValue("default", out var startCommit))
        {
            return history; // No commits yet
        }

        var queue = new Queue<CommitId>();
        queue.Enqueue(startCommit);

        while (queue.Count > 0 && (maxCount == null || history.Count < maxCount))
        {
            var commitId = queue.Dequeue();
            
            if (visited.Contains(commitId))
                continue;
                
            visited.Add(commitId);

            var commitData = await _objectStore.ReadCommitAsync(commitId);
            if (commitData == null)
                continue;

            history.Add((commitId, commitData.Value));

            // Add parents to queue for traversal
            foreach (var parentId in commitData.Value.ParentIds)
            {
                if (!visited.Contains(parentId))
                {
                    queue.Enqueue(parentId);
                }
            }
        }

        return history;
    }

    /// <summary>
    /// Gets the operation history starting from the current operation head.
    /// </summary>
    /// <param name="maxCount">Maximum number of operations to return (optional)</param>
    /// <returns>List of operations in reverse chronological order</returns>
    public async Task<List<(OperationId Id, OperationData Data)>> GetOperationHistoryAsync(int? maxCount = null)
    {
        var history = new List<(OperationId Id, OperationData Data)>();
        var visited = new HashSet<OperationId>();
        var queue = new Queue<OperationId>();
        
        queue.Enqueue(_currentOperationId);

        while (queue.Count > 0 && (maxCount == null || history.Count < maxCount))
        {
            var operationId = queue.Dequeue();
            
            if (visited.Contains(operationId))
                continue;
                
            visited.Add(operationId);

            var operationData = await _operationStore.ReadOperationAsync(operationId);
            if (operationData == null)
                continue;

            history.Add((operationId, operationData.Value));

            // Add parents to queue for traversal
            foreach (var parentId in operationData.Value.ParentOperationIds)
            {
                if (!visited.Contains(parentId))
                {
                    queue.Enqueue(parentId);
                }
            }
        }        return history;
    }

    /// <summary>
    /// Gets the operation log starting from the current operation head.
    /// Implements Task 6.2 - Operation Log functionality.
    /// </summary>
    /// <param name="limit">Maximum number of operations to return (default: null for unlimited)</param>
    /// <returns>List of operations in reverse chronological order</returns>
    public async Task<IReadOnlyList<(OperationId Id, OperationData Data)>> OperationLogAsync(int? limit = null)
    {
        var history = new List<(OperationId Id, OperationData Data)>();
        var currentOpId = _currentOperationId;
        var effectiveLimit = limit ?? 1000; // Default cap to prevent memory issues

        for (int i = 0; i < effectiveLimit; i++)
        {
            // Read the current operation data
            var operationData = await _operationStore.ReadOperationAsync(currentOpId);
            if (operationData == null)
                break;

            // Add to history
            history.Add((currentOpId, operationData.Value));

            // Check if this is the root operation (no parents)
            if (operationData.Value.ParentOperationIds.Count == 0)
                break;

            // Follow first parent for linear history
            currentOpId = operationData.Value.ParentOperationIds.First();
        }

        return history;
    }

    /// <summary>
    /// Gets the basic first-parent history starting from the current workspace commit.
    /// Implements Task 4.4 - Simple first-parent history following only the first parent of each commit.
    /// </summary>
    /// <param name="limit">Maximum number of commits to return (default: 10)</param>
    /// <returns>List of commits in reverse chronological order following first parent chain</returns>
    public async Task<IReadOnlyList<CommitData>> LogAsync(int limit = 10)
    {
        var history = new List<CommitData>();
        
        // currentTraversalCommitId = _currentViewData.WorkspaceCommitIds["default"];
        if (!_currentViewData.WorkspaceCommitIds.TryGetValue("default", out var currentTraversalCommitId))
        {
            return history; // No commits yet
        }

        // Loop limit times:
        for (int i = 0; i < limit; i++)
        {
            // commitData = await _objectStore.ReadCommitAsync(currentTraversalCommitId);
            var commitData = await _objectStore.ReadCommitAsync(currentTraversalCommitId);
            
            // If commitData == null or commitData.ParentIds.Count == 0 (for root), break.
            if (commitData == null || commitData.Value.ParentIds.Count == 0)
            {
                // If we have valid commit data, add it before breaking
                if (commitData != null)
                {
                    history.Add(commitData.Value);
                }
                break;
            }
            
            // Add commitData to history list.
            history.Add(commitData.Value);
            
            // currentTraversalCommitId = commitData.ParentIds.FirstOrDefault();
            currentTraversalCommitId = commitData.Value.ParentIds.FirstOrDefault();
            
            // Check for default CommitId (which would indicate empty list or invalid parent)
            if (currentTraversalCommitId.Equals(default(CommitId)))
            {
                break;
            }
        }

        // Return history.
        return history;
    }

    /// <summary>
    /// Dispose pattern implementation.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose method.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _objectStore?.Dispose();
            _operationStore?.Dispose();
            _operationHeadStore?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Undoes the most recent operation, reverting the repository to its previous state.
    /// Implements Task 6.3 - Undo operation with robustness checks.
    /// </summary>
    /// <param name="settings">User settings for the undo operation metadata</param>
    /// <returns>The OperationId of the new "undo" operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when undo is not possible (nothing to undo, merge operation, dirty working copy)</exception>
    /// <exception cref="IOException">Thrown when undo would overwrite untracked files</exception>
    public async Task<OperationId> UndoOperationAsync(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // 1. Acquire Repository Lock
        using (FileLock.Acquire(_fileSystem, _lockFilePath))
        {
            // 2. Get Current Operation
            var currentOperation = await _operationStore.ReadOperationAsync(_currentOperationId);
            if (currentOperation == null)
            {
                throw new InvalidOperationException($"Failed to read current operation {_currentOperationId}. The repository may be corrupted.");
            }

            // 3. Check if Undo is Possible
            if (currentOperation.Value.ParentOperationIds.Count == 0)
            {
                throw new InvalidOperationException("Nothing to undo.");
            }

            if (currentOperation.Value.ParentOperationIds.Count > 1)
            {
                throw new InvalidOperationException("Cannot undo a merge operation. (This feature is not yet supported).");
            }            // 4. Pre-Undo Dirty Working Copy Check
            var (snapshotTreeId, snapshotStats) = await _workingCopyState.SnapshotAsync(new SnapshotOptions());
            if (snapshotStats.ModifiedFiles.Count > 0 || snapshotStats.DeletedFiles.Count > 0 || snapshotStats.NewFilesTracked.Count > 0)
            {
                throw new InvalidOperationException("Working copy has uncommitted changes. Please commit or discard them before undoing.");
            }// 5. Determine Target State - ENHANCED LOGIC
        OperationId targetOperationId;
        OperationData targetOperationData;
        ViewData targetViewData;

        // Check if current operation is an undo operation
        var isCurrentUndo = currentOperation.Value.Metadata.Tags.ContainsKey("type") && 
                           currentOperation.Value.Metadata.Tags["type"] == "undo";

        if (isCurrentUndo)
        {
            // If undoing an undo operation, we need to find what operation to continue undoing from
            // by looking at the operation that was originally reverted to
            if (currentOperation.Value.Metadata.Tags.TryGetValue("reverts_to_operation", out var revertsToOperationIdStr))
            {
                var revertsToOperationId = OperationId.FromHexString(revertsToOperationIdStr);
                var revertsToOperationData = await _operationStore.ReadOperationAsync(revertsToOperationId);
                if (revertsToOperationData == null)
                {
                    throw new InvalidOperationException($"Failed to read reverts-to operation {revertsToOperationId}. The repository may be corrupted.");
                }

                // Continue the undo sequence: target is the parent of the operation we reverted to
                if (revertsToOperationData.Value.ParentOperationIds.Count == 0)
                {
                    throw new InvalidOperationException("Nothing to undo.");
                }
                
                targetOperationId = revertsToOperationData.Value.ParentOperationIds.First();
                targetOperationData = (await _operationStore.ReadOperationAsync(targetOperationId))!.Value;
            }
            else
            {
                // Fallback: traverse backwards to find next undoable operation
                targetOperationId = await FindNextUndoableOperationAsync(currentOperation.Value);
                targetOperationData = (await _operationStore.ReadOperationAsync(targetOperationId))!.Value;
            }
        }
        else
        {
            // Normal case: undo to parent operation
            targetOperationId = currentOperation.Value.ParentOperationIds.First();
            targetOperationData = (await _operationStore.ReadOperationAsync(targetOperationId))!.Value;
        }        targetViewData = (await _operationStore.ReadViewAsync(targetOperationData.AssociatedViewId))!.Value;

        // 6. Update Working Copy to Target State
        var targetWorkspaceCommitId = targetViewData.WorkspaceCommitIds["default"];
            var targetCommitData = await _objectStore.ReadCommitAsync(targetWorkspaceCommitId);
            if (targetCommitData == null)
            {
                throw new InvalidOperationException($"Failed to read target commit {targetWorkspaceCommitId}. The repository may be corrupted.");
            }            CheckoutStats checkoutStats;
            try
            {
                checkoutStats = await _workingCopyState.CheckoutAsync(targetCommitData.Value.RootTreeId, new CheckoutOptions());
                if (checkoutStats.FilesSkipped > 0)
                {
                    throw new IOException($"Undo failed because it would overwrite {checkoutStats.FilesSkipped} untracked files. Please move or delete them and try again.");
                }
            }
            catch (Exception ex) when (!(ex is IOException))
            {
                throw new InvalidOperationException("Undo failed to update the working copy. The repository state has not been changed.", ex);
            }            // 7. Commit the "undo" Operation
            // Always write a new view for V1 (safer approach)
            var newViewId = await _operationStore.WriteViewAsync(targetViewData);// Create operation metadata
            var now = DateTimeOffset.UtcNow;
            var operationMetadata = new OperationMetadata(
                startTime: now,
                endTime: now.AddMilliseconds(10),
                description: $"undo operation {_currentOperationId.ToShortHexString()}",
                username: settings.GetUsername(),
                hostname: settings.GetHostname(),
                tags: new Dictionary<string, string> { 
                    { "type", "undo" },
                    { "undoes_operation", _currentOperationId.ToHexString() },
                    { "reverts_to_operation", targetOperationId.ToHexString() }
                }
            );

            // Create linear chain: undo operation's parent is the operation being undone
            var parentOperationIds = new List<OperationId> { _currentOperationId };

            var newUndoOperationData = new OperationData(
                associatedViewId: newViewId,
                parentOperationIds: parentOperationIds,
                metadata: operationMetadata
            );

            var newUndoOperationId = await _operationStore.WriteOperationAsync(newUndoOperationData);

            // 8. Update Operation Heads
            await _operationHeadStore.UpdateHeadOperationIdsAsync(new List<OperationId> { _currentOperationId }, newUndoOperationId);            // 9. Update Repository Instance State
            _currentOperationId = newUndoOperationId;
            _currentViewData = targetViewData;
            _currentCommitData = targetCommitData.Value;
            // Note: _workingCopyState._currentTreeId was already updated by its successful CheckoutAsync call

            // 10. Return the new undo operation ID
            return newUndoOperationId;
        }
    }

    /// <summary>
    /// Helper method to find the next operation that can be undone by traversing backwards 
    /// through the operation history and skipping undo operations.
    /// </summary>
    private async Task<OperationId> FindNextUndoableOperationAsync(OperationData currentOperation)
    {
        var currentOpId = currentOperation.ParentOperationIds.FirstOrDefault();
        
        while (currentOpId != default(OperationId))
        {
            var opData = await _operationStore.ReadOperationAsync(currentOpId);
            if (opData == null)
            {
                throw new InvalidOperationException($"Failed to read operation {currentOpId} during undo traversal.");
            }

            // If this operation is not an undo operation, we can undo to its parent
            var isUndo = opData.Value.Metadata.Tags.ContainsKey("type") && 
                        opData.Value.Metadata.Tags["type"] == "undo";
            
            if (!isUndo)
            {
                // Found a non-undo operation, return its parent (the state before this operation)
                if (opData.Value.ParentOperationIds.Count > 0)
                {
                    return opData.Value.ParentOperationIds.First();
                }
                else
                {
                    throw new InvalidOperationException("Nothing to undo.");
                }
            }

            // This is an undo operation, keep traversing backwards
            currentOpId = opData.Value.ParentOperationIds.FirstOrDefault();
        }

        throw new InvalidOperationException("Nothing to undo.");
    }

    /// <summary>    /// <summary>
    /// Changes the description of a commit and automatically rebases all descendant commits.
    /// This implements Task 9.3 from Module 9.
    /// </summary>
    /// <param name="targetCommitId">The commit whose description should be changed</param>
    /// <param name="newDescription">The new description for the commit</param>
    /// <param name="settings">User settings for the operation</param>
    /// <returns>The operation ID of the describe operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
    /// <exception cref="InvalidOperationException">Thrown when the target commit doesn't exist</exception>
    public async Task<OperationId> DescribeAsync(CommitId targetCommitId, string newDescription, UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(newDescription);
        ArgumentNullException.ThrowIfNull(settings);        // Check if we're in live working copy mode and targeting the current working copy commit
        if (_workingCopyState is LiveWorkingCopy)
        {
            var liveWorkingCopy = (LiveWorkingCopy)_workingCopyState;
            if (_currentViewData.WorkingCopyId.HasValue && _currentViewData.WorkingCopyId.Value.Equals(targetCommitId))
            {
                // In live working copy mode, amend the working copy commit directly
                // The amend operation will need to create a proper operation record
                await liveWorkingCopy.AmendCommitAsync(newDescription, settings);
                
                // For now, create a placeholder operation ID based on the description change
                // In a complete implementation, this would be the actual operation ID from the amend
                var amendContent = $"amend-description:{newDescription}:{DateTimeOffset.UtcNow:O}";
                return ObjectIdFactory.CreateOperationId(System.Text.Encoding.UTF8.GetBytes(amendContent));
            }
        }

        // Standard behavior for all other cases (not in live working copy mode, or targeting a different commit)
        // 1. Start a transaction
        var tx = StartTransaction(settings);

        // 2. Load target commit data
        var targetCommitData = await _objectStore.ReadCommitAsync(targetCommitId);
        if (!targetCommitData.HasValue)
        {
            throw new InvalidOperationException($"Target commit {targetCommitId} does not exist.");
        }

        // 3. Rewrite the commit with the new description
        await tx.RewriteCommit(targetCommitData.Value).SetDescription(newDescription).WriteAsync();
        
        // 4. Commit the transaction (this will trigger descendant rebasing)
        return await tx.CommitAsync($"describe commit {targetCommitId.ToShortHexString()}");
    }

    /// <summary>
    /// Combines a commit's changes into its parent and automatically rebases all descendant commits.
    /// This implements Task 9.4 from Module 9.
    /// </summary>
    /// <param name="targetCommitId">The commit to squash into its parent</param>
    /// <param name="settings">User settings for the operation</param>
    /// <returns>The operation ID of the squash operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when settings is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when the target commit doesn't exist or doesn't have exactly one parent</exception>
    public async Task<OperationId> SquashAsync(CommitId targetCommitId, UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // 1. Start a transaction
        var tx = StartTransaction(settings);

        // 2. Load target commit data and its single parent
        var targetCommitData = await _objectStore.ReadCommitAsync(targetCommitId);
        if (!targetCommitData.HasValue)
        {
            throw new InvalidOperationException($"Target commit {targetCommitId} does not exist.");
        }

        // For V1, throw if not exactly one parent
        if (targetCommitData.Value.ParentIds.Count != 1)
        {
            throw new InvalidOperationException($"Target commit {targetCommitId} must have exactly one parent to be squashed. Found {targetCommitData.Value.ParentIds.Count} parents.");
        }

        var parentCommitId = targetCommitData.Value.ParentIds[0];
        var parentCommitData = await _objectStore.ReadCommitAsync(parentCommitId);
        if (!parentCommitData.HasValue)
        {
            throw new InvalidOperationException($"Parent commit {parentCommitId} does not exist.");
        }

        // 3. Calculate new parent tree using 3-way merge
        TreeId newParentTreeId;
        
        if (parentCommitData.Value.ParentIds.Count == 0)
        {
            // Parent is root commit, use simple merge with empty tree as base
            var emptyTreeData = new TreeData(new List<TreeEntry>());
            var emptyTreeId = await _objectStore.WriteTreeAsync(emptyTreeData);
            
            newParentTreeId = await TreeMerger.MergeTreesAsync(
                _objectStore,
                emptyTreeId,                            // grandparent tree (empty for root)
                parentCommitData.Value.RootTreeId,      // parent tree
                targetCommitData.Value.RootTreeId       // target tree
            );
        }
        else
        {
            // Find grandparent's tree and perform 3-way merge
            var grandparentCommitId = parentCommitData.Value.ParentIds[0]; // Use first grandparent for V1
            var grandparentCommitData = await _objectStore.ReadCommitAsync(grandparentCommitId);
            if (!grandparentCommitData.HasValue)
            {
                throw new InvalidOperationException($"Grandparent commit {grandparentCommitId} does not exist.");
            }

            newParentTreeId = await TreeMerger.MergeTreesAsync(
                _objectStore,
                grandparentCommitData.Value.RootTreeId, // grandparent tree (merge base)
                parentCommitData.Value.RootTreeId,      // parent tree
                targetCommitData.Value.RootTreeId       // target tree
            );
        }

        // 4. Rewrite parent commit with new tree
        var newParentCommit = await tx.RewriteCommit(parentCommitData.Value).SetTreeId(newParentTreeId).WriteAsync();
        var newParentCommitId = ObjectHasher.ComputeCommitId(newParentCommit);

        // 5. Abandon the target commit
        tx.AbandonCommit(targetCommitId, new List<CommitId> { newParentCommitId });

        // 6. Commit the transaction (this will trigger descendant rebasing)
        return await tx.CommitAsync($"squash commit {targetCommitId.ToShortHexString()}");
    }

    /// <summary>
    /// Creates a new branch pointing to the specified commit.
    /// /// </summary>
    /// <param name="branchName">Name of the branch to create</param>
    /// <param name="targetCommitId">Commit that the branch should point to</param>
    /// <returns>Task representing the asynchronous operation</returns>
    /// <exception cref="ArgumentException">Thrown when branch name is invalid or already exists</exception>
    /// <exception cref="InvalidOperationException">Thrown when target commit doesn't exist</exception>
    public async Task CreateBranchAsync(string branchName, CommitId targetCommitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName, nameof(branchName));

        using (FileLock.Acquire(_fileSystem, _lockFilePath))
        {
            // Validate that the target commit exists
            var targetCommitData = await _objectStore.ReadCommitAsync(targetCommitId);
            if (!targetCommitData.HasValue)
            {
                throw new InvalidOperationException($"Target commit {targetCommitId} does not exist.");
            }

            // Check if branch already exists
            if (_currentViewData.Branches.ContainsKey(branchName))
            {
                throw new ArgumentException($"Branch '{branchName}' already exists.", nameof(branchName));
            }

            // Create new view data with the new branch
            var newBranches = new Dictionary<string, CommitId>(_currentViewData.Branches)
            {
                [branchName] = targetCommitId
            };

            var newViewData = new ViewData(
                workspaceCommitIds: _currentViewData.WorkspaceCommitIds,
                headCommitIds: _currentViewData.HeadCommitIds,
                branches: newBranches
            );

            var newViewId = await _operationStore.WriteViewAsync(newViewData);

            // Create operation metadata
            var now = DateTimeOffset.UtcNow;
            var operationMetadata = new OperationMetadata(
                startTime: now,
                endTime: now.AddMilliseconds(10),
                description: $"create branch: {branchName}",
                username: "system", // TODO: Pass user settings if needed
                hostname: Environment.MachineName,
                tags: new Dictionary<string, string> { { "type", "create-branch" } }
            );

            var newOperationData = new OperationData(
                associatedViewId: newViewId,
                parentOperationIds: new List<OperationId> { _currentOperationId },
                metadata: operationMetadata
            );

            var newOperationId = await _operationStore.WriteOperationAsync(newOperationData);

            // Update operation head
            await _operationHeadStore.UpdateHeadOperationIdsAsync(new List<OperationId> { _currentOperationId }, newOperationId);

            // Update instance state
            _currentOperationId = newOperationId;
            _currentViewData = newViewData;
        }
    }

    /// <summary>
    /// Deletes the specified branch.
    /// </summary>
    /// <param name="branchName">Name of the branch to delete</param>
    /// <returns>Task representing the asynchronous operation</returns>
    /// <exception cref="ArgumentException">Thrown when branch name is invalid or doesn't exist</exception>
    public async Task DeleteBranchAsync(string branchName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName, nameof(branchName));

        using (FileLock.Acquire(_fileSystem, _lockFilePath))
        {
            // Check if branch exists
            if (!_currentViewData.Branches.ContainsKey(branchName))
            {
                throw new ArgumentException($"Branch '{branchName}' does not exist.", nameof(branchName));
            }

            // Create new view data without the branch
            var newBranches = new Dictionary<string, CommitId>(_currentViewData.Branches);
            newBranches.Remove(branchName);

            var newViewData = new ViewData(
                workspaceCommitIds: _currentViewData.WorkspaceCommitIds,
                headCommitIds: _currentViewData.HeadCommitIds,
                branches: newBranches
            );

            var newViewId = await _operationStore.WriteViewAsync(newViewData);

            // Create operation metadata
            var now = DateTimeOffset.UtcNow;
            var operationMetadata = new OperationMetadata(
                startTime: now,
                endTime: now.AddMilliseconds(10),
                description: $"delete branch: {branchName}",
                username: "system", // TODO: Pass user settings if needed
                hostname: Environment.MachineName,
                tags: new Dictionary<string, string> { { "type", "delete-branch" } }
            );

            var newOperationData = new OperationData(
                associatedViewId: newViewId,
                parentOperationIds: new List<OperationId> { _currentOperationId },
                metadata: operationMetadata
            );

            var newOperationId = await _operationStore.WriteOperationAsync(newOperationData);

            // Update operation head
            await _operationHeadStore.UpdateHeadOperationIdsAsync(new List<OperationId> { _currentOperationId }, newOperationId);

            // Update instance state
            _currentOperationId = newOperationId;
            _currentViewData = newViewData;
        }
    }

    /// <summary>
    /// Gets the commit ID that the specified branch points to.
    /// </summary>
    /// <param name="branchName">Name of the branch to get</param>
    /// <returns>The commit ID that the branch points to, or null if the branch doesn't exist</returns>
    public CommitId? GetBranch(string branchName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName, nameof(branchName));

        return _currentViewData.Branches.TryGetValue(branchName, out var commitId) ? commitId : null;
    }

    /// <summary>
    /// Merges the specified branch into the current branch.
    /// Creates a merge commit unless it's a fast-forward scenario.
    /// </summary>
    /// <param name="otherBranchName">Name of the branch to merge into current branch</param>
    /// <param name="settings">User settings for commit signature</param>
    /// <returns>The commit ID of the merge result (new merge commit or fast-forwarded commit)</returns>
    /// <exception cref="ArgumentException">Thrown when branch name is invalid or doesn't exist</exception>
    /// <exception cref="InvalidOperationException">Thrown when merge cannot be completed</exception>
    public async Task<CommitId> MergeAsync(string otherBranchName, UserSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(otherBranchName, nameof(otherBranchName));
        ArgumentNullException.ThrowIfNull(settings);

        using (FileLock.Acquire(_fileSystem, _lockFilePath))
        {
            // 1. Get current and other commit IDs
            var currentCommitId = _currentViewData.WorkspaceCommitIds["default"];
            var otherCommitId = GetBranch(otherBranchName);
            
            if (!otherCommitId.HasValue)
            {
                throw new ArgumentException($"Branch '{otherBranchName}' does not exist.", nameof(otherBranchName));
            }

            // 2. Find merge base using two-sided BFS
            var mergeBases = await FindMergeBasesAsync(currentCommitId, otherCommitId.Value);
            if (mergeBases.Count == 0)
            {
                throw new InvalidOperationException("No common ancestor found between branches.");
            }
            
            // For V1, use the first merge base
            var baseCommitId = mergeBases[0];

            // 3. Handle fast-forward scenarios
            if (baseCommitId.Equals(otherCommitId.Value))
            {
                // Current branch is ahead; this is a no-op merge
                return currentCommitId;
            }
            
            if (baseCommitId.Equals(currentCommitId))
            {
                // Fast-forward: checkout other commit and update branch pointer
                await CheckoutAsync(otherCommitId.Value, new CheckoutOptions(), settings);
                return otherCommitId.Value;
            }

            // 4. Perform 3-way tree merge
            var currentCommitData = await _objectStore.ReadCommitAsync(currentCommitId);
            var otherCommitData = await _objectStore.ReadCommitAsync(otherCommitId.Value);
            var baseCommitData = await _objectStore.ReadCommitAsync(baseCommitId);
            
            if (!currentCommitData.HasValue || !otherCommitData.HasValue || !baseCommitData.HasValue)
            {
                throw new InvalidOperationException("Failed to read commit data for merge.");
            }

            var mergedTreeId = await TreeMerger.MergeTreesAsync(
                _objectStore,
                baseCommitData.Value.RootTreeId,
                currentCommitData.Value.RootTreeId,
                otherCommitData.Value.RootTreeId
            );

            // 5. Create merge commit
            var now = DateTimeOffset.UtcNow;
            var changeId = SimpleContentHashable.CreateChangeId($"merge-{otherBranchName}-{Guid.NewGuid()}");
            var signature = settings.GetSignature();
            
            var mergeCommitData = new CommitData(
                rootTreeId: mergedTreeId,
                parentIds: new List<CommitId> { currentCommitId, otherCommitId.Value },
                associatedChangeId: changeId,
                author: signature,
                committer: signature,
                description: $"Merge branch '{otherBranchName}'"
            );

            var mergeCommitId = await _objectStore.WriteCommitAsync(mergeCommitData);

            // 6. Update repository state
            var newWorkspaceCommits = new Dictionary<string, CommitId>(_currentViewData.WorkspaceCommitIds)
            {
                ["default"] = mergeCommitId
            };

            var newHeadCommits = new List<CommitId> { mergeCommitId };
            var newBranches = new Dictionary<string, CommitId>(_currentViewData.Branches);

            var newViewData = new ViewData(newWorkspaceCommits, newHeadCommits, newBranches);
            var newViewId = await _operationStore.WriteViewAsync(newViewData);

            // 7. Create operation metadata
            var operationMetadata = new OperationMetadata(
                startTime: now,
                endTime: DateTimeOffset.UtcNow,
                description: $"merge: {otherBranchName}",
                username: settings.GetUsername(),
                hostname: settings.GetHostname(),
                tags: new Dictionary<string, string> { { "type", "merge" } }
            );

            var newOperationData = new OperationData(
                associatedViewId: newViewId,
                parentOperationIds: new List<OperationId> { _currentOperationId },
                metadata: operationMetadata
            );

            var newOperationId = await _operationStore.WriteOperationAsync(newOperationData);

            // 8. Update operation head
            await _operationHeadStore.UpdateHeadOperationIdsAsync(new List<OperationId> { _currentOperationId }, newOperationId);

            // 9. Checkout merge result to working copy
            await _workingCopyState.CheckoutAsync(mergedTreeId, new CheckoutOptions());

            // 10. Update instance state
            _currentOperationId = newOperationId;
            _currentViewData = newViewData;
            _currentCommitData = mergeCommitData;

            return mergeCommitId;
        }
    }

    /// <summary>
    /// Finds the merge bases between two commits using two-sided BFS/DFS algorithm.
    /// Returns the common ancestors that are not ancestors of any other common ancestor.
    /// </summary>
    /// <param name="id1">First commit ID</param>
    /// <param name="id2">Second commit ID</param>
    /// <returns>List of merge base commit IDs</returns>
    private async Task<IReadOnlyList<CommitId>> FindMergeBasesAsync(CommitId id1, CommitId id2)
    {
        if (id1.Equals(id2))
        {
            return new List<CommitId> { id1 };
        }

        var visited1 = new HashSet<CommitId>();
        var visited2 = new HashSet<CommitId>();
        var queue1 = new Queue<CommitId>();
        var queue2 = new Queue<CommitId>();
        var commonAncestors = new HashSet<CommitId>();

        // Initialize queues
        queue1.Enqueue(id1);
        queue2.Enqueue(id2);
        visited1.Add(id1);
        visited2.Add(id2);

        // Two-sided BFS to find first intersection
        while (queue1.Count > 0 || queue2.Count > 0)
        {
            // Process from first queue
            if (queue1.Count > 0)
            {
                var current = queue1.Dequeue();
                
                // Check if this commit was already visited from the other side
                if (visited2.Contains(current))
                {
                    commonAncestors.Add(current);
                }

                // Add parents to queue
                var commitData = await _objectStore.ReadCommitAsync(current);
                if (commitData.HasValue)
                {
                    foreach (var parentId in commitData.Value.ParentIds)
                    {
                        if (!visited1.Contains(parentId))
                        {
                            visited1.Add(parentId);
                            queue1.Enqueue(parentId);
                            
                            // Check immediate intersection
                            if (visited2.Contains(parentId))
                            {
                                commonAncestors.Add(parentId);
                            }
                        }
                    }
                }
            }

            // Process from second queue
            if (queue2.Count > 0)
            {
                var current = queue2.Dequeue();
                
                // Check if this commit was already visited from the other side
                if (visited1.Contains(current))
                {
                    commonAncestors.Add(current);
                }

                // Add parents to queue
                var commitData = await _objectStore.ReadCommitAsync(current);
                if (commitData.HasValue)
                {
                    foreach (var parentId in commitData.Value.ParentIds)
                    {
                        if (!visited2.Contains(parentId))
                        {
                            visited2.Add(parentId);
                            queue2.Enqueue(parentId);
                            
                            // Check immediate intersection
                            if (visited1.Contains(parentId))
                            {
                                commonAncestors.Add(parentId);
                            }
                        }
                    }
                }
            }

            // If we found common ancestors, we can stop the search
            // In a more sophisticated implementation, we'd continue to find all merge bases
            // and filter out those that are ancestors of others
            if (commonAncestors.Count > 0)
            {
                break;
            }
        }        return commonAncestors.ToList();
    }

    /// <summary>
    /// Public wrapper for finding merge bases between commits.
    /// Used by transaction framework for rebase operations.
    /// </summary>
    /// <param name="commitIds">List of commit IDs to find merge bases for</param>
    /// <returns>List of merge base commit IDs</returns>
    public async Task<IReadOnlyList<CommitId>> FindMergeBasesAsync(IReadOnlyList<CommitId> commitIds)
    {
        if (commitIds == null || commitIds.Count == 0)
            return new List<CommitId>();
        
        if (commitIds.Count == 1)
            return commitIds.ToList();
            
        // For now, handle the simple case of two commits
        // In a more sophisticated implementation, we'd handle n-way merges
        if (commitIds.Count == 2)
        {
            return await FindMergeBasesAsync(commitIds[0], commitIds[1]);
        }
        
        // For multiple commits, find pairwise merge bases (simplified approach)
        var result = new HashSet<CommitId>();
        for (int i = 0; i < commitIds.Count - 1; i++)
        {
            for (int j = i + 1; j < commitIds.Count; j++)
            {
                var bases = await FindMergeBasesAsync(commitIds[i], commitIds[j]);
                foreach (var baseId in bases)
                    result.Add(baseId);
            }
        }
        
        return result.ToList();
    }/// <summary>
    /// Gets the current status of the working copy, showing which files have been modified,    /// added, deleted, or are untracked compared to the current commit.
    /// This operation is read-only and does not write any new objects to the store.
    /// </summary>
    /// <returns>A WorkingCopyStatus record containing categorized file lists</returns>
    /// <exception cref="InvalidOperationException">Thrown when there are issues accessing the working copy</exception>
    public async Task<WorkingCopyStatus> GetStatusAsync()
    {        try
        {
            // Create snapshot with dry-run mode to avoid writing to object store
            var options = new SnapshotOptions { DryRun = true };
            var (_, stats) = await _workingCopyState.SnapshotAsync(options);// Map SnapshotStats to WorkingCopyStatus according to Module 8.1 specification
            // For UI purposes, combine UntrackedKeptFiles and NewFilesTracked as "untracked"
            // since both represent files that haven't been committed yet
            var allUntrackedFiles = new List<RepoPath>();
            allUntrackedFiles.AddRange(stats.UntrackedKeptFiles);
            allUntrackedFiles.AddRange(stats.NewFilesTracked);
            
            return new WorkingCopyStatus(
                UntrackedFiles: allUntrackedFiles.AsReadOnly(),
                ModifiedFiles: stats.ModifiedFiles,
                AddedFiles: new List<RepoPath>().AsReadOnly(), // Empty for status - files are untracked until committed
                RemovedFiles: stats.DeletedFiles,
                ignoredFiles: stats.UntrackedIgnoredFiles,
                skippedFiles: stats.SkippedDueToLock
            );
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get working copy status: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets a diff for the specified commit compared to its base.
    /// For merge commits, finds the merge base; for regular commits, uses the first parent.
    /// </summary>
    /// <param name="commitId">The commit to generate a diff for</param>
    /// <returns>A dictionary mapping file paths to their formatted diff strings</returns>
    /// <exception cref="InvalidOperationException">Thrown when the commit doesn't exist</exception>
    public async Task<Dictionary<RepoPath, string>> GetCommitDiffAsync(CommitId commitId)
    {
        var commitData = await _objectStore.ReadCommitAsync(commitId);
        if (!commitData.HasValue)
        {
            throw new InvalidOperationException($"Commit {commitId} does not exist.");
        }

        TreeId baseTreeId;
        
        // Find the base tree for comparison
        if (commitData.Value.ParentIds.Count == 0)
        {
            // Initial commit - compare against empty tree
            var emptyTreeData = new TreeData(new List<TreeEntry>());
            baseTreeId = await _objectStore.WriteTreeAsync(emptyTreeData);
        }
        else if (commitData.Value.ParentIds.Count == 1)
        {
            // Regular commit - use first parent
            var parentCommitData = await _objectStore.ReadCommitAsync(commitData.Value.ParentIds[0]);
            if (!parentCommitData.HasValue)
            {
                throw new InvalidOperationException($"Parent commit {commitData.Value.ParentIds[0]} does not exist.");
            }
            baseTreeId = parentCommitData.Value.RootTreeId;
        }
        else
        {
            // Merge commit - find merge base using existing logic
            var mergeBases = await FindMergeBasesAsync(commitData.Value.ParentIds[0], commitData.Value.ParentIds[1]);
            if (mergeBases.Count == 0)
            {
                throw new InvalidOperationException("No merge base found for merge commit.");
            }
            
            var baseCommitData = await _objectStore.ReadCommitAsync(mergeBases[0]);
            if (!baseCommitData.HasValue)
            {
                throw new InvalidOperationException($"Merge base commit {mergeBases[0]} does not exist.");
            }
            baseTreeId = baseCommitData.Value.RootTreeId;
        }

        return await GenerateTreeDiffAsync(baseTreeId, commitData.Value.RootTreeId);
    }    /// <summary>
    /// Gets a diff for the working copy compared to HEAD.
    /// </summary>
    /// <returns>A dictionary mapping file paths to their formatted diff strings</returns>
    /// <exception cref="InvalidOperationException">Thrown when there are issues accessing the working copy</exception>
    public async Task<Dictionary<RepoPath, string>> GetWorkingCopyDiffAsync()
    {
        // Get the current workspace commit (HEAD)
        if (!_currentViewData.WorkspaceCommitIds.TryGetValue("default", out var headCommitId))
        {
            throw new InvalidOperationException("No workspace commit found (repository may be in invalid state).");
        }

        var headCommitData = await _objectStore.ReadCommitAsync(headCommitId);
        if (!headCommitData.HasValue)
        {
            throw new InvalidOperationException($"HEAD commit {headCommitId} does not exist.");
        }        // First scan the working copy to detect all changes including deletions
        await _workingCopyState.ScanWorkingCopyAsync();

        // Create a snapshot of the working copy to get the current tree
        // Note: We need to use a regular snapshot, not dry run, to properly detect files
        var snapshotOptions = new SnapshotOptions { DryRun = false };
        var (workingCopyTreeId, _) = await _workingCopyState.SnapshotAsync(snapshotOptions, dryRun: false);

        // Debug output
        Console.WriteLine($"HEAD tree ID: {headCommitData.Value.RootTreeId}");
        Console.WriteLine($"Working copy tree ID: {workingCopyTreeId}");

        return await GenerateTreeDiffAsync(headCommitData.Value.RootTreeId, workingCopyTreeId);
    }

    /// <summary>
    /// Generates a diff between two trees by recursively comparing all files.
    /// </summary>
    /// <param name="baseTreeId">The base tree to compare from</param>
    /// <param name="targetTreeId">The target tree to compare to</param>
    /// <returns>A dictionary mapping file paths to their formatted diff strings</returns>
    private async Task<Dictionary<RepoPath, string>> GenerateTreeDiffAsync(TreeId baseTreeId, TreeId targetTreeId)
    {
        var result = new Dictionary<RepoPath, string>();
          // Get all files from both trees
        var baseFiles = await GetAllFilesFromTreeAsync(baseTreeId);
        var targetFiles = await GetAllFilesFromTreeAsync(targetTreeId);
        
        // Debug output
        Console.WriteLine($"Base tree files: {string.Join(", ", baseFiles.Keys)}");
        Console.WriteLine($"Target tree files: {string.Join(", ", targetFiles.Keys)}");
          // Find all paths that exist in either tree
        var allPaths = baseFiles.Keys.Union(targetFiles.Keys).ToHashSet();
          foreach (var path in allPaths.OrderBy(p => p.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            var hasBaseEntry = baseFiles.TryGetValue(path, out var baseEntry);
            var hasTargetEntry = targetFiles.TryGetValue(path, out var targetEntry);
            
            // Skip if both are missing or if both exist and are identical
            if ((!hasBaseEntry && !hasTargetEntry) ||
                (hasBaseEntry && hasTargetEntry && 
                 baseEntry.ObjectId.Equals(targetEntry.ObjectId)))
            {
                continue;
            }
            
            var diffText = await GenerateFileDiffAsync(path, hasBaseEntry ? baseEntry : null, hasTargetEntry ? targetEntry : null);
            if (!string.IsNullOrEmpty(diffText))
            {
                result[path] = diffText;
            }
        }
        
        return result;
    }

    /// <summary>
    /// Recursively gets all files from a tree, returning a dictionary mapping paths to tree entries.
    /// </summary>
    /// <param name="treeId">The tree to get files from</param>
    /// <returns>A dictionary mapping file paths to their tree entries</returns>
    private async Task<Dictionary<RepoPath, TreeEntry>> GetAllFilesFromTreeAsync(TreeId treeId)
    {
        var result = new Dictionary<RepoPath, TreeEntry>();
        await GetAllFilesFromTreeRecursiveAsync(treeId, VCS.Core.RepoPath.Root, result);
        return result;
    }

    /// <summary>
    /// Recursively traverses a tree to collect all file entries.
    /// </summary>
    /// <param name="treeId">The tree to traverse</param>
    /// <param name="currentPath">The current path in the traversal</param>
    /// <param name="result">The dictionary to populate with file entries</param>
    private async Task GetAllFilesFromTreeRecursiveAsync(TreeId treeId, RepoPath currentPath, Dictionary<RepoPath, TreeEntry> result)
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
            
            if (entry.Type == TreeEntryType.File)
            {
                result[entryPath] = entry;
            }
            else if (entry.Type == TreeEntryType.Directory)
            {
                var subTreeId = new TreeId(entry.ObjectId.HashValue.ToArray());
                await GetAllFilesFromTreeRecursiveAsync(subTreeId, entryPath, result);
            }
            // Skip conflicts and symlinks for now in V1 implementation
        }
    }

    /// <summary>
    /// Generates a diff for a single file between base and target versions.
    /// </summary>
    /// <param name="path">The path of the file</param>
    /// <param name="baseEntry">The base tree entry (null if file was added)</param>
    /// <param name="targetEntry">The target tree entry (null if file was deleted)</param>
    /// <returns>The formatted diff string, or empty string if no diff needed</returns>
    private async Task<string> GenerateFileDiffAsync(RepoPath path, TreeEntry? baseEntry, TreeEntry? targetEntry)
    {        // Handle file addition
        if (baseEntry == null && targetEntry != null)
        {
            var targetContent = await SafeGetTextContentAsync(new FileContentId(targetEntry.Value.ObjectId.HashValue.ToArray()));
            if (targetContent == null)
            {
                return $"--- /dev/null\n+++ {path}\nBinary files differ\n";
            }
            
            var diffLines = VCS.Diffing.UnifiedDiffFormatter.GenerateDiffLines("", targetContent).ToList();
            var unifiedDiff = VCS.Diffing.UnifiedDiffFormatter.Format(diffLines);
            return $"--- /dev/null\n+++ {path}\n{unifiedDiff}";
        }          // Handle file deletion
        if (baseEntry != null && targetEntry == null)
        {
            var baseContent = await SafeGetTextContentAsync(new FileContentId(baseEntry.Value.ObjectId.HashValue.ToArray()));
            if (baseContent == null)
            {
                return $"--- {path}\n+++ /dev/null\nBinary files differ\n";
            }
            
            var diffLines = VCS.Diffing.UnifiedDiffFormatter.GenerateDiffLines(baseContent, "").ToList();
            var unifiedDiff = VCS.Diffing.UnifiedDiffFormatter.Format(diffLines);
            return $"--- {path}\n+++ /dev/null\n{unifiedDiff}";
        }
          // Handle file modification
        if (baseEntry != null && targetEntry != null)
        {
            var baseContent = await SafeGetTextContentAsync(new FileContentId(baseEntry.Value.ObjectId.HashValue.ToArray()));
            var targetContent = await SafeGetTextContentAsync(new FileContentId(targetEntry.Value.ObjectId.HashValue.ToArray()));
            
            // Handle binary files
            if (baseContent == null || targetContent == null)
            {
                return $"--- {path}\n+++ {path}\nBinary files differ\n";
            }
              // Generate diff only if content actually differs
            if (baseContent != targetContent)
            {
                var diffLines = VCS.Diffing.UnifiedDiffFormatter.GenerateDiffLines(baseContent, targetContent).ToList();
                var unifiedDiff = VCS.Diffing.UnifiedDiffFormatter.Format(diffLines);
                return $"--- {path}\n+++ {path}\n{unifiedDiff}";
            }
        }
        
        return "";
    }

    /// <summary>
    /// Safely extracts text content from file data, returning null for binary or large files.
    /// </summary>
    /// <param name="fileContentId">The file content ID to read</param>
    /// <returns>The text content as string, or null if the file is binary/large</returns>
    private async Task<string?> SafeGetTextContentAsync(FileContentId fileContentId)
    {
        var fileContentData = await _objectStore.ReadFileContentAsync(fileContentId);
        if (!fileContentData.HasValue)
        {
            return null;
        }
        
        var content = fileContentData.Value.Content.ToArray();
        
        // Check for large files (>5MB threshold as specified in Module 8)
        const int maxDiffFileSize = 5 * 1024 * 1024;
        if (content.Length > maxDiffFileSize)
        {
            return null;
        }
        
        // Check for binary content (null bytes)
        if (IsBinaryContent(content))
        {
            return null;
        }
        
        try
        {
            return System.Text.Encoding.UTF8.GetString(content);
        }
        catch
        {
            // If UTF-8 decoding fails, treat as binary
            return null;
        }
    }

    /// <summary>
    /// Detects if content appears to be binary by checking for null bytes.
    /// </summary>
    /// <param name="content">The content to check</param>
    /// <returns>True if the content appears to be binary</returns>
    private static bool IsBinaryContent(byte[] content)
    {
        // Check first 8KB or entire content if smaller
        var sampleSize = Math.Min(content.Length, 8192);
          for (int i = 0; i < sampleSize; i++)
        {
            if (content[i] == 0)
            {
                return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// Gets a topologically sorted log of commits with graph edges for ASCII graph rendering.
    /// Implements Task 8.3 - Enhanced log command with topological sorting using generation numbers.
    /// </summary>
    /// <param name="limit">Maximum number of commits to return (default: 1000)</param>
    /// <returns>List of commit data with graph edges in topological order</returns>
    public async Task<IReadOnlyList<(CommitData Commit, IReadOnlyList<GraphEdge> Edges)>> GetGraphLogAsync(int limit = 1000)
    {
        var result = new List<(CommitData Commit, IReadOnlyList<GraphEdge> Edges)>();
        
        // Step 1: Get starting points (union of HeadCommitIds and WorkspaceCommitIds.Values)
        var startingCommits = new HashSet<CommitId>();
        startingCommits.UnionWith(_currentViewData.HeadCommitIds);
        startingCommits.UnionWith(_currentViewData.WorkspaceCommitIds.Values);
        
        if (startingCommits.Count == 0)
        {
            return result; // No commits to traverse
        }
        
        // Step 2: Set up topological sorting with PriorityQueue
        // Priority: (generation_number, Reverse(committer_timestamp))
        var priorityQueue = new PriorityQueue<CommitId, (int generation, long reverseTimestamp)>();
        var visited = new HashSet<CommitId>();
        var processedCommits = new Dictionary<CommitId, CommitData>();
        
        // Initialize queue with starting commits
        foreach (var commitId in startingCommits)
        {
            var commitData = await _objectStore.ReadCommitAsync(commitId);
            if (commitData.HasValue)
            {
                var generation = await _index.GetGenerationNumberAsync(commitId);
                var reverseTimestamp = -commitData.Value.Committer.Timestamp.ToUnixTimeSeconds();
                priorityQueue.Enqueue(commitId, (generation, reverseTimestamp));
                processedCommits[commitId] = commitData.Value;
            }
        }
        
        // Step 3: Process commits in topological order
        var previousCommitId = default(CommitId);
        
        while (priorityQueue.Count > 0 && result.Count < limit)
        {
            var currentCommitId = priorityQueue.Dequeue();
            
            if (visited.Contains(currentCommitId))
                continue;
                
            visited.Add(currentCommitId);
            
            var commitData = processedCommits[currentCommitId];
            
            // Step 4: Calculate graph edges for this commit
            var edges = new List<GraphEdge>();
            
            foreach (var parentId in commitData.ParentIds)
            {
                // Determine edge type based on whether parent is immediately preceding in topological order
                var edgeType = GraphEdgeType.Direct;
                
                if (!previousCommitId.Equals(default(CommitId)) && !parentId.Equals(previousCommitId))
                {
                    edgeType = GraphEdgeType.Indirect;
                }
                
                // Check if parent commit exists
                var parentCommitData = await _objectStore.ReadCommitAsync(parentId);
                if (!parentCommitData.HasValue)
                {
                    edgeType = GraphEdgeType.Missing;
                }
                else if (!processedCommits.ContainsKey(parentId))
                {
                    // Add parent to queue for future processing
                    var parentGeneration = await _index.GetGenerationNumberAsync(parentId);
                    var parentReverseTimestamp = -parentCommitData.Value.Committer.Timestamp.ToUnixTimeSeconds();
                    priorityQueue.Enqueue(parentId, (parentGeneration, parentReverseTimestamp));
                    processedCommits[parentId] = parentCommitData.Value;
                }
                
                edges.Add(new GraphEdge(parentId, edgeType));
            }
            
            result.Add((commitData, edges.AsReadOnly()));
            previousCommitId = currentCommitId;
        }
        
        return result;
    }    /// <summary>
    /// Creates the appropriate working copy implementation based on the specified mode.
    /// </summary>
    /// <param name="mode">Working copy mode to create</param>
    /// <param name="fileSystem">File system abstraction</param>
    /// <param name="objectStore">Object store for reading tree data</param>
    /// <param name="repoPath">Repository root path</param>
    /// <returns>The appropriate working copy implementation</returns>
    private static IWorkingCopy CreateWorkingCopy(WorkingCopyMode mode, IFileSystem fileSystem, IObjectStore objectStore, string repoPath)
    {
        return mode switch
        {
            WorkingCopyMode.Live => new LiveWorkingCopy(fileSystem, objectStore, repoPath),
            WorkingCopyMode.Explicit => new ExplicitSnapshotWorkingCopy(fileSystem, objectStore, repoPath),
            _ => throw new ArgumentException($"Unknown working copy mode: {mode}", nameof(mode))
        };
    }
}

/// <summary>
/// Helper class for merging trees using 3-way merge algorithm.
/// Handles recursive tree merging with conflict detection.
/// </summary>
public static class TreeMerger
{
    /// <summary>
    /// Merges three trees using 3-way merge algorithm.
    /// </summary>
    /// <param name="store">Object store for reading tree data</param>
    /// <param name="baseTreeId">Base (common ancestor) tree ID</param>
    /// <param name="side1TreeId">First side tree ID</param>
    /// <param name="side2TreeId">Second side tree ID</param>
    /// <returns>The merged tree ID</returns>
    public static async Task<TreeId> MergeTreesAsync(
        IObjectStore store, 
        TreeId baseTreeId, 
        TreeId side1TreeId, 
        TreeId side2TreeId)
    {
        // Read all three trees
        var baseTree = await store.ReadTreeAsync(baseTreeId);
        var side1Tree = await store.ReadTreeAsync(side1TreeId);
        var side2Tree = await store.ReadTreeAsync(side2TreeId);

        if (!baseTree.HasValue || !side1Tree.HasValue || !side2Tree.HasValue)
        {
            throw new InvalidOperationException("Failed to read tree data for merge.");
        }

        // Create dictionaries for efficient lookup
        var baseEntries = CreateEntryDict(baseTree.Value.Entries);
        var side1Entries = CreateEntryDict(side1Tree.Value.Entries);
        var side2Entries = CreateEntryDict(side2Tree.Value.Entries);

        // Get all paths involved in the merge
        var allPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        allPaths.UnionWith(baseEntries.Keys);
        allPaths.UnionWith(side1Entries.Keys);
        allPaths.UnionWith(side2Entries.Keys);

        var mergedEntries = new List<TreeEntry>();        foreach (var path in allPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            // Find entries with the exact case from each tree
            TreeEntry? baseEntry = baseEntries.Values.Where(e => string.Equals(e.Name, path, StringComparison.OrdinalIgnoreCase)).Cast<TreeEntry?>().FirstOrDefault();
            TreeEntry? side1Entry = side1Entries.Values.Where(e => string.Equals(e.Name, path, StringComparison.OrdinalIgnoreCase)).Cast<TreeEntry?>().FirstOrDefault();
            TreeEntry? side2Entry = side2Entries.Values.Where(e => string.Equals(e.Name, path, StringComparison.OrdinalIgnoreCase)).Cast<TreeEntry?>().FirstOrDefault();

            var mergedEntry = await MergeTreeEntry(store, path, baseEntry, side1Entry, side2Entry);
            if (mergedEntry.HasValue)
            {
                mergedEntries.Add(mergedEntry.Value);
            }
        }// Check for case conflicts in paths
        var pathGroups = mergedEntries.GroupBy<TreeEntry, string>(e => e.Name, StringComparer.OrdinalIgnoreCase);        foreach (var group in pathGroups)
        {            if (group.Count() > 1)
            {                // Case conflict detected - create conflict entry
                // For path casing conflicts, there's no common ancestor, so use null in removes list
                // and all different case variants in the Adds list
                var conflictMerge = new Merge<TreeValue?>(
                    new List<TreeValue?> { null }, // null represents no common ancestor for add/add conflicts
                    group.Select(e => new TreeValue(e.Type, e.ObjectId)).Cast<TreeValue?>().ToList()
                );
                var conflictData = new ConflictData(conflictMerge);
                
                var conflictId = await store.WriteConflictAsync(conflictData);
                
                // Use the first entry's name (case) as the canonical form
                var canonicalName = group.First().Name;
                var conflictEntry = new TreeEntry(
                    canonicalName,
                    TreeEntryType.Conflict,
                    new ObjectIdBase(conflictId.HashValue.ToArray())
                );

                // Remove all original entries and add conflict entry
                mergedEntries.RemoveAll(e => string.Equals(e.Name, canonicalName, StringComparison.OrdinalIgnoreCase));
                mergedEntries.Add(conflictEntry);
            }
        }

        // Sort entries by name for deterministic tree structure
        mergedEntries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var mergedTreeData = new TreeData(mergedEntries);
        return await store.WriteTreeAsync(mergedTreeData);
    }

    /// <summary>
    /// Merges a single tree entry using 3-way merge logic.
    /// </summary>
    private static async Task<TreeEntry?> MergeTreeEntry(
        IObjectStore store,
        string path,
        TreeEntry? baseEntry,
        TreeEntry? side1Entry, 
        TreeEntry? side2Entry)
    {
        // Case 1: No changes on either side
        if (EntriesEqual(side1Entry, baseEntry) && EntriesEqual(side2Entry, baseEntry))
        {
            return side1Entry; // Could also return side2Entry or baseEntry
        }

        // Case 2: Only side1 changed
        if (EntriesEqual(side2Entry, baseEntry))
        {
            return side1Entry;
        }

        // Case 3: Only side2 changed  
        if (EntriesEqual(side1Entry, baseEntry))
        {
            return side2Entry;
        }

        // Case 4: Both sides changed to the same thing
        if (EntriesEqual(side1Entry, side2Entry))
        {
            return side1Entry;
        }        // Case 5: Conflict - both sides changed differently
        var conflictMerge = new Merge<TreeValue?>(
            baseEntry != null ? new List<TreeValue?> { new TreeValue(baseEntry.Value.Type, baseEntry.Value.ObjectId) } : new List<TreeValue?> { null },
            new List<TreeValue?>
            {
                side1Entry != null ? new TreeValue(side1Entry.Value.Type, side1Entry.Value.ObjectId) : null,
                side2Entry != null ? new TreeValue(side2Entry.Value.Type, side2Entry.Value.ObjectId) : null
            }
        );
        var conflictData = new ConflictData(conflictMerge);        var conflictId = await store.WriteConflictAsync(conflictData);
        return new TreeEntry(
            path,
            TreeEntryType.Conflict,
            new ObjectIdBase(conflictId.HashValue.ToArray())
        );
    }    /// <summary>
    /// Creates a dictionary from tree entries for efficient lookup.
    /// </summary>
    private static Dictionary<string, TreeEntry> CreateEntryDict(IReadOnlyList<TreeEntry> entries)
    {
        var dict = new Dictionary<string, TreeEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            dict[entry.Name] = entry;
        }
        return dict;
    }

    /// <summary>
    /// Compares two tree entries for equality.
    /// </summary>
    private static bool EntriesEqual(TreeEntry? entry1, TreeEntry? entry2)
    {
        if (entry1 == null && entry2 == null) return true;
        if (entry1 == null || entry2 == null) return false;          return entry1.Value.Name == entry2.Value.Name &&
               entry1.Value.Type == entry2.Value.Type &&
               entry1.Value.ObjectId.Equals(entry2.Value.ObjectId);
    }
}

/// <summary>
/// Extension of Repository class for live working copy functionality.
/// </summary>
public partial class Repository
{
    /// <summary>
    /// Creates a new working copy commit in live mode by finalizing the current working copy commit
    /// and creating a new empty working copy commit parented to it.
    /// This is the equivalent of 'commit' in live working copy mode.
    /// </summary>
    /// <param name="message">The commit message for the finalized working copy commit</param>
    /// <param name="settings">User settings for the operation</param>
    /// <returns>The commit ID of the finalized working copy commit</returns>
    /// <exception cref="InvalidOperationException">Thrown when not in live working copy mode or when working copy ID is missing</exception>
    public async Task<CommitId> AutoCommitAsync(string message, UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(settings);

        using (FileLock.Acquire(_fileSystem, _lockFilePath))
        {
            // Check that we're in live working copy mode
            if (!(_workingCopyState is LiveWorkingCopy))
            {
                throw new InvalidOperationException("AutoCopyAsync is only available in live working copy mode. Use CommitAsync for explicit snapshot mode.");
            }            var now = DateTimeOffset.UtcNow;

            // Get current working copy commit ID from ViewData
            if (!_currentViewData.WorkingCopyId.HasValue)
            {
                throw new InvalidOperationException("No working copy commit found in live mode.");
            }

            var currentWorkingCopyId = _currentViewData.WorkingCopyId.Value;

            // Read the current working copy commit data
            var workingCopyCommitData = await _objectStore.ReadCommitAsync(currentWorkingCopyId);
            if (!workingCopyCommitData.HasValue)
            {
                throw new InvalidOperationException($"Failed to read working copy commit {currentWorkingCopyId}.");
            }

            // Finalize the current working copy commit by taking a snapshot of the current working directory
            // Create signature with current timestamp to ensure it's after the 'now' timestamp
            var signature = new Signature(
                settings.GetUsername().ToLowerInvariant(), 
                settings.GetEmail(), 
                DateTimeOffset.UtcNow
            );
            
            // Take a snapshot to capture the current file system state for the finalized commit
            var (finalizedTreeId, _) = await _workingCopyState.SnapshotAsync(new SnapshotOptions());
            
            // Generate a new change ID for the finalized commit to distinguish it from the working copy commit
            var finalizedChangeContent = $"{message}\n{Guid.NewGuid()}\n{settings.GetUsername()}";
            var finalizedChangeId = SimpleContentHashable.CreateChangeId(finalizedChangeContent);
            
            var finalizedCommitData = new CommitData(
                rootTreeId: finalizedTreeId, // Use snapshot of current working directory
                parentIds: workingCopyCommitData.Value.ParentIds,
                associatedChangeId: finalizedChangeId, // Use new change ID
                author: workingCopyCommitData.Value.Author,
                committer: signature, // Update committer timestamp
                description: message
            );            var finalizedCommitId = await _objectStore.WriteCommitAsync(finalizedCommitData);

            // Create a new working copy commit parented to the finalized one
            // In live mode, the new working copy should reflect the current file system state
            var (newWorkingCopyTreeId, _) = await _workingCopyState.SnapshotAsync(new SnapshotOptions());

            var changeContent = $"working-copy-{Guid.NewGuid()}\n{settings.GetUsername()}";
            var newChangeId = SimpleContentHashable.CreateChangeId(changeContent);

            var newWorkingCopyCommitData = new CommitData(
                rootTreeId: newWorkingCopyTreeId,
                parentIds: new List<CommitId> { finalizedCommitId },
                associatedChangeId: newChangeId,
                author: signature,
                committer: signature,
                description: "Working copy commit" // Temporary description
            );

            var newWorkingCopyId = await _objectStore.WriteCommitAsync(newWorkingCopyCommitData);

            // Update ViewData with finalized commit in workspace and new working copy ID
            var newWorkspaceCommits = new Dictionary<string, CommitId>(_currentViewData.WorkspaceCommitIds)
            {
                ["default"] = finalizedCommitId
            };

            // Update heads: replace any occurrence of old working copy ID with finalized commit ID
            var newHeadCommits = new List<CommitId>();
            foreach (var headId in _currentViewData.HeadCommitIds)
            {
                if (headId.Equals(currentWorkingCopyId))
                {
                    newHeadCommits.Add(finalizedCommitId);
                }
                else
                {
                    newHeadCommits.Add(headId);
                }
            }

            // If working copy wasn't a head, add the finalized commit as a new head
            if (!_currentViewData.HeadCommitIds.Contains(currentWorkingCopyId))
            {
                newHeadCommits.Add(finalizedCommitId);
            }

            // Filter heads to remove any commit that is now an ancestor of another commit
            newHeadCommits = await FilterHeadsAsync(newHeadCommits);

            // Update branch pointers if any were pointing to the working copy
            var newBranches = new Dictionary<string, CommitId>(_currentViewData.Branches);
            var branchesToAdvance = _currentViewData.Branches
                .Where(kvp => kvp.Value.Equals(currentWorkingCopyId))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var branchName in branchesToAdvance)
            {
                newBranches[branchName] = finalizedCommitId;
            }

            var newViewData = new ViewData(
                workspaceCommitIds: newWorkspaceCommits,
                headCommitIds: newHeadCommits,
                branches: newBranches,
                workingCopyId: newWorkingCopyId
            );

            var newViewId = await _operationStore.WriteViewAsync(newViewData);

            // Create operation metadata
            var firstLineOfMessage = message.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var operationMetadata = new OperationMetadata(
                startTime: now,
                endTime: DateTimeOffset.UtcNow,
                description: $"new: {firstLineOfMessage}",
                username: settings.GetUsername(),
                hostname: settings.GetHostname(),
                tags: new Dictionary<string, string> { { "type", "new" } }
            );

            var newOperationData = new OperationData(
                associatedViewId: newViewId,
                parentOperationIds: new List<OperationId> { _currentOperationId },
                metadata: operationMetadata
            );

            var newOperationId = await _operationStore.WriteOperationAsync(newOperationData);

            // Update operation head
            await _operationHeadStore.UpdateHeadOperationIdsAsync(new List<OperationId> { _currentOperationId }, newOperationId);            // Update instance state
            _currentOperationId = newOperationId;
            _currentViewData = newViewData;
            _currentCommitData = finalizedCommitData;            // Update the working copy state with the new working copy tree
            await _workingCopyState.UpdateCurrentTreeIdAsync(newWorkingCopyTreeId);

            return finalizedCommitId;
        }
    }        /// <summary>
        /// Creates an initial working copy commit for live mode when none exists.
        /// This is called when loading a repository that was switched to live mode
        /// but doesn't have an initial working copy commit yet.
        /// </summary>
        /// <param name="objectStore">The object store to write to</param>
        /// <param name="operationStore">The operation store to write to</param>
        /// <param name="operationHeadStore">The operation head store to update</param>
        /// <param name="currentViewData">The current view data</param>
        /// <param name="currentOperationId">The current operation ID</param>
        /// <returns>Updated view data with working copy ID set and new operation ID</returns>
        private static async Task<(ViewData ViewData, OperationId OperationId)> CreateInitialWorkingCopyCommitAsync(
            IObjectStore objectStore,
            IOperationStore operationStore,
            IOperationHeadStore operationHeadStore,
            ViewData currentViewData,
            OperationId currentOperationId)
        {
            // Get the current workspace commit to use as parent for working copy
            if (!currentViewData.WorkspaceCommitIds.TryGetValue("default", out var workspaceCommitId))
            {
                throw new InvalidOperationException("No default workspace commit found when creating initial working copy.");
            }

            // Create an empty tree for the working copy
            var emptyTreeData = new TreeData(new List<TreeEntry>());
            var emptyTreeId = await objectStore.WriteTreeAsync(emptyTreeData);

            // Generate a unique change ID for the working copy commit
            var changeContent = $"working-copy-{Guid.NewGuid()}\ninitial-live-mode";
            var changeId = SimpleContentHashable.CreateChangeId(changeContent);

            // Create a temporary signature for the working copy commit
            var tempSignature = new Signature("system", "system@localhost", DateTimeOffset.UtcNow);

            // Create the working copy commit
            var workingCopyCommitData = new CommitData(
                rootTreeId: emptyTreeId,
                parentIds: new List<CommitId> { workspaceCommitId },
                associatedChangeId: changeId,
                author: tempSignature,
                committer: tempSignature,
                description: "Working copy commit" // Temporary description
            );

            var workingCopyId = await objectStore.WriteCommitAsync(workingCopyCommitData);

            // Create updated view data with the working copy ID
            var updatedViewData = new ViewData(
                workspaceCommitIds: currentViewData.WorkspaceCommitIds,
                headCommitIds: currentViewData.HeadCommitIds,
                branches: currentViewData.Branches,
                workingCopyId: workingCopyId
            );

            // Write the updated view
            var updatedViewId = await operationStore.WriteViewAsync(updatedViewData);

            // Create operation metadata for this initialization
            var operationMetadata = new OperationMetadata(
                startTime: DateTimeOffset.UtcNow,
                endTime: DateTimeOffset.UtcNow.AddMilliseconds(10),
                description: "Initialize live working copy",
                username: "system",
                hostname: Environment.MachineName,
                tags: new Dictionary<string, string> { { "type", "init-live-mode" } }
            );

            // Create new operation
            var newOperationData = new OperationData(
                associatedViewId: updatedViewId,
                parentOperationIds: new List<OperationId> { currentOperationId },
                metadata: operationMetadata
            );

            var newOperationId = await operationStore.WriteOperationAsync(newOperationData);            // Update operation head
            await operationHeadStore.UpdateHeadOperationIdsAsync(new List<OperationId> { currentOperationId }, newOperationId);

            return (updatedViewData, newOperationId);
        }
    }
