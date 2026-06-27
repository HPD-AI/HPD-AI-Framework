using FluentAssertions;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Components;
using HPD.TUI.Models;

namespace HPD.Agent.TUI.Tests;

public sealed class AgentTuiSessionStateTests
{
    [Fact]
    public async Task ApplyEventAsync_StreamsAssistantIntoSingleKeyedRow()
    {
        var state = CreateState();

        await state.ApplyEventAsync(new TextMessageStartEvent("m1", "assistant"));
        await state.ApplyEventAsync(new TextDeltaEvent("hello ", "m1"));
        await state.ApplyEventAsync(new TextDeltaEvent("world", "m1"));

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle(row => row.EntryKey == "assistant:m1");
        rows.Count(row => row.Cell is AssistantMessageCell).Should().Be(1);
    }

    [Fact]
    public async Task ApplyEventAsync_UpdatesToolRowByCallId()
    {
        var state = CreateState();

        await state.ApplyEventAsync(new ToolCallStartEvent("call-1", "sample.inspect", "m1"));
        await state.ApplyEventAsync(new ToolCallArgsEvent("call-1", """{"path":"Program.cs"}"""));
        await state.ApplyEventAsync(new ToolCallEndEvent("call-1", "m1", "sample.inspect", """{"path":"Program.cs"}"""));

        var rows = ReadRows(state.Shell.Transcript);
        rows.Should().ContainSingle(row => row.EntryKey == "tool:call-1");
    }

    [Fact]
    public async Task ApplyEventAsync_TracksRunStatusInFooterAndActivities()
    {
        var state = CreateState();

        await state.ApplyEventAsync(new ThreadRunStartedEvent("run-12345678", "agent", DateTimeOffset.UtcNow));
        state.Shell.FooterText.Should().Contain("running");

        await state.ApplyEventAsync(new ThreadRunCompletedEvent("run-12345678", "agent", Cancelled: false));

        state.Shell.FooterText.Should().Contain("idle");
        state.Shell.Activities.Activities.Should().Contain(activity => activity.State == HPD.TUI.Models.ActivityState.Completed);
    }

    [Fact]
    public async Task ApplyEventAsync_IgnoresUnhandledEvents()
    {
        var state = CreateState();

        await state.ApplyEventAsync(new InterruptionHandledEvent("flow", "stopped", InterruptionSource.User));

        state.Shell.Transcript.Count.Should().Be(0);
    }

    [Fact]
    public async Task AddAgentTuiDefaults_DoesNotHandleHpdCoreEvents()
    {
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            new HpdAgentTuiBuilder().AddAgentTuiDefaults().Build());

        await state.ApplyEventAsync(new TextMessageStartEvent("m1", "assistant"));
        await state.ApplyEventAsync(new TextDeltaEvent("hello", "m1"));

