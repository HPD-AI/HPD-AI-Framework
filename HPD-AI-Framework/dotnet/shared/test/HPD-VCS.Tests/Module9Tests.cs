using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HPD.VCS.Core;
using HPD.VCS.Storage;
using HPD.VCS.WorkingCopy;
using Xunit;

namespace HPD.VCS.Tests;

public class Module9Tests : IDisposable
{
    private readonly string _tempDir;
    private readonly IFileSystem _fileSystem;
    private readonly UserSettings _userSettings;
    private Repository _repo;

    public Module9Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hpd_module9_test", Guid.NewGuid().ToString());
        _fileSystem = new FileSystem();
        _userSettings = new UserSettings("Test User", "test@example.com");
    }

    public void Dispose()
    {
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

    private async Task<Repository> InitializeRepository()
    {
        _repo = await Repository.InitializeAsync(_tempDir, _userSettings, _fileSystem);
        return _repo;
    }

    #region Transaction Framework Tests

    [Fact]
    public async Task Transaction_ChangesIsolatedUntilCommit()
    {
        // Arrange
        var repo = await InitializeRepository();
        
        // Create initial commit
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "file1.txt"), "Initial content");
        var initialCommit = await repo.CommitAsync("Initial commit", _userSettings, new SnapshotOptions());
        var initialHeadData = repo.CurrentViewData;

        // Act - Start transaction and make changes
        var transaction = repo.StartTransaction(_userSettings);
        
        // Rewrite the initial commit
        var initialCommitData = await repo.ObjectStore.ReadCommitAsync(initialCommit!.Value);
        var rewriteBuilder = transaction.RewriteCommit(initialCommitData!.Value);
        rewriteBuilder.SetDescription("Modified description");
        await rewriteBuilder.WriteAsync();

        // Assert - Repository state should be unchanged before commit
        Assert.Equal(initialHeadData.Branches, repo.CurrentViewData.Branches);
        Assert.Equal(initialHeadData.WorkspaceCommitIds, repo.CurrentViewData.WorkspaceCommitIds);
        
        // The original commit should still exist and be unchanged
        var unchangedCommit = await repo.ObjectStore.ReadCommitAsync(initialCommit.Value);
        Assert.True(unchangedCommit.HasValue);
        Assert.Equal("Initial commit", unchangedCommit.Value.Description);

        // Act - Commit the transaction
        var operationId = await transaction.CommitAsync("Rewrite commit transaction");

        // Assert - Repository state should now reflect the changes
        Assert.NotEqual(initialHeadData, repo.CurrentViewData);
        
        // The workspace should now point to the new commit
        var workspaceCommit = repo.CurrentViewData.WorkspaceCommitIds["default"];
        var newCommitData = await repo.ObjectStore.ReadCommitAsync(workspaceCommit);
        Assert.True(newCommitData.HasValue);
        Assert.Equal("Modified description", newCommitData.Value.Description);
    }

    [Fact]
    public async Task Transaction_AbandoningLeavesRepositoryUnchanged()
    {
        // Arrange
        var repo = await InitializeRepository();
        
        // Create initial commit
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "file1.txt"), "Initial content");
        var initialCommit = await repo.CommitAsync("Initial commit", _userSettings, new SnapshotOptions());
        var initialHeadData = repo.CurrentViewData;

        // Act - Start transaction and make changes but don't commit
        var transaction = repo.StartTransaction(_userSettings);
        
        // Rewrite the initial commit
        var initialCommitData = await repo.ObjectStore.ReadCommitAsync(initialCommit!.Value);
        var rewriteBuilder = transaction.RewriteCommit(initialCommitData!.Value);
        rewriteBuilder.SetDescription("This should not persist");
        await rewriteBuilder.WriteAsync();

        // Abandon the transaction by letting it go out of scope without committing
        transaction = null;
        GC.Collect(); // Force cleanup

        // Assert - Repository state should be exactly the same
        Assert.Equal(initialHeadData.Branches, repo.CurrentViewData.Branches);
        Assert.Equal(initialHeadData.WorkspaceCommitIds, repo.CurrentViewData.WorkspaceCommitIds);
        
        // The original commit should still exist and be unchanged
        var unchangedCommit = await repo.ObjectStore.ReadCommitAsync(initialCommit.Value);
        Assert.True(unchangedCommit.HasValue);
        Assert.Equal("Initial commit", unchangedCommit.Value.Description);
    }

    #endregion

    #region Rewriter Tests

    [Fact]
    public async Task Rewriter_CorrectTreeRebaseInLinearHistory()
    {
        // Arrange - Create history A->B->C
        var repo = await InitializeRepository();
        
        // Commit A
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "fileA.txt"), "Content A");
        var commitA = await repo.CommitAsync("Commit A", _userSettings, new SnapshotOptions());
        
        // Commit B (adds fileB.txt)
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "fileB.txt"), "Content B");
        var commitB = await repo.CommitAsync("Commit B", _userSettings, new SnapshotOptions());
        
        // Commit C (modifies fileB.txt)
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "fileB.txt"), "Modified Content B");
        var commitC = await repo.CommitAsync("Commit C", _userSettings, new SnapshotOptions());

        // Act - Rewrite B to B' (change description)
        var transaction = repo.StartTransaction(_userSettings);
        var commitBData = await repo.ObjectStore.ReadCommitAsync(commitB!.Value);
        var rewriteBuilder = transaction.RewriteCommit(commitBData!.Value);
        rewriteBuilder.SetDescription("Modified Commit B");
        var newCommitB = await rewriteBuilder.WriteAsync();
        var newCommitBId = ObjectHasher.ComputeCommitId(newCommitB);

        await transaction.CommitAsync("Rewrite commit B");

        // Assert - Verify C was rebased to C' correctly
        var workspaceCommit = repo.CurrentViewData.WorkspaceCommitIds["default"];
        var rebasedCommitC = await repo.ObjectStore.ReadCommitAsync(workspaceCommit);
        
        Assert.True(rebasedCommitC.HasValue);
        Assert.Equal("Commit C", rebasedCommitC.Value.Description); // Description unchanged
        Assert.Single(rebasedCommitC.Value.ParentIds);
        Assert.Equal(newCommitBId, rebasedCommitC.Value.ParentIds[0]); // Parent should be new B'

        // Verify the tree content is correct - should have both files with C's modifications
        var rebasedTree = await repo.ObjectStore.ReadTreeAsync(rebasedCommitC.Value.RootTreeId);
        Assert.True(rebasedTree.HasValue);
        
        // Should contain both fileA.txt and fileB.txt
        var entries = rebasedTree.Value.Entries.ToList();
        Assert.Equal(2, entries.Count);
          var fileAEntry = entries.FirstOrDefault(e => e.Name == "fileA.txt");
        var fileBEntry = entries.FirstOrDefault(e => e.Name == "fileB.txt");
        Assert.NotNull(fileAEntry);
        Assert.NotNull(fileBEntry);
        
        // Check file contents
        var fileABlob = await repo.ObjectStore.ReadFileContentAsync(new FileContentId(fileAEntry.ObjectId.HashValue.ToArray()));
        var fileBBlob = await repo.ObjectStore.ReadFileContentAsync(new FileContentId(fileBEntry.ObjectId.HashValue.ToArray()));
        Assert.Equal("Content A", System.Text.Encoding.UTF8.GetString(fileABlob!.Value.Content.ToArray()));
        Assert.Equal("Modified Content B", System.Text.Encoding.UTF8.GetString(fileBBlob!.Value.Content.ToArray()));
    }

    [Fact]
    public async Task Rewriter_ComplexGraphWithForkAndMerge()
    {
        // Arrange - Create a more complex history:
        //   A
        //   |
        //   B
        //  / \
        // C   D
        //  \ /
        //   E (merge commit)
        var repo = await InitializeRepository();
        
        // Commit A
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "fileA.txt"), "Content A");
        var commitA = await repo.CommitAsync("Commit A", _userSettings, new SnapshotOptions());
        
        // Commit B
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "fileB.txt"), "Content B");
        var commitB = await repo.CommitAsync("Commit B", _userSettings, new SnapshotOptions());
        
        // Create branch for parallel development
        // Commit C (left branch)
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "fileC.txt"), "Content C");
        var commitC = await repo.CommitAsync("Commit C", _userSettings, new SnapshotOptions());
          // Go back to B and create commit D (right branch)
        await repo.CheckoutAsync(commitB!.Value, new CheckoutOptions(), _userSettings);
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "fileD.txt"), "Content D");
        var commitD = await repo.CommitAsync("Commit D", _userSettings, new SnapshotOptions());
        
        // Create merge commit E
        var transaction = repo.StartTransaction(_userSettings);
        var mergeBuilder = transaction.NewCommit();
        
        // For simplicity, use commit D's tree for the merge (in practice would be a real merge)
        var commitDData = await repo.ObjectStore.ReadCommitAsync(commitD!.Value);
        mergeBuilder.SetTreeId(commitDData!.Value.RootTreeId)
                   .SetParents(new[] { commitC!.Value, commitD.Value })
                   .SetDescription("Merge C and D");
          var mergeCommit = await mergeBuilder.WriteAsync();
        var mergeCommitId = ObjectHasher.ComputeCommitId(mergeCommit);
          await transaction.CommitAsync("Create merge commit");
        
        // Update workspace to point to the merge commit using reflection
        var currentViewDataField = typeof(Repository).GetField("_currentViewData", BindingFlags.NonPublic | BindingFlags.Instance);
        var currentViewData = (ViewData)currentViewDataField!.GetValue(repo)!;
        var updatedViewData = currentViewData.WithWorkspaceCommit("default", mergeCommitId);
        currentViewDataField.SetValue(repo, updatedViewData);

        // Act - Rewrite commit B (this should rebase C, D, and E)
        var rewriteTransaction = repo.StartTransaction(_userSettings);
        var commitBData = await repo.ObjectStore.ReadCommitAsync(commitB.Value);
        var rewriteBuilder = rewriteTransaction.RewriteCommit(commitBData!.Value);
        rewriteBuilder.SetDescription("Modified Commit B");
        var newCommitB = await rewriteBuilder.WriteAsync();
        var newCommitBId = ObjectHasher.ComputeCommitId(newCommitB);

        await rewriteTransaction.CommitAsync("Rewrite commit B in complex graph");

        // Assert - Verify all descendants were properly rebased
        var workspaceCommit = repo.CurrentViewData.WorkspaceCommitIds["default"];
        var finalCommit = await repo.ObjectStore.ReadCommitAsync(workspaceCommit);
        
        Assert.True(finalCommit.HasValue);
        Assert.Equal("Merge C and D", finalCommit.Value.Description);
        Assert.Equal(2, finalCommit.Value.ParentIds.Count); // Still a merge commit
        
        // The merge parents should be the rebased versions of C and D
        var parent1 = await repo.ObjectStore.ReadCommitAsync(finalCommit.Value.ParentIds[0]);
        var parent2 = await repo.ObjectStore.ReadCommitAsync(finalCommit.Value.ParentIds[1]);
        
        Assert.True(parent1.HasValue);
        Assert.True(parent2.HasValue);
        
        // Both parents should have the new B as their parent
        Assert.All(new[] { parent1.Value, parent2.Value }, parent => 
        {
            Assert.Single(parent.ParentIds);
            Assert.Equal(newCommitBId, parent.ParentIds[0]);
        });
    }

    #endregion

    #region Squash Command Tests

    [Fact]
    public async Task Squash_LinearChainCorrectlySquashesAndRebases()
    {
        // Arrange - Create chain A->B->C->D
        var repo = await InitializeRepository();
        
        // Commit A
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "fileA.txt"), "Content A");
        var commitA = await repo.CommitAsync("Commit A", _userSettings, new SnapshotOptions());
        
        // Commit B
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "fileB.txt"), "Content B");
        var commitB = await repo.CommitAsync("Commit B", _userSettings, new SnapshotOptions());
        
        // Commit C (to be squashed into B)
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "fileC.txt"), "Content C");
        var commitC = await repo.CommitAsync("Commit C", _userSettings, new SnapshotOptions());
        
        // Commit D
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "fileD.txt"), "Content D");
        var commitD = await repo.CommitAsync("Commit D", _userSettings, new SnapshotOptions());

        // Act - Squash C into B
        var operationId = await repo.SquashAsync(commitC!.Value, _userSettings);

        // Assert - Verify the squash results
        
        // 1. Verify D is now the workspace commit and has been rebased
        var workspaceCommit = repo.CurrentViewData.WorkspaceCommitIds["default"];
        var rebasedD = await repo.ObjectStore.ReadCommitAsync(workspaceCommit);
        
        Assert.True(rebasedD.HasValue);
        Assert.Equal("Commit D", rebasedD.Value.Description);
        Assert.Single(rebasedD.Value.ParentIds);
          // 2. Verify D's parent is the new squashed B' (not the original B or C)
        var newSquashedB = await repo.ObjectStore.ReadCommitAsync(rebasedD.Value.ParentIds[0]);
        Assert.True(newSquashedB.HasValue);
        var newSquashedBId = ObjectHasher.ComputeCommitId(newSquashedB.Value);
        Assert.NotEqual(commitB.Value, newSquashedBId); // Should be a new commit, not original B
        Assert.NotEqual(commitC.Value, newSquashedBId); // Should not be C either
        
        // 3. Verify the squashed B' has the combined changes of B and C
        var squashedTree = await repo.ObjectStore.ReadTreeAsync(newSquashedB.Value.RootTreeId);
        Assert.True(squashedTree.HasValue);
        
        var entries = squashedTree.Value.Entries.ToList();
        var fileNames = entries.Select(e => e.Name).ToHashSet();
        
        // Should contain files from A, B, and C (but not D, which is in the descendant)
        Assert.Contains("fileA.txt", fileNames);
        Assert.Contains("fileB.txt", fileNames);
        Assert.Contains("fileC.txt", fileNames);
        Assert.DoesNotContain("fileD.txt", fileNames); // D's changes are in the rebased D commit
        
        // 4. Verify D's tree contains all files
        var rebasedDTree = await repo.ObjectStore.ReadTreeAsync(rebasedD.Value.RootTreeId);
        Assert.True(rebasedDTree.HasValue);
        
        var dEntries = rebasedDTree.Value.Entries.ToList();
        var dFileNames = dEntries.Select(e => e.Name).ToHashSet();
        
        Assert.Contains("fileA.txt", dFileNames);
        Assert.Contains("fileB.txt", dFileNames);
        Assert.Contains("fileC.txt", dFileNames);
        Assert.Contains("fileD.txt", dFileNames);
        
        // 5. Verify C is effectively "abandoned" (no longer reachable from heads)
        // The original C should still exist in the object store but not be reachable
        var originalC = await repo.ObjectStore.ReadCommitAsync(commitC.Value);
        Assert.True(originalC.HasValue); // Still exists in object store
        // But it should not be reachable from any current head/workspace
    }

    #endregion

    #region Branch/Workspace Pointer Updates Tests

    [Fact]
    public async Task Describe_UpdatesBranchPointers()
    {
        // Arrange
        var repo = await InitializeRepository();
        
        // Create initial commit
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "file1.txt"), "Initial content");
        var initialCommit = await repo.CommitAsync("Initial commit", _userSettings, new SnapshotOptions());
          // Create a branch pointing to this commit
        var branchName = "test-branch";
        var newBranches = new Dictionary<string, CommitId>(repo.CurrentViewData.Branches)
        {
            [branchName] = initialCommit!.Value
        };
        var updatedViewData = new ViewData(
            repo.CurrentViewData.WorkspaceCommitIds,
            repo.CurrentViewData.HeadCommitIds,
            newBranches
        );
        
        // Simulate updating the repository view data by directly updating the internal state
        // (In a real implementation, this would be done through a proper repository method)
        var viewField = typeof(Repository).GetField("_currentViewData", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        viewField?.SetValue(repo, updatedViewData);

        // Act - Describe the commit that the branch points to
        var operationId = await repo.DescribeAsync(initialCommit.Value, "Modified description", _userSettings);

        // Assert - Branch pointer should move to the new commit
        var newBranchCommit = repo.CurrentViewData.Branches[branchName];
        Assert.NotEqual(initialCommit.Value, newBranchCommit); // Should be different commit ID
        
        // Verify the new commit has the updated description
        var newCommitData = await repo.ObjectStore.ReadCommitAsync(newBranchCommit);
        Assert.True(newCommitData.HasValue);
        Assert.Equal("Modified description", newCommitData.Value.Description);
    }

    [Fact]
    public async Task Describe_UpdatesWorkspacePointers()
    {
        // Arrange
        var repo = await InitializeRepository();
        
        // Create initial commit
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "file1.txt"), "Initial content");
        var initialCommit = await repo.CommitAsync("Initial commit", _userSettings, new SnapshotOptions());
        
        // The workspace should already be pointing to this commit
        var workspaceCommit = repo.CurrentViewData.WorkspaceCommitIds["default"];
        Assert.Equal(initialCommit!.Value, workspaceCommit);

        // Act - Describe the commit that the workspace points to
        var operationId = await repo.DescribeAsync(initialCommit.Value, "Modified description", _userSettings);

        // Assert - Workspace pointer should move to the new commit
        var newWorkspaceCommit = repo.CurrentViewData.WorkspaceCommitIds["default"];
        Assert.NotEqual(initialCommit.Value, newWorkspaceCommit); // Should be different commit ID
        
        // Verify the new commit has the updated description
        var newCommitData = await repo.ObjectStore.ReadCommitAsync(newWorkspaceCommit);
        Assert.True(newCommitData.HasValue);
        Assert.Equal("Modified description", newCommitData.Value.Description);
    }

    [Fact]
    public async Task Squash_UpdatesBranchAndWorkspacePointers()
    {
        // Arrange
        var repo = await InitializeRepository();
        
        // Create chain A->B
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "fileA.txt"), "Content A");
        var commitA = await repo.CommitAsync("Commit A", _userSettings, new SnapshotOptions());
        
        await File.WriteAllTextAsync(Path.Combine(repo.RepoPath, "fileB.txt"), "Content B");
        var commitB = await repo.CommitAsync("Commit B", _userSettings, new SnapshotOptions());
          // Create a branch pointing to commit B
        var branchName = "test-branch";
        var newBranches = new Dictionary<string, CommitId>(repo.CurrentViewData.Branches)
        {
            [branchName] = commitB!.Value
        };
        var updatedViewData = new ViewData(
            repo.CurrentViewData.WorkspaceCommitIds,
            repo.CurrentViewData.HeadCommitIds,
            newBranches
        );
        
        // Simulate updating the repository view data
        var viewField = typeof(Repository).GetField("_currentViewData", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        viewField?.SetValue(repo, updatedViewData);

        // Act - Squash B into A
        var operationId = await repo.SquashAsync(commitB.Value, _userSettings);

        // Assert - Both branch and workspace pointers should move to the squashed commit
        var newBranchCommit = repo.CurrentViewData.Branches[branchName];
        var newWorkspaceCommit = repo.CurrentViewData.WorkspaceCommitIds["default"];
        
        // They should both point to the same new squashed commit
        Assert.Equal(newBranchCommit, newWorkspaceCommit);
        Assert.NotEqual(commitB.Value, newBranchCommit); // Should be different from original B
        Assert.NotEqual(commitA!.Value, newBranchCommit); // Should be different from original A
        
        // Verify the new commit has combined changes
        var squashedCommit = await repo.ObjectStore.ReadCommitAsync(newBranchCommit);
        Assert.True(squashedCommit.HasValue);
        
        var squashedTree = await repo.ObjectStore.ReadTreeAsync(squashedCommit.Value.RootTreeId);
        Assert.True(squashedTree.HasValue);
        
        var entries = squashedTree.Value.Entries.ToList();
        var fileNames = entries.Select(e => e.Name).ToHashSet();
        
        // Should contain both files
        Assert.Contains("fileA.txt", fileNames);
        Assert.Contains("fileB.txt", fileNames);
    }

    #endregion
}
