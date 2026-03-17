using System.Text;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Content;

/// <summary>
/// Tests specific to InMemoryContentStore — test-helper methods (Clear, Count, etc.)
/// and named-upsert edge cases that are only observable at the in-memory level.
/// </summary>
public class InMemoryContentStoreTests
{
    // ═══════════════════════════════════════════════════════════════════
    // IM-1: Clear() removes all content across all scopes
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Clear_RemovesAllContent()
    {
        var store = new InMemoryContentStore();
        await store.PutAsync("scope-a", new byte[] { 1 }, "text/plain");
        await store.PutAsync("scope-b", new byte[] { 2 }, "text/plain");
        Assert.Equal(2, store.Count);

        store.Clear();

        Assert.Equal(0, store.Count);
        Assert.Empty(await store.QueryAsync(null));
    }

    // ═══════════════════════════════════════════════════════════════════
    // IM-2: Count tracks total items across scopes
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Count_ReflectsNumberOfItems()
    {
        var store = new InMemoryContentStore();
        Assert.Equal(0, store.Count);

        await store.PutAsync("scope-a", new byte[] { 1 }, "text/plain");
        Assert.Equal(1, store.Count);

        await store.PutAsync("scope-b", new byte[] { 2 }, "text/plain");
        Assert.Equal(2, store.Count);

        var id = await store.PutAsync("scope-a", new byte[] { 3 }, "text/plain");
        Assert.Equal(3, store.Count);

        await store.DeleteAsync("scope-a", id);
        Assert.Equal(2, store.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // IM-3: CountInScope isolates count per scope
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CountInScope_IsolatesPerScope()
    {
        var store = new InMemoryContentStore();
        await store.PutAsync("scope-a", new byte[] { 1 }, "text/plain");
        await store.PutAsync("scope-a", new byte[] { 2 }, "text/plain");
        await store.PutAsync("scope-b", new byte[] { 3 }, "text/plain");

        Assert.Equal(2, store.CountInScope("scope-a"));
        Assert.Equal(1, store.CountInScope("scope-b"));
        Assert.Equal(0, store.CountInScope("scope-c")); // non-existent scope
    }

    // ═══════════════════════════════════════════════════════════════════
    // IM-4: Contains returns true for known ID in correct scope
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Contains_ReturnsTrueForExistingId()
    {
        var store = new InMemoryContentStore();
        var id = await store.PutAsync("scope-a", new byte[] { 1 }, "text/plain");

        Assert.True(store.Contains("scope-a", id));
        Assert.False(store.Contains("scope-b", id));         // wrong scope
        Assert.False(store.Contains("scope-a", "no-such")); // wrong ID
    }

    // ═══════════════════════════════════════════════════════════════════
    // IM-5: Named upsert with same bytes → truly a no-op (no new write)
    //        Verifiable because Count doesn't change and ID is stable.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NamedUpsert_SameBytesMultipleTimes_CountStaysOne()
    {
        var store = new InMemoryContentStore();
        var data = Encoding.UTF8.GetBytes("identical content");
        var meta = new ContentMetadata { Name = "file.txt" };

        var id1 = await store.PutAsync("scope-a", data, "text/plain", meta);
        var id2 = await store.PutAsync("scope-a", data, "text/plain", meta);
        var id3 = await store.PutAsync("scope-a", data, "text/plain", meta);

        Assert.Equal(id1, id2);
        Assert.Equal(id1, id3);
        Assert.Equal(1, store.CountInScope("scope-a"));
    }
}
