using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using HPD.VCS.Core;

namespace HPD.VCS.Tests;

/// <summary>
/// Unit tests for core data objects: FileContentData, TreeEntry, TreeData, Signature, CommitData
/// </summary>
public class CoreDataObjectsTests
{
    #region FileContentData Tests

    [Fact]
    public void FileContentData_Constructor_ValidContent_Success()
    {
        var content = "Hello, World!"u8.ToArray();
        var fileData = new FileContentData(content);
        
        Assert.Equal(content, fileData.Content);
    }

    [Fact]
    public void FileContentData_Constructor_NullContent_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FileContentData(null!));
    }

    [Fact]
    public void FileContentData_Constructor_DefensiveCopy()
    {
        var originalContent = "Original content"u8.ToArray();
        var fileData = new FileContentData(originalContent);
        
        // Modify original array
        originalContent[0] = 0xFF;
        
        // FileContentData should be unaffected
        Assert.NotEqual(originalContent[0], fileData.Content[0]);
    }

    [Fact]
    public void FileContentData_Immutability_PropertiesCannotChange()
    {
        var content = "Test content"u8.ToArray();
        var fileData = new FileContentData(content);
        
        // Verify we can't modify the Content array
        // (This is enforced by the init-only property)
        var contentBytes = fileData.Content;
        Assert.NotSame(content, contentBytes); // Should be different instance due to defensive copy
    }

    [Fact]
    public void FileContentData_GetBytesForHashing_ReturnsSameContent()
    {
        var content = "Hashing test content"u8.ToArray();
        var fileData = new FileContentData(content);
        
        var hashBytes = fileData.GetBytesForHashing();
        
        Assert.Equal(content, hashBytes);
    }

    [Fact]
    public void FileContentData_GetBytesForHashing_Deterministic()
    {
        var content = "Deterministic test"u8.ToArray();
        var fileData = new FileContentData(content);
        
        var hashBytes1 = fileData.GetBytesForHashing();
        var hashBytes2 = fileData.GetBytesForHashing();
        
        Assert.Equal(hashBytes1, hashBytes2);
    }

    [Fact]
    public void FileContentData_SameContent_ProducesSameId()
    {
        var content = "Same content test"u8.ToArray();
        var fileData1 = new FileContentData(content);
        var fileData2 = new FileContentData((byte[])content.Clone());
        
        var id1 = ObjectHasher.ComputeFileContentId(fileData1);
        var id2 = ObjectHasher.ComputeFileContentId(fileData2);
        
        Assert.Equal(id1, id2);    }

    #endregion

    #region TreeEntry Tests

    [Fact]
    public void TreeEntry_Constructor_ValidParameters_Success()
    {
        var fileId = ObjectIdFactory.CreateFileContentId("test content"u8.ToArray());
        var entry = new TreeEntry(new RepoPathComponent("test.txt"), TreeEntryType.File, new ObjectIdBase(fileId.HashValue.ToArray()));
        
        Assert.Equal("test.txt", entry.Name.Value);
        Assert.Equal(TreeEntryType.File, entry.Type);
        Assert.Equal(fileId.HashValue.ToArray(), entry.ObjectId.HashValue.ToArray());
    }

    [Fact]
    public void TreeEntry_Constructor_RepoPathComponent_Validation()
    {
        var fileId = ObjectIdFactory.CreateFileContentId("test"u8.ToArray());
        // Testing RepoPathComponent validation since TreeEntry takes RepoPathComponent
        Assert.Throws<ArgumentNullException>(() => new RepoPathComponent(null!));
        Assert.Throws<ArgumentException>(() => new RepoPathComponent(""));
        Assert.Throws<ArgumentException>(() => new RepoPathComponent("   "));
    }

    [Fact]
    public void TreeEntry_Constructor_InvalidNames_ThrowsArgumentException()
    {
        var fileId = ObjectIdFactory.CreateFileContentId("test"u8.ToArray());
        
        // Test invalid characters through RepoPathComponent
        Assert.Throws<ArgumentException>(() => new RepoPathComponent("test/file.txt"));
        Assert.Throws<ArgumentException>(() => new RepoPathComponent("test\\file.txt"));
        
        // Test reserved names
        Assert.Throws<ArgumentException>(() => new RepoPathComponent("."));
        Assert.Throws<ArgumentException>(() => new RepoPathComponent(".."));
    }    [Fact]
    public void TreeEntry_FileType_PropertiesCorrect()
    {
        var fileId = ObjectIdFactory.CreateFileContentId("test content"u8.ToArray());
        var entry = new TreeEntry(new RepoPathComponent("test.txt"), TreeEntryType.File, new ObjectIdBase(fileId.HashValue.ToArray()));
        
        Assert.Equal("test.txt", entry.Name.Value);
        Assert.Equal(TreeEntryType.File, entry.Type);
        Assert.Equal(fileId.ToHexString(), entry.ObjectId.ToHexString());
    }

    [Fact]
    public void TreeEntry_DirectoryType_PropertiesCorrect()
    {
        var treeId = ObjectIdFactory.CreateTreeId("test tree content"u8.ToArray());
        var entry = new TreeEntry(new RepoPathComponent("subdir"), TreeEntryType.Directory, new ObjectIdBase(treeId.HashValue.ToArray()));
        
        Assert.Equal("subdir", entry.Name.Value);
        Assert.Equal(TreeEntryType.Directory, entry.Type);
        Assert.Equal(treeId.ToHexString(), entry.ObjectId.ToHexString());
    }

    [Fact]
    public void TreeEntry_Comparison_SortsByName()
    {
        var fileId = ObjectIdFactory.CreateFileContentId("test"u8.ToArray());
        var entry1 = new TreeEntry(new RepoPathComponent("a.txt"), TreeEntryType.File, new ObjectIdBase(fileId.HashValue.ToArray()));
        var entry2 = new TreeEntry(new RepoPathComponent("b.txt"), TreeEntryType.File, new ObjectIdBase(fileId.HashValue.ToArray()));
        
        Assert.True(entry1.CompareTo(entry2) < 0);
        Assert.True(entry2.CompareTo(entry1) > 0);
        Assert.Equal(0, entry1.CompareTo(entry1));
    }

    #endregion

    #region TreeData Tests

    [Fact]
    public void TreeData_Constructor_ValidEntries_Success()
    {
        var fileId = ObjectIdFactory.CreateFileContentId("test"u8.ToArray());
        var entries = new List<TreeEntry>
        {
            new TreeEntry(new RepoPathComponent("file1.txt"), TreeEntryType.File, new ObjectIdBase(fileId.HashValue.ToArray())),
            new TreeEntry(new RepoPathComponent("file2.txt"), TreeEntryType.File, new ObjectIdBase(fileId.HashValue.ToArray()))
        };
        
        var treeData = new TreeData(entries);
        
        Assert.Equal(2, treeData.Entries.Count);
        Assert.Equal("file1.txt", treeData.Entries[0].Name.Value);
        Assert.Equal("file2.txt", treeData.Entries[1].Name.Value);
    }

    [Fact]
    public void TreeData_Constructor_NullEntries_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TreeData(null!));
    }

    [Fact]
    public void TreeData_Constructor_AutomaticSorting()
    {
        var fileId = ObjectIdFactory.CreateFileContentId("test"u8.ToArray());
        var unorderedEntries = new List<TreeEntry>
        {
            new TreeEntry(new RepoPathComponent("z-file.txt"), TreeEntryType.File, new ObjectIdBase(fileId.HashValue.ToArray())),
            new TreeEntry(new RepoPathComponent("a-file.txt"), TreeEntryType.File, new ObjectIdBase(fileId.HashValue.ToArray())),
            new TreeEntry(new RepoPathComponent("middle.txt"), TreeEntryType.File, new ObjectIdBase(fileId.HashValue.ToArray()))
        };
        
        var treeData = new TreeData(unorderedEntries);
        
        // Should be sorted alphabetically
        Assert.Equal("a-file.txt", treeData.Entries[0].Name.Value);
        Assert.Equal("middle.txt", treeData.Entries[1].Name.Value);
        Assert.Equal("z-file.txt", treeData.Entries[2].Name.Value);
    }

    [Fact]
    public void TreeData_Constructor_DuplicateNames_ThrowsArgumentException()
    {
        var fileId = ObjectIdFactory.CreateFileContentId("test"u8.ToArray());
        var entriesWithDuplicates = new List<TreeEntry>
        {
            new TreeEntry(new RepoPathComponent("duplicate.txt"), TreeEntryType.File, new ObjectIdBase(fileId.HashValue.ToArray())),
            new TreeEntry(new RepoPathComponent("duplicate.txt"), TreeEntryType.File, new ObjectIdBase(fileId.HashValue.ToArray()))
        };
        
        Assert.Throws<ArgumentException>(() => new TreeData(entriesWithDuplicates));
    }

    [Fact]
    public void TreeData_Immutability_EntriesCannotBeModified()
    {
        var fileId = ObjectIdFactory.CreateFileContentId("test"u8.ToArray());
        var entries = new List<TreeEntry>
        {
            new TreeEntry(new RepoPathComponent("test.txt"), TreeEntryType.File, new ObjectIdBase(fileId.HashValue.ToArray()))
        };
        
        var treeData = new TreeData(entries);
        
        // Original list modification should not affect TreeData
        entries.Add(new TreeEntry(new RepoPathComponent("new.txt"), TreeEntryType.File, new ObjectIdBase(fileId.HashValue.ToArray())));
        
        Assert.Single(treeData.Entries);
    }    [Fact]
    public void TreeData_GetBytesForHashing_ProperFormat()
    {
        var fileId1 = ObjectIdFactory.CreateFileContentId("content1"u8.ToArray());
        var fileId2 = ObjectIdFactory.CreateFileContentId("content2"u8.ToArray());
        var entries = new List<TreeEntry>
        {
            new TreeEntry(new RepoPathComponent("file1.txt"), TreeEntryType.File, new ObjectIdBase(fileId1.HashValue.ToArray())),
            new TreeEntry(new RepoPathComponent("file2.txt"), TreeEntryType.File, new ObjectIdBase(fileId2.HashValue.ToArray()))
        };
        
        var treeData = new TreeData(entries);
        var hashBytes = treeData.GetBytesForHashing();
        var hashString = Encoding.UTF8.GetString(hashBytes);
        
        // Should contain entries in the format: "type name hash\ntype name hash"
        var expectedContent = $"file file1.txt {fileId1.ToHexString()}\nfile file2.txt {fileId2.ToHexString()}";
        Assert.Equal(expectedContent, hashString);
    }

    [Fact]
    public void TreeData_SameEntries_ProducesSameId()
    {
        var fileId = ObjectIdFactory.CreateFileContentId("test"u8.ToArray());
        var entries1 = new List<TreeEntry>
        {
            new TreeEntry(new RepoPathComponent("test.txt"), TreeEntryType.File, new ObjectIdBase(fileId.HashValue.ToArray()))
        };
        var entries2 = new List<TreeEntry>
        {
            new TreeEntry(new RepoPathComponent("test.txt"), TreeEntryType.File, new ObjectIdBase(fileId.HashValue.ToArray()))
        };
        
        var treeData1 = new TreeData(entries1);
        var treeData2 = new TreeData(entries2);
        
        var id1 = ObjectHasher.ComputeTreeId(treeData1);
        var id2 = ObjectHasher.ComputeTreeId(treeData2);
        
        Assert.Equal(id1, id2);
    }

    #endregion

    #region Signature Tests

    [Fact]
    public void Signature_Constructor_ValidParameters_Success()
    {
        var timestamp = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.FromHours(-8));
        var signature = new Signature("John Doe", "john@example.com", timestamp);
        
        Assert.Equal("John Doe", signature.Name);
        Assert.Equal("john@example.com", signature.Email);
        Assert.Equal(timestamp, signature.Timestamp);
    }

    [Fact]
    public void Signature_GetBytesForHashing_CorrectFormat()
    {
        var timestamp = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.FromHours(-8));
        var signature = new Signature("John Doe", "john@example.com", timestamp);
        
        var hashBytes = signature.GetBytesForHashing();        var hashString = Encoding.UTF8.GetString(hashBytes);
        
        // Should be: "Name <Email> UnixTimestamp TimezoneOffset"
        var unixTimestamp = timestamp.ToUnixTimeMilliseconds();
        var expected = $"John Doe <john@example.com> {unixTimestamp} -0800";
        
        Assert.Equal(expected, hashString);
    }

    [Fact]
    public void Signature_GetBytesForHashing_DifferentTimezones()
    {
        var utcTime = new DateTimeOffset(2024, 1, 15, 18, 30, 0, TimeSpan.Zero);
        var pstTime = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.FromHours(-8));
        
        var utcSig = new Signature("John", "john@test.com", utcTime);
        var pstSig = new Signature("John", "john@test.com", pstTime);
        
        var utcBytes = utcSig.GetBytesForHashing();
        var pstBytes = pstSig.GetBytesForHashing();
        
        // Should have different timezone representations
        var utcString = Encoding.UTF8.GetString(utcBytes);
        var pstString = Encoding.UTF8.GetString(pstBytes);
        
        Assert.Contains("+0000", utcString);
        Assert.Contains("-0800", pstString);
        Assert.NotEqual(utcString, pstString);
    }

    [Fact]
    public void Signature_ToString_HumanReadableFormat()
    {
        var timestamp = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.FromHours(-8));
        var signature = new Signature("John Doe", "john@example.com", timestamp);
        
        var result = signature.ToString();
        
        Assert.Contains("John Doe", result);
        Assert.Contains("john@example.com", result);
        Assert.Contains("2024-01-15", result);
    }

    [Fact]
    public void Signature_SameData_ProducesSameHashBytes()
    {
        var timestamp = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.FromHours(-8));
        var signature1 = new Signature("John Doe", "john@example.com", timestamp);
        var signature2 = new Signature("John Doe", "john@example.com", timestamp);
        
        var hashBytes1 = signature1.GetBytesForHashing();
        var hashBytes2 = signature2.GetBytesForHashing();
        
        Assert.Equal(hashBytes1, hashBytes2);
    }

    #endregion

    #region CommitData Tests

    [Fact]
    public void CommitData_Constructor_ValidParameters_Success()
    {
        var treeId = ObjectIdFactory.CreateTreeId("tree content"u8.ToArray());
        var changeId = ObjectIdFactory.CreateChangeId("change"u8.ToArray());
        var timestamp = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.FromHours(-8));
        var author = new Signature("Author", "author@test.com", timestamp);
        var committer = new Signature("Committer", "committer@test.com", timestamp);
        
        var commitData = new CommitData(
            Array.Empty<CommitId>(),
            treeId,
            changeId,
            "Initial commit",
            author,
            committer);
        
        Assert.Empty(commitData.ParentIds);
        Assert.Equal(treeId, commitData.RootTreeId);
        Assert.Equal(changeId, commitData.AssociatedChangeId);
        Assert.Equal("Initial commit", commitData.Description);
        Assert.Equal(author, commitData.Author);
        Assert.Equal(committer, commitData.Committer);
    }

    [Fact]
    public void CommitData_Constructor_NullParameters_ThrowsArgumentNullException()
    {
        var treeId = ObjectIdFactory.CreateTreeId("tree"u8.ToArray());
        var changeId = ObjectIdFactory.CreateChangeId("change"u8.ToArray());
        var timestamp = DateTimeOffset.Now;
        var author = new Signature("Author", "author@test.com", timestamp);
        var committer = new Signature("Committer", "committer@test.com", timestamp);
        
        Assert.Throws<ArgumentNullException>(() => new CommitData(
            null!, treeId, changeId, "message", author, committer));
        Assert.Throws<ArgumentNullException>(() => new CommitData(
            Array.Empty<CommitId>(), treeId, changeId, null!, author, committer));
    }

    [Fact]
    public void CommitData_GetBytesForHashing_CorrectFormat()
    {
        var treeId = ObjectIdFactory.CreateTreeId("tree content"u8.ToArray());
        var changeId = ObjectIdFactory.CreateChangeId("change"u8.ToArray());
        var timestamp = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.FromHours(-8));
        var author = new Signature("Author", "author@test.com", timestamp);
        var committer = new Signature("Committer", "committer@test.com", timestamp);
        
        var commitData = new CommitData(
            Array.Empty<CommitId>(),
            treeId,
            changeId,
            "Test commit message",
            author,
            committer);
        
        var hashBytes = commitData.GetBytesForHashing();
        var hashString = Encoding.UTF8.GetString(hashBytes);
        
        // Should contain expected format
        Assert.Contains($"tree {treeId.ToHexString()}", hashString);
        Assert.Contains("author ", hashString);
        Assert.Contains("committer ", hashString);
        Assert.Contains($"change {changeId.ToHexString()}", hashString);
        Assert.Contains("Test commit message", hashString);
    }

    [Fact]
    public void CommitData_GetBytesForHashing_WithParents_CorrectOrder()
    {
        var parent1 = ObjectIdFactory.CreateCommitId("parent1"u8.ToArray());
        var parent2 = ObjectIdFactory.CreateCommitId("parent2"u8.ToArray());
        var treeId = ObjectIdFactory.CreateTreeId("tree"u8.ToArray());
        var changeId = ObjectIdFactory.CreateChangeId("change"u8.ToArray());
        var timestamp = DateTimeOffset.Now;
        var author = new Signature("Author", "author@test.com", timestamp);
        
        // Create commit with unordered parents
        var commitData = new CommitData(
            new[] { parent2, parent1 }, // Intentionally out of order
            treeId,
            changeId,
            "Merge commit",
            author,
            author);
        
        var hashBytes = commitData.GetBytesForHashing();
        var hashString = Encoding.UTF8.GetString(hashBytes);
        
        // Parents should be sorted by hex string for determinism
        var parent1Hex = parent1.ToHexString();
        var parent2Hex = parent2.ToHexString();
        var parent1Index = hashString.IndexOf($"parent {parent1Hex}", StringComparison.Ordinal);
        var parent2Index = hashString.IndexOf($"parent {parent2Hex}", StringComparison.Ordinal);
        
        if (string.Compare(parent1Hex, parent2Hex, StringComparison.Ordinal) < 0)
        {
            Assert.True(parent1Index < parent2Index, "Parents should be sorted by hex string");
        }
        else
        {
            Assert.True(parent2Index < parent1Index, "Parents should be sorted by hex string");
        }
    }

    [Fact]
    public void CommitData_SameData_ProducesSameId()
    {
        var treeId = ObjectIdFactory.CreateTreeId("tree"u8.ToArray());
        var changeId = ObjectIdFactory.CreateChangeId("change"u8.ToArray());
        var timestamp = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.FromHours(-8));
        var author = new Signature("Author", "author@test.com", timestamp);
        
        var commitData1 = new CommitData(
            Array.Empty<CommitId>(),
            treeId,
            changeId,
            "Same commit",
            author,
            author);
        
        var commitData2 = new CommitData(
            Array.Empty<CommitId>(),
            treeId,
            changeId,
            "Same commit",
            author,
            author);
        
        var id1 = ObjectHasher.ComputeCommitId(commitData1);
        var id2 = ObjectHasher.ComputeCommitId(commitData2);
        
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void CommitData_ToString_CorrectFormat()
    {
        var treeId = ObjectIdFactory.CreateTreeId("tree"u8.ToArray());
        var changeId = ObjectIdFactory.CreateChangeId("change"u8.ToArray());
        var timestamp = DateTimeOffset.Now;
        var author = new Signature("Author", "author@test.com", timestamp);
        
        // Root commit
        var rootCommit = new CommitData(
            Array.Empty<CommitId>(),
            treeId,
            changeId,
            "Initial commit\n\nFirst commit in repository.",
            author,
            author);
        
        var rootString = rootCommit.ToString();
        Assert.Contains("root commit", rootString);
        Assert.Contains("Initial commit", rootString);
        
        // Commit with parent
        var parentId = ObjectIdFactory.CreateCommitId("parent"u8.ToArray());
        var childCommit = new CommitData(
            new[] { parentId },
            treeId,
            changeId,
            "Second commit",
            author,
            author);
        
        var childString = childCommit.ToString();
        Assert.Contains("parent:", childString);
        Assert.Contains("Second commit", childString);
    }

    [Fact]
    public void CommitData_Immutability_PropertiesCannotChange()
    {
        var treeId = ObjectIdFactory.CreateTreeId("tree"u8.ToArray());
        var changeId = ObjectIdFactory.CreateChangeId("change"u8.ToArray());
        var timestamp = DateTimeOffset.Now;
        var author = new Signature("Author", "author@test.com", timestamp);
        var parents = new List<CommitId>();
        
        var commitData = new CommitData(
            parents,
            treeId,
            changeId,
            "Test commit",
            author,
            author);
        
        // Modifying original list should not affect CommitData
        parents.Add(ObjectIdFactory.CreateCommitId("new parent"u8.ToArray()));
        
        Assert.Empty(commitData.ParentIds);
    }

    #endregion
}
