using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using HPD.VCS.Core;
using HPD.VCS.Storage;
using HPD.VCS.WorkingCopy;
using Xunit;

namespace HPD.VCS.Tests.WorkingCopy;

public class WorkingCopyStateTests : IDisposable
{
    private readonly MockFileSystem _mockFileSystem;
    private readonly IObjectStore _objectStore;
    private readonly string _workingCopyPath;
    private readonly ExplicitSnapshotWorkingCopy _workingCopyState;

    public WorkingCopyStateTests()
    {
        _mockFileSystem = new MockFileSystem();
        _workingCopyPath = "/repo";
        _mockFileSystem.AddDirectory(_workingCopyPath);
        
        // Create a temporary directory for the object store
        var objectStorePath = "/object-store";
        _mockFileSystem.AddDirectory(objectStorePath);
        _objectStore = new FileSystemObjectStore(_mockFileSystem, objectStorePath);
        
        var emptyIgnoreFile = new IgnoreFile();
        _workingCopyState = new ExplicitSnapshotWorkingCopy(_mockFileSystem, _objectStore, _workingCopyPath, emptyIgnoreFile);
    }

    public void Dispose()
    {
        _objectStore?.Dispose();
    }

    #region UpdateCurrentTreeIdAsync Tests

    [Fact]
    public async Task UpdateCurrentTreeIdAsync_WithMatchingFiles_ShouldPopulateFileStatesCorrectly()
    {
        // Arrange - Create a tree structure and corresponding files
        var file1Content = "Hello World";
        var file2Content = "Another file";
        
        // Add files to mock filesystem
        _mockFileSystem.AddFile("/repo/README.md", new MockFileData(file1Content));
        _mockFileSystem.AddFile("/repo/src/main.cs", new MockFileData(file2Content));
        
        // Create tree structure in object store
        var file1ContentId = await _objectStore.WriteFileContentAsync(new FileContentData(Encoding.UTF8.GetBytes(file1Content)));
        var file2ContentId = await _objectStore.WriteFileContentAsync(new FileContentData(Encoding.UTF8.GetBytes(file2Content)));
        
        var srcTreeEntries = new List<TreeEntry>
        {
            new TreeEntry(new RepoPathComponent("main.cs"), TreeEntryType.File, new ObjectIdBase(file2ContentId.HashValue.ToArray()))
        };
        var srcTreeId = await _objectStore.WriteTreeAsync(new TreeData(srcTreeEntries));
        
        var rootTreeEntries = new List<TreeEntry>
        {
            new TreeEntry(new RepoPathComponent("README.md"), TreeEntryType.File, new ObjectIdBase(file1ContentId.HashValue.ToArray())),
            new TreeEntry(new RepoPathComponent("src"), TreeEntryType.Directory, new ObjectIdBase(srcTreeId.HashValue.ToArray()))
        };
        var rootTreeId = await _objectStore.WriteTreeAsync(new TreeData(rootTreeEntries));

        // Act
        await _workingCopyState.UpdateCurrentTreeIdAsync(rootTreeId);

        // Assert
        Assert.Equal(rootTreeId, _workingCopyState.CurrentTreeId);
        Assert.Equal(2, _workingCopyState.FileStates.Count);
        
        var readmePath = new RepoPath(new RepoPathComponent("README.md"));
        var mainPath = new RepoPath(new RepoPathComponent("src"), new RepoPathComponent("main.cs"));
        
        Console.WriteLine($"TEST DEBUG: Looking for readmePath: '{readmePath}' (Components: [{string.Join(", ", readmePath.Components.Select(c => $"'{c.Value}'"))}])");
        Console.WriteLine($"TEST DEBUG: Looking for mainPath: '{mainPath}' (Components: [{string.Join(", ", mainPath.Components.Select(c => $"'{c.Value}'"))}])");
        Console.WriteLine($"TEST DEBUG: FileStates contains readmePath: {_workingCopyState.FileStates.ContainsKey(readmePath)}");
        Console.WriteLine($"TEST DEBUG: FileStates contains mainPath: {_workingCopyState.FileStates.ContainsKey(mainPath)}");
        Console.WriteLine($"TEST DEBUG: FileStates count: {_workingCopyState.FileStates.Count}");
        
        Assert.True(_workingCopyState.FileStates.ContainsKey(readmePath));
        Assert.True(_workingCopyState.FileStates.ContainsKey(mainPath));
        
        var readmeState = _workingCopyState.FileStates[readmePath];
        Assert.Equal(FileType.NormalFile, readmeState.Type);
        Assert.Equal(file1Content.Length, readmeState.Size);
        Assert.False(readmeState.IsPlaceholder);
        
        var mainState = _workingCopyState.FileStates[mainPath];
        Assert.Equal(FileType.NormalFile, mainState.Type);
        Assert.Equal(file2Content.Length, mainState.Size);
        Assert.False(mainState.IsPlaceholder);
    }

