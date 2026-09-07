using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.TUI.Tests;
using HPD.TUI.Views;

namespace HPD.Agent.TUI.Tests;

public sealed class NativeChatPresentationTests
{
    [Fact]
    public void ShellPublishesVisibleHistoryThenSwitchesAndCompactsThroughApplicationLifecycle()
    {
        using var terminal = new Display();
        using var app = new ManagedTerminalTuiApplication(terminal);
        var first = Install(app, "first conversation");
        app.Render();
        Assert.Contains("first conversation", terminal.Visible());
        Assert.Contains("Ask HPD", terminal.Visible());
        Assert.True(terminal.Visible().IndexOf("first conversation", StringComparison.Ordinal) <
            terminal.Visible().IndexOf("Ask HPD", StringComparison.Ordinal));
        Assert.Empty(terminal.Oracle.Scrollback);
        Assert.Equal(1, first.Transcript.CommittedCount);

        var second = Install(app, "second conversation");
        app.Render();
        Assert.DoesNotContain("first conversation", terminal.Visible());
        Assert.Contains("second conversation", terminal.Visible());
        second.Transcript.ReplaceHistoryWith(Entry("compacted summary"), CommittedHistoryMutationPolicy.ClearAndReplay);
        app.Render();
        Assert.DoesNotContain("second conversation", terminal.Visible());
        Assert.Contains("compacted summary", terminal.Visible());
        Assert.Equal(1, second.Transcript.CommittedCount);
    }

