using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HPD.VCS.Core;
using HPD.VCS.Storage;
using HPD.VCS.WorkingCopy;
using Xunit;

namespace HPD.VCS.Tests.WorkingCopy;

public class LiveWorkingCopyTests : IDisposable
{
    private readonly MockFileSystem _mockFileSystem;
    private readonly IObjectStore _objectStore;
    private readonly string _workingCopyPath;
    private readonly LiveWorkingCopy _liveWorkingCopy;

    public LiveWorkingCopyTests()
    {
        _mockFileSystem = new MockFileSystem();
        _workingCopyPath = "/repo";
        _mockFileSystem.AddDirectory(_workingCopyPath);
        
        // Create a temporary directory for the object store
        var objectStorePath = "/object-store";
        _mockFileSystem.AddDirectory(objectStorePath);
        _objectStore = new FileSystemObjectStore(_mockFileSystem, objectStorePath);
        
        _liveWorkingCopy = new LiveWorkingCopy(_mockFileSystem, _objectStore, _workingCopyPath);
    }

    public void Dispose()
    {
        _liveWorkingCopy?.Dispose();
        _objectStore?.Dispose();
    }

    #region Constructor and Basic Properties Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldInitializeCorrectly()
    {
        // Assert
        Assert.Equal(_workingCopyPath, _liveWorkingCopy.WorkingCopyPath);
        Assert.Empty(_liveWorkingCopy.FileStates);
        Assert.Null(_liveWorkingCopy.CurrentTreeId);
    }

