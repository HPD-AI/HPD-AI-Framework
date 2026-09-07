using HPD.Events;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace HPD.Agent.Tests.EventRouting;

public sealed class AgentEventSubscriptionTests
{
    [Fact]
    public async Task NoKeySubscription_IncludesRuntimeOwnerAndExcludesSubagentOwner()
    {
        await using var agent = await BuildAgentAsync();
        using var runtime = (EventCoordinator)agent.EventCoordinator.CreateChild(EventChildOwnership.InheritOwner);
        using var subagent = (EventCoordinator)agent.EventCoordinator.CreateChild(EventChildOwnership.NewOwner);
        var seen = new List<string>();
        var runtimeSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = agent.Subscribe<TextDeltaEvent>(evt =>
        {
            lock (seen)
            {
                seen.Add(evt.Text);
                if (evt.Text == "runtime")
                    runtimeSeen.TrySetResult();
            }
        });

        runtime.Emit(EventFor(new ThreadKey("session", "main"), "runtime"), AgentEventRoutes.Create(agent.EventCoordinator, EventFor(new ThreadKey("session", "main"), "route")));
        subagent.Emit(EventFor(new ThreadKey("session", "child"), "subagent"), AgentEventRoutes.Create(agent.EventCoordinator, EventFor(new ThreadKey("session", "child"), "route")));

        await runtimeSeen.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(75);
        Assert.Equal(["runtime"], seen);
    }

    [Fact]
    public async Task NoKeySubscription_IncludesSameOwnerThreadlessEvent()
    {
        await using var agent = await BuildAgentAsync();
        var received = new TaskCompletionSource<TextDeltaEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = agent.Subscribe<TextDeltaEvent>(evt => received.TrySetResult(evt));
        var evt = new TextDeltaEvent("threadless", "message");

        agent.EventCoordinator.Emit(evt);

        Assert.Same(evt, await received.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Theory]
    [InlineData(AgentEventHierarchy.ExactThread, 1)]
    [InlineData(AgentEventHierarchy.DirectChildren, 2)]
    [InlineData(AgentEventHierarchy.ThreadAndDirectChildren, 3)]
    [InlineData(AgentEventHierarchy.Descendants, 3)]
    [InlineData(AgentEventHierarchy.ThreadAndDescendants, 4)]
    public async Task KeyedInbox_SelectsRelativeHierarchy(AgentEventHierarchy hierarchy, int expected)
    {
        await using var agent = await BuildAgentAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var root = new ThreadKey("session-" + suffix, "root");
        var childA = new ThreadKey(root.SessionId, "child-a");
        var childB = new ThreadKey(root.SessionId, "child-b");
        var grandchild = new ThreadKey(root.SessionId, "grandchild");
        var unrelated = new ThreadKey(root.SessionId, "unrelated");
        AgentEventRoutes.RegisterChild(agent.EventCoordinator, childA, root);
        AgentEventRoutes.RegisterChild(agent.EventCoordinator, childB, root);
        AgentEventRoutes.RegisterChild(agent.EventCoordinator, grandchild, childA);
        await using var inbox = agent.CreateEventInbox<TextDeltaEvent>(root, hierarchy);

        Emit(agent, root, "root");
        Emit(agent, childA, "child-a");
        Emit(agent, childB, "child-b");
        Emit(agent, grandchild, "grandchild");
        Emit(agent, unrelated, "unrelated");

        var deliveries = new List<TextDeltaEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (deliveries.Count < expected)
            deliveries.Add(await inbox.Reader.ReadAsync(timeout.Token));

        Assert.DoesNotContain(deliveries, delivery => delivery.ThreadId == unrelated.ThreadId);
        Assert.Equal(expected, deliveries.Count);
    }

    [Fact]
    public async Task DescendantSubscription_PreservesBranchAttributionUnderConcurrentBreadth()
    {
        await using var agent = await BuildAgentAsync();
        var session = "session-" + Guid.NewGuid().ToString("N");
        var root = new ThreadKey(session, "root");
        var left = new ThreadKey(session, "left");
        var right = new ThreadKey(session, "right");
        var leftLeaf = new ThreadKey(session, "left-leaf");
        var rightLeaf = new ThreadKey(session, "right-leaf");
        AgentEventRoutes.RegisterChild(agent.EventCoordinator, left, root);
        AgentEventRoutes.RegisterChild(agent.EventCoordinator, right, root);
        AgentEventRoutes.RegisterChild(agent.EventCoordinator, leftLeaf, left);
        AgentEventRoutes.RegisterChild(agent.EventCoordinator, rightLeaf, right);
        await using var inbox = agent.CreateEventDeliveryInbox(
            root,
            AgentEventHierarchy.Descendants,
            EventInboxOptions.Deterministic(capacity: 32));

        await Task.WhenAll(
            Task.Run(() => Emit(agent, left, "left")),
            Task.Run(() => Emit(agent, right, "right")),
            Task.Run(() => Emit(agent, leftLeaf, "left-leaf")),
            Task.Run(() => Emit(agent, rightLeaf, "right-leaf")));

        var deliveries = new List<AgentEventDelivery>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (deliveries.Count < 4)
            deliveries.Add(await inbox.Reader.ReadAsync(timeout.Token));

        Assert.Contains(deliveries, item => item.Route.Path.SequenceEqual([root, left, leftLeaf]));
        Assert.Contains(deliveries, item => item.Route.Path.SequenceEqual([root, right, rightLeaf]));
        Assert.All(deliveries, item => Assert.Equal(item.Route.Origin, item.Route.Path[^1]));
    }

    [Fact]
    public async Task KeyedSubscription_RejectsUnknownHierarchyImmediately()
    {
        await using var agent = await BuildAgentAsync();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            agent.SubscribeAny(new ThreadKey("session", "main"), (AgentEventHierarchy)99, _ => { }));
    }