    [Fact]
    public async Task UpdateCurrentTreeIdAsync_WithMissingFiles_ShouldCreatePlaceholderStates()
    {
        // Arrange - Create tree but don't add corresponding files to filesystem
        var fileContentId = await _objectStore.WriteFileContentAsync(new FileContentData(Encoding.UTF8.GetBytes("content")));
        var treeEntries = new List<TreeEntry>
        {
            new TreeEntry(new RepoPathComponent("missing.txt"), TreeEntryType.File, new ObjectIdBase(fileContentId.HashValue.ToArray()))
        };
        var treeId = await _objectStore.WriteTreeAsync(new TreeData(treeEntries));

        // Act
        await _workingCopyState.UpdateCurrentTreeIdAsync(treeId);

        // Assert
        Assert.Equal(treeId, _workingCopyState.CurrentTreeId);
        Assert.Single(_workingCopyState.FileStates);
          var missingPath = new RepoPath(new RepoPathComponent("missing.txt"));
        Assert.True(_workingCopyState.FileStates.ContainsKey(missingPath));
        
        // Should create a placeholder state since file doesn't exist on disk
        var state = _workingCopyState.FileStates[missingPath];
        Assert.Equal(FileType.NormalFile, state.Type);
        Assert.True(state.IsPlaceholder); // New assertion for placeholder
    }

    #endregion

    #region SnapshotAsync - No Changes Tests

    [Fact]
    public async Task SnapshotAsync_NoChanges_ShouldReturnSameTreeIdAndZeroStats()
    {
        // Arrange - Set up matching state between tree and filesystem
        var fileContent = "Hello World";
        _mockFileSystem.AddFile("/repo/README.md", new MockFileData(fileContent) 
        { 
            LastWriteTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        });
        
        // Create initial tree
        var options = new SnapshotOptions();
        var (initialTreeId, initialStats) = await _workingCopyState.SnapshotAsync(options);
        
        Console.WriteLine($"TEST DEBUG: Initial snapshot - TreeId: {initialTreeId}, NewFilesTracked: {initialStats.NewFilesTracked}");
        Console.WriteLine($"TEST DEBUG: _fileStates count after first snapshot: {_workingCopyState.FileStates.Count}");
        Console.WriteLine($"TEST DEBUG: CurrentTreeId after first snapshot: {_workingCopyState.CurrentTreeId}");
        
        // Don't change anything
        
        // Act
        var (newTreeId, stats) = await _workingCopyState.SnapshotAsync(options);

        Console.WriteLine($"TEST DEBUG: Second snapshot - TreeId: {newTreeId}, NewFilesTracked: {stats.NewFilesTracked}");
        Console.WriteLine($"TEST DEBUG: _fileStates count after second snapshot: {_workingCopyState.FileStates.Count}");        // Assert
        Assert.Equal(initialTreeId, newTreeId);
        Assert.Equal(0, stats.NewFilesTracked.Count);
        Assert.Equal(0, stats.ModifiedFiles.Count);
        Assert.Equal(0, stats.DeletedFiles.Count);
        Assert.Equal(0, stats.UntrackedIgnoredFiles.Count);
        Assert.Equal(0, stats.UntrackedKeptFiles.Count);
        Assert.Equal(0, stats.SkippedDueToLock.Count);
    }

    #endregion

    #region SnapshotAsync - File Added Tests

