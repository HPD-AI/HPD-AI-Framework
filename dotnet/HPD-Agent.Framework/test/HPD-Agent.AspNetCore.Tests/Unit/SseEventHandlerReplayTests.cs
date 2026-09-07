using System.Text;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AspNetCore.Streaming;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Events;
using HPD.Events.Core;
using Microsoft.AspNetCore.Http;

namespace HPD.Agent.AspNetCore.Tests.Unit;

public sealed class SseEventHandlerReplayTests
{
    [Fact]
    public async Task StreamEventsAsync_ReplaysOnlyCommittedEventsAfterTheCursor()
    {
        var store = new InMemorySessionStore(HPD.Agent.AspNetCore.Tests.TestEventApplication.Codec);
        await store.AppendThreadEventAsync(
            "session-1",
            "main",
            new TextDeltaEvent("first", "message-1"));
        await store.AppendThreadEventAsync(
            "session-1",
            "main",
            new TextDeltaEvent("second", "message-1"));
        var committed = await store.CollectThreadEventsAsync("session-1", "main");
        var firstSequence = committed!
            .OfType<TextDeltaEvent>()
            .Single(evt => evt.Text == "first")
            .ThreadSequenceNumber;
        var secondSequence = committed
            .OfType<TextDeltaEvent>()
            .Single(evt => evt.Text == "second")
            .ThreadSequenceNumber;
        TestEventApplication.Codec.Serialize(committed.Single(evt => evt.ThreadSequenceNumber == secondSequence))
            .Should().Contain("TEXT_DELTA");
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString($"?after=1:{firstSequence}");
        context.Response.Body = new CapturingStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var coordinator = new EventCoordinator();
        await using var observation = new ThreadEventObservationLease(
            store,
            new ThreadKey("session-1", "main"),
            AgentEventHierarchy.ExactThread,
            AgentEventRoutes.CreateDeliveryInbox(coordinator, new ThreadKey("session-1", "main"), AgentEventHierarchy.ExactThread));
        var streamTask = SseEventHandler.StreamEventsAsync(context, observation, timeout.Token);
        while (context.Response.Body.Length < 20)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        await timeout.CancelAsync();
        await streamTask;

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        body.Should().Contain($"id: 1:{secondSequence}\n");
        body.Should().Contain("second");
        body.Should().NotContain($"id: 1:{firstSequence}\n");
        body.Should().NotContain("first");
    }

    [Fact]
    public async Task StreamEventsAsync_DeliversStatelessRuntimeEventsWithoutAJournalCursor()
    {
        var store = await CreateStoreWithThreadAsync();
        var coordinator = new EventCoordinator();
        await using var observation = new ThreadEventObservationLease(
            store,
            new ThreadKey("session-1", "main"),
            AgentEventHierarchy.ExactThread,
            AgentEventRoutes.CreateDeliveryInbox(coordinator, new ThreadKey("session-1", "main"), AgentEventHierarchy.ExactThread));
        var context = CreateContext(after: "1:1");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var streamTask = SseEventHandler.StreamEventsAsync(context, observation, timeout.Token);
        var live = new TextDeltaEvent("runtime-only", "message-live")
        {
            SessionId = "session-1",
            ThreadId = "main"
        };

        await coordinator.EmitAsync(live, AgentEventRoutes.Create(coordinator, live), timeout.Token);
        await WaitForBodyAsync(context, live.EventId, timeout.Token);
        await timeout.CancelAsync();
        await streamTask;

        var body = await ReadBodyAsync(context);
        body.Should().Contain("event: live-agent-event\n");
        body.Should().Contain(live.EventId);
        body.Should().NotContain("id: 1:0");
    }

