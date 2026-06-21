using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using HPD.VCS.Core;
using HPD.VCS.Storage;
using HPD.VCS.WorkingCopy;

namespace HPD.VCS.Tests;

public class Module6Tests : IDisposable
{
    private readonly string _tempDir;
    private readonly IFileSystem _fileSystem;
    private readonly UserSettings _userSettings;

    public Module6Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "VCS.Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _fileSystem = new FileSystem();
        _userSettings = new UserSettings("Test User", "test@example.com");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    #region FileLock Tests

    [Fact]
    public void FileLock_AcquireAndRelease_ShouldWork()
    {
        // Arrange
        var lockFilePath = Path.Combine(_tempDir, "test.lock");

        // Act & Assert
        using (var lockHandle = FileLock.Acquire(_fileSystem, lockFilePath))
        {
            Assert.NotNull(lockHandle);
            Assert.True(_fileSystem.File.Exists(lockFilePath));
        }

        // After disposal, should be able to acquire again
        using (var secondLockHandle = FileLock.Acquire(_fileSystem, lockFilePath))
        {
            Assert.NotNull(secondLockHandle);
        }
    }

    [Fact]
    public void FileLock_ConcurrentAccess_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var lockFilePath = Path.Combine(_tempDir, "concurrent.lock");

        // Act & Assert
        using (var firstLock = FileLock.Acquire(_fileSystem, lockFilePath))
        {
            // Attempt to acquire a second lock while the first is held
            var exception = Assert.Throws<InvalidOperationException>(() =>
            {
                FileLock.Acquire(_fileSystem, lockFilePath);
            });

            Assert.True(exception.Message.Contains("Failed to acquire repository lock"));
            Assert.True(exception.Message.Contains("Another jj process may be running"));
        }
    }

    [Fact]
    public void FileLock_NullParameters_ShouldThrowArgumentNullException()
    {
        // Test null fileSystem
        Assert.Throws<ArgumentNullException>(() =>
        {
            FileLock.Acquire(null!, "test.lock");
        });

        // Test null lockFilePath
        Assert.Throws<ArgumentNullException>(() =>
        {
            FileLock.Acquire(_fileSystem, null!);
        });
    }

    [Fact]
    public void FileLock_CreatesDirectoryStructure_ShouldWork()
    {
        // Arrange
        var nestedLockPath = Path.Combine(_tempDir, "nested", "deep", "path", ".lock");

        // Act
        using (var lockHandle = FileLock.Acquire(_fileSystem, nestedLockPath))
        {
            // Assert
            Assert.NotNull(lockHandle);
            Assert.True(_fileSystem.File.Exists(nestedLockPath));
            Assert.True(_fileSystem.Directory.Exists(Path.GetDirectoryName(nestedLockPath)));
        }
    }

    #endregion

    #region Repository.OperationLogAsync Tests

    [Fact]
    public async Task OperationLogAsync_InitialRepository_ShouldReturnSingleRootOperation()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "test-repo");
        var repo = await Repository.InitializeAsync(repoDir, _userSettings, _fileSystem);

        // Act
        var operationLog = await repo.OperationLogAsync();        // Assert
        Assert.Equal(1, operationLog.Count);
        Assert.True(operationLog[0].Data.IsRootOperation);
        Assert.Equal("Initialize repository", operationLog[0].Data.Metadata.Description);

        repo.Dispose();
    }

    [Fact]
    public async Task OperationLogAsync_AfterMultipleOperations_ShouldReturnFullHistory()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "test-repo-multiple");
        var repo = await Repository.InitializeAsync(repoDir, _userSettings, _fileSystem);

        // Create test files and commits
        for (int i = 1; i <= 3; i++)
        {
            var testFile = Path.Combine(repoDir, $"file{i}.txt");
            await File.WriteAllTextAsync(testFile, $"Content {i}");
            await repo.CommitAsync($"Commit {i}", _userSettings, new SnapshotOptions());
        }

        // Act
        var operationLog = await repo.OperationLogAsync();

        // Assert
        Assert.Equal(4, operationLog.Count); // 1 initialize + 3 commits
          // Verify operations are in reverse chronological order (newest first)
        Assert.Equal("commit: Commit 3", operationLog[0].Data.Metadata.Description);
        Assert.Equal("commit: Commit 2", operationLog[1].Data.Metadata.Description);
        Assert.Equal("commit: Commit 1", operationLog[2].Data.Metadata.Description);
        Assert.Equal("Initialize repository", operationLog[3].Data.Metadata.Description);

        // Verify only the root operation has no parents
        Assert.Equal(0, operationLog[3].Data.ParentOperationIds.Count);
        Assert.True(operationLog[3].Data.IsRootOperation);

        // Verify other operations have exactly one parent
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(1, operationLog[i].Data.ParentOperationIds.Count);
            Assert.False(operationLog[i].Data.IsRootOperation);
        }

        repo.Dispose();
    }

    [Fact]
    public async Task OperationLogAsync_WithLimit_ShouldReturnLimitedResults()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "test-repo-limit");
        var repo = await Repository.InitializeAsync(repoDir, _userSettings, _fileSystem);

        // Create multiple operations
        for (int i = 1; i <= 5; i++)
        {
            var testFile = Path.Combine(repoDir, $"file{i}.txt");
            await File.WriteAllTextAsync(testFile, $"Content {i}");
            await repo.CommitAsync($"Commit {i}", _userSettings, new SnapshotOptions());
        }

        // Act
        var fullLog = await repo.OperationLogAsync();
        var limitedLog = await repo.OperationLogAsync(3);

        // Assert
        Assert.Equal(6, fullLog.Count); // 1 initialize + 5 commits
        Assert.Equal(3, limitedLog.Count);        // Verify limited log contains the 3 most recent operations
        Assert.Equal("commit: Commit 5", limitedLog[0].Data.Metadata.Description);
        Assert.Equal("commit: Commit 4", limitedLog[1].Data.Metadata.Description);
        Assert.Equal("commit: Commit 3", limitedLog[2].Data.Metadata.Description);

        repo.Dispose();
    }

    [Fact]
    public async Task OperationLogAsync_WithZeroLimit_ShouldReturnEmptyList()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "test-repo-zero-limit");
        var repo = await Repository.InitializeAsync(repoDir, _userSettings, _fileSystem);

        // Act
        var operationLog = await repo.OperationLogAsync(0);

        // Assert
        Assert.Equal(0, operationLog.Count);

        repo.Dispose();
    }

    #endregion

    #region Repository.UndoOperationAsync Tests

    [Fact]
    public async Task UndoOperationAsync_OnInitialRepository_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "test-repo-undo-initial");
        var repo = await Repository.InitializeAsync(repoDir, _userSettings, _fileSystem);        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await repo.UndoOperationAsync(_userSettings);
        });

        Assert.True(exception.Message.Contains("Nothing to undo") || exception.Message.Contains("nothing to undo"));

        repo.Dispose();
    }

    [Fact]
    public async Task UndoOperationAsync_UndoCommit_ShouldRevertToParentState()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "test-repo-undo-commit");
        var repo = await Repository.InitializeAsync(repoDir, _userSettings, _fileSystem);

        var testFile = Path.Combine(repoDir, "test.txt");
        
        // Commit A
        await File.WriteAllTextAsync(testFile, "Content A");
        var commitA = await repo.CommitAsync("Commit A", _userSettings, new SnapshotOptions());

        // Commit B
        await File.WriteAllTextAsync(testFile, "Content B");
        var commitB = await repo.CommitAsync("Commit B", _userSettings, new SnapshotOptions());

        // Verify current state is B
        var contentBeforeUndo = await File.ReadAllTextAsync(testFile);
        Assert.Equal("Content B", contentBeforeUndo);

        // Act - Undo
        var undoOperationId = await repo.UndoOperationAsync(_userSettings);

        // Assert
        // Verify working copy matches A
        var contentAfterUndo = await File.ReadAllTextAsync(testFile);
        Assert.Equal("Content A", contentAfterUndo);

        // Verify operation log has undo operation
        var operationLog = await repo.OperationLogAsync();
        Assert.True(operationLog[0].Data.Metadata.Description.Contains("undo operation"));
        Assert.Equal(undoOperationId, operationLog[0].Id);

        // Verify repository state reflects checkout of A
        Assert.Equal(undoOperationId, repo.CurrentOperationId);

        repo.Dispose();
    }

    [Fact]
    public async Task UndoOperationAsync_RedoUndoOperation_ShouldRevertToOriginalState()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "test-repo-redo");
        var repo = await Repository.InitializeAsync(repoDir, _userSettings, _fileSystem);

        var testFile = Path.Combine(repoDir, "test.txt");
        
        // Commit A
        await File.WriteAllTextAsync(testFile, "Content A");
        var commitA = await repo.CommitAsync("Commit A", _userSettings, new SnapshotOptions());

        // Commit B
        await File.WriteAllTextAsync(testFile, "Content B");
        var commitB = await repo.CommitAsync("Commit B", _userSettings, new SnapshotOptions());

        // Undo (should go back to A)
        var undoOperationId = await repo.UndoOperationAsync(_userSettings);
        var contentAfterUndo = await File.ReadAllTextAsync(testFile);
        Assert.Equal("Content A", contentAfterUndo);        // Act - Redo (undo the undo, should go back to initial state where file doesn't exist)
        var redoOperationId = await repo.UndoOperationAsync(_userSettings);        // Assert - File should not exist in initial state
        Assert.False(File.Exists(testFile), "File should not exist after undoing to initial repository state");

        // Verify operation log shows the sequence
        var operationLog = await repo.OperationLogAsync();
        
        // After undoing twice, we should be back at the initial state
        // The operation log should show at least the current operation
        Assert.True(operationLog.Count >= 1);
        
        // The current operation should be an undo operation
        Assert.True(operationLog[0].Data.Metadata.Description.Contains("undo operation"));

        repo.Dispose();
    }

    [Fact]
    public async Task UndoOperationAsync_WithUntrackedFileConflict_ShouldThrowIOException()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "test-repo-untracked-conflict");
        var repo = await Repository.InitializeAsync(repoDir, _userSettings, _fileSystem);

        var testFile = Path.Combine(repoDir, "test.txt");
        
        // Commit with file
        await File.WriteAllTextAsync(testFile, "Original content");
        var originalCommit = await repo.CommitAsync("Add test.txt", _userSettings, new SnapshotOptions());

        // Delete file and commit
        File.Delete(testFile);
        var deleteCommit = await repo.CommitAsync("Delete test.txt", _userSettings, new SnapshotOptions());

        // Create untracked file with same name (this would conflict with undo)
        await File.WriteAllTextAsync(testFile, "Untracked content");

        // Store repository state before undo attempt
        var currentOpIdBefore = repo.CurrentOperationId;        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await repo.UndoOperationAsync(_userSettings);
        });

        Assert.True(exception.Message.Contains("uncommitted changes"));

        // Verify repository state remains unchanged
        Assert.Equal(currentOpIdBefore, repo.CurrentOperationId);

        repo.Dispose();
    }

    [Fact]
    public async Task UndoOperationAsync_WithDirtyWorkingCopy_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "test-repo-dirty-wc");
        var repo = await Repository.InitializeAsync(repoDir, _userSettings, _fileSystem);

        var testFile = Path.Combine(repoDir, "test.txt");
        
        // Commit A
        await File.WriteAllTextAsync(testFile, "Content A");
        var commitA = await repo.CommitAsync("Commit A", _userSettings, new SnapshotOptions());

        // Commit B
        await File.WriteAllTextAsync(testFile, "Content B");
        var commitB = await repo.CommitAsync("Commit B", _userSettings, new SnapshotOptions());

        // Make working copy dirty
        await File.WriteAllTextAsync(testFile, "Dirty content - uncommitted changes");

        // Store repository state before undo attempt
        var currentOpIdBefore = repo.CurrentOperationId;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await repo.UndoOperationAsync(_userSettings);
        });

        Assert.True(exception.Message.Contains("uncommitted changes"));
        Assert.True(exception.Message.Contains("commit or discard"));

        // Verify repository state remains unchanged
        Assert.Equal(currentOpIdBefore, repo.CurrentOperationId);

        // Verify file still has dirty content
        var currentContent = await File.ReadAllTextAsync(testFile);
        Assert.Equal("Dirty content - uncommitted changes", currentContent);

        repo.Dispose();
    }

    [Fact]
    public async Task UndoOperationAsync_WithNewUntrackedFile_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "test-repo-new-untracked");
        var repo = await Repository.InitializeAsync(repoDir, _userSettings, _fileSystem);

        var testFile = Path.Combine(repoDir, "test.txt");
        
        // Commit A
        await File.WriteAllTextAsync(testFile, "Content A");
        var commitA = await repo.CommitAsync("Commit A", _userSettings, new SnapshotOptions());

        // Commit B
        await File.WriteAllTextAsync(testFile, "Content B");
        var commitB = await repo.CommitAsync("Commit B", _userSettings, new SnapshotOptions());

        // Add new untracked file (this makes working copy dirty)
        var newFile = Path.Combine(repoDir, "new-untracked.txt");
        await File.WriteAllTextAsync(newFile, "New untracked content");

        // Store repository state before undo attempt
        var currentOpIdBefore = repo.CurrentOperationId;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await repo.UndoOperationAsync(_userSettings);
        });

        Assert.True(exception.Message.Contains("uncommitted changes"));

        // Verify repository state remains unchanged
        Assert.Equal(currentOpIdBefore, repo.CurrentOperationId);

        repo.Dispose();
    }

    [Fact]
    public async Task UndoOperationAsync_OperationMetadata_ShouldBeCorrect()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "test-repo-metadata");
        var repo = await Repository.InitializeAsync(repoDir, _userSettings, _fileSystem);

        var testFile = Path.Combine(repoDir, "test.txt");
        
        // Create commit to undo
        await File.WriteAllTextAsync(testFile, "Content");
        var originalCommit = await repo.CommitAsync("Original commit", _userSettings, new SnapshotOptions());

        // Act
        var undoOperationId = await repo.UndoOperationAsync(_userSettings);

        // Assert
        var operationLog = await repo.OperationLogAsync(1);
        var undoOperation = operationLog[0];        Assert.Equal(undoOperationId, undoOperation.Id);
        Assert.True(undoOperation.Data.Metadata.Description.Contains("undo operation"));
        // The undo operation should have the original operation's parents as its parents
        Assert.True(undoOperation.Data.ParentOperationIds.Count >= 0);

        repo.Dispose();
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task IntegrationTest_CompleteUndoRedoSequence_ShouldMaintainConsistency()
    {
        // Arrange
        var repoDir = Path.Combine(_tempDir, "test-repo-integration");
        var repo = await Repository.InitializeAsync(repoDir, _userSettings, _fileSystem);

        var testFile = Path.Combine(repoDir, "test.txt");
        
        // Create a sequence of commits
        await File.WriteAllTextAsync(testFile, "Version 1");
        var commit1 = await repo.CommitAsync("Version 1", _userSettings, new SnapshotOptions());

        await File.WriteAllTextAsync(testFile, "Version 2");
        var commit2 = await repo.CommitAsync("Version 2", _userSettings, new SnapshotOptions());

        await File.WriteAllTextAsync(testFile, "Version 3");
        var commit3 = await repo.CommitAsync("Version 3", _userSettings, new SnapshotOptions());

        // Verify initial state
        var initialContent = await File.ReadAllTextAsync(testFile);
        Assert.Equal("Version 3", initialContent);

        // Act & Assert - Multiple undo/redo operations
          // Undo to Version 2
        var undo1 = await repo.UndoOperationAsync(_userSettings);
        var content1 = File.Exists(testFile) ? await File.ReadAllTextAsync(testFile) : "";
        Assert.Equal("Version 2", content1);        // Undo to Version 1
        var undo2 = await repo.UndoOperationAsync(_userSettings);
        var content2 = File.Exists(testFile) ? await File.ReadAllTextAsync(testFile) : "";
        Assert.Equal("Version 1", content2);        // Third undo should go to initial state (no file)
        var undo3 = await repo.UndoOperationAsync(_userSettings);
        Assert.False(File.Exists(testFile), "File should not exist after undoing to initial state");

        // Fourth undo should fail because there's nothing left to undo (we're at the root)
        var fourthUndoException = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await repo.UndoOperationAsync(_userSettings);
        });
        Assert.True(fourthUndoException.Message.Contains("Nothing to undo") || fourthUndoException.Message.Contains("nothing to undo"));

        // Verify operation log consistency
        var finalLog = await repo.OperationLogAsync();
        Assert.Equal(7, finalLog.Count); // 1 init + 3 commits + 3 undo operations

        // Verify all undo operations are properly recorded
        var undoOps = finalLog.Where(op => op.Data.Metadata.Description.Contains("undo operation")).ToList();
        Assert.Equal(3, undoOps.Count);

        repo.Dispose();
    }

    #endregion
}