using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AspNetCore.Streaming;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;

namespace HPD.Agent.AspNetCore.Tests.Unit;

public sealed class SseEventHandlerScopeTests
{
    [Fact]
    public async Task IsInRouteScopeAsync_AllowsSubAgentChildThreadEventsLinkedToObservedParentThread()
    {
        var store = new InMemorySessionStore();
        var agent = await CreateAgentAsync(store);
        await agent.CreateSessionAsync("session-1");
        var forkMessageId = await SeedForkMessageAsync(agent, store, "session-1", "main");
        await agent.ForkThreadAsync(
            "session-1",
            "main",
            "subagent/reviewer/run-1",
            forkMessageId,
            new Dictionary<string, object>
            {
                ["kind"] = "subagent",
                ["parentSessionId"] = "session-1",
                ["parentThreadId"] = "main"
            });

        var evt = new TextDeltaEvent("child output", "msg-1")
        {
            SessionId = "session-1",
            ThreadId = "subagent/reviewer/run-1"
        };

        var inScope = await SseEventHandler.IsInRouteScopeAsync(
            agent,
            evt,
            "session-1",
            "main",
            CancellationToken.None);

        inScope.Should().BeTrue();
    }

    [Fact]
    public async Task IsInRouteScopeAsync_RejectsUnrelatedThreadEvents()
    {
        var store = new InMemorySessionStore();
        var agent = await CreateAgentAsync(store);
        await agent.CreateSessionAsync("session-1");
        var forkMessageId = await SeedForkMessageAsync(agent, store, "session-1", "main");
        await agent.ForkThreadAsync(
            "session-1",
            "main",
            "alternate",
            forkMessageId,
            new Dictionary<string, object> { ["kind"] = "conversation" });

        var evt = new TextDeltaEvent("other output", "msg-1")
        {
            SessionId = "session-1",
            ThreadId = "alternate"
        };

        var inScope = await SseEventHandler.IsInRouteScopeAsync(
            agent,
            evt,
            "session-1",
            "main",
            CancellationToken.None);

        inScope.Should().BeFalse();
    }

    private static Task<Agent> CreateAgentAsync(ISessionStore store)
    {
        var config = new AgentConfig
        {
            Name = "scope-test",
            Clients = new AgentClientConfig { Chat = new ClientProviderConfig {
                ProviderKey = "test",
                ModelName = "test-model"
            } }
        };

        return new AgentBuilder(config, new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync();
    }

    private static async Task<string> SeedForkMessageAsync(
        Agent agent,
        ISessionStore store,
        string sessionId,
        string threadId)
    {
        await agent.RunAsync("seed fork message", sessionId, threadId);

        var thread = await store.LoadThreadAsync(sessionId, threadId);
        var messageId = thread?.Messages.FirstOrDefault()?.MessageId;
        messageId.Should().NotBeNullOrWhiteSpace();

        return messageId!;
    }
}
