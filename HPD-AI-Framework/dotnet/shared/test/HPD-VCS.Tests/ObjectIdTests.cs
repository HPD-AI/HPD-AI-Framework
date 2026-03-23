using System;
using System.Linq;
using Xunit;
using HPD.VCS.Core;

namespace HPD.VCS.Tests;

/// <summary>
/// Unit tests for all Object ID types
/// </summary>
public class ObjectIdTests
{
    private static readonly byte[] TestHashBytes = 
    {
        0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
        0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
        0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
        0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF
    };    private const string ExpectedHexString = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ExpectedShortHex = "0123456789ab";
    
    #region ObjectIdBase Tests

    [Fact]
    public void ObjectIdBase_Constructor_ValidHash_Success()
    {
        var id = new ObjectIdBase(TestHashBytes);
        Assert.True(TestHashBytes.AsSpan().SequenceEqual(id.HashValue.Span));
    }

    [Fact]
    public void ObjectIdBase_Constructor_NullHash_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ObjectIdBase(null!));
    }

    [Fact]
    public void ObjectIdBase_Constructor_InvalidLength_ThrowsArgumentException()
    {
        var shortHash = new byte[16]; // Too short
        var longHash = new byte[64];  // Too long
        
        Assert.Throws<ArgumentException>(() => new ObjectIdBase(shortHash));
        Assert.Throws<ArgumentException>(() => new ObjectIdBase(longHash));
    }    [Fact]
    public void ObjectIdBase_Constructor_DefensiveCopy()
    {
        var originalHash = (byte[])TestHashBytes.Clone();
        var id = new ObjectIdBase(originalHash);
        
        // Verify original state
        Assert.Equal(1, originalHash[0]); // Should be 0x01
        Assert.Equal(1, id.HashValue.Span[0]); // Should also be 0x01
        
        // Modify original array
        originalHash[0] = 0xFF;
        
        // Verify modification happened
        Assert.Equal(255, originalHash[0]); // Should now be 0xFF
        
        // ObjectIdBase should be unaffected (defensive copy worked)
        Assert.Equal(TestHashBytes[0], id.HashValue.Span[0]); // Should still be 0x01
        Assert.NotEqual(originalHash[0], id.HashValue.Span[0]); // 255 != 1
    }

    [Fact]
    public void ObjectIdBase_ToHexString_ReturnsCorrectFormat()
    {
        var id = new ObjectIdBase(TestHashBytes);
        Assert.Equal(ExpectedHexString, id.ToHexString());
    }

    [Fact]
    public void ObjectIdBase_ToShortHexString_ReturnsCorrectLength()
    {
        var id = new ObjectIdBase(TestHashBytes);
        Assert.Equal(ExpectedShortHex, id.ToShortHexString());
        Assert.Equal("01234567", id.ToShortHexString(8));
        Assert.Equal("01", id.ToShortHexString(2));
    }

    [Fact]
    public void ObjectIdBase_ToString_ReturnsShortHex()
    {
        var id = new ObjectIdBase(TestHashBytes);
        Assert.Equal(ExpectedShortHex, id.ToString());
    }

    [Fact]
    public void ObjectIdBase_Equals_SameHash_ReturnsTrue()
    {
        var id1 = new ObjectIdBase(TestHashBytes);
        var id2 = new ObjectIdBase((byte[])TestHashBytes.Clone());
        
        Assert.True(id1.Equals(id2));
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void ObjectIdBase_Equals_DifferentHash_ReturnsFalse()
    {
        var differentHash = (byte[])TestHashBytes.Clone();
        differentHash[0] = 0xFF;
        
        var id1 = new ObjectIdBase(TestHashBytes);
        var id2 = new ObjectIdBase(differentHash);
        
        Assert.False(id1.Equals(id2));
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void ObjectIdBase_GetHashCode_SameHash_SameHashCode()
    {
        var id1 = new ObjectIdBase(TestHashBytes);
        var id2 = new ObjectIdBase((byte[])TestHashBytes.Clone());
        
        Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
    }

    [Fact]    public void ObjectIdBase_FromHexString_ValidHex_Success()
    {
        var parsedId = ObjectIdBase.FromHexString<CommitId>(ExpectedHexString);
        Assert.True(TestHashBytes.AsSpan().SequenceEqual(parsedId.HashValue.Span));
    }

    [Fact]
    public void ObjectIdBase_FromHexString_InvalidLength_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => ObjectIdBase.FromHexString<CommitId>("short"));
        Assert.Throws<ArgumentException>(() => ObjectIdBase.FromHexString<CommitId>("toolongbyfarmorethan64characters"));
    }

    [Fact]
    public void ObjectIdBase_FromHexString_InvalidCharacters_ThrowsArgumentException()
    {
        var invalidHex = ExpectedHexString.Replace('a', 'g'); // Invalid hex character
        Assert.Throws<ArgumentException>(() => ObjectIdBase.FromHexString<CommitId>(invalidHex));
    }    [Fact]
    public void ObjectIdBase_FromHexString_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ObjectIdBase.FromHexString<CommitId>(null!));
    }

    #endregion

    #region Specific ID Type Tests

    [Fact]
    public void CommitId_Creation_Success()
    {
        var commitId = new CommitId(TestHashBytes);
        
        Assert.True(TestHashBytes.AsSpan().SequenceEqual(commitId.HashValue.Span));
        Assert.Equal(ExpectedHexString, commitId.ToHexString());
        Assert.Equal(ExpectedShortHex, commitId.ToShortHexString());
        Assert.Equal($"CommitId({ExpectedShortHex})", commitId.ToString());
    }    [Fact]
    public void TreeId_Creation_Success()
    {
        var treeId = new TreeId(TestHashBytes);
        
        Assert.True(TestHashBytes.AsSpan().SequenceEqual(treeId.HashValue.Span));
        Assert.Equal(ExpectedHexString, treeId.ToHexString());
        Assert.Equal(ExpectedShortHex, treeId.ToShortHexString());
        Assert.Equal($"TreeId({ExpectedShortHex})", treeId.ToString());
    }

    [Fact]
    public void FileContentId_Creation_Success()    {
        var fileId = new FileContentId(TestHashBytes);
        
        Assert.True(TestHashBytes.AsSpan().SequenceEqual(fileId.HashValue.Span));
        Assert.Equal(ExpectedHexString, fileId.ToHexString());
        Assert.Equal(ExpectedShortHex, fileId.ToShortHexString());
        Assert.Equal($"FileContentId({ExpectedShortHex})", fileId.ToString());
    }    [Fact]
    public void ChangeId_Creation_Success()
    {
        var changeId = new ChangeId(TestHashBytes);
        
        Assert.True(TestHashBytes.AsSpan().SequenceEqual(changeId.HashValue.Span));
        Assert.Equal(ExpectedHexString, changeId.ToHexString());
        Assert.Equal(ExpectedShortHex, changeId.ToShortHexString());
        Assert.Equal($"ChangeId({ExpectedShortHex})", changeId.ToString());
    }    [Fact]
    public void AllIdTypes_Equality_WorksCorrectly()
    {
        var commitId1 = new CommitId(TestHashBytes);
        var commitId2 = new CommitId((byte[])TestHashBytes.Clone());
        var treeId = new TreeId(TestHashBytes);
        
        // Same type, same hash
        Assert.Equal(commitId1, commitId2);
        
        // Different types have same hash value but are different objects
        Assert.True(commitId1.HashValue.Span.SequenceEqual(treeId.HashValue.Span)); // Same hash
        Assert.False(commitId1.Equals((object)treeId)); // But different types
    }

    [Fact]
    public void AllIdTypes_FromHexString_RoundTrip()
    {
        // Test all ID types
        var commitId = ObjectIdBase.FromHexString<CommitId>(ExpectedHexString);
        var treeId = ObjectIdBase.FromHexString<TreeId>(ExpectedHexString);
        var fileId = ObjectIdBase.FromHexString<FileContentId>(ExpectedHexString);
        var changeId = ObjectIdBase.FromHexString<ChangeId>(ExpectedHexString);
        
        Assert.Equal(ExpectedHexString, commitId.ToHexString());
        Assert.Equal(ExpectedHexString, treeId.ToHexString());
        Assert.Equal(ExpectedHexString, fileId.ToHexString());
        Assert.Equal(ExpectedHexString, changeId.ToHexString());
    }

    #endregion

    #region ObjectIdFactory Tests

    [Fact]
    public void ObjectIdFactory_HashContent_ByteArray_Success()
    {
        var content = "Hello, World!"u8.ToArray();
        var hash = ObjectIdFactory.HashContent(content);
        
        Assert.Equal(32, hash.Length); // SHA256 length
        Assert.NotNull(hash);
    }

    [Fact]
    public void ObjectIdFactory_HashContent_String_Success()
    {
        var content = "Hello, World!";
        var hash = ObjectIdFactory.HashContent(content);
        
        Assert.Equal(32, hash.Length); // SHA256 length
        Assert.NotNull(hash);
    }

    [Fact]    public void ObjectIdFactory_CreateIds_SameContent_SameHash()
    {
        var content = "Test content"u8.ToArray();
        
        var commitId1 = ObjectIdFactory.CreateCommitId(content);
        var commitId2 = ObjectIdFactory.CreateCommitId(content);
        var treeId = ObjectIdFactory.CreateTreeId(content);
        var fileId = ObjectIdFactory.CreateFileContentId(content);
        var changeId = ObjectIdFactory.CreateChangeId(content);
        
        // Same content, same ID type should produce identical IDs
        Assert.Equal(commitId1, commitId2);
        
        // Same content, different ID types should have same hash value
        Assert.True(commitId1.HashValue.Span.SequenceEqual(treeId.HashValue.Span));
        Assert.True(commitId1.HashValue.Span.SequenceEqual(fileId.HashValue.Span));
        Assert.True(commitId1.HashValue.Span.SequenceEqual(changeId.HashValue.Span));
    }

    [Fact]
    public void ObjectIdFactory_Deterministic_MultipleRuns()
    {
        var content = "Deterministic test"u8.ToArray();
        
        var id1 = ObjectIdFactory.CreateCommitId(content);
        var id2 = ObjectIdFactory.CreateCommitId(content);
        var id3 = ObjectIdFactory.CreateCommitId(content);
        
        Assert.Equal(id1, id2);
        Assert.Equal(id2, id3);
        Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
    }

    #endregion
}
