using FluentAssertions;
using HPD.Agent.Adapters.Slack.OAuth;

namespace HPD.Agent.Adapters.Tests.Unit.SlackOAuth;

public class InMemorySlackTokenStoreTests
{
    [Fact]
    public async Task SaveAsync_StoresToken()
    {
        var store = new InMemorySlackTokenStore();
        await store.SaveAsync("T001", "xoxb-token-1");
        (await store.GetAsync("T001")).Should().Be("xoxb-token-1");
    }

    [Fact]
    public async Task GetAsync_UnknownTeam_ReturnsNull()
    {
        var store = new InMemorySlackTokenStore();
        (await store.GetAsync("T-unknown")).Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_OverwritesExistingToken()
    {
        var store = new InMemorySlackTokenStore();
        await store.SaveAsync("T001", "xoxb-old");
        await store.SaveAsync("T001", "xoxb-new");
        (await store.GetAsync("T001")).Should().Be("xoxb-new");
    }

    [Fact]
    public async Task SaveAsync_MultipleTeams_IsolatedPerTeam()
    {
        var store = new InMemorySlackTokenStore();
        await store.SaveAsync("T001", "xoxb-team-1");
        await store.SaveAsync("T002", "xoxb-team-2");

        (await store.GetAsync("T001")).Should().Be("xoxb-team-1");
        (await store.GetAsync("T002")).Should().Be("xoxb-team-2");
    }

    [Fact]
    public async Task SaveAndGet_ConcurrentCalls_NoDeadlock()
    {
        var store = new InMemorySlackTokenStore();
        var tasks = Enumerable.Range(0, 50).Select(i =>
            store.SaveAsync($"T{i:000}", $"xoxb-{i}"));
        await Task.WhenAll(tasks);

        // Spot-check a few
        (await store.GetAsync("T000")).Should().Be("xoxb-0");
        (await store.GetAsync("T049")).Should().Be("xoxb-49");
    }
}
