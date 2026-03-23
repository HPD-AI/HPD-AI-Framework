using Xunit;
using HPD.Agent;

namespace HPD.Agent.Tests.Content;

/// <summary>
/// Integration tests for IContentStore using InMemoryContentStore.
/// Covers scoped isolation, content-type filtering, timestamp filtering, and get/delete semantics.
/// </summary>
public class ContentStoreIntegrationTests
{
    // ═══════════════════════════════════════════════════════════════════
    // Scoped Isolation
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PutAndGet_WithinSameScope_ReturnsContent()
    {
        var store = new InMemoryContentStore();
        var data = new byte[] { 1, 2, 3 };

        var id = await store.PutAsync("session-a", data, "image/jpeg");
        var result = await store.GetAsync("session-a", id);

        Assert.NotNull(result);
        Assert.Equal(id, result.Id);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(data, result.Data);
    }

    [Fact]
    public async Task Get_FromWrongScope_ReturnsNull()
    {
        var store = new InMemoryContentStore();

        var id = await store.PutAsync("session-a", new byte[] { 1, 2, 3 }, "image/jpeg");
        var result = await store.GetAsync("session-b", id);

        Assert.Null(result);
    }

    [Fact]
    public async Task Query_WithinScope_ReturnsOnlyThatScopesContent()
    {
        var store = new InMemoryContentStore();

        await store.PutAsync("agent-x", new byte[] { 1 }, "text/plain");
        await store.PutAsync("agent-x", new byte[] { 2 }, "text/plain");
        await store.PutAsync("agent-y", new byte[] { 3 }, "text/plain");

        var xResults = await store.QueryAsync("agent-x");
        var yResults = await store.QueryAsync("agent-y");

        Assert.Equal(2, xResults.Count);
        Assert.Single(yResults);
    }

    [Fact]
    public async Task Query_WithNullScope_ReturnsAcrossAllScopes()
    {
        var store = new InMemoryContentStore();

        await store.PutAsync("scope-1", new byte[] { 1 }, "text/plain");
        await store.PutAsync("scope-2", new byte[] { 2 }, "text/plain");

        var all = await store.QueryAsync(null);

        Assert.Equal(2, all.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ContentType Filtering
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task QueryByContentType_ReturnsOnlyMatchingMimeType()
    {
        var store = new InMemoryContentStore();

        await store.PutAsync("session-1", new byte[] { 1 }, "image/jpeg");
        await store.PutAsync("session-1", new byte[] { 2 }, "image/jpeg");
        await store.PutAsync("session-1", new byte[] { 3 }, "audio/mpeg");

        var jpegs = await store.QueryAsync("session-1", new ContentQuery { ContentType = "image/jpeg" });

        Assert.Equal(2, jpegs.Count);
        Assert.All(jpegs, item => Assert.Equal("image/jpeg", item.ContentType));
    }

    [Fact]
    public async Task QueryByContentType_NoMatches_ReturnsEmptyList()
    {
        var store = new InMemoryContentStore();
        await store.PutAsync("session-1", new byte[] { 1 }, "image/jpeg");

        var results = await store.QueryAsync("session-1", new ContentQuery { ContentType = "video/mp4" });

        Assert.Empty(results);
    }

    // ═══════════════════════════════════════════════════════════════════
    // CreatedAfter Filtering
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task QueryByCreatedAfter_ReturnsOnlyRecentContent()
    {
        var store = new InMemoryContentStore();
        var cutoff = DateTime.UtcNow.AddMinutes(-1);

        await Task.Delay(50); // ensure timestamps land after cutoff
        await store.PutAsync("agent-1", new byte[] { 1 }, "text/plain");
        await store.PutAsync("agent-1", new byte[] { 2 }, "text/plain");

        var results = await store.QueryAsync("agent-1", new ContentQuery { CreatedAfter = cutoff });

        Assert.Equal(2, results.Count);
        Assert.All(results, item => Assert.True(item.CreatedAt >= cutoff));
    }

    [Fact]
    public async Task QueryByCreatedAfter_FutureTimestamp_ReturnsEmpty()
    {
        var store = new InMemoryContentStore();
        await store.PutAsync("agent-1", new byte[] { 1 }, "text/plain");

        var results = await store.QueryAsync("agent-1", new ContentQuery
        {
            CreatedAfter = DateTime.UtcNow.AddMinutes(5)
        });

        Assert.Empty(results);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Delete Semantics
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Delete_RemovesContent_GetReturnsNull()
    {
        var store = new InMemoryContentStore();

        var id = await store.PutAsync("session-1", new byte[] { 1, 2, 3 }, "image/png");
        Assert.NotNull(await store.GetAsync("session-1", id));

        await store.DeleteAsync("session-1", id);
        Assert.Null(await store.GetAsync("session-1", id));
    }

    [Fact]
    public async Task Delete_IsIdempotent_NoErrorOnMissingId()
    {
        var store = new InMemoryContentStore();

        // Should not throw
        await store.DeleteAsync("session-1", "nonexistent-id");
    }

    [Fact]
    public async Task Delete_RemovesFromQuery_ButOtherContentRemains()
    {
        var store = new InMemoryContentStore();

        var id1 = await store.PutAsync("session-1", new byte[] { 1 }, "text/plain");
        var id2 = await store.PutAsync("session-1", new byte[] { 2 }, "text/plain");

        await store.DeleteAsync("session-1", id1);

        var results = await store.QueryAsync("session-1");
        Assert.Single(results);
        Assert.Equal(id2, results[0].Id);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ContentInfo Metadata
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Put_WithMetadata_PresentsCorrectInfoOnQuery()
    {
        var store = new InMemoryContentStore();
        var data = System.Text.Encoding.UTF8.GetBytes("hello");

        await store.PutAsync("agent-1", data, "text/plain", new ContentMetadata
        {
            Name = "greeting.txt",
            Description = "A simple greeting",
            Origin = ContentSource.User,
            Tags = new Dictionary<string, string> { ["folder"] = "/uploads" }
        });

        var results = await store.QueryAsync("agent-1");

        Assert.Single(results);
        var info = results[0];
        Assert.Equal("greeting.txt", info.Name);
        Assert.Equal("A simple greeting", info.Description);
        Assert.Equal(ContentSource.User, info.Origin);
        Assert.Equal(data.Length, info.SizeBytes);
        Assert.Equal("/uploads", info.Tags!["folder"]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Tag Filtering
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task QueryByTag_ReturnsOnlyMatchingTaggedContent()
    {
        var store = new InMemoryContentStore();

        await store.PutAsync("agent-1", new byte[] { 1 }, "text/plain", new ContentMetadata
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/knowledge" }
        });
        await store.PutAsync("agent-1", new byte[] { 2 }, "text/plain", new ContentMetadata
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/memory" }
        });

        var knowledge = await store.QueryAsync("agent-1", new ContentQuery
        {
            Tags = new Dictionary<string, string> { ["folder"] = "/knowledge" }
        });

        Assert.Single(knowledge);
        Assert.Equal("/knowledge", knowledge[0].Tags!["folder"]);
    }
}
