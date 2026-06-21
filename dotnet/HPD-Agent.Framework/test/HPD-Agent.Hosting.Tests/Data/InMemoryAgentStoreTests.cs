using FluentAssertions;
using HPD.Agent;

namespace HPD.Agent.Hosting.Tests.Data;

/// <summary>
/// Unit tests for <see cref="InMemoryAgentStore"/>.
/// </summary>
public class InMemoryAgentStoreTests
{
    private readonly InMemoryAgentStore _store = new();

    // ──────────────────────────────────────────────────────────────────────────
    // Load
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenMissing()
    {
        var result = await _store.LoadAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_ReturnsAgent_AfterSave()
    {
        var agent = MakeAgent("agent-1", "My Agent");
        await _store.SaveAsync(agent);

        var loaded = await _store.LoadAsync("agent-1");

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be("agent-1");
        loaded.Name.Should().Be("My Agent");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Save
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_AndLoadAsync_Roundtrip()
    {
        var agent = MakeAgent("roundtrip-id", "Roundtrip Agent");
        await _store.SaveAsync(agent);

        var loaded = await _store.LoadAsync("roundtrip-id");

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(agent.Id);
        loaded.Name.Should().Be(agent.Name);
        loaded.Config.Should().BeSameAs(agent.Config);
    }

    [Fact]
    public async Task SaveAsync_Upserts_ExistingEntry()
    {
        var original = MakeAgent("upsert-id", "Original");
        await _store.SaveAsync(original);

        var updated = MakeAgent("upsert-id", "Updated");
        await _store.SaveAsync(updated);

        var loaded = await _store.LoadAsync("upsert-id");
        loaded!.Name.Should().Be("Updated");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Delete
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesEntry()
    {
        var agent = MakeAgent("delete-id", "To Delete");
        await _store.SaveAsync(agent);

        await _store.DeleteAsync("delete-id");

        var loaded = await _store.LoadAsync("delete-id");
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_IsIdempotent_WhenKeyMissing()
    {
        var act = async () => await _store.DeleteAsync("nonexistent");
        await act.Should().NotThrowAsync();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ListIds
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListIdsAsync_ReturnsEmpty_WhenNoAgentsSaved()
    {
        var ids = await _store.ListIdsAsync();
        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task ListIdsAsync_ReturnsAll_SavedIds()
    {
        await _store.SaveAsync(MakeAgent("id-a", "A"));
        await _store.SaveAsync(MakeAgent("id-b", "B"));
        await _store.SaveAsync(MakeAgent("id-c", "C"));

        var ids = await _store.ListIdsAsync();

        ids.Should().HaveCount(3);
        ids.Should().Contain(["id-a", "id-b", "id-c"]);
    }

    [Fact]
    public async Task ListIdsAsync_DoesNotInclude_DeletedIds()
    {
        await _store.SaveAsync(MakeAgent("keep", "Keep"));
        await _store.SaveAsync(MakeAgent("remove", "Remove"));

        await _store.DeleteAsync("remove");

        var ids = await _store.ListIdsAsync();
        ids.Should().Contain("keep");
        ids.Should().NotContain("remove");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static StoredAgent MakeAgent(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Config = new AgentConfig { Name = name }
    };
}
