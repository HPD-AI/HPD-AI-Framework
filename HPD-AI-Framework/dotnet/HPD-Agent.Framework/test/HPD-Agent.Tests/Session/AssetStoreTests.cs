using Xunit;
using HPD.Agent;

namespace HPD.Agent.Tests.Session;

/// <summary>
/// Tests for IAssetStore implementations.
/// </summary>
public class AssetStoreTests
{
    // ═════════════════════════════════════════════════════════════════════
    // INMEMORY ASSET STORE TESTS
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InMemoryAssetStore_Upload_Returns_AssetId()
    {
        // Arrange
        var store = new InMemoryAssetStore();
        var data = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var assetId = await store.PutAsync("session1", data, "image/jpeg", cancellationToken: CancellationToken.None);

        // Assert
        Assert.NotNull(assetId);
        Assert.NotEmpty(assetId);
    }

    [Fact]
    public async Task InMemoryAssetStore_Download_Returns_UploadedData()
    {
        // Arrange
        var store = new InMemoryAssetStore();
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var assetId = await store.PutAsync("session1", data, "image/jpeg", cancellationToken: CancellationToken.None);

        // Act
        var downloaded = await store.GetAsync("session1", assetId, CancellationToken.None);

        // Assert
        Assert.NotNull(downloaded);
        Assert.Equal(assetId, downloaded.Id);
        Assert.Equal(data, downloaded.Data);
        Assert.Equal("image/jpeg", downloaded.ContentType);
        Assert.True(downloaded.Info.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task InMemoryAssetStore_Download_NonExistent_Returns_Null()
    {
        // Arrange
        var store = new InMemoryAssetStore();

        // Act
        var downloaded = await store.GetAsync("session1", "nonexistent", CancellationToken.None);

        // Assert
        Assert.Null(downloaded);
    }

    [Fact]
    public async Task InMemoryAssetStore_Delete_Removes_Asset()
    {
        // Arrange
        var store = new InMemoryAssetStore();
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var assetId = await store.PutAsync("session1", data, "image/jpeg", cancellationToken: CancellationToken.None);

        // Act
        await store.DeleteAsync("session1", assetId, CancellationToken.None);
        var downloaded = await store.GetAsync("session1", assetId, CancellationToken.None);

        // Assert
        Assert.Null(downloaded);
    }

    [Fact]
    public async Task InMemoryAssetStore_Delete_NonExistent_DoesNotThrow()
    {
        // Arrange
        var store = new InMemoryAssetStore();

        // Act & Assert (should not throw)
        await store.DeleteAsync("session1", "nonexistent", CancellationToken.None);
    }

    [Fact]
    public async Task InMemoryAssetStore_Clear_Removes_AllAssets()
    {
        // Arrange
        var store = new InMemoryAssetStore();
        var assetId1 = await store.PutAsync("session1", new byte[] { 1 }, "image/jpeg", cancellationToken: CancellationToken.None);
        var assetId2 = await store.PutAsync("session1", new byte[] { 2 }, "audio/mp3", cancellationToken: CancellationToken.None);

        // Act
        store.Clear();

        // Assert
        Assert.Null(await store.GetAsync("session1", assetId1, CancellationToken.None));
        Assert.Null(await store.GetAsync("session1", assetId2, CancellationToken.None));
        Assert.Equal(0, store.Count);
    }

    // ═════════════════════════════════════════════════════════════════════
    // LOCALFILE ASSET STORE TESTS
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LocalFileAssetStore_Upload_Creates_File()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalFileAssetStore(tempDir);
            var data = new byte[] { 1, 2, 3, 4, 5 };

            // Act
            var assetId = await store.PutAsync("session1", data, "image/jpeg", cancellationToken: CancellationToken.None);

            // Assert
            Assert.NotNull(assetId);
            Assert.NotEmpty(assetId);

            // Verify file exists
            var expectedFile = Path.Combine(tempDir, "session1", $"{assetId}.jpg");
            Assert.True(File.Exists(expectedFile));
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LocalFileAssetStore_Download_Returns_UploadedData()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalFileAssetStore(tempDir);
            var data = new byte[] { 1, 2, 3, 4, 5 };
            var assetId = await store.PutAsync("session1", data, "image/png", cancellationToken: CancellationToken.None);

            // Act
            var downloaded = await store.GetAsync("session1", assetId, CancellationToken.None);

            // Assert
            Assert.NotNull(downloaded);
            Assert.Equal(assetId, downloaded.Id);
            Assert.Equal(data, downloaded.Data);
            Assert.Equal("image/png", downloaded.ContentType);
            Assert.True(downloaded.Info.CreatedAt <= DateTime.UtcNow);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LocalFileAssetStore_Download_NonExistent_Returns_Null()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalFileAssetStore(tempDir);

            // Act
            var downloaded = await store.GetAsync("session1", "nonexistent", CancellationToken.None);

            // Assert
            Assert.Null(downloaded);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LocalFileAssetStore_Delete_Removes_File()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalFileAssetStore(tempDir);
            var data = new byte[] { 1, 2, 3, 4, 5 };
            var assetId = await store.PutAsync("session1", data, "image/jpeg", cancellationToken: CancellationToken.None);

            // Act
            await store.DeleteAsync("session1", assetId, CancellationToken.None);
            var downloaded = await store.GetAsync("session1", assetId, CancellationToken.None);

            // Assert
            Assert.Null(downloaded);

            // Verify file is gone
            var expectedFile = Path.Combine(tempDir, "session1", "assets", $"{assetId}.jpg");
            Assert.False(File.Exists(expectedFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LocalFileAssetStore_Delete_NonExistent_DoesNotThrow()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalFileAssetStore(tempDir);

            // Act & Assert (should not throw)
            await store.DeleteAsync("session1", "nonexistent", CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("audio/mp3", ".mp3")]
    [InlineData("audio/mpeg", ".mp3")]
    [InlineData("video/mp4", ".mp4")]
    [InlineData("application/pdf", ".pdf")]
    [InlineData("application/octet-stream", ".bin")]
    [InlineData("unknown/type", ".bin")]
    public async Task LocalFileAssetStore_Uses_CorrectExtension(
        string contentType,
        string expectedExtension)
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalFileAssetStore(tempDir);
            var data = new byte[] { 1, 2, 3 };

            // Act
            var assetId = await store.PutAsync("session1", data, contentType, cancellationToken: CancellationToken.None);

            // Assert
            var expectedFile = Path.Combine(tempDir, "session1", $"{assetId}{expectedExtension}");
            Assert.True(File.Exists(expectedFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LocalFileAssetStore_Handles_LargeFiles()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalFileAssetStore(tempDir);
            var data = new byte[10 * 1024 * 1024]; // 10 MB
            new Random().NextBytes(data);

            // Act
            var assetId = await store.PutAsync("session1", data, "application/octet-stream", cancellationToken: CancellationToken.None);
            var downloaded = await store.GetAsync("session1", assetId, CancellationToken.None);

            // Assert
            Assert.NotNull(downloaded);
            Assert.Equal(data.Length, downloaded.Data.Length);
            Assert.Equal(data, downloaded.Data);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // ICONTENTSTORE INTERFACE TESTS
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InMemoryAssetStore_PutAsync_Returns_ContentId()
    {
        // Arrange
        var store = new InMemoryAssetStore();
        var data = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var contentId = await store.PutAsync("session1", data, "image/jpeg", cancellationToken: CancellationToken.None);

        // Assert
        Assert.NotNull(contentId);
        Assert.NotEmpty(contentId);
    }

    [Fact]
    public async Task InMemoryAssetStore_GetAsync_Returns_ContentData()
    {
        // Arrange
        var store = new InMemoryAssetStore();
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var contentId = await store.PutAsync("session1", data, "image/jpeg", cancellationToken: CancellationToken.None);

        // Act
        var content = await store.GetAsync("session1", contentId, CancellationToken.None);

        // Assert
        Assert.NotNull(content);
        Assert.Equal(contentId, content.Id);
        Assert.Equal(data, content.Data);
        Assert.Equal("image/jpeg", content.ContentType);
        Assert.NotNull(content.Info);
        Assert.Equal(contentId, content.Info.Id);
        Assert.Equal(contentId, content.Info.Name); // Defaults to ID
        Assert.Equal("image/jpeg", content.Info.ContentType);
        Assert.Equal(data.Length, content.Info.SizeBytes);
        Assert.Equal(ContentSource.User, content.Info.Origin);
    }

    [Fact]
    public async Task InMemoryAssetStore_QueryAsync_ReturnsAll()
    {
        // Arrange
        var store = new InMemoryAssetStore();
        await store.PutAsync("session1", new byte[] { 1 }, "image/jpeg", cancellationToken: CancellationToken.None);
        await store.PutAsync("session1", new byte[] { 2 }, "audio/mp3", cancellationToken: CancellationToken.None);
        await store.PutAsync("session1", new byte[] { 3 }, "video/mp4", cancellationToken: CancellationToken.None);

        // Act
        var results = await store.QueryAsync("session1", cancellationToken: CancellationToken.None);

        // Assert
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task InMemoryAssetStore_QueryAsync_FiltersByContentType()
    {
        // Arrange
        var store = new InMemoryAssetStore();
        await store.PutAsync("session1", new byte[] { 1 }, "image/jpeg", cancellationToken: CancellationToken.None);
        await store.PutAsync("session1", new byte[] { 2 }, "image/jpeg", cancellationToken: CancellationToken.None);
        await store.PutAsync("session1", new byte[] { 3 }, "audio/mp3", cancellationToken: CancellationToken.None);

        // Act
        var results = await store.QueryAsync(
            "session1",
            new ContentQuery { ContentType = "image/jpeg" },
            CancellationToken.None);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("image/jpeg", r.ContentType));
    }

    [Fact]
    public async Task InMemoryAssetStore_QueryAsync_FiltersByCreatedAfter()
    {
        // Arrange
        var store = new InMemoryAssetStore();
        var cutoff = DateTime.UtcNow;
        await Task.Delay(50); // Ensure time passes

        await store.PutAsync("session1", new byte[] { 1 }, "image/jpeg", cancellationToken: CancellationToken.None);
        await store.PutAsync("session1", new byte[] { 2 }, "audio/mp3", cancellationToken: CancellationToken.None);

        // Act
        var results = await store.QueryAsync(
            "session1",
            new ContentQuery { CreatedAfter = cutoff },
            CancellationToken.None);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.CreatedAt >= cutoff));
    }

    [Fact]
    public async Task InMemoryAssetStore_QueryAsync_AppliesLimit()
    {
        // Arrange
        var store = new InMemoryAssetStore();
        await store.PutAsync("session1", new byte[] { 1 }, "image/jpeg", cancellationToken: CancellationToken.None);
        await store.PutAsync("session1", new byte[] { 2 }, "audio/mp3", cancellationToken: CancellationToken.None);
        await store.PutAsync("session1", new byte[] { 3 }, "video/mp4", cancellationToken: CancellationToken.None);

        // Act
        var results = await store.QueryAsync(
            "session1",
            new ContentQuery { Limit = 2 },
            CancellationToken.None);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task InMemoryAssetStore_QueryAsync_CombinesFilters()
    {
        // Arrange
        var store = new InMemoryAssetStore();
        await store.PutAsync("session1", new byte[] { 1 }, "image/jpeg", cancellationToken: CancellationToken.None);

        var cutoff = DateTime.UtcNow;
        await Task.Delay(50);

        await store.PutAsync("session1", new byte[] { 2 }, "image/jpeg", cancellationToken: CancellationToken.None);
        await store.PutAsync("session1", new byte[] { 3 }, "image/jpeg", cancellationToken: CancellationToken.None);
        await store.PutAsync("session1", new byte[] { 4 }, "audio/mp3", cancellationToken: CancellationToken.None);

        // Act
        var results = await store.QueryAsync(
            "session1",
            new ContentQuery
            {
                ContentType = "image/jpeg",
                CreatedAfter = cutoff,
                Limit = 1
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(1, results.Count);
        Assert.Equal("image/jpeg", results[0].ContentType);
        Assert.True(results[0].CreatedAt >= cutoff);
    }

    [Fact]
    public async Task InMemoryAssetStore_IContentStore_DeleteAsync_RemovesContent()
    {
        // Arrange
        var store = new InMemoryAssetStore();
        IContentStore contentStore = store; // Cast to interface
        var contentId = await contentStore.PutAsync(
            "session1",
            new byte[] { 1, 2, 3 },
            "image/jpeg",
            cancellationToken: CancellationToken.None);

        // Act
        await contentStore.DeleteAsync("session1", contentId, CancellationToken.None);
        var content = await contentStore.GetAsync("session1", contentId, CancellationToken.None);

        // Assert
        Assert.Null(content);
    }

    [Fact]
    public async Task LocalFileAssetStore_PutAsync_CreatesFile()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalFileAssetStore(tempDir);
            var data = new byte[] { 1, 2, 3, 4, 5 };

            // Act
            var contentId = await store.PutAsync("session1", data, "image/png", cancellationToken: CancellationToken.None);

            // Assert
            Assert.NotNull(contentId);
            Assert.NotEmpty(contentId);

            var expectedFile = Path.Combine(tempDir, "session1", $"{contentId}.png");
            Assert.True(File.Exists(expectedFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LocalFileAssetStore_QueryAsync_ReturnsAllFiles()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalFileAssetStore(tempDir);
            await store.PutAsync("session1", new byte[] { 1 }, "image/jpeg", cancellationToken: CancellationToken.None);
            await store.PutAsync("session1", new byte[] { 2 }, "audio/mp3", cancellationToken: CancellationToken.None);

            // Act
            var results = await store.QueryAsync("session1", cancellationToken: CancellationToken.None);

            // Assert
            Assert.Equal(2, results.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LocalFileAssetStore_QueryAsync_FiltersByContentType()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalFileAssetStore(tempDir);
            await store.PutAsync("session1", new byte[] { 1 }, "image/jpeg", cancellationToken: CancellationToken.None);
            await store.PutAsync("session1", new byte[] { 2 }, "image/jpeg", cancellationToken: CancellationToken.None);
            await store.PutAsync("session1", new byte[] { 3 }, "audio/mp3", cancellationToken: CancellationToken.None);

            // Act
            var results = await store.QueryAsync(
                "session1",
                new ContentQuery { ContentType = "image/jpeg" },
                CancellationToken.None);

            // Assert
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal("image/jpeg", r.ContentType));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AssetStore_V2API_PutAndGetWorkTogether()
    {
        // Arrange
        var store = new InMemoryAssetStore();
        var data = new byte[] { 1, 2, 3, 4, 5 };

        // Act - Upload using V2 API
        var assetId = await store.PutAsync("session1", data, "image/jpeg", cancellationToken: CancellationToken.None);

        // Act - Retrieve using V2 API
        var content = await store.GetAsync("session1", assetId, CancellationToken.None);

        // Assert
        Assert.NotNull(content);
        Assert.Equal(data, content.Data);
        Assert.Equal("image/jpeg", content.ContentType);

        // Act - Upload to different session
        var contentId = await store.PutAsync("session2", new byte[] { 6, 7, 8 }, "audio/mp3", cancellationToken: CancellationToken.None);

        // Act - Retrieve from correct session
        var asset = await store.GetAsync("session2", contentId, CancellationToken.None);

        // Assert
        Assert.NotNull(asset);
        Assert.Equal(new byte[] { 6, 7, 8 }, asset.Data);
        Assert.Equal("audio/mp3", asset.ContentType);

        // Assert - Cannot retrieve from wrong session
        var wrongSession = await store.GetAsync("session1", contentId, CancellationToken.None);
        Assert.Null(wrongSession);
    }
}
