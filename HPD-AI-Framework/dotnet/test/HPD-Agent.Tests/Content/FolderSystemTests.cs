using System.Text;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Content;

/// <summary>
/// Tests for the folder registration and management system:
/// ContentStoreExtensions (CreateFolder, GetFolder, helpers),
/// IContentFolder (PutAsync, GetAsync, DeleteAsync, ListAsync),
/// and the convenience upload helpers (UploadSkillDocumentAsync, etc.).
/// </summary>
public class FolderSystemTests
{
    // ═══════════════════════════════════════════════════════════════════
    // F-1: CreateFolder registers the folder
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateFolder_RegistersFolder()
    {
        var store = new InMemoryContentStore();
        Assert.False(store.HasFolder("knowledge"));

        store.CreateFolder("knowledge", new FolderOptions { Description = "API docs" });

        Assert.True(store.HasFolder("knowledge"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-2: GetFolder on an unregistered name throws
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetFolder_UnknownName_Throws()
    {
        var store = new InMemoryContentStore();
        Assert.Throws<InvalidOperationException>(() => store.GetFolder("does-not-exist"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-3: Content stored via IContentFolder always has the folder tag
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateFolder_InjectsFolderTag_OnEveryPut()
    {
        var store = new InMemoryContentStore();
        var folder = store.CreateFolder("knowledge", new FolderOptions { Description = "Docs" });

        await folder.PutAsync("agent-x", Encoding.UTF8.GetBytes("content"), "text/plain",
            new ContentMetadata { Name = "api.md" });

        var results = await store.QueryAsync("agent-x");
        Assert.Single(results);
        Assert.NotNull(results[0].Tags);
        Assert.Equal("/knowledge", results[0].Tags!["folder"]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-4: Caller-provided folder tag is overridden by the folder's own tag
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateFolder_FolderTag_OverridesCallerSuppliedTag()
    {
        var store = new InMemoryContentStore();
        var folder = store.CreateFolder("knowledge", new FolderOptions { Description = "Docs" });

        // Caller tries to set a different folder tag
        await folder.PutAsync("agent-x", Encoding.UTF8.GetBytes("content"), "text/plain",
            new ContentMetadata
            {
                Name = "api.md",
                Tags = new Dictionary<string, string> { ["folder"] = "/wrong-folder" }
            });

        var results = await store.QueryAsync("agent-x");
        Assert.Single(results);
        Assert.Equal("/knowledge", results[0].Tags!["folder"]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-5: ListFoldersAsync returns all registered folders
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListFoldersAsync_ReturnsAllRegistered()
    {
        var store = new InMemoryContentStore();
        store.CreateFolder("skills", new FolderOptions { Description = "Skill docs" });
        store.CreateFolder("knowledge", new FolderOptions { Description = "Knowledge base" });
        store.CreateFolder("memory", new FolderOptions { Description = "Agent memory" });

        var folders = await store.ListFoldersAsync();

        Assert.Equal(3, folders.Count);
        Assert.Contains(folders, f => f.Name == "skills");
        Assert.Contains(folders, f => f.Name == "knowledge");
        Assert.Contains(folders, f => f.Name == "memory");
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-6: IContentFolder.ListAsync filters to its own folder only
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IContentFolder_ListAsync_FiltersToOwnFolder()
    {
        var store = new InMemoryContentStore();
        var knowledge = store.CreateFolder("knowledge", new FolderOptions { Description = "Docs" });
        var memory = store.CreateFolder("memory", new FolderOptions { Description = "Notes" });

        await knowledge.PutAsync("agent-x", Encoding.UTF8.GetBytes("doc"), "text/plain",
            new ContentMetadata { Name = "api.md" });
        await memory.PutAsync("agent-x", Encoding.UTF8.GetBytes("note"), "text/plain",
            new ContentMetadata { Name = "note.md" });

        var knowledgeItems = await knowledge.ListAsync("agent-x");
        var memoryItems = await memory.ListAsync("agent-x");

        Assert.Single(knowledgeItems);
        Assert.Equal("api.md", knowledgeItems[0].Name);
        Assert.Single(memoryItems);
        Assert.Equal("note.md", memoryItems[0].Name);
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-7: IContentFolder.GetAsync by name resolves correctly
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IContentFolder_GetAsync_ByName_ResolvesCorrectly()
    {
        var store = new InMemoryContentStore();
        var folder = store.CreateFolder("knowledge", new FolderOptions { Description = "Docs" });
        var data = Encoding.UTF8.GetBytes("API reference");

        await folder.PutAsync("agent-x", data, "text/plain", new ContentMetadata { Name = "api-ref.md" });

        var result = await folder.GetAsync("agent-x", "api-ref.md");

        Assert.NotNull(result);
        Assert.Equal(data, result.Data);
        Assert.Equal("api-ref.md", result.Info.Name);
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-8: IContentFolder.DeleteAsync by name removes the item
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IContentFolder_DeleteAsync_ByName_Works()
    {
        var store = new InMemoryContentStore();
        var folder = store.CreateFolder("memory", new FolderOptions { Description = "Notes" });

        await folder.PutAsync("agent-x", Encoding.UTF8.GetBytes("stale note"), "text/plain",
            new ContentMetadata { Name = "old.md" });

        Assert.NotNull(await folder.GetAsync("agent-x", "old.md"));

        await folder.DeleteAsync("agent-x", "old.md");

        Assert.Null(await folder.GetAsync("agent-x", "old.md"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-9: .Skills(), .Knowledge(), .Memory() shortcuts work after registration
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Skills_Knowledge_Memory_Shortcuts_ReturnCorrectFolders()
    {
        var store = new InMemoryContentStore();
        store.CreateFolder("skills", new FolderOptions { Description = "Skill docs" });
        store.CreateFolder("knowledge", new FolderOptions { Description = "Knowledge base" });
        store.CreateFolder("memory", new FolderOptions { Description = "Agent memory" });

        Assert.Equal("skills", store.Skills().Name);
        Assert.Equal("knowledge", store.Knowledge().Name);
        Assert.Equal("memory", store.Memory().Name);
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-10: UploadSkillDocumentAsync tags with /skills folder
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UploadSkillDocumentAsync_TagsWithSkillsFolder()
    {
        var store = new InMemoryContentStore();

        await store.UploadSkillDocumentAsync("oauth-guide", "# OAuth Guide", "OAuth description");

        var results = await store.QueryAsync(null, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/skills" }
        });

        Assert.Single(results);
        Assert.Equal("oauth-guide", results[0].Name);
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-11: UploadSkillDocumentAsync is idempotent — same content → same ID
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UploadSkillDocumentAsync_Idempotent_SameId()
    {
        var store = new InMemoryContentStore();
        var content = "# OAuth Guide\nHow to authenticate.";

        var id1 = await store.UploadSkillDocumentAsync("oauth-guide", content, "OAuth description");
        var id2 = await store.UploadSkillDocumentAsync("oauth-guide", content, "OAuth description");

        Assert.Equal(id1, id2);

        var all = await store.QueryAsync(null, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/skills" }
        });
        Assert.Single(all);
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-12: UploadSkillDocumentAsync with changed content — ID stable, bytes updated
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UploadSkillDocumentAsync_ChangedContent_IdStable_ContentUpdated()
    {
        var store = new InMemoryContentStore();

        var id1 = await store.UploadSkillDocumentAsync("oauth-guide", "# v1", "description");
        var id2 = await store.UploadSkillDocumentAsync("oauth-guide", "# v2 — updated", "description");

        Assert.Equal(id1, id2);

        var result = await store.GetAsync(null, id1);
        Assert.NotNull(result);
        Assert.Contains("v2", Encoding.UTF8.GetString(result.Data));
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-13: LinkSkillDocumentAsync adds per-skill description tag
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LinkSkillDocumentAsync_AddsPerSkillDescriptionTag()
    {
        var store = new InMemoryContentStore();
        await store.UploadSkillDocumentAsync("oauth-guide", "# OAuth", "Global description");

        await store.LinkSkillDocumentAsync("oauth-guide", "PaymentSkill", "Payment token validation");

        var results = await store.QueryAsync(null, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/skills" }
        });

        Assert.Single(results);
        Assert.True(results[0].Tags!.ContainsKey("description:PaymentSkill"));
        Assert.Equal("Payment token validation", results[0].Tags!["description:PaymentSkill"]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-14: LinkSkillDocumentAsync for two skills — both tags coexist
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LinkSkillDocumentAsync_TwoSkills_BothTagsPresent()
    {
        var store = new InMemoryContentStore();
        await store.UploadSkillDocumentAsync("oauth-guide", "# OAuth", "Global description");
        await store.LinkSkillDocumentAsync("oauth-guide", "PaymentSkill", "Payment OAuth flow");
        await store.LinkSkillDocumentAsync("oauth-guide", "UserSkill", "User login flow");

        var results = await store.QueryAsync(null, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/skills" }
        });

        Assert.Single(results); // still one document
        Assert.True(results[0].Tags!.ContainsKey("description:PaymentSkill"));
        Assert.True(results[0].Tags!.ContainsKey("description:UserSkill"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-15: LinkSkillDocumentAsync on a missing document throws
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LinkSkillDocumentAsync_DocumentMissing_Throws()
    {
        var store = new InMemoryContentStore();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.LinkSkillDocumentAsync("no-such-doc", "SomeSkill", "description"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-16: UploadKnowledgeDocumentAsync is agent-scoped
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UploadKnowledgeDocumentAsync_AgentScoped()
    {
        var store = new InMemoryContentStore();
        var data = Encoding.UTF8.GetBytes("# API Guide");

        await store.UploadKnowledgeDocumentAsync("agent-alice", "api-guide", data, "text/markdown");

        // Visible to alice
        var aliceItems = await store.QueryAsync("agent-alice", new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/knowledge" }
        });
        Assert.Single(aliceItems);

        // Not visible to bob
        var bobItems = await store.QueryAsync("agent-bob", new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/knowledge" }
        });
        Assert.Empty(bobItems);
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-17: UploadKnowledgeDocumentAsync tags with /knowledge folder
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UploadKnowledgeDocumentAsync_TagsWithKnowledgeFolder()
    {
        var store = new InMemoryContentStore();
        await store.UploadKnowledgeDocumentAsync("agent-x", "api-guide",
            Encoding.UTF8.GetBytes("# Guide"), "text/markdown");

        var results = await store.QueryAsync("agent-x");
        Assert.Single(results);
        Assert.Equal("/knowledge", results[0].Tags!["folder"]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-18: WriteMemoryAsync tags with /memory folder
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WriteMemoryAsync_TagsWithMemoryFolder()
    {
        var store = new InMemoryContentStore();
        await store.WriteMemoryAsync("agent-x", "user-prefs", "prefers dark mode");

        var results = await store.QueryAsync("agent-x");
        Assert.Single(results);
        Assert.Equal("/memory", results[0].Tags!["folder"]);
        Assert.Equal("user-prefs", results[0].Name);
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-19: WriteMemoryAsync with same title overwrites content
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WriteMemoryAsync_SameTitle_OverwritesContent()
    {
        var store = new InMemoryContentStore();
        var id1 = await store.WriteMemoryAsync("agent-x", "preferences", "prefers email");
        var id2 = await store.WriteMemoryAsync("agent-x", "preferences", "prefers SMS");

        Assert.Equal(id1, id2);

        var result = await store.GetAsync("agent-x", id1);
        Assert.NotNull(result);
        Assert.Contains("SMS", Encoding.UTF8.GetString(result.Data));
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-20: Extra tags on UploadKnowledgeDocumentAsync don't overwrite folder tag
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UploadKnowledgeDocumentAsync_ExtraTags_MergesWithFolderTag()
    {
        var store = new InMemoryContentStore();
        var extraTags = new Dictionary<string, string> { ["category"] = "auth", ["priority"] = "high" };

        await store.UploadKnowledgeDocumentAsync("agent-x", "auth-guide",
            Encoding.UTF8.GetBytes("# Auth Guide"), "text/markdown",
            extraTags: extraTags);

        var results = await store.QueryAsync("agent-x");
        Assert.Single(results);
        Assert.Equal("/knowledge", results[0].Tags!["folder"]);
        Assert.Equal("auth", results[0].Tags!["category"]);
        Assert.Equal("high", results[0].Tags!["priority"]);
    }
}
