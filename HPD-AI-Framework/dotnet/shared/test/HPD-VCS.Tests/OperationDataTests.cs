using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using HPD.VCS.Core;

namespace HPD.VCS.Tests;

public class OperationDataTests
{
    [Fact]
    public void Constructor_WithValidInputs_CreatesOperationData()
    {
        // Arrange
        var viewId = ObjectIdFactory.CreateViewId("view"u8.ToArray());
        var parentIds = new List<OperationId>
        {
            ObjectIdFactory.CreateOperationId("parent1"u8.ToArray()),
            ObjectIdFactory.CreateOperationId("parent2"u8.ToArray())
        };
        var metadata = CreateTestMetadata();

        // Act
        var operationData = new OperationData(viewId, parentIds, metadata);

        // Assert
        Assert.Equal(viewId, operationData.AssociatedViewId);
        Assert.Equal(parentIds, operationData.ParentOperationIds);
        Assert.Equal(metadata, operationData.Metadata);
    }

    [Fact]
    public void Constructor_WithNullParentIds_ThrowsArgumentNullException()
    {
        // Arrange
        var viewId = ObjectIdFactory.CreateViewId("view"u8.ToArray());
        var metadata = CreateTestMetadata();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new OperationData(viewId, null!, metadata));
    }

    [Fact]
    public void IsRootOperation_WithNoParents_ReturnsTrue()
    {
        // Arrange
        var viewId = ObjectIdFactory.CreateViewId("view"u8.ToArray());
        var operationData = new OperationData(viewId, new List<OperationId>(), CreateTestMetadata());

        // Act & Assert
        Assert.True(operationData.IsRootOperation);
    }

    [Fact]
    public void IsRootOperation_WithParents_ReturnsFalse()
    {
        // Arrange
        var viewId = ObjectIdFactory.CreateViewId("view"u8.ToArray());
        var parentIds = new List<OperationId> { ObjectIdFactory.CreateOperationId("parent"u8.ToArray()) };
        var operationData = new OperationData(viewId, parentIds, CreateTestMetadata());

        // Act & Assert
        Assert.False(operationData.IsRootOperation);
    }

    [Fact]
    public void IsMergeOperation_WithMultipleParents_ReturnsTrue()
    {
        // Arrange
        var viewId = ObjectIdFactory.CreateViewId("view"u8.ToArray());
        var parentIds = new List<OperationId>
        {
            ObjectIdFactory.CreateOperationId("parent1"u8.ToArray()),
            ObjectIdFactory.CreateOperationId("parent2"u8.ToArray())
        };
        var operationData = new OperationData(viewId, parentIds, CreateTestMetadata());

        // Act & Assert
        Assert.True(operationData.IsMergeOperation);
    }

    [Fact]
    public void IsMergeOperation_WithSingleParent_ReturnsFalse()
    {
        // Arrange
        var viewId = ObjectIdFactory.CreateViewId("view"u8.ToArray());
        var parentIds = new List<OperationId> { ObjectIdFactory.CreateOperationId("parent"u8.ToArray()) };
        var operationData = new OperationData(viewId, parentIds, CreateTestMetadata());

        // Act & Assert
        Assert.False(operationData.IsMergeOperation);
    }

    [Fact]
    public void GetBytesForHashing_SortsParentOperationsByHexString()
    {
        // Arrange
        var viewId = ObjectIdFactory.CreateViewId("view"u8.ToArray());
        
        // Create operations with predictable hex ordering
        var parent1 = ObjectIdFactory.CreateOperationId("aaa"u8.ToArray()); // Will start with lower hex
        var parent2 = ObjectIdFactory.CreateOperationId("zzz"u8.ToArray()); // Will start with higher hex
        
        var parentIds = new List<OperationId> { parent2, parent1 }; // Intentionally reversed
        var operationData = new OperationData(viewId, parentIds, CreateTestMetadata());

        // Act
        var bytes = operationData.GetBytesForHashing();
        var content = System.Text.Encoding.UTF8.GetString(bytes);

        // Assert - Parents should be sorted by hex string
        var parent1Hex = parent1.ToHexString();
        var parent2Hex = parent2.ToHexString();
        
        var parent1Index = content.IndexOf(parent1Hex);
        var parent2Index = content.IndexOf(parent2Hex);
        
        // The operation with lexicographically smaller hex should appear first
        if (string.Compare(parent1Hex, parent2Hex, StringComparison.Ordinal) < 0)
        {
            Assert.True(parent1Index < parent2Index);
        }
        else
        {
            Assert.True(parent2Index < parent1Index);
        }
    }

    [Fact]
    public void GetBytesForHashing_WithUnsortedParents_ProducesSameOutputAsSorted()
    {
        // Arrange
        var viewId = ObjectIdFactory.CreateViewId("view"u8.ToArray());
        var parent1 = ObjectIdFactory.CreateOperationId("parent1"u8.ToArray());
        var parent2 = ObjectIdFactory.CreateOperationId("parent2"u8.ToArray());
        var parent3 = ObjectIdFactory.CreateOperationId("parent3"u8.ToArray());
        
        var unsortedParents = new List<OperationId> { parent3, parent1, parent2 };
        var sortedParents = new List<OperationId> { parent1, parent2, parent3 };
        
        var metadata = CreateTestMetadata();
        var operationData1 = new OperationData(viewId, unsortedParents, metadata);
        var operationData2 = new OperationData(viewId, sortedParents, metadata);

        // Act
        var bytes1 = operationData1.GetBytesForHashing();
        var bytes2 = operationData2.GetBytesForHashing();

        // Assert
        Assert.Equal(bytes1, bytes2);
    }

    [Fact]
    public void ParseFromCanonicalBytes_RoundTripConsistency()
    {
        // Arrange
        var viewId = ObjectIdFactory.CreateViewId("test-view"u8.ToArray());
        var parentIds = new List<OperationId>
        {
            ObjectIdFactory.CreateOperationId("parent1"u8.ToArray()),
            ObjectIdFactory.CreateOperationId("parent2"u8.ToArray())
        };
        var metadata = CreateTestMetadata();
        
        var original = new OperationData(viewId, parentIds, metadata);

        // Act
        var bytes = original.GetBytesForHashing();
        var parsed = OperationData.ParseFromCanonicalBytes(bytes);

        // Assert
        Assert.Equal(original.AssociatedViewId, parsed.AssociatedViewId);
        Assert.Equal(original.ParentOperationIds.Count, parsed.ParentOperationIds.Count);
        Assert.All(original.ParentOperationIds, parentId => 
            Assert.Contains(parentId, parsed.ParentOperationIds));
        Assert.Equal(original.Metadata, parsed.Metadata);
    }

    [Fact]
    public void ParseFromCanonicalBytes_WithWindowsLineEndings_ParsesCorrectly()
    {
        // Arrange
        var viewId = ObjectIdFactory.CreateViewId("view"u8.ToArray());
        var parentIds = new List<OperationId> { ObjectIdFactory.CreateOperationId("parent"u8.ToArray()) };
        var operationData = new OperationData(viewId, parentIds, CreateTestMetadata());
            
        var bytes = operationData.GetBytesForHashing();
        var contentWithCRLF = System.Text.Encoding.UTF8.GetString(bytes).Replace("\n", "\r\n");
        var bytesWithCRLF = System.Text.Encoding.UTF8.GetBytes(contentWithCRLF);

        // Act
        var parsed = OperationData.ParseFromCanonicalBytes(bytesWithCRLF);

        // Assert
        Assert.Equal(operationData.AssociatedViewId, parsed.AssociatedViewId);
        Assert.Equal(operationData.ParentOperationIds.Count, parsed.ParentOperationIds.Count);
        Assert.Equal(operationData.Metadata, parsed.Metadata);
    }

    [Fact]
    public void ParseFromCanonicalBytes_WithInvalidFormat_ThrowsArgumentException()
    {
        // Arrange
        var invalidBytes = System.Text.Encoding.UTF8.GetBytes("invalid format");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => OperationData.ParseFromCanonicalBytes(invalidBytes));
    }

    [Fact]
    public void ParseFromCanonicalBytes_WithNullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => OperationData.ParseFromCanonicalBytes(null!));
    }

    [Fact]
    public void GetBytesForHashing_IncludesAllComponents()
    {
        // Arrange
        var viewId = ObjectIdFactory.CreateViewId("view"u8.ToArray());
        var parentIds = new List<OperationId> { ObjectIdFactory.CreateOperationId("parent"u8.ToArray()) };
        var metadata = CreateTestMetadata();
        var operationData = new OperationData(viewId, parentIds, metadata);

        // Act
        var bytes = operationData.GetBytesForHashing();
        var content = System.Text.Encoding.UTF8.GetString(bytes);

        // Assert
        Assert.Contains(viewId.ToHexString(), content);
        Assert.Contains(parentIds[0].ToHexString(), content);
        Assert.Contains(metadata.Description, content);
        Assert.Contains(metadata.Username, content);
        Assert.Contains(metadata.Hostname, content);
    }

    private static OperationMetadata CreateTestMetadata()
    {
        var startTime = new DateTimeOffset(2023, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var endTime = startTime.AddMinutes(1);
        var tags = new Dictionary<string, string> { { "type", "test" } };
        
        return new OperationMetadata(
            startTime, 
            endTime, 
            "Test operation", 
            "testuser", 
            "testhost", 
            tags);
    }
}