    [Fact]
    public async Task StreamEventsAsync_DeliversBubbledDescendantEventsAsLiveEvents()
    {
        var store = await CreateStoreWithThreadAsync();
        var parent = new EventCoordinator();
        var child = (EventCoordinator)parent.CreateChild(EventChildOwnership.NewOwner);
        var root = new ThreadKey("session-1", "main");
        var childKey = new ThreadKey("session-1", "subagent/explore/invocation-1");
        AgentEventRoutes.AttachCoordinator(child, parent);
        AgentEventRoutes.RegisterChild(parent, childKey, root);
        await using var observation = new ThreadEventObservationLease(
            store,
            new ThreadKey("session-1", "main"),
            AgentEventHierarchy.ThreadAndDescendants,
            AgentEventRoutes.CreateDeliveryInbox(parent, root, AgentEventHierarchy.ThreadAndDescendants));
        var context = CreateContext(after: "1:1");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var streamTask = SseEventHandler.StreamEventsAsync(context, observation, timeout.Token);
        var childEvent = new TextDeltaEvent("child", "message-child")
        {
            SessionId = "session-1",
            ThreadId = "subagent/explore/invocation-1",
            ThreadSequenceNumber = 7
        };

        await child.EmitAsync(childEvent, AgentEventRoutes.Create(child, childEvent), timeout.Token);
        await WaitForBodyAsync(context, childEvent.EventId, timeout.Token);
        await timeout.CancelAsync();
        await streamTask;

        var body = await ReadBodyAsync(context);
        body.Should().Contain("event: live-agent-event\n");
        body.Should().Contain(childEvent.EventId);
        body.Should().NotContain("id: 1:7");
    }

    [Fact]
    public async Task StreamEventsAsync_DeliversPostBoundaryCommitsOnceFromCoordinator()
    {
        var store = await CreateStoreWithThreadAsync();
        var coordinator = new EventCoordinator();
        await using var observation = new ThreadEventObservationLease(
            store,
            new ThreadKey("session-1", "main"),
            AgentEventHierarchy.ExactThread,
            AgentEventRoutes.CreateDeliveryInbox(coordinator, new ThreadKey("session-1", "main"), AgentEventHierarchy.ExactThread));
        var context = CreateContext(after: "1:1");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var streamTask = SseEventHandler.StreamEventsAsync(context, observation, timeout.Token);
        var publisher = new AgentEventPublisher(store, coordinator);

        var committed = await publisher.CommitAndPublishAsync(
            new ThreadKey("session-1", "main"),
            new TextDeltaEvent("committed-once", "message-committed"),
            timeout.Token);
        await WaitForBodyAsync(context, committed.EventId, timeout.Token);
        await timeout.CancelAsync();
        await streamTask;

        var body = await ReadBodyAsync(context);
        body.Split(committed.EventId, StringSplitOptions.None).Should().HaveCount(2);
        body.Should().Contain($"id: 1:{committed.ThreadSequenceNumber}\n");
        body.Should().Contain("event: live-agent-event\n");
    }

    private static async Task<InMemorySessionStore> CreateStoreWithThreadAsync()
    {
        var store = new InMemorySessionStore(HPD.Agent.AspNetCore.Tests.TestEventApplication.Codec);
        await store.AppendThreadEventAsync(
            "session-1",
            "main",
            new TextDeltaEvent("seed", "message-seed"));
        return store;
    }

    private static DefaultHttpContext CreateContext(string after)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString($"?after={after}");
        context.Response.Body = new CapturingStream();
        return context;
    }

    private static async Task WaitForBodyAsync(
        DefaultHttpContext context,
        string value,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var body = await ReadBodyAsync(context);
            if (body.Contains(value, StringComparison.Ordinal))
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private static Task<string> ReadBodyAsync(DefaultHttpContext context)
    {
        var stream = Assert.IsType<CapturingStream>(context.Response.Body);
        return Task.FromResult(Encoding.UTF8.GetString(stream.Snapshot()));
    }

    private sealed class CapturingStream : MemoryStream
    {
        private readonly object _gate = new();

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_gate)
                base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            lock (_gate)
                base.Write(buffer);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                base.Write(buffer.Span);
                return ValueTask.CompletedTask;
            }
        }

        public byte[] Snapshot()
        {
            lock (_gate)
                return ToArray();
        }
    }
}