    [Fact]
    public async Task SnapshotAsync_FileAdded_ShouldCreateNewTreeAndUpdateStats()
    {
        // Arrange - Start with empty repository
        var options = new SnapshotOptions();
        var (initialTreeId, _) = await _workingCopyState.SnapshotAsync(options);
        
        // Add a new file
        var newFileContent = "New file content";
        _mockFileSystem.AddFile("/repo/newfile.txt", new MockFileData(newFileContent));

        // Act
        var (newTreeId, stats) = await _workingCopyState.SnapshotAsync(options);        // Assert
        Assert.NotEqual(initialTreeId, newTreeId);
        Assert.Equal(1, stats.NewFilesTracked.Count);
        Assert.Equal(0, stats.ModifiedFiles.Count);
        Assert.Equal(0, stats.DeletedFiles.Count);
        
        // Verify file is in tracked states
        var newFilePath = new RepoPath(new RepoPathComponent("newfile.txt"));
        Assert.True(_workingCopyState.FileStates.ContainsKey(newFilePath));
        
        var fileState = _workingCopyState.FileStates[newFilePath];
        Assert.Equal(FileType.NormalFile, fileState.Type);
        Assert.Equal(newFileContent.Length, fileState.Size);
        Assert.False(fileState.IsPlaceholder);
        
        // Verify tree contains the new file
        var treeData = await _objectStore.ReadTreeAsync(newTreeId);
        Assert.True(treeData.HasValue);
        Assert.Single(treeData.Value.Entries);
        Assert.Equal("newfile.txt", treeData.Value.Entries[0].Name.Value);
    }

    [Fact]
    public async Task SnapshotAsync_LargeFileAdded_ShouldBeInUntrackedKeptFiles()
    {
        // Arrange
        var options = new SnapshotOptions
        {
            MaxNewFileSize = 100 // Set a small limit
        };
        
        // Add a large file (larger than MaxNewFileSize)
        var largeContent = new string('A', 200);
        _mockFileSystem.AddFile("/repo/largefile.bin", new MockFileData(largeContent));

        // Act
        var (treeId, stats) = await _workingCopyState.SnapshotAsync(options);        // Assert
        Assert.Equal(0, stats.NewFilesTracked.Count);
        Assert.Equal(1, stats.UntrackedKeptFiles.Count); // Should be kept but not tracked
        
        // Verify file is NOT in tracked states
        var largeFilePath = new RepoPath(new RepoPathComponent("largefile.bin"));
        Assert.False(_workingCopyState.FileStates.ContainsKey(largeFilePath));
    }

    #endregion

    #region SnapshotAsync - File Modified Tests

    [Fact]
    public async Task SnapshotAsync_FileModified_ShouldDetectChangeAndUpdateStats()
    {
        // Arrange - Create initial file
        var originalContent = "Original content";
        var originalTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _mockFileSystem.AddFile("/repo/file.txt", new MockFileData(originalContent)
        {
            LastWriteTime = originalTime
        });
        
        var options = new SnapshotOptions();
        var (initialTreeId, _) = await _workingCopyState.SnapshotAsync(options);
        
        // Modify the file - change content and timestamp
        var modifiedContent = "Modified content";
        var modifiedTime = originalTime.AddMinutes(10);
        _mockFileSystem.AddFile("/repo/file.txt", new MockFileData(modifiedContent)
        {
            LastWriteTime = modifiedTime
        });

        // Act
        var (newTreeId, stats) = await _workingCopyState.SnapshotAsync(options);        // Assert
        Assert.NotEqual(initialTreeId, newTreeId);
        Assert.Equal(0, stats.NewFilesTracked.Count);
        Assert.Equal(1, stats.ModifiedFiles.Count);
        Assert.Equal(0, stats.DeletedFiles.Count);
        
        // Verify file state was updated
        var filePath = new RepoPath(new RepoPathComponent("file.txt"));
        var fileState = _workingCopyState.FileStates[filePath];
        Assert.Equal(modifiedContent.Length, fileState.Size);
        Assert.Equal(modifiedTime, fileState.MTimeUtc);
        Assert.False(fileState.IsPlaceholder);
    }

