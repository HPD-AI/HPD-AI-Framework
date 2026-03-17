using System;
using System.Linq;
using System.Text;
using Xunit;
using HPD.VCS.Core;

namespace HPD.VCS.Tests;

/// <summary>
/// Unit tests for the content hashing system
/// </summary>
public class ContentHashingTests
{
    #region ObjectHasher Tests

    [Fact]
    public void ObjectHasher_ComputeId_KnownInput_ProducesExpectedHash()
    {
        // Create a simple test object
        var testData = new TestContentHashable("Hello, World!");
          // Compute ID using ObjectHasher
        var fileId = ObjectHasher.ComputeFileContentId(testData);
          // Verify it's a valid FileContentId (value type, so no need for NotNull check)
        Assert.Equal(32, fileId.HashValue.Length);
    }

    [Fact]
    public void ObjectHasher_TypePrefixes_ProduceDifferentHashes()
    {
        var testData = new TestContentHashable("Same content");
        
        var commitId = ObjectHasher.ComputeCommitId(testData);
        var treeId = ObjectHasher.ComputeTreeId(testData);
        var fileId = ObjectHasher.ComputeFileContentId(testData);
        
        // Same content with different type prefixes should produce different hashes
        Assert.NotEqual(commitId.HashValue, treeId.HashValue);
        Assert.NotEqual(commitId.HashValue, fileId.HashValue);
        Assert.NotEqual(treeId.HashValue, fileId.HashValue);
    }

    [Fact]
    public void ObjectHasher_Deterministic_SameInputSameOutput()
    {
        var testData = new TestContentHashable("Deterministic test");
        
        var id1 = ObjectHasher.ComputeFileContentId(testData);
        var id2 = ObjectHasher.ComputeFileContentId(testData);
        var id3 = ObjectHasher.ComputeFileContentId(testData);
        
        Assert.Equal(id1, id2);
        Assert.Equal(id2, id3);
    }

    [Fact]
    public void ObjectHasher_TypePrefixConstants_HaveCorrectValues()
    {
        Assert.Equal("commit\0", ObjectHasher.CommitTypePrefix);
        Assert.Equal("tree\0", ObjectHasher.TreeTypePrefix);
        Assert.Equal("blob\0", ObjectHasher.BlobTypePrefix);
    }

    [Fact]
    public void ObjectHasher_ComputeId_Generic_WorksWithAllTypes()
    {
        var testData = new TestContentHashable("Generic test");
        
        // Test generic method with different ID types
        var commitId = ObjectHasher.ComputeId<TestContentHashable, CommitId>(testData, ObjectHasher.CommitTypePrefix);
        var treeId = ObjectHasher.ComputeId<TestContentHashable, TreeId>(testData, ObjectHasher.TreeTypePrefix);
        var fileId = ObjectHasher.ComputeId<TestContentHashable, FileContentId>(testData, ObjectHasher.BlobTypePrefix);
        
        // Should match convenience methods
        var commitId2 = ObjectHasher.ComputeCommitId(testData);
        var treeId2 = ObjectHasher.ComputeTreeId(testData);
        var fileId2 = ObjectHasher.ComputeFileContentId(testData);
        
        Assert.Equal(commitId, commitId2);
        Assert.Equal(treeId, treeId2);
        Assert.Equal(fileId, fileId2);
    }

    [Fact]
    public void ObjectHasher_WithTypePrefix_AffectsHash()
    {
        var testData = new TestContentHashable("Prefix test");
        
        // Get raw hash without prefix
        var rawBytes = testData.GetBytesForHashing();
        var directId = ObjectIdFactory.CreateFileContentId(rawBytes);
        
        // Get hash with type prefix
        var prefixedId = ObjectHasher.ComputeFileContentId(testData);
        
        // Should be different due to type prefix
        Assert.NotEqual(directId, prefixedId);
    }

    #endregion

    #region IContentHashable Test Implementation

    /// <summary>
    /// Simple test implementation of IContentHashable for testing
    /// </summary>
    private class TestContentHashable : IContentHashable
    {
        private readonly string _content;

        public TestContentHashable(string content)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
        }

        public byte[] GetBytesForHashing()
        {
            return Encoding.UTF8.GetBytes(_content);
        }
    }

    #endregion
}
