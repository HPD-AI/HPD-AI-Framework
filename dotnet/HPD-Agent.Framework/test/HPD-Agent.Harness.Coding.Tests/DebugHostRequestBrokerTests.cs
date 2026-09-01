using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent;
using HPD.Agent.Serialization;
using HPD.Events;
using HPD.Events.Core;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugHostRequestBrokerTests
{
    [Fact]
    public async Task Debug_event_publisher_separates_durable_commit_from_live_only_emission()
    {
        using var events = new EventCoordinator();
        var threadEvents = new RecordingThreadPublisher(events);
        var publisher = new DebugEventPublisher(events, threadEvents);
        var observed = new List<AgentEvent>();
        using var durableSubscription = events.Subscribe<DebugSessionStoppedEvent>(value =>
        {
            observed.Add(value);
            return ValueTask.CompletedTask;
        });
        using var liveSubscription = events.Subscribe<DebugThreadChangedEvent>(value =>
        {
            observed.Add(value);
            return ValueTask.CompletedTask;
        });
        var scope = new DebugEventScope("trace", "session", "thread");
        var durable = new DebugSessionStoppedEvent
        {
            DebugTreeId = "tree", DebugSessionId = "debug", AdapterId = "adapter", Reason = "pause"
        };
        var live = new DebugThreadChangedEvent
        {
            DebugTreeId = "tree", DebugSessionId = "debug", AdapterId = "adapter",
            Reason = "started", AdapterThreadId = 1
        };

        await publisher.PublishDurableAsync(scope, durable);
        await publisher.PublishLiveAsync(scope, live);
        await WaitUntilAsync(() => observed.Count == 2);

        threadEvents.CommitCount.Should().Be(1);
        observed.Should().HaveCount(2);
        observed.Should().OnlyContain(x => x.SessionId == "session" && x.ThreadId == "thread" && x.TraceId == "trace");
    }

    [Fact]
    public async Task Bound_debug_event_publisher_owns_immutable_tree_scope()
    {
        using var events = new EventCoordinator();
        var threadEvents = new RecordingThreadPublisher(events);
        IDebugEventPublisher publisher = new DebugEventPublisher(events, threadEvents);
        var bound = publisher.Bind(new DebugEventScope("trace", "session", "thread"));
        DebugThreadChangedEvent? observed = null;
        using var subscription = events.Subscribe<DebugThreadChangedEvent>(value =>
        {
            observed = value;
            return ValueTask.CompletedTask;
        });

        await bound.PublishLiveAsync(new DebugThreadChangedEvent
        {
            SessionId = "attacker-session",
            ThreadId = "attacker-thread",
            TraceId = "attacker-trace",
            DebugTreeId = "tree",
            DebugSessionId = "debug",
            AdapterId = "adapter",
            Reason = "started",
            AdapterThreadId = 1
        });
        await WaitUntilAsync(() => observed is not null);

        bound.Scope.Should().Be(new DebugEventScope("trace", "session", "thread"));
        observed!.SessionId.Should().Be("session");
        observed.ThreadId.Should().Be("thread");
        observed.TraceId.Should().Be("trace");
    }

    [Fact]
    public async Task Run_in_terminal_preserves_verbatim_arguments_and_null_environment_delta()
    {
        using var events = new EventCoordinator();
        var publisher = new RecordingThreadPublisher(events);
        var broker = new DebugHostRequestBroker(events, publisher, TimeSpan.FromSeconds(2));
        DebugRunInTerminalRequestEvent? observed = null;
        using var subscription = events.Subscribe<DebugRunInTerminalRequestEvent>(async request =>
        {
            observed = request;
            var result = await broker.RespondAsync(new DebugRunInTerminalResponseEvent
            {
                DebugRequestId = request.DebugRequestId,
                ProcessId = 41,
                ShellProcessId = 42,
                SessionId = request.SessionId,
                ThreadId = request.ThreadId
            });
            result.Accepted.Should().BeTrue();
        });

        var response = await broker.RequestRunInTerminalAsync(
            new(null, "session", "thread"), "tree", "debug-session", "external", "fixture",
            "/workspace", ["tool path", "a b", "'literal'"],
            new Dictionary<string, string?> { ["SET"] = "value", ["DELETE"] = null },
            argsCanBeInterpretedByShell: false, CancellationToken.None);

        response.ProcessId.Should().Be(41);
        response.ShellProcessId.Should().Be(42);
        publisher.ResponseCommitted.Should().BeTrue("the accepted response must commit before the waiter resumes");
        observed.Should().NotBeNull();
        observed!.Arguments.Should().Equal("tool path", "a b", "'literal'");
        observed.EnvironmentDelta.Should().ContainKey("DELETE").WhoseValue.Should().BeNull();
        observed.WorkingDirectory.Should().Be("/workspace");
    }

    private sealed class RecordingThreadPublisher(IEventCoordinator events) : IAgentEventPublisher
    {
        public AgentEventCodec EventCodec => CodingEventTestCodec.Codec;
        public bool ResponseCommitted { get; private set; }
        public int CommitCount { get; private set; }
        public ValueTask<ThreadEventHead?> GetHeadAsync(ThreadKey thread, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ThreadEventHead?>(null);
        public ValueTask<AgentEvent> PublishAsync(ThreadKey thread, AgentEvent value, CancellationToken cancellationToken = default)
            => CommitAndPublishAsync(thread, value, cancellationToken);
        public async ValueTask<AgentEvent> PublishLiveAsync(AgentEvent value, CancellationToken cancellationToken = default)
        {
            await events.EmitAsync(value, cancellationToken);
            return value;
        }
        public async ValueTask<AgentEvent> CommitAndPublishAsync(ThreadKey thread, AgentEvent proposedEvent, CancellationToken cancellationToken = default)
        {
            CommitCount++;
            if (proposedEvent is DebugRunInTerminalResponseEvent) ResponseCommitted = true;
            await events.EmitAsync(proposedEvent, cancellationToken);
            return proposedEvent;
        }
        public ValueTask<ThreadEventAppendResult> CommitAndPublishAsync(ThreadKey thread, IReadOnlyList<AgentEvent> proposedEvents, ThreadAppendCondition condition = default, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public ValueTask<AgentEvent> StageAndPublishDeltaAsync(ThreadKey thread, AgentEvent delta, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public ValueTask<ThreadEventAppendResult> FinalizeAndPublishDeltasAsync(ThreadKey thread, AgentEvent messageEnd, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public ValueTask<ThreadJournalReplaceResult> ReplaceAndPublishAsync(ThreadKey thread, IReadOnlyList<AgentEvent> replacementEvents, ThreadJournalCursor expectedCursor, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(10);
        }
    }
}