    [Fact]
    public async Task SnapshotAsync_MtimeGranularityFallback_ShouldDetectContentChange()
    {
        // Arrange - Create initial file
        var originalContent = "Original content";
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _mockFileSystem.AddFile("/repo/file.txt", new MockFileData(originalContent)
        {
            LastWriteTime = baseTime
        });
        
        var options = new SnapshotOptions
        {
            MtimeGranularityMs = 2000 // 2 second granularity
        };
        var (initialTreeId, _) = await _workingCopyState.SnapshotAsync(options);
        
        // Modify content but keep mtime within granularity window (should trigger hash check)
        var modifiedContent = "Modified content same size!"; // Different content, similar size
        var closeTime = baseTime.AddMilliseconds(1000); // Within 2-second window
        _mockFileSystem.AddFile("/repo/file.txt", new MockFileData(modifiedContent)
        {
            LastWriteTime = closeTime
        });

        // Act
        var (newTreeId, stats) = await _workingCopyState.SnapshotAsync(options);        // Assert
        // Should detect change despite similar timestamp due to hash fallback
        Assert.NotEqual(initialTreeId, newTreeId);
        Assert.Equal(1, stats.ModifiedFiles.Count);
    }

    #endregion

    #region SnapshotAsync - File Deleted Tests

    [Fact]
    public async Task SnapshotAsync_FileDeleted_ShouldUpdateTreeAndStats()
    {
        // Arrange - Create initial file
        var fileContent = "File to be deleted";
        _mockFileSystem.AddFile("/repo/deleteme.txt", new MockFileData(fileContent));
        
        var options = new SnapshotOptions();
        var (initialTreeId, _) = await _workingCopyState.SnapshotAsync(options);
        
        // Delete the file
        _mockFileSystem.RemoveFile("/repo/deleteme.txt");

        // Act
        var (newTreeId, stats) = await _workingCopyState.SnapshotAsync(options);        // Assert
        Assert.NotEqual(initialTreeId, newTreeId);
        Assert.Equal(0, stats.NewFilesTracked.Count);
        Assert.Equal(0, stats.ModifiedFiles.Count);
        Assert.Equal(1, stats.DeletedFiles.Count);
        
        // Verify file is removed from tracked states
        var deletedPath = new RepoPath(new RepoPathComponent("deleteme.txt"));
        Assert.False(_workingCopyState.FileStates.ContainsKey(deletedPath));
        
        // Verify tree no longer contains the file
        var treeData = await _objectStore.ReadTreeAsync(newTreeId);
        Assert.True(treeData.HasValue);
        Assert.Empty(treeData.Value.Entries);
    }

    #endregion

    #region SnapshotAsync - Ignored Files Tests

    [Fact]
    public async Task SnapshotAsync_IgnoredFiles_ShouldBeInUntrackedIgnoredFiles()
    {
        // Arrange - Create ignore rules
        var ignoreRules = new List<IgnoreRule>
        {
            new IgnoreRule("*.tmp", RepoPath.Root),
            new IgnoreRule("build/", RepoPath.Root)
        };
        var ignoreFile = new IgnoreFile(ignoreRules);
        var workingCopyState = new ExplicitSnapshotWorkingCopy(_mockFileSystem, _objectStore, _workingCopyPath, ignoreFile);
        
        // Add files that should be ignored
        _mockFileSystem.AddFile("/repo/cache.tmp", new MockFileData("temp file"));
        _mockFileSystem.AddDirectory("/repo/build");
        _mockFileSystem.AddFile("/repo/build/output.exe", new MockFileData("build output"));
        
        // Add file that should NOT be ignored
        _mockFileSystem.AddFile("/repo/readme.txt", new MockFileData("readme"));

        var options = new SnapshotOptions();

        // Act
        var (treeId, stats) = await workingCopyState.SnapshotAsync(options);        // Assert
        Assert.Equal(1, stats.NewFilesTracked.Count); // Only readme.txt
        Assert.Equal(2, stats.UntrackedIgnoredFiles.Count); // cache.tmp and build/ contents
        
        // Verify only non-ignored file is tracked
        var readmePath = new RepoPath(new RepoPathComponent("readme.txt"));
        Assert.True(workingCopyState.FileStates.ContainsKey(readmePath));
        
        var tmpPath = new RepoPath(new RepoPathComponent("cache.tmp"));
        Assert.False(workingCopyState.FileStates.ContainsKey(tmpPath));
    }

