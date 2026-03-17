using System.Text;
using System.Text.Json;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Content;

/// <summary>
/// Integration tests for LocalFileContentStore.
/// Each test gets an isolated temp directory and cleans up after itself.
/// These tests also serve as the contract suite for the file-backed implementation
/// by inheriting from IContentStoreContractTests.
/// </summary>
public class LocalFileContentStore_ContractTests : IContentStoreContractTests, IDisposable
{
    private readonly string _tempDir;

    public LocalFileContentStore_ContractTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hpd-lf-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    protected override IContentStore CreateStore() => new LocalFileContentStore(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}

/// <summary>
/// LocalFileContentStore-specific integration tests beyond the contract suite.
/// </summary>
public class LocalFileContentStoreTests : IDisposable
{
    private readonly string _tempDir;

    public LocalFileContentStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hpd-lf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private LocalFileContentStore CreateStore() => new(_tempDir);

    // ═══════════════════════════════════════════════════════════════════
    // LF-1: Put actually writes a file to disk
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Put_CreatesFileOnDisk()
    {
        var store = CreateStore();
        var data = new byte[] { 1, 2, 3 };

        var id = await store.PutAsync("scope-a", data, "image/jpeg");

        // File should exist somewhere under _tempDir/scope-a/
        var scopeDir = Path.Combine(_tempDir, "scope-a");
        Assert.True(Directory.Exists(scopeDir));
        var files = Directory.GetFiles(scopeDir, $"{id}.*")
            .Where(f => !f.EndsWith(".meta") && !f.EndsWith(".nameindex"))
            .ToArray();
        Assert.Single(files);
    }

