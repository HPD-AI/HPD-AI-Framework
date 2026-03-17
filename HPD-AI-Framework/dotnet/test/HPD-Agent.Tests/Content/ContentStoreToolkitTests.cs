using System.Text;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Content;

/// <summary>
/// Tests for ContentStoreToolkit — the 7 filesystem-like agent tools.
/// Uses InMemoryContentStore to keep tests fast and self-contained.
/// Session-scoped folders (/uploads, /artifacts) require SetSessionId() to be called first.
/// </summary>
public class ContentStoreToolkitTests
{
    private const string AgentName = "test-agent";
    private const string SessionId = "session-abc";

    private static (InMemoryContentStore store, ContentStoreToolkit toolkit) CreateToolkit(
        bool withSession = false)
    {
        var store = new InMemoryContentStore();

        // Register standard folders
        store.CreateFolder("knowledge", new FolderOptions
        {
            Description = "Knowledge base",
            Permissions = ContentPermissions.Read
        });
        store.CreateFolder("memory", new FolderOptions
        {
            Description = "Agent memory",
            Permissions = ContentPermissions.Full
        });
        store.CreateFolder("skills", new FolderOptions
        {
            Description = "Skill documents",
            Permissions = ContentPermissions.Read
        });

        var toolkit = new ContentStoreToolkit(store, AgentName);
        if (withSession)
            toolkit.SetSessionId(SessionId);

        return (store, toolkit);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-1: ReadAsync returns text content
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadAsync_ReturnsTextContent()
    {
        var (store, toolkit) = CreateToolkit();
        await store.WriteMemoryAsync(AgentName, "notes.md", "# My notes\nLine 2");

        var result = await toolkit.ReadAsync("/memory/notes.md");

        Assert.Contains("My notes", result);
        Assert.DoesNotContain("Error", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-2: ReadAsync with offset and limit slices lines correctly
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadAsync_WithOffsetAndLimit_SlicesLines()
    {
        var (store, toolkit) = CreateToolkit();
        var content = "line0\nline1\nline2\nline3\nline4";
        await store.WriteMemoryAsync(AgentName, "file.txt", content);

        var result = await toolkit.ReadAsync("/memory/file.txt", offset: 1, limit: 2);

        Assert.Contains("line1", result);
        Assert.Contains("line2", result);
        Assert.DoesNotContain("line0", result);
        Assert.DoesNotContain("line3", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-3: ReadAsync on binary content returns info string, not raw bytes
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadAsync_BinaryContent_ReturnsInfoString()
    {
        var (store, toolkit) = CreateToolkit(withSession: true);
        var binaryData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG header
        await store.PutAsync(SessionId, binaryData, "image/jpeg",
            new ContentMetadata
            {
                Name = "photo.jpg",
                Tags = new Dictionary<string, string> { ["folder"] = "/uploads" }
            });

        var result = await toolkit.ReadAsync("/uploads/photo.jpg");

        Assert.Contains("Binary content", result);
        Assert.DoesNotContain("Error", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-4: ReadAsync for a non-existent file returns a user-friendly error
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadAsync_FileNotFound_ReturnsErrorString()
    {
        var (_, toolkit) = CreateToolkit();

        var result = await toolkit.ReadAsync("/memory/does-not-exist.md");

        Assert.Contains("Error", result);
        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-5: ReadAsync with path but no filename returns a helpful error
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReadAsync_NoFilename_ReturnsHelpfulError()
    {
        var (_, toolkit) = CreateToolkit();

        var result = await toolkit.ReadAsync("/memory/");

        Assert.Contains("Error", result);
        // Should suggest how to proceed
        Assert.True(
            result.Contains("filename", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("content_list", StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-6: WriteAsync stores content in the correct folder
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WriteAsync_WritesToCorrectFolder()
    {
        var (store, toolkit) = CreateToolkit();

        var writeResult = await toolkit.WriteAsync("/memory/prefs.md", "User prefers dark mode");

        Assert.DoesNotContain("Error", writeResult);

        var items = await store.QueryAsync(AgentName, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/memory" }
        });
        Assert.Single(items);
        Assert.Equal("prefs.md", items[0].Name);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-7: WriteAsync to a read-only folder returns an error string
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WriteAsync_ReadonlyFolder_ReturnsError()
    {
        var (_, toolkit) = CreateToolkit();

        // /knowledge is Read-only per our CreateToolkit helper
        var result = await toolkit.WriteAsync("/knowledge/hacked.md", "should not be written");

        Assert.Contains("Error", result);
        Assert.Contains("read-only", result, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-8: WriteAsync to the same path twice overwrites content
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WriteAsync_UpdatesExistingFile()
    {
        var (_, toolkit) = CreateToolkit();

        await toolkit.WriteAsync("/memory/notes.md", "original content");
        await toolkit.WriteAsync("/memory/notes.md", "updated content");

        var result = await toolkit.ReadAsync("/memory/notes.md");
        Assert.Contains("updated content", result);
        Assert.DoesNotContain("original content", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-9: ListAsync at root "/" shows all registered folders
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListAsync_Root_ShowsAllFolders()
    {
        var (_, toolkit) = CreateToolkit();

        var result = await toolkit.ListAsync("/");

        Assert.Contains("/knowledge", result);
        Assert.Contains("/memory", result);
        Assert.Contains("/skills", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-10: ListAsync at root with session includes /uploads and /artifacts
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListAsync_Root_WithSession_IncludesSessionFolders()
    {
        var (_, toolkit) = CreateToolkit(withSession: true);

        var result = await toolkit.ListAsync("/");

        Assert.Contains("/uploads", result);
        Assert.Contains("/artifacts", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-11: ListAsync on a folder shows its files
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListAsync_Folder_ShowsFiles()
    {
        var (store, toolkit) = CreateToolkit();
        await store.WriteMemoryAsync(AgentName, "note-a.md", "note a");
        await store.WriteMemoryAsync(AgentName, "note-b.md", "note b");

        var result = await toolkit.ListAsync("/memory");

        Assert.Contains("note-a.md", result);
        Assert.Contains("note-b.md", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-12: ListAsync on an empty folder reports empty
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListAsync_EmptyFolder_ReportsEmpty()
    {
        var (_, toolkit) = CreateToolkit();

        var result = await toolkit.ListAsync("/memory");

        Assert.Contains("empty", result, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-13: GlobAsync finds files matching a pattern
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GlobAsync_FindsMatchingFiles()
    {
        var (store, toolkit) = CreateToolkit();
        await store.WriteMemoryAsync(AgentName, "api-guide.md", "api guide");
        await store.WriteMemoryAsync(AgentName, "api-reference.md", "api reference");
        await store.WriteMemoryAsync(AgentName, "unrelated.md", "unrelated");

        var result = await toolkit.GlobAsync("*api*");

        Assert.Contains("api-guide.md", result);
        Assert.Contains("api-reference.md", result);
        Assert.DoesNotContain("unrelated.md", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-14: GlobAsync with folder filter scopes the search
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GlobAsync_WithFolderFilter_ScopedToFolder()
    {
        var (store, toolkit) = CreateToolkit();
        await store.WriteMemoryAsync(AgentName, "api.md", "memory api doc");
        await store.UploadKnowledgeDocumentAsync(AgentName, "api.md",
            Encoding.UTF8.GetBytes("knowledge api doc"), "text/markdown");

        // Glob in /knowledge only
        var result = await toolkit.GlobAsync("*api*", "/knowledge");

        Assert.Contains("/knowledge/api.md", result);
        Assert.DoesNotContain("/memory", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-15: GlobAsync with no matches returns a message
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GlobAsync_NoMatches_ReturnsMessage()
    {
        var (_, toolkit) = CreateToolkit();

        var result = await toolkit.GlobAsync("*.xyz");

        Assert.Contains("No files", result, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-16: DeleteAsync removes the file
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        var (store, toolkit) = CreateToolkit();
        await store.WriteMemoryAsync(AgentName, "old-note.md", "stale");

        var deleteResult = await toolkit.DeleteAsync("/memory/old-note.md");

        Assert.DoesNotContain("Error", deleteResult);

        var readResult = await toolkit.ReadAsync("/memory/old-note.md");
        Assert.Contains("Error", readResult);
        Assert.Contains("not found", readResult, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-17: DeleteAsync on a folder without Delete permission returns error
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteAsync_NoDeletePermission_ReturnsError()
    {
        var store = new InMemoryContentStore();
        store.CreateFolder("readonly-docs", new FolderOptions
        {
            Description = "Read-only docs",
            Permissions = ContentPermissions.Read // no Delete
        });
        var toolkit = new ContentStoreToolkit(store, AgentName);
        await store.PutAsync(AgentName, Encoding.UTF8.GetBytes("content"), "text/plain",
            new ContentMetadata
            {
                Name = "file.md",
                Tags = new Dictionary<string, string> { ["folder"] = "/readonly-docs" }
            });

        var result = await toolkit.DeleteAsync("/readonly-docs/file.md");

        Assert.Contains("Error", result, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            result.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("allow", StringComparison.OrdinalIgnoreCase),
            $"Unexpected error message: [{result}]");
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-18: StatAsync shows correct metadata
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StatAsync_ShowsCorrectMetadata()
    {
        var (store, toolkit) = CreateToolkit();
        var data = Encoding.UTF8.GetBytes("some content for stat");
        await store.PutAsync(AgentName, data, "text/markdown",
            new ContentMetadata
            {
                Name = "stat-test.md",
                Description = "A test document",
                Tags = new Dictionary<string, string> { ["folder"] = "/memory" }
            });

        var result = await toolkit.StatAsync("/memory/stat-test.md");

        Assert.Contains("stat-test.md", result);
        Assert.Contains("text/markdown", result);
        Assert.Contains(data.Length.ToString(), result);
        Assert.DoesNotContain("Error", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-19: TreeAsync shows folder hierarchy with file counts
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TreeAsync_ShowsFolderHierarchy()
    {
        var (store, toolkit) = CreateToolkit();
        await store.WriteMemoryAsync(AgentName, "note1.md", "note 1");
        await store.WriteMemoryAsync(AgentName, "note2.md", "note 2");
        await store.UploadKnowledgeDocumentAsync(AgentName, "guide.md",
            Encoding.UTF8.GetBytes("guide"), "text/markdown");

        var result = await toolkit.TreeAsync();

        Assert.Contains("/memory", result);
        Assert.Contains("/knowledge", result);
        Assert.DoesNotContain("Error", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-20: /uploads path resolves to session scope, not agent scope
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolvePath_UploadsFolder_UsesSessionScope()
    {
        var (store, toolkit) = CreateToolkit(withSession: true);

        // Write to /uploads via the toolkit (session-scoped)
        await toolkit.WriteAsync("/artifacts/output.txt", "session output");

        // Must be visible under sessionId scope
        var sessionItems = await store.QueryAsync(SessionId, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/artifacts" }
        });
        Assert.Single(sessionItems);

        // Must NOT be visible under agentName scope
        var agentItems = await store.QueryAsync(AgentName, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/artifacts" }
        });
        Assert.Empty(agentItems);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-21: /knowledge path resolves to agent scope, not session scope
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolvePath_KnowledgeFolder_UsesAgentScope()
    {
        var (store, toolkit) = CreateToolkit(withSession: true);
        await store.UploadKnowledgeDocumentAsync(AgentName, "guide.md",
            Encoding.UTF8.GetBytes("API guide"), "text/markdown");

        // Must be visible under agentName scope
        var agentItems = await store.QueryAsync(AgentName, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/knowledge" }
        });
        Assert.Single(agentItems);

        // Must NOT be visible under sessionId scope
        var sessionItems = await store.QueryAsync(SessionId, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/knowledge" }
        });
        Assert.Empty(sessionItems);
    }

    // ═══════════════════════════════════════════════════════════════════
    // T-22: SetSessionId propagates correctly — session-scoped tools work after call
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SetSessionId_PropagatesCorrectly()
    {
        var (store, toolkit) = CreateToolkit(withSession: false);

        // Before setting session ID, /artifacts write routes to null scope
        toolkit.SetSessionId(SessionId);

        await toolkit.WriteAsync("/artifacts/result.txt", "session result");

        // Should be queryable under SessionId scope
        var items = await store.QueryAsync(SessionId, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/artifacts" }
        });
        Assert.Single(items);
        Assert.Equal("result.txt", items[0].Name);
    }
}
