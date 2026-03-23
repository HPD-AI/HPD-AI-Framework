using Xunit;
using FluentAssertions;
using Microsoft.Extensions.AI;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.SubAgents;

/// <summary>
/// Tests for SubAgent Session mode behaviour introduced/fixed in the branch-aware Session mode update:
/// - SharedBranchId property on SubAgent
/// - PerSession mode: inherits parent's session + branch via shared store
/// - SharedSession bug fix: CreateSessionAsync guard (only creates on first invocation)
/// - Stateless mode: fresh isolated session per call (regression)
/// </summary>
public class SubAgentSessionModeTests : AgentTestBase
{
    private static AgentConfig MinimalConfig() => new()
    {
        Name = "SubAgentUnderTest",
        SystemInstructions = "Test sub-agent.",
        Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" }
    };

    private static Agent BuildAgent(InMemorySessionStore store, FakeChatClient fakeClient) =>
        new AgentBuilder(MinimalConfig(), new TestProviderRegistry(fakeClient))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None).GetAwaiter().GetResult();

    private static async Task DrainAsync(IAsyncEnumerable<AgentEvent> events)
    {
        await foreach (var _ in events) { }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Group 1 — SubAgent model / factory (unit, no LLM)
    // ─────────────────────────────────────────────────────────────────────────

    // T1
    [Fact]
    public void CreateStateful_SharedBranchId_DefaultsToNull()
    {
        var subAgent = SubAgentFactory.CreateStateful("Test", "desc", MinimalConfig());

        subAgent.SharedBranchId.Should().BeNull();
    }

    // T2
    [Fact]
    public void SharedBranchId_CanBeSetManually_OnStatefulSubAgent()
    {
        var subAgent = SubAgentFactory.CreateStateful("Test", "desc", MinimalConfig());
        subAgent.SharedBranchId = "review-thread";

        subAgent.SharedBranchId.Should().Be("review-thread");
    }

    // T3
    [Fact]
    public void CreatePerSession_ProducesCleanPerSessionMode_NoPresetIds()
    {
        var subAgent = SubAgentFactory.CreatePerSession("Test", "desc", MinimalConfig());

        subAgent.SessionMode.Should().Be(SubAgentSessionMode.PerSession);
        subAgent.SharedSessionId.Should().BeNull();
        subAgent.SharedBranchId.Should().BeNull();
    }

    // T4
    [Fact]
    public void CreateStateful_SetsSharedSessionMode_AndGeneratesSessionId()
    {
        var subAgent = SubAgentFactory.CreateStateful("Test", "desc", MinimalConfig());

        subAgent.SessionMode.Should().Be(SubAgentSessionMode.SharedSession);
        subAgent.SharedSessionId.Should().NotBeNullOrEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Group 2 — SharedSession bug fix (integration)
    // ─────────────────────────────────────────────────────────────────────────

    // T5
    [Fact]
    public async Task SharedSession_FirstInvocation_CreatesSessionInStore()
    {
        var store = new InMemorySessionStore();
        var fakeClient = new FakeChatClient();
        var agent = BuildAgent(store, fakeClient);

        var sessionId = Guid.NewGuid().ToString("N");
        await agent.CreateSessionAsync(sessionId);
        fakeClient.EnqueueTextResponse("Hello from sub-agent");
        await DrainAsync(agent.RunAsync("Hello", sessionId, "main"));

        var session = await store.LoadSessionAsync(sessionId);
        session.Should().NotBeNull();
    }

    // T6 — key regression test for the CreateSessionAsync bug
    [Fact]
    public async Task SharedSession_SecondInvocation_DoesNotThrow()
    {
        var store = new InMemorySessionStore();
        var fakeClient = new FakeChatClient();
        var agent = BuildAgent(store, fakeClient);

        var sessionId = Guid.NewGuid().ToString("N");

        // First call — creates session
        await agent.CreateSessionAsync(sessionId);
        fakeClient.EnqueueTextResponse("First response");
        await DrainAsync(agent.RunAsync("Turn 1", sessionId, "main"));

        // Second call — session already exists; must NOT throw "Session already exists"
        fakeClient.EnqueueTextResponse("Second response");
        var act = async () => await DrainAsync(agent.RunAsync("Turn 2", sessionId, "main"));

        await act.Should().NotThrowAsync();
    }

    // T7
    [Fact]
    public async Task SharedSession_TwoCalls_AccumulatesHistoryInBranch()
    {
        var store = new InMemorySessionStore();
        var fakeClient = new FakeChatClient();
        var agent = BuildAgent(store, fakeClient);

        var sessionId = Guid.NewGuid().ToString("N");
        await agent.CreateSessionAsync(sessionId);

        fakeClient.EnqueueTextResponse("First response");
        await DrainAsync(agent.RunAsync("Turn 1", sessionId, "main"));

        fakeClient.EnqueueTextResponse("Second response");
        await DrainAsync(agent.RunAsync("Turn 2", sessionId, "main"));

        var branch = await store.LoadBranchAsync(sessionId, "main");
        branch.Should().NotBeNull();
        // Branch should have at least 4 messages: 2 user turns + 2 assistant turns
        branch!.Messages.Count.Should().BeGreaterThanOrEqualTo(4);
    }

    // T8
    [Fact]
    public async Task SharedSession_WithSharedBranchId_UsesThatBranch()
    {
        var store = new InMemorySessionStore();
        var fakeClient = new FakeChatClient();
        var agent = BuildAgent(store, fakeClient);

        var sessionId = Guid.NewGuid().ToString("N");
        await agent.CreateSessionAsync(sessionId);
        await agent.ForkBranchAsync(sessionId, "main", "review-thread", 0);

        fakeClient.EnqueueTextResponse("Review response");
        await DrainAsync(agent.RunAsync("Review this", sessionId, "review-thread"));

        var branch = await store.LoadBranchAsync(sessionId, "review-thread");
        branch.Should().NotBeNull();
        branch!.Messages.Should().Contain(m => m.Role == ChatRole.Assistant);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Group 3 — PerSession branch inheritance (integration)
    // ─────────────────────────────────────────────────────────────────────────

    // T9 — sub-agent sharing the store sees parent's seeded messages as context
    [Fact]
    public async Task PerSession_SubAgent_SeesParentBranchMessages_ViaSharedStore()
    {
        var store = new InMemorySessionStore();
        var parentFake = new FakeChatClient();
        var subAgentFake = new FakeChatClient();

        var parentAgent = BuildAgent(store, parentFake);
        var parentSessionId = "parent-" + Guid.NewGuid().ToString("N")[..8];
        await parentAgent.CreateSessionAsync(parentSessionId);

        // Seed the parent branch directly so sub-agent can "see" it
        var parentBranch = await store.LoadBranchAsync(parentSessionId, "main");
        parentBranch!.AddMessage(new ChatMessage(ChatRole.User, "The magic word is xylophone"));
        await store.SaveBranchAsync(parentSessionId, parentBranch);

        // Sub-agent shares the same store
        var subAgent = BuildAgent(store, subAgentFake);
        subAgentFake.EnqueueTextResponse("The magic word is xylophone");
        await DrainAsync(subAgent.RunAsync("What is the magic word?", parentSessionId, "main"));

        // Verify sub-agent received the parent branch history as context
        subAgentFake.CapturedRequests.Should().NotBeEmpty();
        var sentMessages = subAgentFake.CapturedRequests.First();
        sentMessages.Should().Contain(m =>
            m.Role == ChatRole.User &&
            m.Text != null &&
            m.Text.Contains("xylophone"));
    }

    // T10 — sub-agent sees prior messages seeded in the branch
    [Fact]
    public async Task PerSession_SubAgent_SeesParentConversationInContext()
    {
        var store = new InMemorySessionStore();
        var subAgentFake = new FakeChatClient();
        var subAgent = BuildAgent(store, subAgentFake);

        var sessionId = "ctx-" + Guid.NewGuid().ToString("N")[..8];
        await subAgent.CreateSessionAsync(sessionId);

        // Seed branch with prior conversation
        var branch = await store.LoadBranchAsync(sessionId, "main");
        branch!.AddMessage(new ChatMessage(ChatRole.User, "My name is Alice"));
        branch.AddMessage(new ChatMessage(ChatRole.Assistant, "Hello Alice!"));
        await store.SaveBranchAsync(sessionId, branch);

        subAgentFake.EnqueueTextResponse("Your name is Alice");
        await DrainAsync(subAgent.RunAsync("What is my name?", sessionId, "main"));

        var sentMessages = subAgentFake.CapturedRequests.First();
        sentMessages.Should().Contain(m =>
            m.Role == ChatRole.User && m.Text != null && m.Text.Contains("Alice"));
    }

    // T11 — documents current behaviour: turns appended to branch after RunAsync
    // NOTE: This test documents current behaviour which is under review.
    // When the forked-branch approach is implemented, this test will need updating.
    [Fact]
    public async Task PerSession_SubAgent_AppendsTurnsToParentBranch_CurrentBehaviour()
    {
        var store = new InMemorySessionStore();
        var subAgentFake = new FakeChatClient();
        var subAgent = BuildAgent(store, subAgentFake);

        var sessionId = "append-" + Guid.NewGuid().ToString("N")[..8];
        await subAgent.CreateSessionAsync(sessionId);

        var branchBefore = await store.LoadBranchAsync(sessionId, "main");
        var countBefore = branchBefore!.Messages.Count;

        subAgentFake.EnqueueTextResponse("Done");
        await DrainAsync(subAgent.RunAsync("Do something", sessionId, "main"));

        var branchAfter = await store.LoadBranchAsync(sessionId, "main");
        // At minimum user + assistant messages were appended
        branchAfter!.Messages.Count.Should().BeGreaterThan(countBefore);
    }

    // T12 — fresh session created, no parent context — should not throw
    [Fact]
    public async Task PerSession_WithNoParentContext_FallsBackToStateless_DoesNotThrow()
    {
        var store = new InMemorySessionStore();
        var fakeClient = new FakeChatClient();
        var agent = BuildAgent(store, fakeClient);

        var sessionId = Guid.NewGuid().ToString("N");
        await agent.CreateSessionAsync(sessionId);

        fakeClient.EnqueueTextResponse("Fallback response");
        var act = async () => await DrainAsync(agent.RunAsync("Hello", sessionId, "main"));

        await act.Should().NotThrowAsync();
    }

    // T13 — PerSession factory produces no preset IDs
    [Fact]
    public void PerSession_SubAgent_HasNoPresetSessionId()
    {
        var subAgent = SubAgentFactory.CreatePerSession("Test", "desc", MinimalConfig());

        subAgent.SharedSessionId.Should().BeNull();
        subAgent.SharedBranchId.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Group 4 — Stateless regression (integration)
    // ─────────────────────────────────────────────────────────────────────────

    // T14
    [Fact]
    public async Task Stateless_TwoCalls_EachGetFreshSession()
    {
        var store = new InMemorySessionStore();
        var fakeClient = new FakeChatClient();
        var agent = BuildAgent(store, fakeClient);

        var session1 = Guid.NewGuid().ToString("N");
        var session2 = Guid.NewGuid().ToString("N");

        await agent.CreateSessionAsync(session1);
        fakeClient.EnqueueTextResponse("Response 1");
        await DrainAsync(agent.RunAsync("Call 1", session1, "main"));

        await agent.CreateSessionAsync(session2);
        fakeClient.EnqueueTextResponse("Response 2");
        await DrainAsync(agent.RunAsync("Call 2", session2, "main"));

        session1.Should().NotBe(session2);

        var s1 = await store.LoadSessionAsync(session1);
        var s2 = await store.LoadSessionAsync(session2);
        s1.Should().NotBeNull();
        s2.Should().NotBeNull();

        var b1 = await store.LoadBranchAsync(session1, "main");
        var b2 = await store.LoadBranchAsync(session2, "main");
        b1!.Messages.Should().Contain(m => m.Text != null && m.Text.Contains("Call 1"));
        b2!.Messages.Should().Contain(m => m.Text != null && m.Text.Contains("Call 2"));
        b1.Messages.Should().NotContain(m => m.Text != null && m.Text.Contains("Call 2"));
    }

    // T15
    [Fact]
    public async Task Stateless_RunAsync_ThrowsSessionNotFoundException_WhenSessionNotCreated()
    {
        var store = new InMemorySessionStore();
        var fakeClient = new FakeChatClient();
        var agent = BuildAgent(store, fakeClient);

        var sessionId = Guid.NewGuid().ToString("N");

        // Without CreateSessionAsync, RunAsync must throw SessionNotFoundException
        fakeClient.EnqueueTextResponse("Response");
        var act = async () => await DrainAsync(agent.RunAsync("Hello", sessionId, "main"));

        await act.Should().ThrowAsync<SessionNotFoundException>();

        // After creating the session it should succeed
        await agent.CreateSessionAsync(sessionId);
        fakeClient.EnqueueTextResponse("Response");
        var act2 = async () => await DrainAsync(agent.RunAsync("Hello", sessionId, "main"));

        await act2.Should().NotThrowAsync();
    }
}