    // ═══════════════════════════════════════════════════════════════════
    // LF-2: Put creates a .meta companion file with correct JSON
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Put_CreatesMetaFile_WithCorrectJson()
    {
        var store = CreateStore();
        var meta = new ContentMetadata
        {
            Name = "photo.jpg",
            Description = "A test image",
            Origin = ContentSource.User
        };

        var id = await store.PutAsync("scope-a", new byte[] { 1, 2, 3 }, "image/jpeg", meta);

        var scopeDir = Path.Combine(_tempDir, "scope-a");
        var metaPath = Path.Combine(scopeDir, $"{id}.meta");
        Assert.True(File.Exists(metaPath), $"Expected .meta file at {metaPath}");

        var json = await File.ReadAllTextAsync(metaPath);
        Assert.False(string.IsNullOrWhiteSpace(json));
        // Must be valid JSON
        var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    // ═══════════════════════════════════════════════════════════════════
    // LF-3: Get reads from disk after creating a fresh store instance
    //        (simulates process restart / reload)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_ReadsFromDisk_AfterRestart()
    {
        var data = Encoding.UTF8.GetBytes("persisted content");

        string id;
        {
            var store1 = CreateStore();
            id = await store1.PutAsync("scope-a", data, "text/plain",
                new ContentMetadata { Name = "saved.txt" });
        }

        // New store instance over same directory
        var store2 = CreateStore();
        var result = await store2.GetAsync("scope-a", id);

        Assert.NotNull(result);
        Assert.Equal(data, result.Data);
    }

    // ═══════════════════════════════════════════════════════════════════
    // LF-4: Delete removes both the data file and the .meta file
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Delete_RemovesFile_AndMetaFile()
    {
        var store = CreateStore();
        var id = await store.PutAsync("scope-a", new byte[] { 1, 2, 3 }, "image/png",
            new ContentMetadata { Name = "img.png" });

        var scopeDir = Path.Combine(_tempDir, "scope-a");
        Assert.NotEmpty(Directory.GetFiles(scopeDir, $"{id}.*"));

        await store.DeleteAsync("scope-a", id);

        Assert.Empty(Directory.GetFiles(scopeDir, $"{id}.*"));
        Assert.False(File.Exists(Path.Combine(scopeDir, $"{id}.meta")));
    }

    // ═══════════════════════════════════════════════════════════════════
    // LF-5: Scope isolation — different scopes use different subdirectories
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ScopeIsolation_UsesSubdirectory()
    {
        var store = CreateStore();

        var idA = await store.PutAsync("agent-alice", new byte[] { 1 }, "text/plain");
        var idB = await store.PutAsync("agent-bob", new byte[] { 2 }, "text/plain");

        var aliceDir = Path.Combine(_tempDir, "agent-alice");
        var bobDir = Path.Combine(_tempDir, "agent-bob");

        Assert.True(Directory.Exists(aliceDir));
        Assert.True(Directory.Exists(bobDir));
        Assert.NotEmpty(Directory.GetFiles(aliceDir, $"{idA}.*"));
        Assert.NotEmpty(Directory.GetFiles(bobDir, $"{idB}.*"));

        // Alice's file is not in Bob's dir
        Assert.Empty(Directory.GetFiles(bobDir, $"{idA}.*"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // LF-6: Named upsert overwrites file content but keeps the same ID
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NamedUpsert_OverwritesFile_SameId()
    {
        var store = CreateStore();
        var v1 = Encoding.UTF8.GetBytes("version 1");
        var v2 = Encoding.UTF8.GetBytes("version 2 — longer content");
        var meta = new ContentMetadata { Name = "doc.txt" };

        var id1 = await store.PutAsync("scope-a", v1, "text/plain", meta);
        var id2 = await store.PutAsync("scope-a", v2, "text/plain", meta);

        Assert.Equal(id1, id2);

        var result = await store.GetAsync("scope-a", id1);
        Assert.NotNull(result);
        Assert.Equal(v2, result.Data);
    }

    // ═══════════════════════════════════════════════════════════════════
    // LF-7: Large file round-trips correctly
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LargeFile_RoundTrips_Correctly()
    {
        var store = CreateStore();
        var data = new byte[2 * 1024 * 1024]; // 2 MB
        Random.Shared.NextBytes(data);

        var id = await store.PutAsync("scope-a", data, "application/octet-stream");
        var result = await store.GetAsync("scope-a", id);

        Assert.NotNull(result);
        Assert.Equal(data.Length, result.Data.Length);
        Assert.Equal(data, result.Data);
    }

    // ═══════════════════════════════════════════════════════════════════
    // LF-8: MIME type maps to the correct file extension
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("text/plain", ".txt")]
    [InlineData("text/markdown", ".md")]
    [InlineData("application/pdf", ".pdf")]
    [InlineData("audio/mpeg", ".mp3")]
    [InlineData("application/octet-stream", ".bin")]
    public async Task MimeType_MapsToCorrectExtension(string mimeType, string expectedExt)
    {
        var store = CreateStore();
        var id = await store.PutAsync("scope-a", new byte[] { 1 }, mimeType);

        var scopeDir = Path.Combine(_tempDir, "scope-a");
        var files = Directory.GetFiles(scopeDir, $"{id}.*")
            .Where(f => !f.EndsWith(".meta") && !f.EndsWith(".nameindex"))
            .ToArray();

        Assert.Single(files);
        Assert.Equal(expectedExt, Path.GetExtension(files[0]));
    }

    // ═══════════════════════════════════════════════════════════════════
    // LF-9: QueryAsync on a fresh store over an existing directory finds persisted content
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Query_AfterDirectoryReload_FindsPersistedContent()
    {
        var meta = new ContentMetadata
        {
            Name = "notes.md",
            Tags = new Dictionary<string, string> { ["folder"] = "/memory" }
        };

        {
            var store1 = CreateStore();
            await store1.PutAsync("agent-x", Encoding.UTF8.GetBytes("some notes"), "text/markdown", meta);
        }

        var store2 = CreateStore();
        var results = await store2.QueryAsync("agent-x", new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/memory" }
        });

        Assert.Single(results);
        Assert.Equal("notes.md", results[0].Name);
    }

    // ═══════════════════════════════════════════════════════════════════
    // LF-10: SizeBytes in ContentInfo matches the actual file size
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ContentInfo_SizeBytes_MatchesFileSize()
    {
        var store = CreateStore();
        var data = Encoding.UTF8.GetBytes("Hello, file store!");

        await store.PutAsync("scope-a", data, "text/plain");
        var results = await store.QueryAsync("scope-a");

        Assert.Single(results);
        Assert.Equal(data.Length, results[0].SizeBytes);
    }
}