    [Fact]
    public async Task DescendantSubscription_FollowsCrossSessionParentEdge()
    {
        await using var agent = await BuildAgentAsync();
        var root = new ThreadKey("parent-session-" + Guid.NewGuid().ToString("N"), "root");
        var child = new ThreadKey("isolated-child-session-" + Guid.NewGuid().ToString("N"), "child");
        AgentEventRoutes.RegisterChild(agent.EventCoordinator, child, root);
        await using var inbox = agent.CreateEventDeliveryInbox(root, AgentEventHierarchy.Descendants);

        Emit(agent, child, "cross-session");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var delivery = await inbox.Reader.ReadAsync(timeout.Token);
        Assert.Equal([root, child], delivery.Route.Path);
    }

    [Fact]
    public async Task NestedSubtree_ExcludesSiblingBranchAndPreservesEventIdentity()
    {
        await using var agent = await BuildAgentAsync();
        var session = "nested-" + Guid.NewGuid().ToString("N");
        var root = new ThreadKey(session, "root");
        var left = new ThreadKey(session, "left");
        var right = new ThreadKey(session, "right");
        var leaf = new ThreadKey(session, "leaf");
        AgentEventRoutes.RegisterChild(agent.EventCoordinator, left, root);
        AgentEventRoutes.RegisterChild(agent.EventCoordinator, right, root);
        AgentEventRoutes.RegisterChild(agent.EventCoordinator, leaf, left);
        await using var inbox = agent.CreateEventDeliveryInbox(left, AgentEventHierarchy.ThreadAndDescendants);
        var leftEvent = EventFor(left, "left");
        var leafEvent = EventFor(leaf, "leaf");

        agent.EventCoordinator.Emit(leftEvent, AgentEventRoutes.Create(agent.EventCoordinator, leftEvent));
        Emit(agent, right, "right");
        agent.EventCoordinator.Emit(leafEvent, AgentEventRoutes.Create(agent.EventCoordinator, leafEvent));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.Same(leftEvent, (await inbox.Reader.ReadAsync(timeout.Token)).Event);
        Assert.Same(leafEvent, (await inbox.Reader.ReadAsync(timeout.Token)).Event);
    }

    [Theory]
    [InlineData("parent-session", null)]
    [InlineData(null, "parent-thread")]
    [InlineData(" ", "parent-thread")]
    public void RuntimeLineage_RejectsIncompleteParentKeys(string? sessionId, string? threadId)
    {
        Assert.Throws<InvalidOperationException>(() => AgentEventRoutes.ValidateParentPair(sessionId, threadId));
    }

    [Fact]
    public async Task RuntimeLineage_IsOwnedByAgentGraph()
    {
        await using var first = await BuildAgentAsync();
        await using var second = await BuildAgentAsync();
        var child = new ThreadKey("shared-session", "child");
        var firstRoot = new ThreadKey("first-session", "root");
        var secondRoot = new ThreadKey("second-session", "root");
        AgentEventRoutes.RegisterChild(first.EventCoordinator, child, firstRoot);
        AgentEventRoutes.RegisterChild(second.EventCoordinator, child, secondRoot);
        var evt = EventFor(child, "owned");

        var firstRoute = AgentEventRoutes.Create(first.EventCoordinator, evt)!;
        var secondRoute = AgentEventRoutes.Create(second.EventCoordinator, evt)!;

        Assert.True(firstRoute.Path.SequenceEqual([firstRoot, child]));
        Assert.True(secondRoute.Path.SequenceEqual([secondRoot, child]));
    }

    [Fact]
    public async Task RuntimeLineage_ConcurrentCycleRegistrationIsAtomic()
    {
        await using var agent = await BuildAgentAsync();
        var left = new ThreadKey("cycle-session", "left");
        var right = new ThreadKey("cycle-session", "right");
        var failures = 0;

        await Task.WhenAll(
            Task.Run(() => TryRegister(left, right)),
            Task.Run(() => TryRegister(right, left)));

        Assert.Equal(1, failures);

        void TryRegister(ThreadKey child, ThreadKey parent)
        {
            try { AgentEventRoutes.RegisterChild(agent.EventCoordinator, child, parent); }
            catch (InvalidOperationException) { Interlocked.Increment(ref failures); }
        }
    }

    internal static void EmitForFfi(HPD.Agent.Agent agent, ThreadKey key, string text) => Emit(agent, key, text);

    private static void Emit(HPD.Agent.Agent agent, ThreadKey key, string text)
    {
        var evt = EventFor(key, text);
        agent.EventCoordinator.Emit(evt, AgentEventRoutes.Create(agent.EventCoordinator, evt));
    }

    private static TextDeltaEvent EventFor(ThreadKey key, string text) =>
        new(text, Guid.NewGuid().ToString("N"))
        {
            SessionId = key.SessionId,
            ThreadId = key.ThreadId
        };

    internal static Task<HPD.Agent.Agent> BuildAgentForFfiAsync() => BuildAgentAsync();

    private static Task<HPD.Agent.Agent> BuildAgentAsync() =>
        new AgentBuilder(new AgentConfig { Name = "event-routing-test" })
            .WithChatClient(new MinimalChatClient())
            .WithEventApplicationIdentity("HPD-Agent")
            .BuildAsync();

    private sealed class MinimalChatClient : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("routing-test", null, "test");
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
