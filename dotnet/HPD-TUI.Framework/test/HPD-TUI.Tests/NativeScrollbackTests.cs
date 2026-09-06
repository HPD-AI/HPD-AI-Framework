using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class NativeScrollbackTests
{
    [Fact]
    public void HistoryRemainsAboveLiveRegionThroughTypingGrowthShrinkAndOverflow()
    {
        using var terminal = new OracleTerminal(24, 6);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);
        var prompt = new Text("prompt");
        renderer.Render(prompt, scrollback: Batch(0, 0, "first", "second"));
        Assert.Equal(new[] { "first", "second", "prompt", "", "", "" }, terminal.Screen());
        renderer.Render(new Text("prompt edited"));
        Assert.Equal(new[] { "first", "second", "prompt edited", "", "", "" }, terminal.Screen());
        renderer.Render(new Text("tail one\ntail two\nprompt"));
        Assert.Equal("first", terminal.Oracle.Line(0));
        Assert.Equal("second", terminal.Oracle.Line(1));
        renderer.Render(prompt);
        Assert.Equal(new[] { "first", "second", "prompt", "", "", "" }, terminal.Screen());
        renderer.Render(prompt, scrollback: Batch(0, 2, "third", "fourth", "fifth", "sixth"));
        Assert.Equal(new[] { "first" }, terminal.Oracle.Scrollback);
        Assert.Equal(new[] { "second", "third", "fourth", "fifth", "sixth", "prompt" }, terminal.Screen());
        Assert.True(terminal.Oracle.Autowrap);
        Assert.Equal(5, renderer.LiveTop);
    }

    [Theory]
    [InlineData("xterm-256color", false, true)]
    [InlineData("dumb", false, false)]
    [InlineData("unrecognized", false, false)]
    [InlineData("xterm-256color", true, false)]
    public void ProductionEnvironmentSelectsSupportedProtocolBeforePublication(string term, bool redirected, bool supported)
    {
        var profile = ManagedTerminalCapabilityProfile.FromEnvironment(name => name == "TERM" ? term : null, redirected);
        Assert.Equal(supported, profile.SupportsSplitFooter);
        if (!supported) return;
        using var terminal = new OracleTerminal(24, 6);
        using var renderer = new ManagedTerminalTuiRenderer(terminal,
            new SynchronousTerminalOutputTransport(terminal), profile);
        renderer.Render(new Text("prompt"), scrollback: Batch(0, 0, "history"));
        Assert.Equal("history", terminal.Oracle.Line(0));
        Assert.Equal("prompt", terminal.Oracle.Line(1));
        Assert.Equal(0, terminal.Oracle.SynchronizedOutputDepth);
    }

    [Fact]
    public void RebaseClearsHistoryAndAllowsNewSequenceWithoutOldRows()
    {
        using var terminal = new OracleTerminal(24, 4);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);
        renderer.Render(new Text("old prompt"), scrollback: Batch(0, 0, "a", "b", "c", "d", "e"));
        var result = renderer.RebaseCommittedHistory(ManagedTerminalRecoveryPolicy.ClearAndReplay);
        Assert.Equal(ManagedHistoryRebaseStatus.Written, result.Status);
        renderer.Render(new Text("new prompt"), scrollback: Batch(result.PresentationEpoch, 0, "new conversation"));
        Assert.Empty(terminal.Oracle.Scrollback);
        Assert.Equal(new[] { "new conversation", "new prompt", "", "" }, terminal.Screen());
    }

    [Fact]
    public void FullScreenPageRestoresVisibleHistoryAndLiveAnchor()
    {
        using var terminal = new OracleTerminal(24, 6);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);
        renderer.Render(new Text("prompt"), scrollback: Batch(0, 0, "history"));
        renderer.SetFullScreen(true);
        renderer.Render(new Text("page"));
        Assert.Equal("page", terminal.Oracle.Line(0));
        renderer.SetFullScreen(false);
        renderer.Render(new Text("prompt again"));
        Assert.Equal("history", terminal.Oracle.Line(0));
        Assert.Equal("prompt again", terminal.Oracle.Line(1));
        Assert.Empty(terminal.Oracle.Scrollback);
    }

    [Fact]
    public void RecoveryAlternateScreenPersistsUntilExplicitNormalHistoryTransition()
    {
        using var terminal = new OracleTerminal(24, 6);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);
        renderer.Render(new Text("prompt"), scrollback: Batch(0, 0, "normal history"));
        var recovery = renderer.RebaseCommittedHistory(ManagedTerminalRecoveryPolicy.SwitchToAlternateScreen);
        renderer.Render(new Text("recovered"), scrollback: Batch(recovery.PresentationEpoch, 0, "replayed"));
        renderer.SetFullScreen(false);
        Assert.Equal("replayed", terminal.Oracle.Line(0));
        renderer.RebaseCommittedHistory(ManagedTerminalRecoveryPolicy.ClearAndReplay);
        renderer.Render(new Text("new normal"));
        Assert.Equal("new normal", terminal.Oracle.Line(0));
    }

    [Fact]
    public void VisibleBoundaryPreservesPreviouslyVisibleHistory()
    {
        using var terminal = new OracleTerminal(24, 6);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);
        renderer.Render(new Text("prompt"), scrollback: Batch(0, 0, "history"));
        var result = renderer.RebaseCommittedHistory(ManagedTerminalRecoveryPolicy.VisibleEpochBoundary);
        Assert.Equal(ManagedHistoryRebaseStatus.Written, result.Status);
        Assert.Contains("history", terminal.Oracle.Scrollback);
        renderer.Render(new Text("new prompt"), scrollback: Batch(result.PresentationEpoch, 0, "new history"));
        Assert.Equal("new history", terminal.Oracle.Line(0));
    }

    [Fact]
    public void EmptySemanticCompletionStillPublishesTheNewLiveFrame()
    {
        using var terminal = new OracleTerminal(24, 6);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);
        renderer.Render(new Text("streaming"), scrollback: Batch(0, 0, "history"));
        renderer.Render(new Text("finished"), scrollback: Batch(0, 1));
        Assert.Equal("history", terminal.Oracle.Line(0));
        Assert.Equal("finished", terminal.Oracle.Line(1));
    }

    [Fact]
    public void FullWidthHistoryRetainsItsLastWideGrapheme()
    {
        using var terminal = new OracleTerminal(24, 6);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);
        var cells = Enumerable.Repeat(new ScrollbackCell("a", Style.Default, default, 1), 22)
            .Append(new ScrollbackCell("界", Style.Default, default, 2)).ToArray();
        renderer.Render(new Text("prompt"), scrollback: new ScrollbackBatch(0, 0, [new ScrollbackRow("wide", cells)]));
        Assert.Equal(new string('a', 22) + "界", terminal.Oracle.Line(0));
        Assert.Equal("prompt", terminal.Oracle.Line(1));
    }

    private static ScrollbackBatch Batch(long epoch, long sequence, params string[] lines)
        => new(epoch, sequence, lines.Select((line, i) => new ScrollbackRow($"row:{sequence + i}",
            line.Select(c => new ScrollbackCell(c.ToString(), Style.Default, default, 1)).ToArray())).ToArray());

    private sealed class OracleTerminal(int width, int height) : ITerminal, ITerminalInput, IManagedTerminalCapabilitySource
    {
        internal VirtualTerminalOracle Oracle { get; } = new(width, height);
        internal string[] Screen() => Enumerable.Range(0, height).Select(Oracle.Line).ToArray();
        public ManagedTerminalCapabilityProfile ManagedTerminalCapabilities => ManagedTerminalCapabilityProfile.Verified;
        public ITerminalInput Input => this;
        public TerminalSize GetSize() => new(width, height);
        public void Write(ReadOnlySpan<char> text) => Oracle.Apply(text.ToString());
        public void Flush() { }
        public void HideCursor() { }
        public void ShowCursor() { }
        public ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromCanceled<TerminalInputEvent>(new CancellationToken(true));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }
}