    [Fact]
    public async Task HistoricalReplayDrainsAllBatchesWithoutAnInputEvent()
    {
        using var terminal = new Display();
        using var app = new ManagedTerminalTuiApplication(terminal);
        var model = Install(app, "first");
        for (var i = 0; i < 180; i++) model.Transcript.AddFinal(Entry($"history-{i:D3}"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        app.FramePreparing = (_, _) =>
        {
            if (model.Transcript.CommittedCount == model.Transcript.Count) timeout.Cancel();
        };
        await app.RunAsync(cancellationToken: timeout.Token);
        Assert.Equal(model.Transcript.Count, model.Transcript.CommittedCount);
        var history = string.Join("\n", terminal.Oracle.Scrollback) + "\n" + terminal.Visible();
        Assert.Contains("history-000", history);
        Assert.Contains("history-179", history);
        Assert.Equal(1, history.Split("history-000", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void UncertainHistoryWriteRebuildsFromCanonicalSourceWithoutAdvancingOldFrontier()
    {
        using var terminal = new Display();
        var transport = new FailFirstPublication(terminal);
        using var app = new ManagedTerminalTuiApplication(terminal, transport,
            terminal.ManagedTerminalCapabilities, recoveryPolicy: ManagedTerminalRecoveryPolicy.ClearAndReplay);
        var model = Install(app, "recovered conversation");
        app.Render();
        Assert.Equal(0, model.Transcript.CommittedCount);
        app.Render();
        Assert.Equal(1, model.Transcript.CommittedCount);
        Assert.Equal(1, terminal.Visible().Split("recovered conversation", StringSplitOptions.None).Length - 1);
        Assert.Empty(terminal.Oracle.Scrollback);
    }

    [Fact]
    public void UncertainPageEntryRecoversTheNormalBufferAndReplaysCanonicalHistory()
    {
        using var terminal = new Display();
        using var app = new ManagedTerminalTuiApplication(terminal, new FailPageOnce(terminal),
            terminal.ManagedTerminalCapabilities, recoveryPolicy: ManagedTerminalRecoveryPolicy.ClearAndReplay);
        var model = Install(app, "history before page");
        var chat = app.Root!;
        var source = new PageSource(app.ScrollbackSource!);
        app.ScrollbackSource = source;
        app.Render();
        source.PageActive = true;
        app.SetRoot(new Text("page"));
        app.Render();
        Assert.Equal(0, model.Transcript.CommittedCount);
        source.PageActive = false;
        app.SetRoot(chat);
        app.Render();
        Assert.Equal(1, model.Transcript.CommittedCount);
        Assert.Contains("history before page", terminal.Visible());
        Assert.Empty(terminal.Oracle.Scrollback);
    }

    private sealed class FailPageOnce(Display terminal) : ITerminalOutputTransport
    {
        private bool _failed;
        public ValueTask<TerminalWriteResult> TryWriteFrameAsync(TerminalFrameLease frame,
            CancellationToken cancellationToken = default)
        {
            terminal.Write(frame.Payload.Span);
            if (!_failed && frame.Payload.Span.Contains("\x1b[?1049h", StringComparison.Ordinal))
            {
                _failed = true;
                return ValueTask.FromResult(new TerminalWriteResult(TerminalWriteStatus.Failed, new IOException("uncertain page entry")));
            }
            return ValueTask.FromResult(TerminalWriteResult.Written);
        }
        public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class PageSource(IScrollbackSource inner) : IScrollbackSource
    {
        public bool PageActive { get; set; }
        public bool IsFullScreen => PageActive;
        public long HistoryRevision => inner.HistoryRevision;
        public ManagedTerminalRecoveryPolicy HistoryResetPolicy => inner.HistoryResetPolicy;
        public void ResetPresentation(long epoch, in RenderContext context) => inner.ResetPresentation(epoch, in context);
        public ScrollbackBatch? PrepareScrollback(in RenderContext context, int maxRows)
            => PageActive ? null : inner.PrepareScrollback(in context, maxRows);
        public void CommitScrollback(ScrollbackBatch batch) => inner.CommitScrollback(batch);
        public void RollbackScrollback(ScrollbackBatch batch) => inner.RollbackScrollback(batch);
    }

    [Fact]
    public void HeaderPrecedesTranscriptAndPublishesOnlyOncePerPresentation()
    {
        using var terminal = new Display();
        using var app = new ManagedTerminalTuiApplication(terminal);
        var model = Install(app, "first message", "CHAT LOGO");
        app.Render();
        var screen = terminal.Visible();
        Assert.True(screen.IndexOf("CHAT LOGO", StringComparison.Ordinal) < screen.IndexOf("first message", StringComparison.Ordinal));
        Assert.True(screen.IndexOf("first message", StringComparison.Ordinal) < screen.IndexOf("Ask HPD", StringComparison.Ordinal));
        model.Transcript.AddFinal(Entry("second message"));
        app.Render();
        app.Render();
        var all = string.Join("\n", terminal.Oracle.Scrollback) + terminal.Visible();
        Assert.Equal(1, all.Split("CHAT LOGO", StringSplitOptions.None).Length - 1);
        model.Transcript.ReplaceHistoryWith(Entry("summary"), CommittedHistoryMutationPolicy.ClearAndReplay);
        app.Render();
        screen = terminal.Visible();
        Assert.True(screen.IndexOf("CHAT LOGO", StringComparison.Ordinal) < screen.IndexOf("summary", StringComparison.Ordinal));
        Assert.DoesNotContain("first message", screen);
        Assert.Equal(1, screen.Split("CHAT LOGO", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void HeaderSharesBoundedSequenceWithTranscriptAndRollbackDoesNotAdvanceIt()
    {
        using var terminal = new Display();
        using var app = new ManagedTerminalTuiApplication(terminal);
        var model = Install(app, "message", "logo one\nlogo two\nlogo three");
        var source = app.ScrollbackSource!;
        var context = new RenderContext(80, 24, Theme.Default);
        source.ResetPresentation(7, in context);
        var rejected = source.PrepareScrollback(in context, 2)!;
        source.RollbackScrollback(rejected);
        var accepted = new List<ScrollbackRow>();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var batch = source.PrepareScrollback(in context, 2);
            if (batch is null) break;
            Assert.Equal(7, batch.PresentationEpoch);
            Assert.Equal(accepted.Count, batch.FirstSequence);
            Assert.InRange(batch.Rows.Count, 1, 2);
            accepted.AddRange(batch.Rows);
            source.CommitScrollback(batch);
        }
        Assert.Equal(1, model.Transcript.CommittedCount);
        var text = string.Join("\n", accepted.Select(row => string.Concat(row.Cells.Select(cell => cell.Grapheme))));
        Assert.StartsWith("logo one\nlogo two\nlogo three", text);
        Assert.Contains("message", text);
        Assert.Equal(1, text.Split("logo one", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void SourceReplacementDuringOutputCommitsToTheSourceThatPreparedTheBatch()
    {
        using var terminal = new Display();
        var transport = new ReplaceSourceOnWrite(terminal);
        using var app = new ManagedTerminalTuiApplication(terminal, transport, terminal.ManagedTerminalCapabilities);
        var first = Install(app, "old conversation", "OLD LOGO");
        ChatShellModel? second = null;
        transport.Replace = () => second = Install(app, "new conversation", "NEW LOGO");
        app.Render();
        Assert.Equal(1, first.Transcript.CommittedCount);
        Assert.NotNull(second);
        Assert.Equal(0, second.Transcript.CommittedCount);
        app.Render();
        Assert.Equal(1, second.Transcript.CommittedCount);
        Assert.Contains("NEW LOGO", terminal.Visible());
        Assert.DoesNotContain("OLD LOGO", terminal.Visible());
    }

    [Theory]
    [InlineData(TerminalWriteStatus.Backpressured)]
    [InlineData(TerminalWriteStatus.Failed)]
    public void SourceReplacementDuringRejectedOutputRollsBackTheOriginalOwner(TerminalWriteStatus status)
    {
        using var terminal = new Display();
        var transport = new ReplaceSourceOnWrite(terminal) { ReplacementStatus = status };
        using var app = new ManagedTerminalTuiApplication(terminal, transport, terminal.ManagedTerminalCapabilities,
            recoveryPolicy: ManagedTerminalRecoveryPolicy.ClearAndReplay);
        var first = Install(app, "old conversation", "OLD LOGO");
        ChatShellModel? second = null;
        transport.Replace = () => second = Install(app, "new conversation", "NEW LOGO");
        if (status == TerminalWriteStatus.Backpressured)
            Assert.Equal("TerminalBackpressureException", Assert.ThrowsAny<Exception>(() => app.Render()).GetType().Name);
        else app.Render();
        Assert.Equal(0, first.Transcript.CommittedCount);
        Assert.NotNull(second);
        Assert.Equal(0, second.Transcript.CommittedCount);
        app.Render();
        Assert.Equal(1, second.Transcript.CommittedCount);
        Assert.Contains("NEW LOGO", terminal.Visible());
        Assert.DoesNotContain("OLD LOGO", terminal.Visible());
    }

    private sealed class ReplaceSourceOnWrite(Display terminal) : ITerminalOutputTransport
    {
        public Action? Replace { get; set; }
        public TerminalWriteStatus ReplacementStatus { get; set; } = TerminalWriteStatus.Written;
        public ValueTask<TerminalWriteResult> TryWriteFrameAsync(TerminalFrameLease frame,
            CancellationToken cancellationToken = default)
        {
            var replace = Replace;
            Replace = null;
            var status = replace is null ? TerminalWriteStatus.Written : ReplacementStatus;
            if (status != TerminalWriteStatus.Backpressured) terminal.Write(frame.Payload.Span);
            replace?.Invoke();
            return ValueTask.FromResult(new TerminalWriteResult(status,
                status == TerminalWriteStatus.Failed ? new IOException("uncertain output") : null));
        }
        public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FailFirstPublication(Display terminal) : ITerminalOutputTransport
    {
        private bool _failed;
        public ValueTask<TerminalWriteResult> TryWriteFrameAsync(TerminalFrameLease frame,
            CancellationToken cancellationToken = default)
        {
            terminal.Write(frame.Payload.Span);
            if (_failed) return ValueTask.FromResult(TerminalWriteResult.Written);
            _failed = true;
            return ValueTask.FromResult(new TerminalWriteResult(TerminalWriteStatus.Failed, new IOException("uncertain acceptance")));
        }
        public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private static ChatShellModel Install(ManagedTerminalTuiApplication app, string message, string? header = null)
    {
        var builder = new HpdAgentTuiBuilder().AddAgentTuiDefaults()
            .UseTranscriptHistoryPresentation(TranscriptHistoryPresentation.TerminalScrollback);
        if (header is not null) builder.ReplaceHeader(_ => new Text(header));
        var registry = builder.Build();
        var model = new ChatShellModel(new AgentTuiRuntimeScope("agent", Guid.NewGuid().ToString(), "main"));
        model.Transcript.AddFinal(Entry(message));
        var shell = registry.ShellLayout.Create(new AgentTuiShellLayoutContext(model,
            PromptView.Create("Ask HPD..."), registry, registry.ShellChrome));
        app.SetRoot(shell);
        app.ScrollbackSource = (IScrollbackSource)shell;
        return model;
    }

    private static TranscriptEntry Entry(string text) => new(Guid.NewGuid().ToString(), null,
        new UserMessageCell(text), new TranscriptEntryMetadata(), VerticalSpacing: 0);

    private sealed class Display : ITerminal, ITerminalInput, IManagedTerminalCapabilitySource
    {
        internal VirtualTerminalOracle Oracle { get; } = new(80, 24);
        internal string Visible() => string.Join("\n", Enumerable.Range(0, 24).Select(Oracle.Line));
        public ManagedTerminalCapabilityProfile ManagedTerminalCapabilities =>
            ManagedTerminalCapabilityProfile.FromEnvironment(name => name == "TERM" ? "xterm-256color" : null, false);
        public ITerminalInput Input => this;
        public TerminalSize GetSize() => new(80, 24);
        public void Write(ReadOnlySpan<char> text) => Oracle.Apply(text.ToString());
        public void Flush() { }
        public void HideCursor() { }
        public void ShowCursor() { }
        public async ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return default;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }
}