    [Fact]
    public void Constructor_WithNullFileSystem_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new LiveWorkingCopy(null!, _objectStore, _workingCopyPath));
    }

    [Fact]
    public void Constructor_WithNullObjectStore_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new LiveWorkingCopy(_mockFileSystem, null!, _workingCopyPath));
    }

    [Fact]
    public void Constructor_WithNullWorkingCopyPath_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new LiveWorkingCopy(_mockFileSystem, _objectStore, null!));
    }

    #endregion

    #region ScanWorkingCopyAsync Tests

    [Fact]
    public async Task ScanWorkingCopyAsync_EmptyDirectory_ShouldResultInEmptyFileStates()
    {
        // Act
        await _liveWorkingCopy.ScanWorkingCopyAsync();

        // Assert
        Assert.Empty(_liveWorkingCopy.FileStates);
    }

    [Fact]
    public async Task ScanWorkingCopyAsync_SingleFile_ShouldTrackFile()
    {
        // Arrange
        var content = "Hello World";
        _mockFileSystem.AddFile("/repo/test.txt", new MockFileData(content));

        // Act
        await _liveWorkingCopy.ScanWorkingCopyAsync();

        // Assert
        Assert.Single(_liveWorkingCopy.FileStates);
        
        var testPath = new RepoPath(new RepoPathComponent("test.txt"));
        Assert.True(_liveWorkingCopy.FileStates.ContainsKey(testPath));
        
        var fileState = _liveWorkingCopy.FileStates[testPath];
        Assert.Equal(FileType.NormalFile, fileState.Type);
        Assert.Equal(content.Length, fileState.Size);
        Assert.False(fileState.IsPlaceholder);
    }

    [Fact]
    public async Task ScanWorkingCopyAsync_NestedFiles_ShouldTrackAllFiles()
    {
        // Arrange
        _mockFileSystem.AddFile("/repo/root.txt", new MockFileData("Root file"));
        _mockFileSystem.AddFile("/repo/dir1/file1.txt", new MockFileData("File 1"));
        _mockFileSystem.AddFile("/repo/dir1/dir2/file2.txt", new MockFileData("File 2"));
        _mockFileSystem.AddFile("/repo/dir3/file3.txt", new MockFileData("File 3"));

        // Act
        await _liveWorkingCopy.ScanWorkingCopyAsync();

        // Assert
        Assert.Equal(4, _liveWorkingCopy.FileStates.Count);
        
        var rootPath = new RepoPath(new RepoPathComponent("root.txt"));
        var file1Path = new RepoPath(new RepoPathComponent("dir1"), new RepoPathComponent("file1.txt"));
        var file2Path = new RepoPath(new RepoPathComponent("dir1"), new RepoPathComponent("dir2"), new RepoPathComponent("file2.txt"));
        var file3Path = new RepoPath(new RepoPathComponent("dir3"), new RepoPathComponent("file3.txt"));
        
        Assert.True(_liveWorkingCopy.FileStates.ContainsKey(rootPath));
        Assert.True(_liveWorkingCopy.FileStates.ContainsKey(file1Path));
        Assert.True(_liveWorkingCopy.FileStates.ContainsKey(file2Path));
        Assert.True(_liveWorkingCopy.FileStates.ContainsKey(file3Path));
    }

    [Fact]
    public async Task ScanWorkingCopyAsync_hpdDirectory_ShouldBeIgnored()
    {
        // Arrange
        _mockFileSystem.AddFile("/repo/normal.txt", new MockFileData("Normal file"));
        _mockFileSystem.AddFile("/repo/.hpd/config.json", new MockFileData("{}"));
        _mockFileSystem.AddFile("/repo/.hpd/objects/obj1", new MockFileData("Object 1"));
        _mockFileSystem.AddFile("/repo/.hpd/operations/op1", new MockFileData("Operation 1"));

        // Act
        await _liveWorkingCopy.ScanWorkingCopyAsync();

        // Assert
        Assert.Single(_liveWorkingCopy.FileStates);
        
        var normalPath = new RepoPath(new RepoPathComponent("normal.txt"));
        Assert.True(_liveWorkingCopy.FileStates.ContainsKey(normalPath));
        
        // Verify .hpd files are not tracked
        var hpdFiles = _liveWorkingCopy.FileStates.Keys
            .Where(path => path.Components.Any(component => component.Value == ".hpd"))
            .ToList();
        Assert.Empty(hpdFiles);
    }

    [Fact]
    public async Task ScanWorkingCopyAsync_ConsecutiveCalls_ShouldUpdateFileStates()
    {
        // Arrange - Initial file
        _mockFileSystem.AddFile("/repo/file1.txt", new MockFileData("Content 1"));

        // Act 1 - First scan
        await _liveWorkingCopy.ScanWorkingCopyAsync();
        
        // Assert 1
        Assert.Single(_liveWorkingCopy.FileStates);

        // Arrange - Add more files
        _mockFileSystem.AddFile("/repo/file2.txt", new MockFileData("Content 2"));
        _mockFileSystem.RemoveFile("/repo/file1.txt");
        
        // Act 2 - Second scan
        await _liveWorkingCopy.ScanWorkingCopyAsync();

        // Assert 2 - Should now only have file2
        Assert.Single(_liveWorkingCopy.FileStates);
        var file2Path = new RepoPath(new RepoPathComponent("file2.txt"));
        Assert.True(_liveWorkingCopy.FileStates.ContainsKey(file2Path));
        
        var file1Path = new RepoPath(new RepoPathComponent("file1.txt"));
        Assert.False(_liveWorkingCopy.FileStates.ContainsKey(file1Path));
    }

    #endregion

    #region CreateSnapshotAsync Tests

    [Fact]
    public async Task CreateSnapshotAsync_EmptyWorkingCopy_ShouldCreateEmptyTree()
    {        // Act
        var treeId = await _liveWorkingCopy.CreateSnapshotAsync();

        // Assert
        var treeData = await _objectStore.ReadTreeAsync(treeId);
        Assert.True(treeData.HasValue);
        Assert.Empty(treeData.Value.Entries);
    }

    [Fact]
    public async Task CreateSnapshotAsync_WithFiles_ShouldCreateTreeWithEntries()
    {
        // Arrange
        _mockFileSystem.AddFile("/repo/file1.txt", new MockFileData("Content 1"));
        _mockFileSystem.AddFile("/repo/file2.txt", new MockFileData("Content 2"));

        // Act
        var treeId = await _liveWorkingCopy.CreateSnapshotAsync();        // Assert
        var treeData = await _objectStore.ReadTreeAsync(treeId);
        Assert.True(treeData.HasValue);
        Assert.Equal(2, treeData.Value.Entries.Count);
        
        var entryNames = treeData.Value.Entries.Select(e => e.Name.Value).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "file1.txt", "file2.txt" }, entryNames);
        
        // Verify each entry is a file
        foreach (var entry in treeData.Value.Entries)
        {
            Assert.Equal(TreeEntryType.File, entry.Type);
        }
    }

    [Fact]
    public async Task CreateSnapshotAsync_ShouldAutomaticallyScanBeforeSnapshot()
    {
        // Arrange - Add file after creating LiveWorkingCopy
        _mockFileSystem.AddFile("/repo/newfile.txt", new MockFileData("New content"));

        // Act - CreateSnapshotAsync should scan and include the new file
        var treeId = await _liveWorkingCopy.CreateSnapshotAsync();

        // Assert
        var treeData = await _objectStore.ReadTreeAsync(treeId);
        Assert.True(treeData.HasValue);
        Assert.Single(treeData.Value.Entries);
        Assert.Equal("newfile.txt", treeData.Value.Entries.First().Name.Value);
    }

    #endregion

    #region SnapshotAsync Tests

    [Fact]
    public async Task SnapshotAsync_WithDryRun_ShouldNotWriteToObjectStore()
    {
        // Arrange
        _mockFileSystem.AddFile("/repo/test.txt", new MockFileData("Test content"));
        var options = new SnapshotOptions();        // Act
        var (treeId, stats) = await _liveWorkingCopy.SnapshotAsync(options, dryRun: true);

        // Assert
        // In dry run mode, the tree shouldn't actually exist in the object store
        var treeData = await _objectStore.ReadTreeAsync(treeId);
        Assert.False(treeData.HasValue);
        
        // Stats should still be calculated
        Assert.Single(stats.ModifiedFiles);
    }

    [Fact]
    public async Task SnapshotAsync_WithOptionsWithDryRun_ShouldNotWriteToObjectStore()
    {
        // Arrange
        _mockFileSystem.AddFile("/repo/test.txt", new MockFileData("Test content"));
        var options = new SnapshotOptions { DryRun = true };

        // Act
        var (treeId, stats) = await _liveWorkingCopy.SnapshotAsync(options, dryRun: false);        // Assert
        // Options.DryRun should take precedence
        var treeData = await _objectStore.ReadTreeAsync(treeId);
        Assert.False(treeData.HasValue);
    }

    [Fact]
    public async Task SnapshotAsync_NormalMode_ShouldWriteToObjectStore()
    {
        // Arrange
        _mockFileSystem.AddFile("/repo/test.txt", new MockFileData("Test content"));
        var options = new SnapshotOptions();

        // Act
        var (treeId, stats) = await _liveWorkingCopy.SnapshotAsync(options);        // Assert
        // Tree should exist in the object store
        var treeData = await _objectStore.ReadTreeAsync(treeId);
        Assert.True(treeData.HasValue);
        Assert.Single(treeData.Value.Entries);
          // File content should also be in the object store
        var entry = treeData.Value.Entries.First();
        var fileContentId = new FileContentId(entry.ObjectId.HashValue.ToArray());
        var fileContent = await _objectStore.ReadFileContentAsync(fileContentId);
        Assert.True(fileContent.HasValue);
        Assert.Equal("Test content", Encoding.UTF8.GetString(fileContent.Value.Content.ToArray()));
    }

    [Fact]
    public async Task SnapshotAsync_Stats_ShouldProvideCorrectCounts()
    {
        // Arrange
        _mockFileSystem.AddFile("/repo/file1.txt", new MockFileData("Content 1"));
        _mockFileSystem.AddFile("/repo/file2.txt", new MockFileData("Content 2"));
        _mockFileSystem.AddFile("/repo/file3.txt", new MockFileData("Content 3"));
        var options = new SnapshotOptions();

        // Act
        var (treeId, stats) = await _liveWorkingCopy.SnapshotAsync(options);

        // Assert
        Assert.Equal(3, stats.ModifiedFiles.Count); // In live mode, all files are considered modified
        Assert.Empty(stats.NewFilesTracked);
        Assert.Empty(stats.DeletedFiles);
        Assert.Empty(stats.SkippedDueToLock);
        Assert.Empty(stats.UntrackedIgnoredFiles);
        Assert.Empty(stats.UntrackedKeptFiles);
    }

    [Fact]
    public async Task SnapshotAsync_FileThatNoLongerExists_ShouldBeInDeletedFiles()
    {
        // Arrange - Initial scan with file
        _mockFileSystem.AddFile("/repo/temp.txt", new MockFileData("Temporary"));
        await _liveWorkingCopy.ScanWorkingCopyAsync();
        
        // Verify file is tracked
        var tempPath = new RepoPath(new RepoPathComponent("temp.txt"));
        Assert.True(_liveWorkingCopy.FileStates.ContainsKey(tempPath));
        
        // Remove file from filesystem but keep in file states (simulating file deletion detection)
        _mockFileSystem.RemoveFile("/repo/temp.txt");
        
        var options = new SnapshotOptions();

        // Act
        var (treeId, stats) = await _liveWorkingCopy.SnapshotAsync(options);

        // Assert
        Assert.Single(stats.DeletedFiles);
        Assert.Contains(tempPath, stats.DeletedFiles);
        
        // Tree should be empty since the only file was deleted
        var treeData = await _objectStore.ReadTreeAsync(treeId);
        Assert.True(treeData.HasValue);
        Assert.Empty(treeData.Value.Entries);
    }

    #endregion

    #region UpdateCurrentTreeIdAsync Tests

    [Fact]
    public async Task UpdateCurrentTreeIdAsync_ShouldSetCurrentTreeId()
    {
        // Arrange
        var emptyTreeData = new TreeData(new List<TreeEntry>());
        var treeId = await _objectStore.WriteTreeAsync(emptyTreeData);

        // Act
        await _liveWorkingCopy.UpdateCurrentTreeIdAsync(treeId);

        // Assert
        Assert.Equal(treeId, _liveWorkingCopy.CurrentTreeId);
    }    [Fact]
    public async Task UpdateCurrentTreeIdAsync_MultipleUpdates_ShouldKeepLatest()
    {
        // Arrange - Create two different trees to ensure different TreeIds
        var tree1Data = new TreeData(new List<TreeEntry>());
        var tree1Id = await _objectStore.WriteTreeAsync(tree1Data);
        
        // Create a tree with content to make it different from tree1
        var fileContent = new FileContentData(Encoding.UTF8.GetBytes("test content"));
        var fileContentId = await _objectStore.WriteFileContentAsync(fileContent);
        var tree2Entries = new List<TreeEntry>
        {
            new TreeEntry(
                new RepoPathComponent("test.txt"),
                TreeEntryType.File,
                new ObjectIdBase(fileContentId.HashValue.ToArray())
            )
        };
        var tree2Data = new TreeData(tree2Entries);
        var tree2Id = await _objectStore.WriteTreeAsync(tree2Data);

        // Act
        await _liveWorkingCopy.UpdateCurrentTreeIdAsync(tree1Id);
        Assert.Equal(tree1Id, _liveWorkingCopy.CurrentTreeId);
        
        await _liveWorkingCopy.UpdateCurrentTreeIdAsync(tree2Id);

        // Assert
        Assert.Equal(tree2Id, _liveWorkingCopy.CurrentTreeId);
        Assert.NotEqual(tree1Id, _liveWorkingCopy.CurrentTreeId);
    }

    #endregion

    #region AmendCommitAsync Tests

    [Fact]
    public async Task AmendCommitAsync_WithValidParameters_ShouldNotThrow()
    {
        // Arrange
        var userSettings = new UserSettings("Test User", "test@example.com");

        // Act & Assert - Should not throw
        await _liveWorkingCopy.AmendCommitAsync("New description", userSettings);
    }

    [Fact]
    public async Task AmendCommitAsync_WithNullDescription_ShouldThrowArgumentNullException()
    {
        // Arrange
        var userSettings = new UserSettings("Test User", "test@example.com");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _liveWorkingCopy.AmendCommitAsync(null!, userSettings));
    }

    [Fact]
    public async Task AmendCommitAsync_WithNullSettings_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _liveWorkingCopy.AmendCommitAsync("New description", null!));
    }

    [Fact]
    public async Task AmendCommitAsync_ShouldTriggerRescan()
    {
        // Arrange
        var userSettings = new UserSettings("Test User", "test@example.com");
        _mockFileSystem.AddFile("/repo/test.txt", new MockFileData("Test content"));
        
        // Verify file is not yet tracked
        Assert.Empty(_liveWorkingCopy.FileStates);

        // Act
        await _liveWorkingCopy.AmendCommitAsync("New description", userSettings);

        // Assert - File should now be tracked due to rescan
        Assert.Single(_liveWorkingCopy.FileStates);
        var testPath = new RepoPath(new RepoPathComponent("test.txt"));
        Assert.True(_liveWorkingCopy.FileStates.ContainsKey(testPath));
    }

    #endregion

    #region CheckoutAsync Tests

    [Fact]
    public async Task CheckoutAsync_EmptyTree_ShouldSucceed()
    {
        // Arrange
        var emptyTreeData = new TreeData(new List<TreeEntry>());
        var emptyTreeId = await _objectStore.WriteTreeAsync(emptyTreeData);
        var options = new CheckoutOptions();

        // Act
        var stats = await _liveWorkingCopy.CheckoutAsync(emptyTreeId, options);

        // Assert
        Assert.Equal(0, stats.FilesUpdated);
        Assert.Equal(0, stats.FilesSkipped);
    }

    [Fact]
    public async Task CheckoutAsync_WithFileTree_ShouldMaterializeFiles()
    {
        // Arrange
        var content = "Hello World";
        var fileContentData = new FileContentData(Encoding.UTF8.GetBytes(content));
        var fileContentId = await _objectStore.WriteFileContentAsync(fileContentData);
        
        var treeEntries = new List<TreeEntry>
        {
            new TreeEntry(
                new RepoPathComponent("hello.txt"),
                TreeEntryType.File,
                new ObjectIdBase(fileContentId.HashValue.ToArray())
            )
        };
        var treeData = new TreeData(treeEntries);
        var treeId = await _objectStore.WriteTreeAsync(treeData);
        var options = new CheckoutOptions();

        // Act
        var stats = await _liveWorkingCopy.CheckoutAsync(treeId, options);

        // Assert
        Assert.Equal(1, stats.FilesUpdated);
        Assert.Equal(0, stats.FilesSkipped);
        
        // Verify file was created
        Assert.True(_mockFileSystem.File.Exists("/repo/hello.txt"));
        Assert.Equal(content, _mockFileSystem.File.ReadAllText("/repo/hello.txt"));
    }

    #endregion

    #region File State Management Tests

    [Fact]
    public void GetFileState_ExistingFile_ShouldReturnState()
    {
        // Arrange
        var testPath = new RepoPath(new RepoPathComponent("test.txt"));
        var testState = new FileState(FileType.NormalFile, DateTimeOffset.UtcNow, 100);
        _liveWorkingCopy.UpdateFileState(testPath, testState);

        // Act
        var retrievedState = _liveWorkingCopy.GetFileState(testPath);

        // Assert
        Assert.Equal(testState, retrievedState);
    }

    [Fact]
    public void GetFileState_NonExistentFile_ShouldReturnNull()
    {
        // Arrange
        var testPath = new RepoPath(new RepoPathComponent("nonexistent.txt"));

        // Act
        var retrievedState = _liveWorkingCopy.GetFileState(testPath);

        // Assert
        Assert.Null(retrievedState);
    }

    [Fact]
    public void UpdateFileState_ShouldAddOrUpdateState()
    {
        // Arrange
        var testPath = new RepoPath(new RepoPathComponent("test.txt"));
        var initialState = new FileState(FileType.NormalFile, DateTimeOffset.UtcNow, 100);
        var updatedState = new FileState(FileType.NormalFile, DateTimeOffset.UtcNow, 200);

        // Act 1 - Add new state
        _liveWorkingCopy.UpdateFileState(testPath, initialState);
        
        // Assert 1
        Assert.Equal(initialState, _liveWorkingCopy.GetFileState(testPath));

        // Act 2 - Update existing state
        _liveWorkingCopy.UpdateFileState(testPath, updatedState);

        // Assert 2
        Assert.Equal(updatedState, _liveWorkingCopy.GetFileState(testPath));
        Assert.NotEqual(initialState, _liveWorkingCopy.GetFileState(testPath));
    }

    [Fact]
    public void RemoveFileState_ExistingFile_ShouldRemoveState()
    {
        // Arrange
        var testPath = new RepoPath(new RepoPathComponent("test.txt"));
        var testState = new FileState(FileType.NormalFile, DateTimeOffset.UtcNow, 100);
        _liveWorkingCopy.UpdateFileState(testPath, testState);
        
        // Verify state exists
        Assert.Equal(testState, _liveWorkingCopy.GetFileState(testPath));

        // Act
        _liveWorkingCopy.RemoveFileState(testPath);

        // Assert
        Assert.Null(_liveWorkingCopy.GetFileState(testPath));
        Assert.Empty(_liveWorkingCopy.FileStates);
    }

    [Fact]
    public void RemoveFileState_NonExistentFile_ShouldNotThrow()
    {
        // Arrange
        var testPath = new RepoPath(new RepoPathComponent("nonexistent.txt"));

        // Act & Assert - Should not throw
        _liveWorkingCopy.RemoveFileState(testPath);
    }

    [Fact]
    public void GetTrackedPaths_ShouldReturnAllTrackedPaths()
    {
        // Arrange
        var path1 = new RepoPath(new RepoPathComponent("file1.txt"));
        var path2 = new RepoPath(new RepoPathComponent("dir"), new RepoPathComponent("file2.txt"));
        var state = new FileState(FileType.NormalFile, DateTimeOffset.UtcNow, 100);
        
        _liveWorkingCopy.UpdateFileState(path1, state);
        _liveWorkingCopy.UpdateFileState(path2, state);

        // Act
        var trackedPaths = _liveWorkingCopy.GetTrackedPaths().ToList();

        // Assert
        Assert.Equal(2, trackedPaths.Count);
        Assert.Contains(path1, trackedPaths);
        Assert.Contains(path2, trackedPaths);
    }

    [Fact]
    public void ReplaceTrackedFileStates_ShouldReplaceAllStates()
    {
        // Arrange
        var oldPath = new RepoPath(new RepoPathComponent("old.txt"));
        var oldState = new FileState(FileType.NormalFile, DateTimeOffset.UtcNow, 100);
        _liveWorkingCopy.UpdateFileState(oldPath, oldState);
        
        var newStates = new Dictionary<RepoPath, FileState>
        {
            { new RepoPath(new RepoPathComponent("new1.txt")), new FileState(FileType.NormalFile, DateTimeOffset.UtcNow, 200) },
            { new RepoPath(new RepoPathComponent("new2.txt")), new FileState(FileType.NormalFile, DateTimeOffset.UtcNow, 300) }
        };

        // Act
        _liveWorkingCopy.ReplaceTrackedFileStates(newStates);

        // Assert
        Assert.Equal(2, _liveWorkingCopy.FileStates.Count);
        Assert.Null(_liveWorkingCopy.GetFileState(oldPath)); // Old state should be gone
        
        foreach (var kvp in newStates)
        {
            Assert.Equal(kvp.Value, _liveWorkingCopy.GetFileState(kvp.Key));
        }
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Act & Assert - Should not throw
        _liveWorkingCopy.Dispose();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Act & Assert - Should not throw
        _liveWorkingCopy.Dispose();
        _liveWorkingCopy.Dispose();
        _liveWorkingCopy.Dispose();
    }

    #endregion
}
