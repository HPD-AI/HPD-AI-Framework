using FluentAssertions;
using HPD.Agent.TUI;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Markdown;
using HPD.Agent.TUI.Observability;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.Events;
using HPD.TUI.Components;
using HPD.TUI.Observability;
using HPD.TUI.Rendering;
using HPD.TUI.Views;
using HPD.TUI.Markdown;

namespace HPD.Agent.TUI.Tests;

public sealed class AgentTuiPerformanceDiagnosticsTests
{
    [Fact]
    public void MarkdownPerformanceEventIsStructuredAndNeverFormatsSource()
    {
        var evt = new MarkdownProjectionMeasured(
            "agent", "message", MarkdownStreamKind.Assistant, MarkdownMessageState.Completed,
            MarkdownInvalidationKind.Finalized, MarkdownDegradationReason.None,
            new MarkdownStreamDiagnosticsSnapshot(12, 3, 2, 1, TimeSpan.FromMilliseconds(1), 0, 1, 0, 0, TimeSpan.FromMilliseconds(2)),
            new MarkdownProjectionDiagnosticsSnapshot(1, TimeSpan.FromMilliseconds(3), 2, 2, 1, 0, 1, 0, 0));

        var summary = evt.FormatSummary();

        summary.Should().Contain("parses=1").And.Contain("layouts=1");
        summary.Should().NotContain("secret source");
    }

    [Fact]
    public void TranscriptView_WhenSinkIsConfigured_PublishesDiagnosticEvent()
    {
        var model = new TranscriptModel();
        model.AddFinal(Row("latest"));
        var sink = new RecordingSink();
        var scope = new AgentTuiRuntimeScope("agent-1", "session-1", "thread-1");

        var view = new TranscriptView(
            model,
            DefaultTranscriptRenderers(),
            height: 4,
            scope,
            sink);

        TuiCapture.RenderToString(view, width: 80, height: 6, trimTrailingBlankLines: true);

        var evt = sink.Events.Should().ContainSingle()
            .Which.Should().BeOfType<TranscriptViewRendered>().Subject;
        evt.Kind.Should().Be(EventKind.Diagnostic);
        evt.Channel.Should().Be(EventChannel.Streaming);
        evt.ThreadSequenceNumber.Should().Be(0);
        evt.AgentId.Should().Be("agent-1");
        evt.SessionId.Should().Be("session-1");
        evt.ThreadId.Should().Be("thread-1");
        evt.RowsRendered.Should().BeGreaterThan(0);
        evt.RowsCaptured.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DefaultShellLayout_UsesPerformanceSinkFromState()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .Build();
        var scope = new AgentTuiRuntimeScope("agent-1", "session-1", "thread-1");
        var shell = new ChatShellModel(scope);
        shell.Transcript.AddFinal(Row("from shell"));
        var state = new AgentTuiStateBag();
        var sink = new RecordingSink();
        AgentTuiPerformanceDiagnostics.SetSink(state, sink);

        var view = registry.ShellLayout.Create(new AgentTuiShellLayoutContext(
            shell,
            PromptView.Create("Ask HPD..."),
            registry,
            registry.ShellChrome,
            state));

        TuiCapture.RenderToString(view, width: 100, height: 24, trimTrailingBlankLines: true);

        sink.Events.OfType<TranscriptViewRendered>().Should().ContainSingle()
            .Which.ThreadId.Should().Be("thread-1");
    }

    [Fact]
    public void SetSink_WithEventPublisher_EmitsThroughHpdEventsPublisher()
    {
        var state = new AgentTuiStateBag();
        var publisher = new RecordingPublisher();
        AgentTuiPerformanceDiagnostics.SetSink(state, publisher);

        AgentTuiPerformanceDiagnostics.TryGetSink(state, out var sink).Should().BeTrue();
        sink.Publish(new TranscriptViewRendered(
            AgentId: "agent-1",
            EntriesVisited: 1,
            RowsCaptured: 1,
            RowsRendered: 1,
            CacheHits: 0,
            CacheMisses: 1,
            Duration: TimeSpan.FromMilliseconds(1)));

        publisher.Events.Should().ContainSingle()
            .Which.Should().BeOfType<TranscriptViewRendered>()
            .Which.Kind.Should().Be(EventKind.Diagnostic);
    }

    [Fact]
    public void ConfigureFromEnvironment_WhenDisabled_DoesNotInstallSink()
    {
        var state = new AgentTuiStateBag();

        var configured = AgentTuiPerformanceDiagnostics.ConfigureFromEnvironment(
            state,
            _ => null,
            new StringWriter());

        configured.Should().BeFalse();
        AgentTuiPerformanceDiagnostics.TryGetSink(state, out _).Should().BeFalse();
    }

    [Fact]
    public void ConfigureFromEnvironment_WhenEnabled_WritesConciseSummaries()
    {
        var state = new AgentTuiStateBag();
        var writer = new StringWriter();

        var configured = AgentTuiPerformanceDiagnostics.ConfigureFromEnvironment(
            state,
            name => name == AgentTuiPerformanceDiagnostics.EnvironmentVariableName ? "1" : null,
            writer);

        configured.Should().BeTrue();
        AgentTuiPerformanceDiagnostics.TryGetSink(state, out var sink).Should().BeTrue();
        sink.Publish(new TranscriptViewRendered(
            AgentId: "agent-1",
            EntriesVisited: 3,
            RowsCaptured: 2,
            RowsRendered: 2,
            CacheHits: 1,
            CacheMisses: 2,
            Duration: TimeSpan.FromMilliseconds(12.345)));

        writer.ToString().Should().Contain("transcript render 12.345ms rows=2 captured=2 visited=3 cache=1/2");
    }

    [Fact]
    public void HpdAgentTuiApp_RebuildShell_ConfiguresEnvironmentSinkBeforeShellView()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .Build();
        var state = new AgentTuiStateBag();
        var writer = new StringWriter();
        AgentTuiPerformanceDiagnostics.ConfigureFromEnvironment(
            state,
            name => name == AgentTuiPerformanceDiagnostics.EnvironmentVariableName ? "true" : null,
            writer);
        var scope = new AgentTuiRuntimeScope("agent-1", "session-1", "thread-1");
        var shell = new ChatShellModel(scope);
        shell.Transcript.AddFinal(Row("from shell"));

        var view = registry.ShellLayout.Create(new AgentTuiShellLayoutContext(
            shell,
            PromptView.Create("Ask HPD..."),
            registry,
            registry.ShellChrome,
            state));

        TuiCapture.RenderToString(view, width: 100, height: 24, trimTrailingBlankLines: true);

        writer.ToString().Should().Contain("transcript render");
    }

    private static AgentTuiTranscriptRendererRegistry DefaultTranscriptRenderers()
        => new HpdAgentTuiBuilder()
            .AddDefaultTranscriptRenderers()
            .Build()
            .TranscriptRenderers;

    private static TranscriptEntry Row(string text)
        => new(
            Id: $"entry-{Guid.NewGuid():N}",
            EntryKey: null,
            Cell: new UserMessageCell(new Text(text)),
            Metadata: new TranscriptEntryMetadata());

    private sealed class RecordingSink : IHpdTuiPerformanceEventSink
    {
        public List<Event> Events { get; } = [];

        public void Publish(Event evt)
        {
            Events.Add(evt);
        }
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<Event> Events { get; } = [];

        public void Emit(Event evt)
        {
            Events.Add(evt);
        }

        public ValueTask EmitAsync(Event evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return ValueTask.CompletedTask;
        }
    }
}