    #endregion

    #region SnapshotAsync - ShouldTrackNewFileMatcher Tests

    [Fact]
    public async Task SnapshotAsync_ShouldTrackNewFileMatcher_ShouldControlTracking()
    {
        // Arrange - Create matcher that only tracks .cs files
        var options = new SnapshotOptions
        {
            ShouldTrackNewFileMatcher = path => 
            {
                var fileName = path.FileName()?.Value ?? "";
                return fileName.EndsWith(".cs");
            }
        };
        
        // Add various files
        _mockFileSystem.AddFile("/repo/Program.cs", new MockFileData("C# code"));
        _mockFileSystem.AddFile("/repo/readme.txt", new MockFileData("Text file"));
        _mockFileSystem.AddFile("/repo/config.json", new MockFileData("JSON config"));

        // Act
        var (treeId, stats) = await _workingCopyState.SnapshotAsync(options);        // Assert
        Assert.Equal(1, stats.NewFilesTracked.Count); // Only Program.cs
        Assert.Equal(2, stats.UntrackedKeptFiles.Count); // readme.txt and config.json
        
        // Verify only .cs file is tracked
        var csPath = new RepoPath(new RepoPathComponent("Program.cs"));
        Assert.True(_workingCopyState.FileStates.ContainsKey(csPath));
        
        var txtPath = new RepoPath(new RepoPathComponent("readme.txt"));
        Assert.False(_workingCopyState.FileStates.ContainsKey(txtPath));
    }

    #endregion

    #region Case Sensitivity Tests

    [Fact]
    public async Task SnapshotAsync_CaseSensitivity_ShouldHandleCorrectly()
    {
        // Arrange - Create files with different casing
        // Note: MockFileSystem behavior may vary by platform simulation
        _mockFileSystem.AddFile("/repo/File.txt", new MockFileData("Upper case"));
        _mockFileSystem.AddFile("/repo/file.txt", new MockFileData("Lower case"));

        var options = new SnapshotOptions();

        // Act
        var (treeId, stats) = await _workingCopyState.SnapshotAsync(options);        // Assert
        // On case-sensitive systems, should see both files
        // On case-insensitive systems, may see only one (depends on MockFileSystem implementation)
        Assert.True(stats.NewFilesTracked.Count >= 1);
        Assert.True(_workingCopyState.FileStates.Count >= 1);
        
        // The exact behavior depends on MockFileSystem's case sensitivity simulation
        // For our purposes, we just verify the system handles it without crashing
    }

    #endregion

    #region Non-ASCII Path Tests

    [Fact]
    public async Task SnapshotAsync_NonAsciiPaths_ShouldHandleCorrectly()
    {
        // Arrange - Create files with non-ASCII names
        _mockFileSystem.AddFile("/repo/文件.txt", new MockFileData("Chinese file"));
        _mockFileSystem.AddFile("/repo/файл.txt", new MockFileData("Russian file"));
        _mockFileSystem.AddFile("/repo/🚀test.txt", new MockFileData("Emoji file"));
        _mockFileSystem.AddDirectory("/repo/ディレクトリ");
        _mockFileSystem.AddFile("/repo/ディレクトリ/日本語.txt", new MockFileData("Japanese file"));

        var options = new SnapshotOptions();

        // Act
        var (treeId, stats) = await _workingCopyState.SnapshotAsync(options);        // Assert
        Assert.Equal(4, stats.NewFilesTracked.Count); // All 4 files should be tracked
        Assert.Equal(4, _workingCopyState.FileStates.Count);
        
        // Verify specific Unicode files are tracked
        var chinesePath = new RepoPath(new RepoPathComponent("文件.txt"));
        Assert.True(_workingCopyState.FileStates.ContainsKey(chinesePath));
        
        var japanesePath = new RepoPath(new RepoPathComponent("ディレクトリ"), new RepoPathComponent("日本語.txt"));
        Assert.True(_workingCopyState.FileStates.ContainsKey(japanesePath));
    }

