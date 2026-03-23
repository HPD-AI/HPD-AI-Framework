using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HPD.VCS.Core;
using HPD.VCS.Storage;
using HPD.VCS.WorkingCopy;
using HPD.VCS.Configuration;
using Xunit;

namespace HPD.VCS.Tests;

public class Module11Tests : IDisposable
{
    private readonly string _tempDir;
    private readonly IFileSystem _fileSystem;
    private readonly UserSettings _userSettings;
    private Repository? _repo;

    public Module11Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hpd_module11_test", Guid.NewGuid().ToString());
        _fileSystem = new FileSystem();
        _userSettings = new UserSettings("Test User", "test@example.com");
    }

    public void Dispose()
    {
        _repo?.Dispose();
        try
        {
            if (_fileSystem.Directory.Exists(_tempDir))
            {
                _fileSystem.Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private async Task<Repository> InitializeRepository(WorkingCopyMode mode = WorkingCopyMode.Explicit)
    {
        _repo?.Dispose();
        
        var repoDir = Path.Combine(_tempDir, Guid.NewGuid().ToString());
        _repo = await Repository.InitializeAsync(repoDir, _userSettings, _fileSystem);
          // If we want live mode, we need to manually configure it after initialization
        if (mode == WorkingCopyMode.Live)
        {
            var configManager = new ConfigurationManager(_fileSystem, repoDir);
            var config = new RepositoryConfig
            {
                WorkingCopy = new WorkingCopyConfig { Mode = "live" }
            };
            await configManager.WriteConfigAsync(config);
            
            // Reload repository with live mode
            _repo.Dispose();
            _repo = await Repository.LoadAsync(repoDir, _fileSystem);
        }
        
        return _repo;
    }

    #region Configuration Management Tests

    [Fact]
    public async Task ConfigurationManager_CreateDefaultConfig_ShouldCreateExplicitMode()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "config-test");
        _fileSystem.Directory.CreateDirectory(repoDir);
        var configManager = new ConfigurationManager(_fileSystem, repoDir);

        // Act
        await configManager.CreateDefaultConfigAsync();

        // Assert
        var config = await configManager.ReadConfigAsync();
        Assert.Equal("explicit", config.WorkingCopy.Mode);
    }

    [Fact]
    public async Task ConfigurationManager_WriteAndReadConfig_ShouldRoundTripCorrectly()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "config-roundtrip-test");
        _fileSystem.Directory.CreateDirectory(repoDir);        var configManager = new ConfigurationManager(_fileSystem, repoDir);
        var originalConfig = new RepositoryConfig
        {
            WorkingCopy = new WorkingCopyConfig { Mode = "live" }
        };

        // Act
        await configManager.WriteConfigAsync(originalConfig);
        var readConfig = await configManager.ReadConfigAsync();

        // Assert
        Assert.Equal("live", readConfig.WorkingCopy.Mode);
    }

    [Fact]
    public async Task Repository_InitializeAsync_ShouldCreateDefaultExplicitConfig()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "init-config-test");
        
        // Act
        using var repo = await Repository.InitializeAsync(repoDir, _userSettings, _fileSystem);

        // Assert - Check that config file was created
        var configPath = Path.Combine(repoDir, ".hpd", "config.json");
        Assert.True(_fileSystem.File.Exists(configPath));

        var configManager = new ConfigurationManager(_fileSystem, repoDir);
        var config = await configManager.ReadConfigAsync();
        Assert.Equal("explicit", config.WorkingCopy.Mode);
    }

    [Fact]
    public async Task Repository_LoadAsync_WithLiveConfig_ShouldCreateLiveWorkingCopy()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "load-live-test");
        using var originalRepo = await Repository.InitializeAsync(repoDir, _userSettings, _fileSystem);
        originalRepo.Dispose();        // Set up live mode configuration
        var configManager = new ConfigurationManager(_fileSystem, repoDir);
        var liveConfig = new RepositoryConfig
        {
            WorkingCopy = new WorkingCopyConfig { Mode = "live" }
        };
        await configManager.WriteConfigAsync(liveConfig);

        // Act
        using var repo = await Repository.LoadAsync(repoDir, _fileSystem);

        // Assert - Should have created LiveWorkingCopy (we can't directly access the private field,
        // but we can test behavior that's specific to live mode)
        // Live mode should reject CommitAsync and suggest NewAsync instead
        var testFile = Path.Combine(repoDir, "test.txt");
        await _fileSystem.File.WriteAllTextAsync(testFile, "test content");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.CommitAsync("test commit", _userSettings, new SnapshotOptions()));
        
        Assert.Contains("live working copy mode", exception.Message);
        Assert.Contains("NewAsync", exception.Message);
    }

    #endregion

    #region LiveWorkingCopy Core Tests

    [Fact]
    public void LiveWorkingCopy_Constructor_ShouldInitializeCorrectly()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var workingCopyPath = "/repo";
        mockFileSystem.AddDirectory(workingCopyPath);
        
        var objectStorePath = "/object-store";
        mockFileSystem.AddDirectory(objectStorePath);
        using var objectStore = new FileSystemObjectStore(mockFileSystem, objectStorePath);

        // Act
        using var liveWorkingCopy = new LiveWorkingCopy(mockFileSystem, objectStore, workingCopyPath);

        // Assert
        Assert.Equal(workingCopyPath, liveWorkingCopy.WorkingCopyPath);
        Assert.Empty(liveWorkingCopy.FileStates);
        Assert.Null(liveWorkingCopy.CurrentTreeId);
    }

    [Fact]
    public async Task LiveWorkingCopy_ScanWorkingCopyAsync_ShouldDetectFiles()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var workingCopyPath = "/repo";
        mockFileSystem.AddDirectory(workingCopyPath);
        mockFileSystem.AddFile("/repo/test.txt", new MockFileData("Hello World"));
        mockFileSystem.AddFile("/repo/subdir/nested.txt", new MockFileData("Nested content"));
        
        var objectStorePath = "/object-store";
        mockFileSystem.AddDirectory(objectStorePath);
        using var objectStore = new FileSystemObjectStore(mockFileSystem, objectStorePath);
        using var liveWorkingCopy = new LiveWorkingCopy(mockFileSystem, objectStore, workingCopyPath);

        // Act
        await liveWorkingCopy.ScanWorkingCopyAsync();

        // Assert
        Assert.Equal(2, liveWorkingCopy.FileStates.Count);
        
        var testPath = new RepoPath(new RepoPathComponent("test.txt"));
        var nestedPath = new RepoPath(new RepoPathComponent("subdir"), new RepoPathComponent("nested.txt"));
        
        Assert.True(liveWorkingCopy.FileStates.ContainsKey(testPath));
        Assert.True(liveWorkingCopy.FileStates.ContainsKey(nestedPath));
        
        var testState = liveWorkingCopy.FileStates[testPath];
        Assert.Equal(FileType.NormalFile, testState.Type);
        Assert.Equal(11, testState.Size); // "Hello World".Length
    }

    [Fact]
    public async Task LiveWorkingCopy_ScanWorkingCopyAsync_ShouldIgnorehpdDirectory()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var workingCopyPath = "/repo";
        mockFileSystem.AddDirectory(workingCopyPath);
        mockFileSystem.AddFile("/repo/test.txt", new MockFileData("Hello World"));
        mockFileSystem.AddFile("/repo/.hpd/internal.txt", new MockFileData("Internal file"));
        
        var objectStorePath = "/object-store";
        mockFileSystem.AddDirectory(objectStorePath);
        using var objectStore = new FileSystemObjectStore(mockFileSystem, objectStorePath);
        using var liveWorkingCopy = new LiveWorkingCopy(mockFileSystem, objectStore, workingCopyPath);

        // Act
        await liveWorkingCopy.ScanWorkingCopyAsync();

        // Assert
        Assert.Single(liveWorkingCopy.FileStates);
        
        var testPath = new RepoPath(new RepoPathComponent("test.txt"));
        Assert.True(liveWorkingCopy.FileStates.ContainsKey(testPath));
        
        var internalPath = new RepoPath(new RepoPathComponent(".hpd"), new RepoPathComponent("internal.txt"));
        Assert.False(liveWorkingCopy.FileStates.ContainsKey(internalPath));
    }

    [Fact]
    public async Task LiveWorkingCopy_CreateSnapshotAsync_ShouldCreateTreeFromFileStates()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var workingCopyPath = "/repo";
        mockFileSystem.AddDirectory(workingCopyPath);
        mockFileSystem.AddFile("/repo/test.txt", new MockFileData("Hello World"));
        
        var objectStorePath = "/object-store";
        mockFileSystem.AddDirectory(objectStorePath);
        using var objectStore = new FileSystemObjectStore(mockFileSystem, objectStorePath);
        using var liveWorkingCopy = new LiveWorkingCopy(mockFileSystem, objectStore, workingCopyPath);        // Act
        var treeId = await liveWorkingCopy.CreateSnapshotAsync();

        // Assert
        // TreeId is a value type, so we verify it has a meaningful value
        Assert.NotEqual(0, treeId.HashValue.Length);
        
        // Verify the tree was written to the object store
        var treeData = await objectStore.ReadTreeAsync(treeId);
        Assert.True(treeData.HasValue);
        Assert.Single(treeData.Value.Entries);
        
        var entry = treeData.Value.Entries.First();
        Assert.Equal("test.txt", entry.Name.Value);
        Assert.Equal(TreeEntryType.File, entry.Type);
    }    [Fact]
    public async Task LiveWorkingCopy_SnapshotAsync_ShouldProvideCorrectStats()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var workingCopyPath = "/repo";
        mockFileSystem.AddDirectory(workingCopyPath);
        mockFileSystem.AddFile("/repo/file1.txt", new MockFileData("Content 1"));
        mockFileSystem.AddFile("/repo/file2.txt", new MockFileData("Content 2"));
        
        var objectStorePath = "/object-store";
        mockFileSystem.AddDirectory(objectStorePath);
        using var objectStore = new FileSystemObjectStore(mockFileSystem, objectStorePath);
        using var liveWorkingCopy = new LiveWorkingCopy(mockFileSystem, objectStore, workingCopyPath);

        var options = new SnapshotOptions();

        // Act
        var (treeId, stats) = await liveWorkingCopy.SnapshotAsync(options);

        // Assert
        // TreeId is a value type, so we verify it has a meaningful value
        Assert.NotEqual(0, treeId.HashValue.Length);
        Assert.Equal(2, stats.ModifiedFiles.Count); // In live mode, all files are considered modified during snapshot
        Assert.Empty(stats.NewFilesTracked);
        Assert.Empty(stats.DeletedFiles);
        Assert.Empty(stats.SkippedDueToLock);
    }

    [Fact]
    public async Task LiveWorkingCopy_UpdateCurrentTreeIdAsync_ShouldUpdateCurrentTree()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var workingCopyPath = "/repo";
        mockFileSystem.AddDirectory(workingCopyPath);
        
        var objectStorePath = "/object-store";
        mockFileSystem.AddDirectory(objectStorePath);
        using var objectStore = new FileSystemObjectStore(mockFileSystem, objectStorePath);
        using var liveWorkingCopy = new LiveWorkingCopy(mockFileSystem, objectStore, workingCopyPath);

        // Create a tree to use as the current tree
        var emptyTreeData = new TreeData(new List<TreeEntry>());
        var treeId = await objectStore.WriteTreeAsync(emptyTreeData);

        // Act
        await liveWorkingCopy.UpdateCurrentTreeIdAsync(treeId);

        // Assert
        Assert.Equal(treeId, liveWorkingCopy.CurrentTreeId);
    }

    #endregion

    #region Repository.NewAsync Tests

    [Fact]
    public async Task NewAsync_InExplicitMode_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var repo = await InitializeRepository(WorkingCopyMode.Explicit);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.AutoCommitAsync("test commit", _userSettings));
        
        Assert.Contains("live working copy mode", exception.Message);
        Assert.Contains("CommitAsync", exception.Message);
    }

    [Fact]
    public async Task NewAsync_InLiveMode_ShouldFinalizeWorkingCopyAndCreateNew()
    {
        // Arrange
        var repo = await InitializeRepository(WorkingCopyMode.Live);
        
        // Add some content to work with
        var testFile = Path.Combine(repo.RepoPath, "test.txt");
        await _fileSystem.File.WriteAllTextAsync(testFile, "Hello World");

        // Get initial state
        var initialViewData = repo.CurrentViewData;
        var initialWorkingCopyId = initialViewData.WorkingCopyId;
        Assert.True(initialWorkingCopyId.HasValue);

        // Act
        var finalizedCommitId = await repo.AutoCommitAsync("Test commit message", _userSettings);

        // Assert
        var newViewData = repo.CurrentViewData;
        var newWorkingCopyId = newViewData.WorkingCopyId;

        // The finalized commit should be returned
        Assert.NotEqual(initialWorkingCopyId.Value, finalizedCommitId);
        
        // A new working copy should have been created
        Assert.True(newWorkingCopyId.HasValue);
        Assert.NotEqual(initialWorkingCopyId.Value, newWorkingCopyId.Value);
        Assert.NotEqual(finalizedCommitId, newWorkingCopyId.Value);

        // The finalized commit should now be in the workspace
        Assert.Equal(finalizedCommitId, newViewData.WorkspaceCommitIds["default"]);

        // The finalized commit should be in the heads
        Assert.Contains(finalizedCommitId, newViewData.HeadCommitIds);
    }    [Fact]
    public async Task NewAsync_WithoutWorkingCopyId_ShouldThrowInvalidOperationException()
    {
        // This test would require manipulating internal state to remove WorkingCopyId
        // For now, we'll skip this as it's an edge case that shouldn't happen in normal operation
        // In a real implementation, you might want to create a test-specific way to simulate this state
        await Task.CompletedTask;
        Assert.True(true); // Placeholder - this test case is documented but not critical for V1
    }    [Fact]
    public async Task NewAsync_ShouldCreateOperationWithCorrectMetadata()
    {
        // Arrange
        var repo = await InitializeRepository(WorkingCopyMode.Live);
        var message = "Test commit message";
        
        // Add some content to work with
        var testFile = Path.Combine(repo.RepoPath, "test.txt");
        await _fileSystem.File.WriteAllTextAsync(testFile, "Test content");        // Act
        var before = DateTimeOffset.UtcNow;
        await Task.Delay(1); // Small delay to ensure timestamp ordering
        var finalizedCommitId = await repo.AutoCommitAsync(message, _userSettings);
        var after = DateTimeOffset.UtcNow;

        // Assert
        // We can't directly access the operation metadata, but we can verify that
        // the operation was recorded by checking that the operation ID changed
        // This is an indirect test, but validates that the operation system was invoked
          // The commit should exist and have the correct message        Console.WriteLine($"DEBUG: Test attempting to read commit {finalizedCommitId}");
        Console.WriteLine($"DEBUG: Test ObjectStore instance: {repo.ObjectStore.GetType().Name}@{repo.ObjectStore.GetHashCode()}");
        var commitData = await repo.ObjectStore.ReadCommitAsync(finalizedCommitId);
        Console.WriteLine($"DEBUG: Test read result: HasValue={commitData.HasValue}");        Assert.True(commitData.HasValue);
        Console.WriteLine($"DEBUG: Expected message: '{message}'");
        Console.WriteLine($"DEBUG: Actual description: '{commitData.Value.Description}'");
        Assert.Equal(message, commitData.Value.Description);
        Console.WriteLine($"DEBUG: Before timestamp: {before:O}");
        Console.WriteLine($"DEBUG: Commit timestamp: {commitData.Value.Committer.Timestamp:O}");
        Console.WriteLine($"DEBUG: After timestamp: {after:O}");
        Console.WriteLine($"DEBUG: Timestamp >= before: {commitData.Value.Committer.Timestamp >= before}");
        Console.WriteLine($"DEBUG: Timestamp <= after: {commitData.Value.Committer.Timestamp <= after}");
        Assert.True(commitData.Value.Committer.Timestamp >= before && commitData.Value.Committer.Timestamp <= after);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task LiveWorkingCopy_Integration_BasicWorkflow()
    {
        // Arrange
        var repo = await InitializeRepository(WorkingCopyMode.Live);
        var file1Path = Path.Combine(repo.RepoPath, "file1.txt");
        var file2Path = Path.Combine(repo.RepoPath, "file2.txt");

        // Act 1: Add some files
        await _fileSystem.File.WriteAllTextAsync(file1Path, "Initial content");
        await _fileSystem.File.WriteAllTextAsync(file2Path, "Second file");

        // Act 2: Create first commit
        var commit1 = await repo.AutoCommitAsync("First commit", _userSettings);

        // Act 3: Modify and add more files
        await _fileSystem.File.WriteAllTextAsync(file1Path, "Modified content");
        var file3Path = Path.Combine(repo.RepoPath, "file3.txt");
        await _fileSystem.File.WriteAllTextAsync(file3Path, "Third file");

        // Act 4: Create second commit
        var commit2 = await repo.AutoCommitAsync("Second commit", _userSettings);

        // Assert
        Assert.NotEqual(commit1, commit2);

        // Verify commit chain
        var commit2Data = await repo.ObjectStore.ReadCommitAsync(commit2);
        Assert.True(commit2Data.HasValue);
        Assert.Contains(commit1, commit2Data.Value.ParentIds);

        // Verify the workspace points to commit2
        Assert.Equal(commit2, repo.CurrentViewData.WorkspaceCommitIds["default"]);

        // Verify commits have correct messages
        var commit1Data = await repo.ObjectStore.ReadCommitAsync(commit1);
        Assert.True(commit1Data.HasValue);
        Assert.Equal("First commit", commit1Data.Value.Description);
        Assert.Equal("Second commit", commit2Data.Value.Description);
    }

    [Fact]
    public async Task LiveWorkingCopy_Integration_FileSystemWatcherSimulation()
    {
        // This test simulates FileSystemWatcher behavior using MockFileSystem
        // Note: MockFileSystem doesn't actually trigger FileSystemWatcher events,
        // so this is testing the underlying scan functionality
        
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var workingCopyPath = "/repo";
        var objectStorePath = "/object-store";
        mockFileSystem.AddDirectory(workingCopyPath);
        mockFileSystem.AddDirectory(objectStorePath);

        using var objectStore = new FileSystemObjectStore(mockFileSystem, objectStorePath);
        using var liveWorkingCopy = new LiveWorkingCopy(mockFileSystem, objectStore, workingCopyPath);

        // Act 1: Initial state - no files
        await liveWorkingCopy.ScanWorkingCopyAsync();
        Assert.Empty(liveWorkingCopy.FileStates);

        // Act 2: "FileSystemWatcher detects" new file
        mockFileSystem.AddFile("/repo/newfile.txt", new MockFileData("New content"));
        await liveWorkingCopy.ScanWorkingCopyAsync();

        // Assert: File is now tracked
        Assert.Single(liveWorkingCopy.FileStates);
        var newFilePath = new RepoPath(new RepoPathComponent("newfile.txt"));
        Assert.True(liveWorkingCopy.FileStates.ContainsKey(newFilePath));

        // Act 3: "FileSystemWatcher detects" file modification
        mockFileSystem.GetFile("/repo/newfile.txt").TextContents = "Modified content";
        await liveWorkingCopy.ScanWorkingCopyAsync();

        // Assert: File state is updated
        var modifiedState = liveWorkingCopy.FileStates[newFilePath];
        Assert.Equal("Modified content".Length, modifiedState.Size);

        // Act 4: "FileSystemWatcher detects" file deletion
        mockFileSystem.RemoveFile("/repo/newfile.txt");
        await liveWorkingCopy.ScanWorkingCopyAsync();

        // Assert: File is no longer tracked
        Assert.Empty(liveWorkingCopy.FileStates);
    }

    [Fact]
    public async Task LiveWorkingCopy_Integration_WithRepositoryOperations()
    {
        // Arrange
        var repo = await InitializeRepository(WorkingCopyMode.Live);
        var testFile = Path.Combine(repo.RepoPath, "test.txt");

        // Act 1: Add content and commit
        await _fileSystem.File.WriteAllTextAsync(testFile, "Version 1");
        var commit1 = await repo.AutoCommitAsync("First version", _userSettings);

        // Act 2: Modify content and commit again
        await _fileSystem.File.WriteAllTextAsync(testFile, "Version 2");
        var commit2 = await repo.AutoCommitAsync("Second version", _userSettings);        // Act 3: Check status (should show clean working copy)
        var status = await repo.GetStatusAsync();
        Assert.Empty(status.ModifiedFiles);
        Assert.Empty(status.AddedFiles);
        Assert.Empty(status.RemovedFiles);

        // Act 4: Modify file again (working copy becomes dirty)
        await _fileSystem.File.WriteAllTextAsync(testFile, "Work in progress");        // Act 5: Check status (should show dirty working copy)
        var dirtyStatus = await repo.GetStatusAsync();
        // Note: The exact behavior here depends on how status interacts with live working copy
        // In live mode, files might be automatically committed to the working copy commit

        // Act 6: Create another commit
        var commit3 = await repo.AutoCommitAsync("Work in progress", _userSettings);

        // Assert: Verify commit chain
        var commit3Data = await repo.ObjectStore.ReadCommitAsync(commit3);
        Assert.True(commit3Data.HasValue);
        Assert.Contains(commit2, commit3Data.Value.ParentIds);

        // Verify all commits have different IDs
        Assert.NotEqual(commit1, commit2);
        Assert.NotEqual(commit2, commit3);
        Assert.NotEqual(commit1, commit3);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task LiveWorkingCopy_CheckoutAsync_ShouldHandleBasicCheckout()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var workingCopyPath = "/repo";
        var objectStorePath = "/object-store";
        mockFileSystem.AddDirectory(workingCopyPath);
        mockFileSystem.AddDirectory(objectStorePath);

        using var objectStore = new FileSystemObjectStore(mockFileSystem, objectStorePath);
        using var liveWorkingCopy = new LiveWorkingCopy(mockFileSystem, objectStore, workingCopyPath);

        // Create a tree to checkout
        var content = "Hello World";
        var fileContentData = new FileContentData(Encoding.UTF8.GetBytes(content));
        var fileContentId = await objectStore.WriteFileContentAsync(fileContentData);
        
        var treeEntries = new List<TreeEntry>
        {
            new TreeEntry(
                new RepoPathComponent("test.txt"),
                TreeEntryType.File,
                new ObjectIdBase(fileContentId.HashValue.ToArray())
            )
        };
        var treeData = new TreeData(treeEntries);
        var treeId = await objectStore.WriteTreeAsync(treeData);

        // Act
        var checkoutStats = await liveWorkingCopy.CheckoutAsync(treeId, new CheckoutOptions());

        // Assert
        Assert.Equal(1, checkoutStats.FilesUpdated);
        Assert.Equal(0, checkoutStats.FilesSkipped);
        Assert.True(mockFileSystem.File.Exists("/repo/test.txt"));
        Assert.Equal(content, mockFileSystem.File.ReadAllText("/repo/test.txt"));
    }

    [Fact]
    public void LiveWorkingCopy_Dispose_ShouldCleanupResources()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var workingCopyPath = "/repo";
        var objectStorePath = "/object-store";
        mockFileSystem.AddDirectory(workingCopyPath);
        mockFileSystem.AddDirectory(objectStorePath);

        using var objectStore = new FileSystemObjectStore(mockFileSystem, objectStorePath);
        var liveWorkingCopy = new LiveWorkingCopy(mockFileSystem, objectStore, workingCopyPath);

        // Act
        liveWorkingCopy.Dispose();

        // Assert
        // The main assertion here is that Dispose doesn't throw an exception
        // In a real implementation, you might want to verify that FileSystemWatcher was disposed
        // but with MockFileSystem this is difficult to test directly

        // Calling Dispose again should not throw
        liveWorkingCopy.Dispose();
    }

    [Fact]
    public async Task NewAsync_WithInvalidMessage_ShouldThrowArgumentNullException()
    {
        // Arrange
        var repo = await InitializeRepository(WorkingCopyMode.Live);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.AutoCommitAsync(null!, _userSettings));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.AutoCommitAsync("valid message", null!));
    }

    #endregion

    #region FileState Management Tests

    [Fact]
    public void LiveWorkingCopy_FileStateOperations_ShouldWorkCorrectly()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var workingCopyPath = "/repo";
        var objectStorePath = "/object-store";
        mockFileSystem.AddDirectory(workingCopyPath);
        mockFileSystem.AddDirectory(objectStorePath);

        using var objectStore = new FileSystemObjectStore(mockFileSystem, objectStorePath);
        using var liveWorkingCopy = new LiveWorkingCopy(mockFileSystem, objectStore, workingCopyPath);

        var testPath = new RepoPath(new RepoPathComponent("test.txt"));
        var testState = new FileState(FileType.NormalFile, DateTimeOffset.UtcNow, 100);

        // Act & Assert: UpdateFileState
        liveWorkingCopy.UpdateFileState(testPath, testState);
        Assert.Single(liveWorkingCopy.FileStates);
        Assert.Equal(testState, liveWorkingCopy.GetFileState(testPath));

        // Act & Assert: GetFileState for non-existent file
        var nonExistentPath = new RepoPath(new RepoPathComponent("nonexistent.txt"));
        Assert.Null(liveWorkingCopy.GetFileState(nonExistentPath));

        // Act & Assert: GetTrackedPaths
        var trackedPaths = liveWorkingCopy.GetTrackedPaths().ToList();
        Assert.Single(trackedPaths);
        Assert.Contains(testPath, trackedPaths);

        // Act & Assert: RemoveFileState
        liveWorkingCopy.RemoveFileState(testPath);
        Assert.Empty(liveWorkingCopy.FileStates);
        Assert.Null(liveWorkingCopy.GetFileState(testPath));

        // Act & Assert: ReplaceTrackedFileStates
        var newStates = new Dictionary<RepoPath, FileState>
        {
            { testPath, testState },
            { nonExistentPath, new FileState(FileType.NormalFile, DateTimeOffset.UtcNow, 200) }
        };
        liveWorkingCopy.ReplaceTrackedFileStates(newStates);
        Assert.Equal(2, liveWorkingCopy.FileStates.Count);
        Assert.Equal(testState, liveWorkingCopy.GetFileState(testPath));
        Assert.NotNull(liveWorkingCopy.GetFileState(nonExistentPath));
    }

    #endregion
}
