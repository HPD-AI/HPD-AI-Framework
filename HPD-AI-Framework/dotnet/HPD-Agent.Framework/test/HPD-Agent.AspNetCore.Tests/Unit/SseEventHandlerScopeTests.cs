using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AspNetCore.Streaming;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;

namespace HPD.Agent.AspNetCore.Tests.Unit;

public sealed class SseEventHandlerScopeTests
{
    [Fact]
    public async Task IsInRouteScopeAsync_AllowsSubAgentChildBranchEventsLinkedToObservedParentBranch()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var agent = await CreateAgentAsync(repository);
        await agent.CreateSessionAsync("session-1");
        var forkMessageId = await SeedForkMessageAsync(agent, repository, "session-1", "main");
        await agent.ForkBranchAsync(
            "session-1",
            "main",
            "subagent/reviewer/run-1",
            forkMessageId,
            new Dictionary<string, object>
            {
                ["kind"] = "subagent",
                ["parentSessionId"] = "session-1",
                ["parentBranchId"] = "main"
            });

        var evt = new TextDeltaEvent("child output", "msg-1")
        {
            SessionId = "session-1",
            BranchId = "subagent/reviewer/run-1"
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
    public async Task IsInRouteScopeAsync_RejectsUnrelatedBranchEvents()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var agent = await CreateAgentAsync(repository);
        await agent.CreateSessionAsync("session-1");
        var forkMessageId = await SeedForkMessageAsync(agent, repository, "session-1", "main");
        await agent.ForkBranchAsync(
            "session-1",
            "main",
            "alternate",
            forkMessageId,
            new Dictionary<string, object> { ["kind"] = "conversation" });

        var evt = new TextDeltaEvent("other output", "msg-1")
        {
            SessionId = "session-1",
            BranchId = "alternate"
        };

        var inScope = await SseEventHandler.IsInRouteScopeAsync(
            agent,
            evt,
            "session-1",
            "main",
            CancellationToken.None);

        inScope.Should().BeFalse();
    }

    private static Task<Agent> CreateAgentAsync(ISessionRepository repository)
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
            .WithSessionRepository(repository)
            .BuildAsync();
    }

    private static async Task<string> SeedForkMessageAsync(
        Agent agent,
        ISessionRepository repository,
        string sessionId,
        string branchId)
    {
        await agent.RunAsync("seed fork message", sessionId, branchId);

        var branch = await repository.LoadBranchAsync(sessionId, branchId);
        var messageId = branch?.Messages.FirstOrDefault()?.MessageId;
        messageId.Should().NotBeNullOrWhiteSpace();

        return messageId!;
    }
}
