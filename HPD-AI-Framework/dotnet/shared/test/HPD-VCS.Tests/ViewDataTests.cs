using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using HPD.VCS.Core;

namespace HPD.VCS.Tests;

public class ViewDataTests
{
    [Fact]
    public void Constructor_WithValidInputs_CreatesViewData()
    {
        // Arrange
        var workspaceCommits = new Dictionary<string, CommitId>
        {
            { "default", ObjectIdFactory.CreateCommitId("test1"u8.ToArray()) },
            { "feature", ObjectIdFactory.CreateCommitId("test2"u8.ToArray()) }
        };
        var headCommits = new List<CommitId>
        {
            ObjectIdFactory.CreateCommitId("head1"u8.ToArray()),
            ObjectIdFactory.CreateCommitId("head2"u8.ToArray())
        };

        // Act
        var viewData = new ViewData(workspaceCommits, headCommits);

        // Assert
        Assert.Equal(workspaceCommits, viewData.WorkspaceCommitIds);
        Assert.Equal(headCommits, viewData.HeadCommitIds);
    }

    [Fact]
    public void Constructor_WithNullWorkspaceCommits_ThrowsArgumentNullException()
    {
        // Arrange
        var headCommits = new List<CommitId>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ViewData(null!, headCommits));
    }

    [Fact]
    public void Constructor_WithNullHeadCommits_ThrowsArgumentNullException()
    {
        // Arrange
        var workspaceCommits = new Dictionary<string, CommitId>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ViewData(workspaceCommits, null!));
    }

    [Fact]
    public void Constructor_WithEmptyWorkspaceName_ThrowsArgumentException()
    {
        // Arrange
        var workspaceCommits = new Dictionary<string, CommitId>
        {
            { "", ObjectIdFactory.CreateCommitId("test"u8.ToArray()) }
        };
        var headCommits = new List<CommitId>();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new ViewData(workspaceCommits, headCommits));
    }

    [Fact]
    public void GetBytesForHashing_WithSortedWorkspaces_ProducesCanonicalOutput()
    {
        // Arrange
        var commit1 = ObjectIdFactory.CreateCommitId("commit1"u8.ToArray());
        var commit2 = ObjectIdFactory.CreateCommitId("commit2"u8.ToArray());
        var commit3 = ObjectIdFactory.CreateCommitId("commit3"u8.ToArray());
        
        var workspaceCommits = new Dictionary<string, CommitId>
        {
            { "zebra", commit1 },
            { "alpha", commit2 },
            { "beta", commit3 }
        };
        var headCommits = new List<CommitId> { commit1, commit2 };

        var viewData = new ViewData(workspaceCommits, headCommits);

        // Act
        var bytes = viewData.GetBytesForHashing();
        var content = System.Text.Encoding.UTF8.GetString(bytes);

        // Assert - Workspaces should be sorted by name
        var lines = content.Split('\n');
        
        // Find workspace section
        var workspaceIndex = 0;
        var workspaceCount = int.Parse(lines[workspaceIndex]);
        Assert.Equal(3, workspaceCount);
        
        // Check that workspaces are sorted
        var alphaIndex = content.IndexOf("alpha");
        var betaIndex = content.IndexOf("beta");
        var zebraIndex = content.IndexOf("zebra");
        
        Assert.True(alphaIndex < betaIndex);
        Assert.True(betaIndex < zebraIndex);
    }

    [Fact]
    public void GetBytesForHashing_WithUnsortedInputs_ProducesSameOutputAsSorted()
    {
        // Arrange
        var commit1 = ObjectIdFactory.CreateCommitId("commit1"u8.ToArray());
        var commit2 = ObjectIdFactory.CreateCommitId("commit2"u8.ToArray());
        var commit3 = ObjectIdFactory.CreateCommitId("commit3"u8.ToArray());
        
        var unsortedWorkspaces = new Dictionary<string, CommitId>
        {
            { "zebra", commit1 },
            { "alpha", commit2 },
            { "beta", commit3 }
        };
        var sortedWorkspaces = new Dictionary<string, CommitId>
        {
            { "alpha", commit2 },
            { "beta", commit3 },
            { "zebra", commit1 }
        };
        
        // Create heads in different orders
        var unsortedHeads = new List<CommitId> { commit3, commit1, commit2 };
        var sortedHeads = new List<CommitId> { commit1, commit2, commit3 };

        var viewData1 = new ViewData(unsortedWorkspaces, unsortedHeads);
        var viewData2 = new ViewData(sortedWorkspaces, sortedHeads);

        // Act
        var bytes1 = viewData1.GetBytesForHashing();
        var bytes2 = viewData2.GetBytesForHashing();

        // Assert
        Assert.Equal(bytes1, bytes2);
    }

    [Fact]
    public void GetBytesForHashing_SortsHeadCommitsByHexString()
    {
        // Arrange - Create commits with predictable hex ordering
        var commit1 = ObjectIdFactory.CreateCommitId("aaa"u8.ToArray()); // Will start with lower hex
        var commit2 = ObjectIdFactory.CreateCommitId("zzz"u8.ToArray()); // Will start with higher hex
        
        var workspaceCommits = new Dictionary<string, CommitId>();
        var headCommits = new List<CommitId> { commit2, commit1 }; // Intentionally reversed

        var viewData = new ViewData(workspaceCommits, headCommits);

        // Act
        var bytes = viewData.GetBytesForHashing();
        var content = System.Text.Encoding.UTF8.GetString(bytes);

        // Assert - Commits should be sorted by hex string
        var commit1Hex = commit1.ToHexString();
        var commit2Hex = commit2.ToHexString();
        
        var commit1Index = content.IndexOf(commit1Hex);
        var commit2Index = content.IndexOf(commit2Hex);
        
        // The commit with lexicographically smaller hex should appear first
        if (string.Compare(commit1Hex, commit2Hex, StringComparison.Ordinal) < 0)
        {
            Assert.True(commit1Index < commit2Index);
        }
        else
        {
            Assert.True(commit2Index < commit1Index);
        }
    }

    [Fact]
    public void ParseFromCanonicalBytes_RoundTripConsistency()
    {
        // Arrange
        var commit1 = ObjectIdFactory.CreateCommitId("test1"u8.ToArray());
        var commit2 = ObjectIdFactory.CreateCommitId("test2"u8.ToArray());
        var commit3 = ObjectIdFactory.CreateCommitId("test3"u8.ToArray());
        
        var workspaceCommits = new Dictionary<string, CommitId>
        {
            { "default", commit1 },
            { "feature-branch", commit2 },
            { "experimental", commit3 }
        };
        var headCommits = new List<CommitId> { commit1, commit2 };

        var original = new ViewData(workspaceCommits, headCommits);

        // Act
        var bytes = original.GetBytesForHashing();
        var parsed = ViewData.ParseFromCanonicalBytes(bytes);

        // Assert
        Assert.Equal(original.WorkspaceCommitIds.Count, parsed.WorkspaceCommitIds.Count);
        Assert.All(original.WorkspaceCommitIds, kvp => 
            Assert.Equal(kvp.Value, parsed.WorkspaceCommitIds[kvp.Key]));
            
        Assert.Equal(original.HeadCommitIds.Count, parsed.HeadCommitIds.Count);
        Assert.All(original.HeadCommitIds, headCommit => 
            Assert.Contains(headCommit, parsed.HeadCommitIds));
    }

    [Fact]
    public void ParseFromCanonicalBytes_RoundTripConsistencyWithBranches()
    {
        // Arrange
        var commit1 = ObjectIdFactory.CreateCommitId("test1"u8.ToArray());
        var commit2 = ObjectIdFactory.CreateCommitId("test2"u8.ToArray());
        var commit3 = ObjectIdFactory.CreateCommitId("test3"u8.ToArray());
        
        var workspaceCommits = new Dictionary<string, CommitId>
        {
            { "default", commit1 },
            { "feature-branch", commit2 }
        };
        var headCommits = new List<CommitId> { commit1, commit2 };
        var branches = new Dictionary<string, CommitId>
        {
            { "main", commit1 },
            { "feature", commit2 },
            { "experimental", commit3 }
        };

        var original = new ViewData(workspaceCommits, headCommits, branches);

        // Act
        var bytes = original.GetBytesForHashing();
        var parsed = ViewData.ParseFromCanonicalBytes(bytes);

        // Assert
        Assert.Equal(original.WorkspaceCommitIds.Count, parsed.WorkspaceCommitIds.Count);
        Assert.All(original.WorkspaceCommitIds, kvp => 
            Assert.Equal(kvp.Value, parsed.WorkspaceCommitIds[kvp.Key]));
            
        Assert.Equal(original.HeadCommitIds.Count, parsed.HeadCommitIds.Count);
        Assert.All(original.HeadCommitIds, headCommit => 
            Assert.Contains(headCommit, parsed.HeadCommitIds));

        // Test branches
        Assert.Equal(original.Branches.Count, parsed.Branches.Count);
        Assert.All(original.Branches, kvp => 
            Assert.Equal(kvp.Value, parsed.Branches[kvp.Key]));
    }

    [Fact]
    public void ParseFromCanonicalBytes_WithWindowsLineEndings_ParsesCorrectly()
    {
        // Arrange
        var commit = ObjectIdFactory.CreateCommitId("test"u8.ToArray());
        var workspaceCommits = new Dictionary<string, CommitId> { { "default", commit } };
        var headCommits = new List<CommitId> { commit };
        var viewData = new ViewData(workspaceCommits, headCommits);
            
        var bytes = viewData.GetBytesForHashing();
        var contentWithCRLF = System.Text.Encoding.UTF8.GetString(bytes).Replace("\n", "\r\n");
        var bytesWithCRLF = System.Text.Encoding.UTF8.GetBytes(contentWithCRLF);

        // Act
        var parsed = ViewData.ParseFromCanonicalBytes(bytesWithCRLF);

        // Assert
        Assert.Equal(viewData.WorkspaceCommitIds.Count, parsed.WorkspaceCommitIds.Count);
        Assert.Equal(viewData.HeadCommitIds.Count, parsed.HeadCommitIds.Count);
    }

    [Fact]
    public void ParseFromCanonicalBytes_WithInvalidFormat_ThrowsArgumentException()
    {
        // Arrange
        var invalidBytes = System.Text.Encoding.UTF8.GetBytes("invalid format");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => ViewData.ParseFromCanonicalBytes(invalidBytes));
    }

    [Fact]
    public void ParseFromCanonicalBytes_WithNullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ViewData.ParseFromCanonicalBytes(null!));
    }

    [Fact]
    public void Empty_ReturnsEmptyViewData()
    {
        // Act
        var empty = ViewData.Empty;

        // Assert
        Assert.Empty(empty.WorkspaceCommitIds);
        Assert.Empty(empty.HeadCommitIds);
    }

    [Fact]
    public void WithWorkspaceCommit_AddsNewWorkspace()
    {
        // Arrange
        var commit1 = ObjectIdFactory.CreateCommitId("commit1"u8.ToArray());
        var commit2 = ObjectIdFactory.CreateCommitId("commit2"u8.ToArray());
        
        var original = new ViewData(
            new Dictionary<string, CommitId> { { "default", commit1 } },
            new List<CommitId> { commit1 });

        // Act
        var updated = original.WithWorkspaceCommit("feature", commit2);

        // Assert
        Assert.Equal(2, updated.WorkspaceCommitIds.Count);
        Assert.Equal(commit1, updated.WorkspaceCommitIds["default"]);
        Assert.Equal(commit2, updated.WorkspaceCommitIds["feature"]);
        Assert.Equal(original.HeadCommitIds, updated.HeadCommitIds);
    }

    [Fact]
    public void WithWorkspaceCommit_UpdatesExistingWorkspace()
    {
        // Arrange
        var commit1 = ObjectIdFactory.CreateCommitId("commit1"u8.ToArray());
        var commit2 = ObjectIdFactory.CreateCommitId("commit2"u8.ToArray());
        
        var original = new ViewData(
            new Dictionary<string, CommitId> { { "default", commit1 } },
            new List<CommitId> { commit1 });

        // Act
        var updated = original.WithWorkspaceCommit("default", commit2);

        // Assert
        Assert.Single(updated.WorkspaceCommitIds);
        Assert.Equal(commit2, updated.WorkspaceCommitIds["default"]);
        Assert.Equal(original.HeadCommitIds, updated.HeadCommitIds);
    }

    [Fact]
    public void WithHeadCommit_AddsNewHead()
    {
        // Arrange
        var commit1 = ObjectIdFactory.CreateCommitId("commit1"u8.ToArray());
        var commit2 = ObjectIdFactory.CreateCommitId("commit2"u8.ToArray());
        
        var original = new ViewData(
            new Dictionary<string, CommitId> { { "default", commit1 } },
            new List<CommitId> { commit1 });

        // Act
        var updated = original.WithHeadCommit(commit2);

        // Assert
        Assert.Equal(2, updated.HeadCommitIds.Count);
        Assert.Contains(commit1, updated.HeadCommitIds);
        Assert.Contains(commit2, updated.HeadCommitIds);
        Assert.Equal(original.WorkspaceCommitIds, updated.WorkspaceCommitIds);
    }

    [Fact]
    public void WithHeadCommit_WithExistingHead_ReturnsUnchanged()
    {
        // Arrange
        var commit1 = ObjectIdFactory.CreateCommitId("commit1"u8.ToArray());
        
        var original = new ViewData(
            new Dictionary<string, CommitId> { { "default", commit1 } },
            new List<CommitId> { commit1 });

        // Act
        var updated = original.WithHeadCommit(commit1);

        // Assert
        Assert.Equal(original, updated);
    }
}
