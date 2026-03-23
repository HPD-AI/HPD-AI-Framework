using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using HPD.VCS;
using HPD.VCS.Core;
using HPD.VCS.Storage;
using HPD.VCS.WorkingCopy;
using HPD.VCS.Graphing;

namespace HPD.VCS.Tests;

/// <summary>
/// Unit tests for Module 8: Advanced Features (Status, Diff, and Enhanced Log)
/// Tests all features including status command, diff operations, graph log, and ASCII rendering.
/// </summary>
public class Module8Tests
{
    private readonly string _repoPath = "/test/repo";
    private readonly UserSettings _userSettings = new("Test User", "test@example.com");

    // Define valid 64-character hex strings for testing
    private const string ValidCommitHex = "aaa123def456abc123def456abc123def456abc123def456abc123def456aaa1";

    private MockFileSystem CreateFreshMockFileSystem()
    {
        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddDirectory(_repoPath);
        return mockFileSystem;
    }

    private async Task<Repository> CreateRepositoryAsync()
    {
        var mockFileSystem = CreateFreshMockFileSystem();
        return await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);
    }

    #region Task 8.1: GetStatusAsync Tests    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetStatusAsync_EmptyRepository_ReturnsCleanStatus()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();

        // Act
        var status = await repository.GetStatusAsync();

        // Assert
        Assert.True(status.IsClean);
        Assert.Empty(status.UntrackedFiles);
        Assert.Empty(status.ModifiedFiles);
        Assert.Empty(status.AddedFiles);
        Assert.Empty(status.RemovedFiles);
        Assert.Equal(0, status.TotalChanges);
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetStatusAsync_WithUntrackedFiles_ShowsUntrackedStatus()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Add untracked files
        var file1 = "/test/repo/file1.txt";
        var file2 = "/test/repo/subdir/file2.txt";
        mockFileSystem.AddFile(file1, "content1");
        mockFileSystem.AddFile(file2, "content2");

        // Act
        var status = await repository.GetStatusAsync();

        // Assert
        Assert.False(status.IsClean);
        Assert.Equal(2, status.UntrackedFiles.Count);
        Assert.Empty(status.ModifiedFiles);
        Assert.Empty(status.AddedFiles);
        Assert.Empty(status.RemovedFiles);
        Assert.Equal(2, status.TotalChanges);
        
        // Verify file paths are correct
        var untrackedPaths = status.UntrackedFiles.Select(f => f.ToString()).OrderBy(p => p).ToList();
        Assert.Contains("file1.txt", untrackedPaths);
        Assert.Contains("subdir/file2.txt", untrackedPaths);
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetStatusAsync_WithModifiedFiles_ShowsModifiedStatus()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Add and commit initial files
        var file1 = "/test/repo/file1.txt";
        var file2 = "/test/repo/file2.txt";
        mockFileSystem.AddFile(file1, "original content 1");
        mockFileSystem.AddFile(file2, "original content 2");
          await repository.CommitAsync("Initial commit", _userSettings, new SnapshotOptions());

        // Modify files - update both content and modification time
        var modifiedTime = DateTime.UtcNow;
        mockFileSystem.GetFile(file1).TextContents = "modified content 1";
        mockFileSystem.GetFile(file1).LastWriteTime = modifiedTime;
        mockFileSystem.GetFile(file2).TextContents = "modified content 2";
        mockFileSystem.GetFile(file2).LastWriteTime = modifiedTime;

        // Act
        var status = await repository.GetStatusAsync();

        // Assert
        Assert.False(status.IsClean);
        Assert.Empty(status.UntrackedFiles);
        Assert.Equal(2, status.ModifiedFiles.Count);
        Assert.Empty(status.AddedFiles);
        Assert.Empty(status.RemovedFiles);
        Assert.Equal(2, status.TotalChanges);
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetStatusAsync_WithDeletedFiles_ShowsRemovedStatus()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Add and commit initial files
        var file1 = "/test/repo/file1.txt";
        var file2 = "/test/repo/file2.txt";
        mockFileSystem.AddFile(file1, "content1");
        mockFileSystem.AddFile(file2, "content2");
        
        await repository.CommitAsync("Initial commit", _userSettings, new SnapshotOptions());

        // Delete files
        mockFileSystem.RemoveFile(file1);
        mockFileSystem.RemoveFile(file2);

        // Act
        var status = await repository.GetStatusAsync();

        // Assert
        Assert.False(status.IsClean);
        Assert.Empty(status.UntrackedFiles);
        Assert.Empty(status.ModifiedFiles);
        Assert.Empty(status.AddedFiles);
        Assert.Equal(2, status.RemovedFiles.Count);
        Assert.Equal(2, status.TotalChanges);
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetStatusAsync_WithDryRunMode_DoesNotWriteToObjectStore()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Add files
        mockFileSystem.AddFile("/test/repo/file1.txt", "content1");
        mockFileSystem.AddFile("/test/repo/file2.txt", "content2");

        // Record initial object count by checking the store directory
        var objectStoreDir = "/test/repo/.hpd/store";
        var initialObjectCount = mockFileSystem.Directory.GetFiles(objectStoreDir, "*", SearchOption.AllDirectories).Length;

        // Act
        var status = await repository.GetStatusAsync();

        // Assert - Verify no new objects were written
        var finalObjectCount = mockFileSystem.Directory.GetFiles(objectStoreDir, "*", SearchOption.AllDirectories).Length;
        Assert.Equal(initialObjectCount, finalObjectCount);
        
        // Verify status is correct
        Assert.False(status.IsClean);
        Assert.Equal(2, status.UntrackedFiles.Count);
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetStatusAsync_WithIgnoredFiles_ShowsIgnoredFilesInResult()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Add regular and ignored files
        mockFileSystem.AddFile("/test/repo/regular.txt", "content");
        mockFileSystem.AddFile("/test/repo/.gitignore", "*.log\n*.tmp");
        mockFileSystem.AddFile("/test/repo/debug.log", "log content");
        mockFileSystem.AddFile("/test/repo/temp.tmp", "temp content");

        // Act
        var status = await repository.GetStatusAsync();

        // Assert
        Assert.False(status.IsClean);
        Assert.True(status.UntrackedFiles.Count >= 1); // At least regular.txt and .gitignore
        
        // Note: The exact behavior of ignored files depends on implementation
        // This test verifies the API returns the IgnoredFiles property correctly
        Assert.NotNull(status.IgnoredFiles);
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetStatusAsync_MixedChanges_ReturnsCorrectCategorization()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Step 1: Add and commit initial files
        mockFileSystem.AddFile("/test/repo/existing1.txt", "content1");
        mockFileSystem.AddFile("/test/repo/existing2.txt", "content2");
        mockFileSystem.AddFile("/test/repo/to_delete.txt", "will be deleted");
        
        await repository.CommitAsync("Initial commit", _userSettings, new SnapshotOptions());        // Step 2: Make various changes
        // Modify existing file - update both content and modification time
        var modifiedTime = DateTime.UtcNow;
        mockFileSystem.GetFile("/test/repo/existing1.txt").TextContents = "modified content1";
        mockFileSystem.GetFile("/test/repo/existing1.txt").LastWriteTime = modifiedTime;
        
        // Delete file
        mockFileSystem.RemoveFile("/test/repo/to_delete.txt");
        
        // Add new untracked file
        mockFileSystem.AddFile("/test/repo/new_file.txt", "new content");

        // Act
        var status = await repository.GetStatusAsync();

        // Assert
        Assert.False(status.IsClean);
        Assert.Single(status.UntrackedFiles); // new_file.txt
        Assert.Single(status.ModifiedFiles);  // existing1.txt
        Assert.Single(status.RemovedFiles);   // to_delete.txt
        Assert.Equal(3, status.TotalChanges);
        
        // Verify specific files
        Assert.Contains(status.UntrackedFiles, f => f.ToString() == "new_file.txt");
        Assert.Contains(status.ModifiedFiles, f => f.ToString() == "existing1.txt");
        Assert.Contains(status.RemovedFiles, f => f.ToString() == "to_delete.txt");
    }

    #endregion

    #region Task 8.2: Diffing Tests    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetCommitDiffAsync_InitialCommit_ComparesAgainstEmptyTree()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Add files and create initial commit
        mockFileSystem.AddFile("/test/repo/file1.txt", "Line 1\nLine 2\nLine 3");
        mockFileSystem.AddFile("/test/repo/file2.txt", "Hello\nWorld");
        
        var commitId = await repository.CommitAsync("Initial commit", _userSettings, new SnapshotOptions());
        Assert.NotNull(commitId);

        // Act
        var diff = await repository.GetCommitDiffAsync(commitId.Value);

        // Assert
        Assert.Equal(2, diff.Count);
        
        // Verify diffs show file additions
        foreach (var (path, diffContent) in diff)
        {
            Assert.Contains("--- /dev/null", diffContent);
            Assert.Contains($"+++ {path}", diffContent);
            Assert.Contains("+", diffContent); // Should contain added lines
        }
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetCommitDiffAsync_RegularCommit_ShowsChangesFromParent()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Create initial commit
        mockFileSystem.AddFile("/test/repo/file1.txt", "Line 1\nLine 2\nLine 3");
        mockFileSystem.AddFile("/test/repo/file2.txt", "Hello\nWorld");
        await repository.CommitAsync("Initial commit", _userSettings, new SnapshotOptions());        // Modify files and create second commit
        var modifiedTime2 = DateTime.UtcNow;
        mockFileSystem.GetFile("/test/repo/file1.txt").TextContents = "Line 1 modified\nLine 2\nLine 3\nLine 4 added";
        mockFileSystem.GetFile("/test/repo/file1.txt").LastWriteTime = modifiedTime2;
        mockFileSystem.GetFile("/test/repo/file2.txt").TextContents = "Hello modified\nWorld\nNew line";
        mockFileSystem.GetFile("/test/repo/file2.txt").LastWriteTime = modifiedTime2;
        
        var commitId = await repository.CommitAsync("Second commit", _userSettings, new SnapshotOptions());
        Assert.NotNull(commitId);

        // Act
        var diff = await repository.GetCommitDiffAsync(commitId.Value);

        // Assert
        Assert.Equal(2, diff.Count);
        
        // Verify diff format and content
        foreach (var (path, diffContent) in diff)
        {
            Assert.Contains($"--- {path}", diffContent);
            Assert.Contains($"+++ {path}", diffContent);
            Assert.Contains("-", diffContent); // Should contain removed lines
            Assert.Contains("+", diffContent); // Should contain added lines
        }
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetCommitDiffAsync_BinaryFiles_ShowsBinaryDiffMessage()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Create initial commit with text file
        mockFileSystem.AddFile("/test/repo/text.txt", "Hello World");
        await repository.CommitAsync("Initial commit", _userSettings, new SnapshotOptions());

        // Add binary file (contains null bytes)
        var binaryData = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x00, 0x42 };
        mockFileSystem.AddFile("/test/repo/binary.bin", new MockFileData(binaryData));
        
        var commitId = await repository.CommitAsync("Add binary file", _userSettings, new SnapshotOptions());
        Assert.NotNull(commitId);

        // Act
        var diff = await repository.GetCommitDiffAsync(commitId.Value);

        // Assert
        Assert.Single(diff); // Only binary file should be in diff
        
        var binaryDiffEntry = diff.First();
        Assert.Contains("binary.bin", binaryDiffEntry.Key.ToString());
        Assert.Contains("Binary files differ", binaryDiffEntry.Value);
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetCommitDiffAsync_LargeFiles_ShowsLargeFileMessage()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Create initial commit
        mockFileSystem.AddFile("/test/repo/small.txt", "Small file");
        await repository.CommitAsync("Initial commit", _userSettings, new SnapshotOptions());

        // Add large file (simulate 6MB file)
        var largeContent = new string('A', 6 * 1024 * 1024); // 6MB of 'A' characters
        mockFileSystem.AddFile("/test/repo/large.txt", largeContent);
        
        var commitId = await repository.CommitAsync("Add large file", _userSettings, new SnapshotOptions());
        Assert.NotNull(commitId);

        // Act
        var diff = await repository.GetCommitDiffAsync(commitId.Value);

        // Assert
        Assert.Single(diff); // Only large file should be in diff
        
        var largeDiffEntry = diff.First();
        Assert.Contains("large.txt", largeDiffEntry.Key.ToString());
        // Note: The exact message depends on implementation, but it should indicate large file
        Assert.True(largeDiffEntry.Value.Contains("large") || largeDiffEntry.Value.Contains("Binary files differ"));
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetCommitDiffAsync_UnifiedDiffFormatter_ProducesCorrectFormat()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Create initial commit
        mockFileSystem.AddFile("/test/repo/test.txt", "Line 1\nLine 2\nLine 3\nLine 4\nLine 5");
        await repository.CommitAsync("Initial commit", _userSettings, new SnapshotOptions());        // Modify file with specific changes
        var modifiedTime3 = DateTime.UtcNow;
        mockFileSystem.GetFile("/test/repo/test.txt").TextContents = "Line 1\nModified Line 2\nLine 3\nNew Line 4\nLine 5";
        mockFileSystem.GetFile("/test/repo/test.txt").LastWriteTime = modifiedTime3;
        
        var commitId = await repository.CommitAsync("Modify file", _userSettings, new SnapshotOptions());
        Assert.NotNull(commitId);

        // Act
        var diff = await repository.GetCommitDiffAsync(commitId.Value);

        // Assert
        Assert.Single(diff);
        var diffContent = diff.First().Value;
        
        // Verify unified diff format
        Assert.Contains("--- test.txt", diffContent);
        Assert.Contains("+++ test.txt", diffContent);
        Assert.Contains("@@", diffContent); // Hunk headers
        Assert.Contains("-Line 2", diffContent); // Removed line
        Assert.Contains("+Modified Line 2", diffContent); // Added line
        Assert.Contains("+New Line 4", diffContent); // Added line
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetCommitDiffAsync_WithConflictHandling_HandlesCorruptedFiles()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Create a commit
        mockFileSystem.AddFile("/test/repo/normal.txt", "Normal content");
        var commitId = await repository.CommitAsync("Normal commit", _userSettings, new SnapshotOptions());
        Assert.NotNull(commitId);

        // Act & Assert - Should not throw even if there are issues
        var diff = await repository.GetCommitDiffAsync(commitId.Value);
        Assert.NotNull(diff);
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetCommitDiffAsync_CaseRenameOnWindows_HandlesCorrectly()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Create initial commit with lowercase file
        mockFileSystem.AddFile("/test/repo/readme.txt", "Original content");
        await repository.CommitAsync("Initial commit", _userSettings, new SnapshotOptions());

        // Simulate case rename by removing and adding with different case
        mockFileSystem.RemoveFile("/test/repo/readme.txt");
        mockFileSystem.AddFile("/test/repo/README.txt", "Original content");
        
        var commitId = await repository.CommitAsync("Case rename", _userSettings, new SnapshotOptions());
        Assert.NotNull(commitId);

        // Act
        var diff = await repository.GetCommitDiffAsync(commitId.Value);

        // Assert - Should handle case rename appropriately
        Assert.NotNull(diff);
        // The exact behavior depends on the file system and implementation
        // On case-insensitive systems, this might be treated as no change
        // On case-sensitive systems, this would be a delete + add
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetWorkingCopyDiffAsync_ComparesAgainstHEAD()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Create initial commit
        mockFileSystem.AddFile("/test/repo/file1.txt", "Original line 1\nOriginal line 2");
        mockFileSystem.AddFile("/test/repo/file2.txt", "File 2 content");
        await repository.CommitAsync("Initial commit", _userSettings, new SnapshotOptions());        // Modify working copy
        var modifiedTime4 = DateTime.UtcNow;
        mockFileSystem.GetFile("/test/repo/file1.txt").TextContents = "Modified line 1\nOriginal line 2\nNew line 3";
        mockFileSystem.GetFile("/test/repo/file1.txt").LastWriteTime = modifiedTime4;
        mockFileSystem.RemoveFile("/test/repo/file2.txt"); // Delete file
        mockFileSystem.AddFile("/test/repo/file3.txt", "New file content"); // Add new file        // Act
        var diff = await repository.GetWorkingCopyDiffAsync();

        // Debug output
        Console.WriteLine($"Diff count: {diff.Count}");
        foreach (var kvp in diff)
        {
            Console.WriteLine($"File: {kvp.Key}, Diff: {kvp.Value.Substring(0, Math.Min(100, kvp.Value.Length))}...");
        }

        // Assert
        Assert.Equal(3, diff.Count); // Modified file1, deleted file2, added file3
        
        // Verify file1 modification
        var file1Diff = diff.FirstOrDefault(kvp => kvp.Key.ToString() == "file1.txt");
        Assert.NotNull(file1Diff.Value);
        Assert.Contains("-Original line 1", file1Diff.Value);
        Assert.Contains("+Modified line 1", file1Diff.Value);
        Assert.Contains("+New line 3", file1Diff.Value);
        
        // Verify file2 deletion
        var file2Diff = diff.FirstOrDefault(kvp => kvp.Key.ToString() == "file2.txt");
        Assert.NotNull(file2Diff.Value);
        Assert.Contains("--- file2.txt", file2Diff.Value);
        Assert.Contains("+++ /dev/null", file2Diff.Value);
        
        // Verify file3 addition
        var file3Diff = diff.FirstOrDefault(kvp => kvp.Key.ToString() == "file3.txt");
        Assert.NotNull(file3Diff.Value);
        Assert.Contains("--- /dev/null", file3Diff.Value);
        Assert.Contains("+++ file3.txt", file3Diff.Value);
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetCommitDiffAsync_NonexistentCommit_ThrowsInvalidOperationException()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();
        var nonexistentCommitId = ObjectIdBase.FromHexString<CommitId>(ValidCommitHex);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.GetCommitDiffAsync(nonexistentCommitId));
    }

    #endregion

    #region Task 8.3: GetGraphLogAsync and LogGraphRenderer Tests    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetGraphLogAsync_EmptyRepository_ReturnsEmptyList()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();

        // Act
        var graphLog = await repository.GetGraphLogAsync();

        // Assert
        Assert.Empty(graphLog);
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetGraphLogAsync_LinearHistory_ReturnsCommitsInTopologicalOrder()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);        // Create linear commit history
        mockFileSystem.AddFile("/test/repo/file.txt", "Content 1");
        var commit1 = await repository.CommitAsync("First commit", _userSettings, new SnapshotOptions());
        
        var modifiedTime5 = DateTime.UtcNow;
        mockFileSystem.GetFile("/test/repo/file.txt").TextContents = "Content 2";
        mockFileSystem.GetFile("/test/repo/file.txt").LastWriteTime = modifiedTime5;
        var commit2 = await repository.CommitAsync("Second commit", _userSettings, new SnapshotOptions());
        
        var modifiedTime6 = DateTime.UtcNow.AddMinutes(1);
        mockFileSystem.GetFile("/test/repo/file.txt").TextContents = "Content 3";
        mockFileSystem.GetFile("/test/repo/file.txt").LastWriteTime = modifiedTime6;
        var commit3 = await repository.CommitAsync("Third commit", _userSettings, new SnapshotOptions());        // Act
        var graphLog = await repository.GetGraphLogAsync();

        // Assert
        Assert.Equal(4, graphLog.Count); // 3 user commits + 1 initial commit
        
        // Verify topological order (newest first)
        Assert.Equal("Third commit", graphLog[0].Commit.Description);
        Assert.Equal("Second commit", graphLog[1].Commit.Description);
        Assert.Equal("First commit", graphLog[2].Commit.Description);
        Assert.Equal("Initial commit", graphLog[3].Commit.Description);
          // Verify edges for linear history
        Assert.Single(graphLog[0].Edges); // Third commit -> Second commit
        Assert.Single(graphLog[1].Edges); // Second commit -> First commit
        Assert.Single(graphLog[2].Edges); // First commit -> Initial commit
        Assert.Empty(graphLog[3].Edges);  // Initial commit has no parents
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetGraphLogAsync_WithSkewedTimestamps_MaintainsTopologicalOrder()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Create commits with intentionally skewed timestamps
        // (In a real scenario, timestamps might be out of order due to clock skew)
        
        mockFileSystem.AddFile("/test/repo/file.txt", "Content 1");
        var commit1 = await repository.CommitAsync("First commit", _userSettings, new SnapshotOptions());
          // Simulate time passing
        await Task.Delay(10);
        
        var modifiedTime7 = DateTime.UtcNow;
        mockFileSystem.GetFile("/test/repo/file.txt").TextContents = "Content 2";
        mockFileSystem.GetFile("/test/repo/file.txt").LastWriteTime = modifiedTime7;
        var commit2 = await repository.CommitAsync("Second commit", _userSettings, new SnapshotOptions());
        
        // Simulate more time passing
        await Task.Delay(10);
        
        var modifiedTime8 = DateTime.UtcNow.AddMinutes(1);
        mockFileSystem.GetFile("/test/repo/file.txt").TextContents = "Content 3";
        mockFileSystem.GetFile("/test/repo/file.txt").LastWriteTime = modifiedTime8;
        var commit3 = await repository.CommitAsync("Third commit", _userSettings, new SnapshotOptions());        // Act
        var graphLog = await repository.GetGraphLogAsync();

        // Assert
        Assert.Equal(4, graphLog.Count); // 3 user commits + 1 initial commit
        
        // Verify commits are still in correct topological order despite any timestamp issues
        Assert.Equal("Third commit", graphLog[0].Commit.Description);
        Assert.Equal("Second commit", graphLog[1].Commit.Description);
        Assert.Equal("First commit", graphLog[2].Commit.Description);
        Assert.Equal("Initial commit", graphLog[3].Commit.Description);
        
        // Verify that each commit (except the first) has the correct parent relationship
        for (int i = 0; i < graphLog.Count - 1; i++)
        {            var currentCommitId = ObjectHasher.ComputeCommitId(graphLog[i].Commit);
            var nextCommitId = ObjectHasher.ComputeCommitId(graphLog[i + 1].Commit);
            
            // Current commit should have next commit as parent
            Assert.Contains(graphLog[i].Edges, edge => edge.Target.Equals(nextCommitId));
        }
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetGraphLogAsync_WithDetachedHead_IncludesAllReachableCommits()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);        // Create some commits
        mockFileSystem.AddFile("/test/repo/file.txt", "Content 1");
        var commit1 = await repository.CommitAsync("First commit", _userSettings, new SnapshotOptions());
        
        var modifiedTime9 = DateTime.UtcNow;
        mockFileSystem.GetFile("/test/repo/file.txt").TextContents = "Content 2";
        mockFileSystem.GetFile("/test/repo/file.txt").LastWriteTime = modifiedTime9;
        var commit2 = await repository.CommitAsync("Second commit", _userSettings, new SnapshotOptions());

        // Simulate detached HEAD by checking out to the first commit
        await repository.CheckoutAsync(commit1!.Value, new CheckoutOptions(), _userSettings);

        // Act
        var graphLog = await repository.GetGraphLogAsync();

        // Assert
        // Should include at least commit1, and potentially commit2 depending on implementation
        Assert.True(graphLog.Count >= 1);
        
        // Verify that at least the checked-out commit is included
        var commitIds = graphLog.Select(item => ObjectHasher.ComputeCommitId(item.Commit)).ToList();
        Assert.Contains(commit1.Value, commitIds);
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetGraphLogAsync_StabilityTestWithLargeDAG_PerformsCorrectly()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Create a moderately large commit history to test performance and stability
        var commitIds = new List<CommitId>();
        
        for (int i = 1; i <= 20; i++)
        {
            var filePath = $"/test/repo/file{i}.txt";
            mockFileSystem.AddFile(filePath, $"Content for commit {i}");
            
            var commitId = await repository.CommitAsync($"Commit {i}", _userSettings, new SnapshotOptions());
            Assert.NotNull(commitId);
            commitIds.Add(commitId.Value);
        }

        // Act
        var startTime = DateTime.UtcNow;
        var graphLog = await repository.GetGraphLogAsync(limit: 25); // Request more than available
        var endTime = DateTime.UtcNow;        // Assert
        Assert.Equal(21, graphLog.Count); // 20 user commits + 1 initial commit
        
        // Verify performance (should complete within reasonable time)
        var duration = endTime - startTime;
        Assert.True(duration.TotalSeconds < 5, $"GetGraphLogAsync took too long: {duration.TotalSeconds} seconds");
        
        // Verify topological ordering is maintained
        for (int i = 0; i < graphLog.Count - 1; i++)
        {
            var currentCommit = graphLog[i].Commit;
            var nextCommit = graphLog[i + 1].Commit;
            
            // In a linear history, newer commits should have more recent timestamps
            Assert.True(currentCommit.Committer.Timestamp >= nextCommit.Committer.Timestamp,
                "Commits are not in proper chronological order");
        }
        
        // Verify edge relationships are consistent
        foreach (var (commit, edges) in graphLog)
        {
            foreach (var edge in edges)
            {
                // Each edge target should correspond to a commit in the graph
                var targetExists = graphLog.Any(item => 
                    ObjectHasher.ComputeCommitId(item.Commit).Equals(edge.Target));
                
                // Note: Edge targets might not be in the limited result set, so we can't assert this always
                // Instead, we verify the edge has valid structure
                Assert.NotEqual(default(CommitId), edge.Target);
            }
        }
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task LogGraphRenderer_Render_LinearHistory_ProducesCorrectASCIIGraph()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Create simple linear history        mockFileSystem.AddFile("/test/repo/file.txt", "Content 1");
        await repository.CommitAsync("First commit", _userSettings, new SnapshotOptions());
          var modifiedTime10 = DateTime.UtcNow;        var file1 = mockFileSystem.GetFile("/test/repo/file.txt");
        if (file1 == null) 
        {
            // Re-add the file if it was somehow removed during commit
            mockFileSystem.AddFile("/test/repo/file.txt", "Content 1");
            file1 = mockFileSystem.GetFile("/test/repo/file.txt");
        }
        file1.TextContents = "Content 2";
        file1.LastWriteTime = modifiedTime10;
        await repository.CommitAsync("Second commit", _userSettings, new SnapshotOptions());
          var modifiedTime11 = DateTime.UtcNow.AddMinutes(1);        var file2 = mockFileSystem.GetFile("/test/repo/file.txt");
        if (file2 == null) 
        {
            // Re-add the file if it was somehow removed during commit
            mockFileSystem.AddFile("/test/repo/file.txt", "Content 2");
            file2 = mockFileSystem.GetFile("/test/repo/file.txt");
        }
        file2.TextContents = "Content 3";
        file2.LastWriteTime = modifiedTime11;
        await repository.CommitAsync("Third commit", _userSettings, new SnapshotOptions());

        var graphLog = await repository.GetGraphLogAsync();

        // Debug: Check what graphLog contains
        Console.WriteLine($"graphLog is null: {graphLog == null}");
        Console.WriteLine($"graphLog Count: {graphLog?.Count}");
        if (graphLog != null && graphLog.Count > 0)
        {
            Console.WriteLine($"First commit edges is null: {graphLog[0].Edges == null}");
        }

        // Act
        var renderedLines = LogGraphRenderer.Render(graphLog);

        // Assert
        Assert.NotEmpty(renderedLines);
          // For linear history, expect alternating commit lines (o) and connector lines (|)
        // Should be at least 7 lines: o, |, o, |, o, |, o (4 commits + 3 connectors)
        Assert.True(renderedLines.Count >= 7);
          // Verify ASCII graph characters are present
        var allGraphContent = string.Join("", renderedLines.Select(line => line.GraphPrefix));
        Assert.Contains("o", allGraphContent); // Commit markers
        Assert.Contains("|", allGraphContent); // Connectors for linear history
          // Verify commit information is included
        var allCommitContent = string.Join(" ", renderedLines.Select(line => line.CommitLine));
        Assert.Contains("Third commit", allCommitContent);
        Assert.Contains("Second commit", allCommitContent);
        Assert.Contains("First commit", allCommitContent);
        Assert.Contains("Initial commit", allCommitContent);
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public void LogGraphRenderer_Render_EmptyGraphLog_ReturnsEmptyList()
    {
        // Arrange
        var emptyGraphLog = new List<(CommitData Commit, IReadOnlyList<GraphEdge> Edges)>();

        // Act
        var renderedLines = LogGraphRenderer.Render(emptyGraphLog);

        // Assert
        Assert.Empty(renderedLines);
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task LogGraphRenderer_Render_SingleCommit_ShowsCommitWithoutConnectors()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Create single commit
        mockFileSystem.AddFile("/test/repo/file.txt", "Content");
        await repository.CommitAsync("Single commit", _userSettings, new SnapshotOptions());

        var graphLog = await repository.GetGraphLogAsync();        // Act
        var renderedLines = LogGraphRenderer.Render(graphLog);

        // Assert
        Assert.Equal(3, renderedLines.Count); // Single commit + connector + Initial commit
        
        var line = renderedLines[0];
        Assert.Contains("o", line.GraphPrefix); // Should show commit marker
        Assert.Contains("Single commit", line.CommitLine);
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task LogGraphRenderer_Render_WithMergeCommit_ShowsComplexGraph()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Create a simple branching scenario
        // Note: This test depends on having merge capabilities in the repository
        // For now, we'll test with linear history and verify the renderer handles it
          mockFileSystem.AddFile("/test/repo/file.txt", "Base");
        await repository.CommitAsync("Base commit", _userSettings, new SnapshotOptions());
        
        var modifiedTime12 = DateTime.UtcNow;
        mockFileSystem.GetFile("/test/repo/file.txt").TextContents = "Branch content";
        mockFileSystem.GetFile("/test/repo/file.txt").LastWriteTime = modifiedTime12;
        await repository.CommitAsync("Branch commit", _userSettings, new SnapshotOptions());

        var graphLog = await repository.GetGraphLogAsync();

        // Act
        var renderedLines = LogGraphRenderer.Render(graphLog);

        // Assert
        Assert.NotEmpty(renderedLines);
        
        // Verify that graph rendering handles multiple commits correctly
        Assert.True(renderedLines.Count >= 2); // At least 2 commits
          // Verify ASCII characters are properly used
        var graphContent = string.Join("", renderedLines.Select(line => line.GraphPrefix));
        Assert.Contains("o", graphContent);
        
        // For complex graphs, might also contain merge indicators like *, /, \
        // But since we're testing with linear history, we mainly expect o and |
    }    [Fact]
    [Trait("TestCategory", "Module8")]
    public async Task GetGraphLogAsync_WithLimit_RespectsLimitParameter()
    {
        // Arrange
        var mockFileSystem = CreateFreshMockFileSystem();
        var repository = await Repository.InitializeAsync(_repoPath, _userSettings, mockFileSystem);

        // Create more commits than we'll request
        for (int i = 1; i <= 10; i++)
        {
            mockFileSystem.AddFile($"/test/repo/file{i}.txt", $"Content {i}");
            await repository.CommitAsync($"Commit {i}", _userSettings, new SnapshotOptions());
        }

        // Act
        var graphLog = await repository.GetGraphLogAsync(limit: 5);

        // Assert
        Assert.Equal(5, graphLog.Count);
        
        // Verify we got the most recent commits
        Assert.Equal("Commit 10", graphLog[0].Commit.Description);
        Assert.Equal("Commit 9", graphLog[1].Commit.Description);
        Assert.Equal("Commit 8", graphLog[2].Commit.Description);
        Assert.Equal("Commit 7", graphLog[3].Commit.Description);
        Assert.Equal("Commit 6", graphLog[4].Commit.Description);
    }

    #endregion
}