        state.Shell.Transcript.Count.Should().Be(0);
    }

    [Fact]
    public async Task ApplyEventAsync_ContinuesAfterHandlerFailure()
    {
        var afterFailure = new CountingEventHandler();
        var state = new AgentTuiSessionState(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            new HpdAgentTuiBuilder()
                .AddEventHandler("sample.failing", new FailingEventHandler())
                .AddEventHandler("sample.after", afterFailure)
                .Build());

        await state.ApplyEventAsync(new TextDeltaEvent("hello", "m1"));

        afterFailure.Count.Should().Be(1);
        ReadRows(state.Shell.Transcript)
            .Any(row => row.Cell is NoticeCell { Severity: TranscriptSeverity.Error })
            .Should()
            .BeTrue();
    }

    private static AgentTuiSessionState CreateState()
        => new(
            new AgentTuiRuntimeScope("agent", "session", "main"),
            new HpdAgentTuiBuilder()
                .AddAgentTuiDefaults()
                .AddEventHandler("test.text", new TestTextMessageHandler())
                .AddEventHandler("test.tool", new TestToolHandler())
                .AddEventHandler("test.run-status", new TestRunStatusHandler())
                .Build());

    private static List<TranscriptEntry> ReadRows(TranscriptModel model)
    {
        return model.Snapshot().Entries.ToList();
    }

    private sealed class TestTextMessageHandler : IAgentTuiEventHandler
    {
        private readonly Dictionary<string, string> _buffers = new(StringComparer.Ordinal);

        public bool CanHandle(AgentEvent evt)
            => evt is TextMessageStartEvent or TextDeltaEvent or TextMessageEndEvent;

        public ValueTask HandleAsync(
            AgentEvent evt,
            AgentTuiEventContext context,
            CancellationToken cancellationToken)
        {
            switch (evt)
            {
                case TextMessageStartEvent start:
                    _buffers[start.MessageId] = "";
                    UpsertAssistantRow(context, start.MessageId, "");
                    break;

                case TextDeltaEvent delta:
                    _buffers.TryGetValue(delta.MessageId, out var current);
                    current += delta.Text;
                    _buffers[delta.MessageId] = current;
                    UpsertAssistantRow(context, delta.MessageId, current);
                    break;

                case TextMessageEndEvent end when _buffers.TryGetValue(end.MessageId, out var final):
                    FinalizeAssistantRow(context, end.MessageId, final);
                    break;
            }

            return ValueTask.CompletedTask;
        }

        private static TranscriptEntry AssistantEntry(
            AgentTuiEventContext context,
            string messageId,
            string markdown)
            => new(
                Id: $"assistant-{messageId}",
                EntryKey: $"assistant:{messageId}",
                new AssistantMessageCell("assistant", new Markdown(string.IsNullOrWhiteSpace(markdown) ? "_thinking..._" : markdown)),
                new TranscriptEntryMetadata(
                    AgentId: context.Scope.AgentId,
                    AgentName: "assistant",
                    ParentAgentId: null,
                    AgentChain: ["assistant"],
                    AgentDepth: 0));

        private static void UpsertAssistantRow(
            AgentTuiEventContext context,
            string messageId,
            string markdown)
            => context.Shell.Transcript.UpsertLive(AssistantEntry(context, messageId, markdown));

        private static void FinalizeAssistantRow(
            AgentTuiEventContext context,
            string messageId,
            string markdown)
            => context.Shell.Transcript.FinalizeLive($"assistant:{messageId}", AssistantEntry(context, messageId, markdown));
    }

    private sealed class TestToolHandler : IAgentTuiEventHandler
    {
        public bool CanHandle(AgentEvent evt)
            => evt is ToolCallStartEvent or ToolCallArgsEvent or ToolCallEndEvent;

        public ValueTask HandleAsync(
            AgentEvent evt,
            AgentTuiEventContext context,
            CancellationToken cancellationToken)
        {
            var callId = evt switch
            {
                ToolCallStartEvent start => start.CallId,
                ToolCallArgsEvent args => args.CallId,
                ToolCallEndEvent end => end.CallId,
                _ => null
            };

            if (callId is not null)
            {
                context.Shell.Transcript.UpsertLive(new TranscriptEntry(
                    Id: $"tool-{callId}",
                    EntryKey: $"tool:{callId}",
                    new ToolCallCell("tool", TranscriptRunState.Running, Summary: new Text(callId)),
                    new TranscriptEntryMetadata(
                        AgentId: context.Scope.AgentId,
                        AgentName: "tool",
                        ParentAgentId: null,
                        AgentChain: ["assistant", "tool"],
                        AgentDepth: 1)));
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestRunStatusHandler : IAgentTuiEventHandler
    {
        public bool CanHandle(AgentEvent evt)
            => evt is ThreadRunStartedEvent or ThreadRunCompletedEvent;

        public ValueTask HandleAsync(
            AgentEvent evt,
            AgentTuiEventContext context,
            CancellationToken cancellationToken)
        {
            switch (evt)
            {
                case ThreadRunStartedEvent:
                    context.Shell.Activities.Add(new ActivityModel("run")
                    {
                        State = ActivityState.Running,
                        Severity = ActivitySeverity.Info
                    });
                    context.Shell.FooterText = "state: running";
                    break;

                case ThreadRunCompletedEvent:
                    foreach (var activity in context.Shell.Activities.Activities.Where(activity => activity.State == ActivityState.Running))
                    {
                        activity.State = ActivityState.Completed;
                        activity.Severity = ActivitySeverity.Success;
                    }

                    context.Shell.FooterText = "state: idle";
                    break;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingEventHandler : AgentTuiEventHandler<TextDeltaEvent>
    {
        public override ValueTask HandleAsync(
            TextDeltaEvent evt,
            AgentTuiEventContext context,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("failure");
    }

    private sealed class CountingEventHandler : AgentTuiEventHandler<TextDeltaEvent>
    {
        public int Count { get; private set; }

        public override ValueTask HandleAsync(
            TextDeltaEvent evt,
            AgentTuiEventContext context,
            CancellationToken cancellationToken)
        {
            Count++;
            return ValueTask.CompletedTask;
        }
    }
}
