using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;
using Xunit;

namespace HPD.Agent.Tests.Session;

/// <summary>
/// Delete tests for durable lineage and child references.
/// </summary>
public class ThreadTreeV3DeleteTests : AgentTestBase
{
    private static async Task<Agent> CreateAgentWithStore(InMemorySessionStore store)
        => await new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None);

    [Fact]
    public async Task DeleteThread_WithChildren_Throws()
    {
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveInitialThreadAsync(session.Id, main);
        main.Session = session;

        var fork1 = await agent.ForkThreadAsync(main, "fork-1", fromMessageId: main.Messages[0].MessageId!);
        fork1.Session = session;
        fork1.AddMessage(AssistantMessage("Response"));
        await store.SaveInitialThreadAsync(session.Id, fork1);
        await agent.ForkThreadAsync(fork1, "fork-1a", fromMessageId: fork1.Messages[0].MessageId!);

        var act = async () => await agent.DeleteThreadAsync(session.Id, "fork-1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*child*");
    }

    [Fact]
    public async Task DeleteLeafFork_RemovesParentChildReference()
    {
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveInitialThreadAsync(session.Id, main);
        main.Session = session;

        await agent.ForkThreadAsync(main, "fork-1", fromMessageId: main.Messages[0].MessageId!);
        (await store.ProjectThreadAsync(session.Id, "main", ThreadProjectionPurpose.ThreadHistory))!.ChildThreads.Should().Equal("fork-1");

        await agent.DeleteThreadAsync(session.Id, "fork-1");

        var afterMain = await store.ProjectThreadAsync(session.Id, "main", ThreadProjectionPurpose.ThreadHistory);
        var deleted = await store.ProjectThreadAsync(session.Id, "fork-1", ThreadProjectionPurpose.ThreadHistory);
        afterMain!.ChildThreads.Should().BeEmpty();
        afterMain.TotalForks.Should().Be(0);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeleteOneFork_DoesNotMutateRemainingForkLineage()
    {
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveInitialThreadAsync(session.Id, main);
        main.Session = session;
        var forkMessageId = main.Messages[0].MessageId!;

        await agent.ForkThreadAsync(main, "fork-1", fromMessageId: forkMessageId);
        main = (await store.ProjectThreadAsync(session.Id, "main", ThreadProjectionPurpose.ThreadHistory))!;
        main.Session = session;
        await agent.ForkThreadAsync(main, "fork-2", fromMessageId: forkMessageId);

        await agent.DeleteThreadAsync(session.Id, "fork-1");

        var afterMain = await store.ProjectThreadAsync(session.Id, "main", ThreadProjectionPurpose.ThreadHistory);
        var afterFork2 = await store.ProjectThreadAsync(session.Id, "fork-2", ThreadProjectionPurpose.ThreadHistory);
        afterMain!.ChildThreads.Should().Equal("fork-2");
        afterFork2!.ForkedFrom.Should().Be("main");
        afterFork2.ForkedAtMessageId.Should().Be(forkMessageId);
        afterFork2.Ancestors.Should().Contain("0", "main");
    }

    [Fact]
    public async Task DeleteMainThread_Throws()
    {
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateThread("main");
        await store.SaveInitialThreadAsync(session.Id, main);

        var act = async () => await agent.DeleteThreadAsync(session.Id, "main");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*main*");
    }
}
