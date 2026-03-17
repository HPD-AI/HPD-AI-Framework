using Xunit;
using HPD.Agent;

namespace HPD.Agent.Tests.Skills;

/// <summary>
/// Tests for skill document upload, idempotency, cross-skill sharing, and description overrides.
/// All tests use InMemoryContentStore + ContentStoreExtensions (V3 API).
/// </summary>
public class Phase5DocumentLinkingTests
{
    // ═══════════════════════════════════════════════════════════════════
    // Idempotent Upload
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UploadSkillDocument_SameContentTwice_IsNoOp()
    {
        var store = new InMemoryContentStore();
        var content = "# OAuth Guide\nHow to handle OAuth flows.";

        var id1 = await store.UploadSkillDocumentAsync("oauth-guide", content, "OAuth authentication guide");
        var id2 = await store.UploadSkillDocumentAsync("oauth-guide", content, "OAuth authentication guide");

        // Same content = same ID returned (no-op, no duplicate entry)
        Assert.Equal(id1, id2);
        var all = await store.QueryAsync(null, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/skills" }
        });
        Assert.Single(all);
    }

    [Fact]
    public async Task UploadSkillDocument_ChangedContent_OverwritesInPlace()
    {
        var store = new InMemoryContentStore();

        var id1 = await store.UploadSkillDocumentAsync("oauth-guide", "# Version 1", "OAuth guide");
        var id2 = await store.UploadSkillDocumentAsync("oauth-guide", "# Version 2 — updated", "OAuth guide");

        // Same ID (in-place overwrite), content updated
        Assert.Equal(id1, id2);

        var data = await store.GetAsync(null, id1);
        Assert.NotNull(data);
        Assert.Contains("Version 2", System.Text.Encoding.UTF8.GetString(data.Data));
    }

    [Fact]
    public async Task UploadSkillDocument_CalledEveryStartup_IsStartupSafe()
    {
        var store = new InMemoryContentStore();
        const string content = "# Auth Guide";

        // Simulate being called 3× at startup
        for (var i = 0; i < 3; i++)
            await store.UploadSkillDocumentAsync("auth-guide", content, "Auth guide");

        var results = await store.QueryAsync(null, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/skills" }
        });
        Assert.Single(results);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Cross-Skill Document Sharing
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LinkSkillDocument_TwoSkills_ShareOneDocument()
    {
        var store = new InMemoryContentStore();

        await store.UploadSkillDocumentAsync("oauth-guide", "# OAuth", "OAuth protocol reference");
        await store.LinkSkillDocumentAsync("oauth-guide", "SecuritySkill", "Token validation and expiry rules");
        await store.LinkSkillDocumentAsync("oauth-guide", "AuthToolkit", "OAuth flow for login integration");

        // Only one document in the store — not duplicated
        var all = await store.QueryAsync(null, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/skills" }
        });
        Assert.Single(all);
    }

    [Fact]
    public async Task LinkSkillDocument_StoresPerSkillDescriptionTag()
    {
        var store = new InMemoryContentStore();

        await store.UploadSkillDocumentAsync("oauth-guide", "# OAuth", "Global default description");
        await store.LinkSkillDocumentAsync("oauth-guide", "SecuritySkill", "Token validation rules");

        var results = await store.QueryAsync(null, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/skills" }
        });

        Assert.Single(results);
        var doc = results[0];
        Assert.True(doc.Tags!.ContainsKey("description:SecuritySkill"));
        Assert.Equal("Token validation rules", doc.Tags["description:SecuritySkill"]);
    }

    [Fact]
    public async Task LinkSkillDocument_MultipleSkills_EachGetOwnDescriptionTag()
    {
        var store = new InMemoryContentStore();

        await store.UploadSkillDocumentAsync("shared-doc", "# Shared", "Default description");
        await store.LinkSkillDocumentAsync("shared-doc", "SkillA", "Description for Skill A");
        await store.LinkSkillDocumentAsync("shared-doc", "SkillB", "Description for Skill B");

        var results = await store.QueryAsync(null, new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/skills" }
        });

        Assert.Single(results);
        var tags = results[0].Tags!;
        Assert.Equal("Description for Skill A", tags["description:SkillA"]);
        Assert.Equal("Description for Skill B", tags["description:SkillB"]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // LinkSkillDocument Validation
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LinkSkillDocument_DocumentNotUploaded_Throws()
    {
        var store = new InMemoryContentStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.LinkSkillDocumentAsync("missing-doc", "SomeSkill", "Some description"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // Knowledge Documents (Agent-Scoped)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UploadKnowledgeDocument_SameAgentAndName_IsIdempotent()
    {
        var store = new InMemoryContentStore();
        var data = System.Text.Encoding.UTF8.GetBytes("# API Guide");

        var id1 = await store.UploadKnowledgeDocumentAsync("my-agent", "api-guide", data, "text/markdown");
        var id2 = await store.UploadKnowledgeDocumentAsync("my-agent", "api-guide", data, "text/markdown");

        Assert.Equal(id1, id2);

        var results = await store.QueryAsync("my-agent", new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/knowledge" }
        });
        Assert.Single(results);
    }

    [Fact]
    public async Task UploadKnowledgeDocument_DifferentAgents_AreIsolated()
    {
        var store = new InMemoryContentStore();
        var data = System.Text.Encoding.UTF8.GetBytes("# Guide");

        await store.UploadKnowledgeDocumentAsync("agent-a", "guide", data, "text/markdown");
        await store.UploadKnowledgeDocumentAsync("agent-b", "guide", data, "text/markdown");

        var agentAResults = await store.QueryAsync("agent-a");
        var agentBResults = await store.QueryAsync("agent-b");

        Assert.Single(agentAResults);
        Assert.Single(agentBResults);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Memory (Agent-Scoped)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WriteMemory_SameTitleTwice_OverwritesContent()
    {
        var store = new InMemoryContentStore();

        var id1 = await store.WriteMemoryAsync("my-agent", "user-prefs", "Prefers email");
        var id2 = await store.WriteMemoryAsync("my-agent", "user-prefs", "Prefers SMS now");

        Assert.Equal(id1, id2);

        var data = await store.GetAsync("my-agent", id1);
        Assert.Contains("SMS", System.Text.Encoding.UTF8.GetString(data!.Data));
    }

    [Fact]
    public async Task WriteMemory_IsTaggedWithMemoryFolder()
    {
        var store = new InMemoryContentStore();

        await store.WriteMemoryAsync("my-agent", "note-1", "Remember this");

        var results = await store.QueryAsync("my-agent", new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/memory" }
        });

        Assert.Single(results);
        Assert.Equal("note-1", results[0].Name);
    }
}
