using System.Text;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Content;

/// <summary>
/// Contract tests for IContentStore. Every implementation must pass all tests.
/// Parameterised via the abstract factory method — add new implementations by
/// creating a subclass that overrides CreateStore().
/// </summary>
public abstract class IContentStoreContractTests
{
    protected abstract IContentStore CreateStore();

    // ═══════════════════════════════════════════════════════════════════
    // C-1: Put returns a non-empty ID
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Put_ReturnsNonEmptyId()
    {
        var store = CreateStore();
        var id = await store.PutAsync("scope-a", new byte[] { 1, 2, 3 }, "image/jpeg");
        Assert.NotNull(id);
        Assert.NotEmpty(id);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-2: Named upsert — same name + same content → same ID
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Put_SameScopeAndName_ReturnsStableId()
    {
        var store = CreateStore();
        var data = Encoding.UTF8.GetBytes("hello");
        var meta = new ContentMetadata { Name = "doc.txt" };

        var id1 = await store.PutAsync("scope-a", data, "text/plain", meta);
        var id2 = await store.PutAsync("scope-a", data, "text/plain", meta);

        Assert.Equal(id1, id2);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-3: Named upsert — same name + different content → ID stable, bytes updated
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Put_SameScopeAndName_ChangedContent_IdStable_ContentUpdated()
    {
        var store = CreateStore();
        var original = Encoding.UTF8.GetBytes("version 1");
        var updated = Encoding.UTF8.GetBytes("version 2");
        var meta = new ContentMetadata { Name = "doc.txt" };

        var id1 = await store.PutAsync("scope-a", original, "text/plain", meta);
        var id2 = await store.PutAsync("scope-a", updated, "text/plain", meta);

        Assert.Equal(id1, id2);

        var result = await store.GetAsync("scope-a", id1);
        Assert.NotNull(result);
        Assert.Equal(updated, result.Data);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-4: Same name in different scopes → two independent entries
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Put_SameName_DifferentScope_CreatesTwoEntries()
    {
        var store = CreateStore();
        var meta = new ContentMetadata { Name = "shared.txt" };

        var idA = await store.PutAsync("scope-a", Encoding.UTF8.GetBytes("data-a"), "text/plain", meta);
        var idB = await store.PutAsync("scope-b", Encoding.UTF8.GetBytes("data-b"), "text/plain", meta);

        Assert.NotEqual(idA, idB);

        var fromA = await store.GetAsync("scope-a", idA);
        var fromB = await store.GetAsync("scope-b", idB);

        Assert.NotNull(fromA);
        Assert.NotNull(fromB);
        Assert.Equal("data-a", Encoding.UTF8.GetString(fromA.Data));
        Assert.Equal("data-b", Encoding.UTF8.GetString(fromB.Data));
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-5: GetAsync returns correct data, content type, and ContentInfo
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_ReturnsCorrectData_ContentType_AndInfo()
    {
        var store = CreateStore();
        var data = new byte[] { 10, 20, 30 };
        var meta = new ContentMetadata
        {
            Name = "photo.jpg",
            Description = "A test photo",
            Origin = ContentSource.User,
            Tags = new Dictionary<string, string> { ["folder"] = "/uploads" },
            OriginalSource = "/tmp/photo.jpg"
        };

        var id = await store.PutAsync("sess-1", data, "image/jpeg", meta);
        var result = await store.GetAsync("sess-1", id);

        Assert.NotNull(result);
        Assert.Equal(id, result.Id);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(data, result.Data);
        Assert.Equal("photo.jpg", result.Info.Name);
        Assert.Equal("A test photo", result.Info.Description);
        Assert.Equal(ContentSource.User, result.Info.Origin);
        Assert.Equal("/tmp/photo.jpg", result.Info.OriginalSource);
        Assert.NotNull(result.Info.Tags);
        Assert.Equal("/uploads", result.Info.Tags["folder"]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-6: Get unknown ID returns null
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_UnknownId_ReturnsNull()
    {
        var store = CreateStore();
        var result = await store.GetAsync("scope-a", "does-not-exist-12345");
        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-7: Get with wrong scope returns null
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_WrongScope_ReturnsNull()
    {
        var store = CreateStore();
        var id = await store.PutAsync("scope-a", new byte[] { 1 }, "text/plain");
        var result = await store.GetAsync("scope-b", id);
        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-8: Delete removes content
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Delete_RemovesContent()
    {
        var store = CreateStore();
        var id = await store.PutAsync("scope-a", new byte[] { 1, 2, 3 }, "image/png");
        Assert.NotNull(await store.GetAsync("scope-a", id));

        await store.DeleteAsync("scope-a", id);

        Assert.Null(await store.GetAsync("scope-a", id));
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-9: Delete is idempotent
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Delete_IsIdempotent()
    {
        var store = CreateStore();
        var id = await store.PutAsync("scope-a", new byte[] { 1 }, "text/plain");
        await store.DeleteAsync("scope-a", id);

        // Second delete should not throw
        var ex = await Record.ExceptionAsync(() => store.DeleteAsync("scope-a", id));
        Assert.Null(ex);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-10: Delete doesn't affect other content in the scope
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Delete_OtherContentInScope_Unaffected()
    {
        var store = CreateStore();
        var id1 = await store.PutAsync("scope-a", new byte[] { 1 }, "text/plain");
        var id2 = await store.PutAsync("scope-a", new byte[] { 2 }, "text/plain");

        await store.DeleteAsync("scope-a", id1);

        Assert.Null(await store.GetAsync("scope-a", id1));
        Assert.NotNull(await store.GetAsync("scope-a", id2));
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-11: Query with null query returns all content in scope
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Query_NullQuery_ReturnsAllInScope()
    {
        var store = CreateStore();
        await store.PutAsync("scope-a", new byte[] { 1 }, "text/plain");
        await store.PutAsync("scope-a", new byte[] { 2 }, "image/jpeg");
        await store.PutAsync("scope-a", new byte[] { 3 }, "audio/mpeg");

        // Content in different scope should not appear
        await store.PutAsync("scope-b", new byte[] { 99 }, "text/plain");

        var results = await store.QueryAsync("scope-a");

        Assert.Equal(3, results.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-12: Query with null scope returns across all scopes
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Query_NullScope_ReturnsAcrossAllScopes()
    {
        var store = CreateStore();
        await store.PutAsync("scope-a", new byte[] { 1 }, "text/plain");
        await store.PutAsync("scope-b", new byte[] { 2 }, "text/plain");

        var results = await store.QueryAsync(null);

        Assert.Equal(2, results.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-13: Query by ContentType — exact MIME match
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Query_ContentType_ExactMatch()
    {
        var store = CreateStore();
        await store.PutAsync("scope-a", new byte[] { 1 }, "image/jpeg");
        await store.PutAsync("scope-a", new byte[] { 2 }, "image/jpeg");
        await store.PutAsync("scope-a", new byte[] { 3 }, "audio/mpeg");

        var results = await store.QueryAsync("scope-a", new ContentQuery { ContentType = "image/jpeg" });

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("image/jpeg", r.ContentType));
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-14: Query by ContentType — no matches returns empty
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Query_ContentType_NoMatch_ReturnsEmpty()
    {
        var store = CreateStore();
        await store.PutAsync("scope-a", new byte[] { 1 }, "image/jpeg");

        var results = await store.QueryAsync("scope-a", new ContentQuery { ContentType = "video/mp4" });

        Assert.Empty(results);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-15: Query by CreatedAfter — excludes older content
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Query_CreatedAfter_ExcludesOlderContent()
    {
        var store = CreateStore();
        var cutoff = DateTime.UtcNow.AddMilliseconds(50);

        await store.PutAsync("scope-a", new byte[] { 1 }, "text/plain");
        await Task.Delay(100);

        await store.PutAsync("scope-a", new byte[] { 2 }, "text/plain");

        var results = await store.QueryAsync("scope-a", new ContentQuery { CreatedAfter = cutoff });

        Assert.Single(results);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-16: Query with future CreatedAfter returns empty
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Query_CreatedAfter_FutureTimestamp_ReturnsEmpty()
    {
        var store = CreateStore();
        await store.PutAsync("scope-a", new byte[] { 1 }, "text/plain");

        var results = await store.QueryAsync("scope-a",
            new ContentQuery { CreatedAfter = DateTime.UtcNow.AddDays(1) });

        Assert.Empty(results);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-17: Query Limit caps results
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Query_Limit_CapsResults()
    {
        var store = CreateStore();
        for (int i = 0; i < 5; i++)
            await store.PutAsync("scope-a", new byte[] { (byte)i }, "text/plain");

        var results = await store.QueryAsync("scope-a", new ContentQuery { Limit = 3 });

        Assert.True(results.Count <= 3);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-18: Query by tag — returns only tagged content
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Query_ByTag_ReturnsOnlyTaggedContent()
    {
        var store = CreateStore();
        await store.PutAsync("scope-a", new byte[] { 1 }, "text/plain",
            new ContentMetadata { Tags = new Dictionary<string, string> { ["folder"] = "/knowledge" } });
        await store.PutAsync("scope-a", new byte[] { 2 }, "text/plain",
            new ContentMetadata { Tags = new Dictionary<string, string> { ["folder"] = "/memory" } });
        await store.PutAsync("scope-a", new byte[] { 3 }, "text/plain"); // no tag

        var results = await store.QueryAsync("scope-a",
            new ContentQuery { Tags = new Dictionary<string, string> { ["folder"] = "/knowledge" } });

        Assert.Single(results);
        Assert.Equal("/knowledge", results[0].Tags!["folder"]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-19: Query by Name — exact match
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Query_ByName_ReturnsMatchingItem()
    {
        var store = CreateStore();
        await store.PutAsync("scope-a", Encoding.UTF8.GetBytes("doc a"), "text/plain",
            new ContentMetadata { Name = "api-guide.md" });
        await store.PutAsync("scope-a", Encoding.UTF8.GetBytes("doc b"), "text/plain",
            new ContentMetadata { Name = "readme.md" });

        var results = await store.QueryAsync("scope-a",
            new ContentQuery { Name = "api-guide.md" });

        Assert.Single(results);
        Assert.Equal("api-guide.md", results[0].Name);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-20: Combined filters use AND logic
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Query_CombinedFilters_AND_Logic()
    {
        var store = CreateStore();
        // Only this one matches both ContentType=image/jpeg AND has folder=/uploads tag
        await store.PutAsync("scope-a", new byte[] { 1 }, "image/jpeg",
            new ContentMetadata { Tags = new Dictionary<string, string> { ["folder"] = "/uploads" } });
        // Wrong content type
        await store.PutAsync("scope-a", new byte[] { 2 }, "text/plain",
            new ContentMetadata { Tags = new Dictionary<string, string> { ["folder"] = "/uploads" } });
        // Wrong tag
        await store.PutAsync("scope-a", new byte[] { 3 }, "image/jpeg",
            new ContentMetadata { Tags = new Dictionary<string, string> { ["folder"] = "/memory" } });

        var results = await store.QueryAsync("scope-a", new ContentQuery
        {
            ContentType = "image/jpeg",
            Tags = new Dictionary<string, string> { ["folder"] = "/uploads" }
        });

        Assert.Single(results);
        Assert.Equal("image/jpeg", results[0].ContentType);
        Assert.Equal("/uploads", results[0].Tags!["folder"]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-21: QueryAsync returns metadata only, not bytes
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Query_ReturnsMetadataOnly_NotBytes()
    {
        var store = CreateStore();
        await store.PutAsync("scope-a", new byte[] { 1, 2, 3 }, "image/jpeg");

        var results = await store.QueryAsync("scope-a");

        // Result is ContentInfo, not ContentData — no Data field
        Assert.Single(results);
        Assert.IsAssignableFrom<ContentInfo>(results[0]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-22: Full ContentMetadata round-trips through ContentInfo
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ContentInfo_FieldMapping_AllPopulated()
    {
        var store = CreateStore();
        var meta = new ContentMetadata
        {
            Name = "test-doc.md",
            Description = "Test description",
            Origin = ContentSource.Agent,
            Tags = new Dictionary<string, string> { ["category"] = "notes", ["folder"] = "/memory" },
            OriginalSource = "https://example.com/doc"
        };

        var id = await store.PutAsync("agent-x", Encoding.UTF8.GetBytes("content"), "text/markdown", meta);
        var results = await store.QueryAsync("agent-x");

        Assert.Single(results);
        var info = results[0];
        Assert.Equal("test-doc.md", info.Name);
        Assert.Equal("Test description", info.Description);
        Assert.Equal(ContentSource.Agent, info.Origin);
        Assert.Equal("https://example.com/doc", info.OriginalSource);
        Assert.Equal("notes", info.Tags!["category"]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-23: Unspecified origin gets a sensible default (not thrown / not random)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ContentInfo_Origin_DefaultsWhenNotSpecified()
    {
        var store = CreateStore();
        await store.PutAsync("scope-a", new byte[] { 1 }, "text/plain"); // no metadata

        var results = await store.QueryAsync("scope-a");

        Assert.Single(results);
        // Origin must be a valid enum value — not uninitialized garbage
        Assert.True(Enum.IsDefined(typeof(ContentSource), results[0].Origin));
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-24: SizeBytes reflects actual data length
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ContentInfo_SizeBytes_MatchesActualData()
    {
        var store = CreateStore();
        var data = Encoding.UTF8.GetBytes("Hello, world!");

        await store.PutAsync("scope-a", data, "text/plain");
        var results = await store.QueryAsync("scope-a");

        Assert.Single(results);
        Assert.Equal(data.Length, results[0].SizeBytes);
    }

    // ═══════════════════════════════════════════════════════════════════
    // C-25: CreatedAt is UTC and within a reasonable test window
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ContentInfo_CreatedAt_IsUtcAndReasonable()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var store = CreateStore();
        await store.PutAsync("scope-a", new byte[] { 1 }, "text/plain");
        var after = DateTime.UtcNow.AddSeconds(1);

        var results = await store.QueryAsync("scope-a");

        Assert.Single(results);
        var createdAt = results[0].CreatedAt;
        Assert.Equal(DateTimeKind.Utc, createdAt.Kind);
        Assert.True(createdAt >= before, $"CreatedAt {createdAt} < test start {before}");
        Assert.True(createdAt <= after, $"CreatedAt {createdAt} > test end {after}");
    }
}

// ═══════════════════════════════════════════════════════════════════════
// Concrete subclass: runs all contract tests against InMemoryContentStore
// ═══════════════════════════════════════════════════════════════════════

public class InMemoryContentStore_ContractTests : IContentStoreContractTests
{
    protected override IContentStore CreateStore() => new InMemoryContentStore();
}
