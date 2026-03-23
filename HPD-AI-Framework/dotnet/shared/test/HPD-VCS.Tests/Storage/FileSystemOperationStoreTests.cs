using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using Xunit;
using HPD.VCS.Core;
using HPD.VCS.Storage;

namespace HPD.VCS.Tests.Storage;

public class FileSystemOperationStoreTests
{
    [Fact]
    public async Task WriteViewAsync_WithValidViewData_StoresAndReturnsId()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var storeDir = "/test/store";
        var store = new FileSystemOperationStore(mockFileSystem, storeDir);
        
        var commit = ObjectIdFactory.CreateCommitId("test"u8.ToArray());
        var viewData = new ViewData(
            new Dictionary<string, CommitId> { { "default", commit } },
            new List<CommitId> { commit });

        // Act
        var viewId = await store.WriteViewAsync(viewData);

        // Assert
        Assert.NotEqual(default(ViewId), viewId);
        
        // Verify file was created
        var expectedPath = GetExpectedObjectPath(storeDir, viewId, mockFileSystem);
        Assert.True(mockFileSystem.File.Exists(expectedPath));
    }

    [Fact]
    public async Task ReadViewAsync_WithExistingView_ReturnsViewData()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var storeDir = "/test/store";
        var store = new FileSystemOperationStore(mockFileSystem, storeDir);
        
        var commit = ObjectIdFactory.CreateCommitId("test"u8.ToArray());
        var originalViewData = new ViewData(
            new Dictionary<string, CommitId> { { "default", commit } },
            new List<CommitId> { commit });

        // Act - Write then read
        var viewId = await store.WriteViewAsync(originalViewData);
        var retrievedViewData = await store.ReadViewAsync(viewId);

        // Assert
        Assert.NotNull(retrievedViewData);
        Assert.Equal(originalViewData.WorkspaceCommitIds.Count, retrievedViewData.Value.WorkspaceCommitIds.Count);
        Assert.Equal(originalViewData.HeadCommitIds.Count, retrievedViewData.Value.HeadCommitIds.Count);
        Assert.All(originalViewData.WorkspaceCommitIds, kvp =>
            Assert.Equal(kvp.Value, retrievedViewData.Value.WorkspaceCommitIds[kvp.Key]));
    }    [Fact]
    public async Task ReadViewAsync_WithNonExistentView_ReturnsNull()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var storeDir = "/test/store";
        var store = new FileSystemOperationStore(mockFileSystem, storeDir);
          var nonExistentViewId = ObjectIdFactory.CreateViewId("nonexistent"u8.ToArray());

        // Act
        var result = await store.ReadViewAsync(nonExistentViewId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task WriteOperationAsync_WithValidOperationData_StoresAndReturnsId()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var storeDir = "/test/store";
        var store = new FileSystemOperationStore(mockFileSystem, storeDir);
        
        var operationData = CreateTestOperationData();

        // Act
        var operationId = await store.WriteOperationAsync(operationData);

        // Assert
        Assert.NotEqual(default(OperationId), operationId);
        
        // Verify file was created
        var expectedPath = GetExpectedObjectPath(storeDir, operationId, mockFileSystem);
        Assert.True(mockFileSystem.File.Exists(expectedPath));
    }

    [Fact]
    public async Task ReadOperationAsync_WithExistingOperation_ReturnsOperationData()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var storeDir = "/test/store";
        var store = new FileSystemOperationStore(mockFileSystem, storeDir);
        
        var originalOperationData = CreateTestOperationData();

        // Act - Write then read
        var operationId = await store.WriteOperationAsync(originalOperationData);
        var retrievedOperationData = await store.ReadOperationAsync(operationId);

        // Assert
        Assert.NotNull(retrievedOperationData);
        Assert.Equal(originalOperationData.AssociatedViewId, retrievedOperationData.Value.AssociatedViewId);
        Assert.Equal(originalOperationData.ParentOperationIds.Count, retrievedOperationData.Value.ParentOperationIds.Count);
        Assert.Equal(originalOperationData.Metadata, retrievedOperationData.Value.Metadata);
    }

    [Fact]
    public async Task ReadOperationAsync_WithNonExistentOperation_ReturnsNull()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var storeDir = "/test/store";
        var store = new FileSystemOperationStore(mockFileSystem, storeDir);
        
        var nonExistentOperationId = ObjectIdFactory.CreateOperationId("nonexistent"u8.ToArray());

        // Act
        var result = await store.ReadOperationAsync(nonExistentOperationId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task WriteViewAsync_WithSameData_WritesOnlyOnce()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var storeDir = "/test/store";
        var store = new FileSystemOperationStore(mockFileSystem, storeDir);
        
        var commit = ObjectIdFactory.CreateCommitId("test"u8.ToArray());
        var viewData = new ViewData(
            new Dictionary<string, CommitId> { { "default", commit } },
            new List<CommitId> { commit });

        // Act - Write the same data twice
        var viewId1 = await store.WriteViewAsync(viewData);
        var viewId2 = await store.WriteViewAsync(viewData);

        // Assert
        Assert.Equal(viewId1, viewId2);
        
        // Verify only one file exists
        var expectedPath = GetExpectedObjectPath(storeDir, viewId1, mockFileSystem);
        Assert.True(mockFileSystem.File.Exists(expectedPath));
    }

    [Fact]
    public async Task ReadViewAsync_WithCorruptedFile_ThrowsCorruptObjectException()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var storeDir = "/test/store";
        var store = new FileSystemOperationStore(mockFileSystem, storeDir);
        
        var viewId = ObjectIdFactory.CreateViewId("test"u8.ToArray());
        var expectedPath = GetExpectedObjectPath(storeDir, viewId, mockFileSystem);
        
        // Create directory and write corrupted data
        mockFileSystem.Directory.CreateDirectory(mockFileSystem.Path.GetDirectoryName(expectedPath)!);
        await mockFileSystem.File.WriteAllTextAsync(expectedPath, "view\0corrupted data");

        // Act & Assert
        await Assert.ThrowsAsync<CorruptObjectException>(() => store.ReadViewAsync(viewId));
    }

    [Fact]
    public async Task ReadViewAsync_WithWrongTypePrefix_ThrowsObjectTypeMismatchException()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var storeDir = "/test/store";
        var store = new FileSystemOperationStore(mockFileSystem, storeDir);
        
        var viewId = ObjectIdFactory.CreateViewId("test"u8.ToArray());
        var expectedPath = GetExpectedObjectPath(storeDir, viewId, mockFileSystem);
        
        // Create directory and write data with wrong type prefix
        mockFileSystem.Directory.CreateDirectory(mockFileSystem.Path.GetDirectoryName(expectedPath)!);
        await mockFileSystem.File.WriteAllTextAsync(expectedPath, "operation\0some data");

        // Act & Assert
        await Assert.ThrowsAsync<ObjectTypeMismatchException>(() => store.ReadViewAsync(viewId));
    }

    [Fact]
    public void Constructor_CreatesStoreDirectory()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var storeDir = "/test/store";

        // Act
        var store = new FileSystemOperationStore(mockFileSystem, storeDir);

        // Assert
        Assert.True(mockFileSystem.Directory.Exists(storeDir));
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Arrange
        var mockFileSystem = new MockFileSystem();
        var store = new FileSystemOperationStore(mockFileSystem, "/test/store");

        // Act & Assert
        store.Dispose(); // Should not throw
    }

    private static string GetExpectedObjectPath(string storeDir, IObjectId objectId, MockFileSystem fileSystem)
    {
        var hexString = objectId.ToHexString();
        var prefix = hexString[..2];
        var suffix = hexString[2..];
        return fileSystem.Path.Combine(storeDir, prefix, suffix);
    }

    private static OperationData CreateTestOperationData()
    {
        var viewId = ObjectIdFactory.CreateViewId("test-view"u8.ToArray());
        var parentIds = new List<OperationId> { ObjectIdFactory.CreateOperationId("parent"u8.ToArray()) };
        var startTime = new DateTimeOffset(2023, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var endTime = startTime.AddMinutes(1);
        var tags = new Dictionary<string, string> { { "type", "test" } };
        
        var metadata = new OperationMetadata(
            startTime, 
            endTime, 
            "Test operation", 
            "testuser", 
            "testhost", 
            tags);
            
        return new OperationData(viewId, parentIds, metadata);
    }
}