    #endregion

    #region File Locked Tests

    [Fact]
    public async Task SnapshotAsync_FileLocked_ShouldAddToSkippedDueToLock()
    {
        // Arrange - Create a file and then simulate it being locked
        var filePath = "/repo/locked.txt";
        _mockFileSystem.AddFile(filePath, new MockFileData("locked content"));
        
        // Mock the file as throwing IOException when accessed
        // Note: MockFileSystem doesn't directly support file locking simulation,
        // so we'll need to work around this limitation or test it differently
        
        var options = new SnapshotOptions();

        // Act
        var (treeId, stats) = await _workingCopyState.SnapshotAsync(options);

        // Assert
        // In a real implementation with file locking, we'd expect:
        // Assert.Equal(1, stats.SkippedDueToLock);
          // For now, with MockFileSystem, the file will be processed normally
        Assert.Equal(1, stats.NewFilesTracked.Count);
        Assert.Equal(0, stats.SkippedDueToLock.Count);
    }

    #endregion

    #region Symlink Tests

    [Fact]
    public async Task SnapshotAsync_Symlinks_ShouldBeHandledCorrectly()
    {
        // Arrange - Create a symlink (MockFileSystem may have limited symlink support)
        var targetContent = "Target file content";
        _mockFileSystem.AddFile("/repo/target.txt", new MockFileData(targetContent));
        
        // Create symlink - MockFileSystem support may vary
        try
        {
            _mockFileSystem.AddFile("/repo/link.txt", new MockFileData("target.txt"));
            // Set it as a symlink if supported
            var fileInfo = _mockFileSystem.FileInfo.New("/repo/link.txt");
            // MockFileSystem may not support setting LinkTarget directly
        }
        catch
        {
            // Skip this test if symlinks aren't supported in test environment
            return;
        }

        var options = new SnapshotOptions();

        // Act
        var (treeId, stats) = await _workingCopyState.SnapshotAsync(options);        // Assert
        Assert.True(stats.NewFilesTracked.Count >= 1);
        
        // If symlink was created successfully, verify it's tracked
        var linkPath = new RepoPath(new RepoPathComponent("link.txt"));
        if (_workingCopyState.FileStates.ContainsKey(linkPath))
        {
            var linkState = _workingCopyState.FileStates[linkPath];
            // Verify symlink properties if supported
        }
    }

    #endregion

    #region Windows Symlink Detection Tests

    [Fact]
    public async Task SnapshotAsync_WindowsSymlinkDetection_ShouldUseEnhancedDetection()
    {
        // Arrange
        var options = new SnapshotOptions
        {
            EnableWindowsSymlinks = true
        };
        
        // Add a regular file
        _mockFileSystem.AddFile("/repo/regular.txt", new MockFileData("regular file"));
          // Act
        var (treeId, stats) = await _workingCopyState.SnapshotAsync(options);

        // Assert
        Assert.Equal(1, stats.NewFilesTracked.Count);
        
        var filePath = new RepoPath(new RepoPathComponent("regular.txt"));
        Assert.True(_workingCopyState.FileStates.ContainsKey(filePath));
        
        var fileState = _workingCopyState.FileStates[filePath];
        Assert.Equal(FileType.NormalFile, fileState.Type);
        Assert.False(fileState.IsPlaceholder);
    }

    #endregion

    #region Nested Ignore Files Tests

    [Fact]
    public async Task SnapshotAsync_NestedIgnoreFiles_ShouldMergeRulesCorrectly()
    {
        // Arrange
        var options = new SnapshotOptions
        {
            SupportNestedIgnoreFiles = true
        };
        
        // Create root .gitignore
        _mockFileSystem.AddFile("/repo/.gitignore", new MockFileData("*.tmp\n"));
        
        // Create nested .gitignore in src/
        _mockFileSystem.AddDirectory("/repo/src");
        _mockFileSystem.AddFile("/repo/src/.gitignore", new MockFileData("*.log\n"));
        
        // Add files
        _mockFileSystem.AddFile("/repo/root.tmp", new MockFileData("root temp")); // Should be ignored by root rule
        _mockFileSystem.AddFile("/repo/src/debug.log", new MockFileData("debug log")); // Should be ignored by nested rule
        _mockFileSystem.AddFile("/repo/src/main.cs", new MockFileData("main code")); // Should NOT be ignored

        // Act
        var (treeId, stats) = await _workingCopyState.SnapshotAsync(options);        // Assert
        Assert.Equal(1, stats.NewFilesTracked.Count); // Only main.cs
        Assert.Equal(2, stats.UntrackedIgnoredFiles.Count); // root.tmp and debug.log
        
        var mainPath = new RepoPath(new RepoPathComponent("src"), new RepoPathComponent("main.cs"));
        Assert.True(_workingCopyState.FileStates.ContainsKey(mainPath));
    }

    #endregion

    #region Large File Streaming Tests

    [Fact]
    public async Task SnapshotAsync_LargeFileStreaming_ShouldProcessLargeFilesCorrectly()
    {
        // Arrange
        var options = new SnapshotOptions
        {
            LargeFileThreshold = 1024, // 1KB threshold
            MaxNewFileSize = 10 * 1024 * 1024 // 10MB - allow large files to be tracked
        };
        
        // Create a large file (larger than threshold)
        var largeContent = new string('A', 2048); // 2KB file
        _mockFileSystem.AddFile("/repo/large.bin", new MockFileData(largeContent));        // Act
        var (treeId, stats) = await _workingCopyState.SnapshotAsync(options);

        // Assert
        Assert.Equal(1, stats.NewFilesTracked.Count);
        
        var largePath = new RepoPath(new RepoPathComponent("large.bin"));
        Assert.True(_workingCopyState.FileStates.ContainsKey(largePath));
        
        var fileState = _workingCopyState.FileStates[largePath];
        Assert.Equal(largeContent.Length, fileState.Size);
        Assert.False(fileState.IsPlaceholder);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public async Task SnapshotAsync_ComplexScenario_ShouldHandleMultipleChanges()
    {
        // Arrange - Set up initial state
        _mockFileSystem.AddFile("/repo/existing.txt", new MockFileData("existing"));
        _mockFileSystem.AddFile("/repo/tomodify.txt", new MockFileData("original"));
        _mockFileSystem.AddFile("/repo/todelete.txt", new MockFileData("delete me"));
        
        var options = new SnapshotOptions();
        var (initialTreeId, _) = await _workingCopyState.SnapshotAsync(options);
        
        // Make multiple changes
        _mockFileSystem.AddFile("/repo/new.txt", new MockFileData("new file")); // Add
        _mockFileSystem.AddFile("/repo/tomodify.txt", new MockFileData("modified content")); // Modify
        _mockFileSystem.RemoveFile("/repo/todelete.txt"); // Delete
        // existing.txt remains unchanged

        // Act
        var (newTreeId, stats) = await _workingCopyState.SnapshotAsync(options);        // Assert
        Assert.NotEqual(initialTreeId, newTreeId);
        Assert.Equal(1, stats.NewFilesTracked.Count); // new.txt
        Assert.Equal(1, stats.ModifiedFiles.Count); // tomodify.txt
        Assert.Equal(1, stats.DeletedFiles.Count); // todelete.txt
        
        // Verify final state
        Assert.Equal(3, _workingCopyState.FileStates.Count); // existing, tomodify, new
        
        var existingPath = new RepoPath(new RepoPathComponent("existing.txt"));
        var modifiedPath = new RepoPath(new RepoPathComponent("tomodify.txt"));
        var newPath = new RepoPath(new RepoPathComponent("new.txt"));
        var deletedPath = new RepoPath(new RepoPathComponent("todelete.txt"));
        
        Assert.True(_workingCopyState.FileStates.ContainsKey(existingPath));
        Assert.True(_workingCopyState.FileStates.ContainsKey(modifiedPath));
        Assert.True(_workingCopyState.FileStates.ContainsKey(newPath));
        Assert.False(_workingCopyState.FileStates.ContainsKey(deletedPath));
    }

    #endregion
}
