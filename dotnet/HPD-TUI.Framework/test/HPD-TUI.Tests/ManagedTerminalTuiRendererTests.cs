using System.Text;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Observability;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.Events;

namespace HPD.TUI.Tests;

public sealed class ManagedTerminalTuiRendererTests
{
    [Fact]
    public async Task Application_UsesNormalScreen()
    {
        using var terminal = new TestTerminal(40, 8);
        using var app = new ManagedTerminalTuiApplication(terminal);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        app.SetRoot(new Text("hello"));

        await app.RunAsync(cancellationToken: cancellation.Token);

        Assert.DoesNotContain("\x1b[?1049h", terminal.Output);
        Assert.DoesNotContain("\x1b[?1049l", terminal.Output);
    }

    [Fact]
    public async Task Application_DoesNotEnterAlternateScreenWhenUserExits()
    {
        using var terminal = new TestTerminal(40, 8);
        using var app = new ManagedTerminalTuiApplication(terminal);

        terminal.EnqueueKey(new KeyEvent(KeyCode.Escape, Modifiers: KeyModifiers.Ctrl));
        app.SetRoot(new Text("hello"));

        await app.RunAsync();

        Assert.DoesNotContain("\x1b[?1049h", terminal.Output);
        Assert.DoesNotContain("\x1b[?1049l", terminal.Output);
    }

    [Fact]
    public void Render_FirstFrame_ClearsVisibleScreenWithoutClearingTerminalScrollback()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new Text("hello"), Theme.Default);

        Assert.Contains("\x1b[2J\x1b[H", terminal.Output);
        Assert.DoesNotContain("\x1b[3J", terminal.Output);
        Assert.DoesNotContain("\x1b[?1049h", terminal.Output);
        Assert.Contains("hello", terminal.Output);
    }

    [Fact]
    public void Render_AfterPartialFailure_RecoversWithFullPhysicalRepaint()
    {
        using var terminal = new TestTerminal(40, 8);
        var transport = new FailOnceTransport();
        using var renderer = new ManagedTerminalTuiRenderer(
            terminal, transport, ManagedTerminalCapabilityProfile.Verified,
            recoveryPolicy: ManagedTerminalRecoveryPolicy.ClearAndReplay);

        Assert.Throws<InvalidOperationException>(() => renderer.Render(new Text("hello")));
        renderer.Render(new Text("hello"));

        Assert.Equal(2, transport.Attempts);
        Assert.Contains("\x1b[2J\x1b[H", transport.AcceptedPayload);
        Assert.Contains("hello", transport.AcceptedPayload);
    }

    [Fact]
    public void Render_AfterUncertainScrollbackWrite_ClearsHistoryBeforeReplay()
    {
        using var terminal = new TestTerminal(40, 8);
        var transport = new FailOnceTransport();
        using var renderer = new ManagedTerminalTuiRenderer(
            terminal, transport, ManagedTerminalCapabilityProfile.Verified,
            recoveryPolicy: ManagedTerminalRecoveryPolicy.ClearAndReplay);
        var batch = new ScrollbackBatch(1, 1,
        [
            new ScrollbackRow("row-1",
            [
                new ScrollbackCell("history", default, default, 7)
            ])
        ]);

        Assert.Throws<InvalidOperationException>(() => renderer.Render(new Text("live"), scrollback: batch));
        Assert.Throws<InvalidOperationException>(() => renderer.Render(new Text("live"), scrollback: batch));

        Assert.Contains("\x1b[3J", transport.AcceptedPayload);
        Assert.Contains("live", transport.AcceptedPayload);
    }

    [Fact]
    public void Render_AfterResize_RedrawsWithoutDestroyingScrollback()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new Text("hello"), Theme.Default);
        terminal.ClearOutput();
        terminal.SetSize(24, 8);

        renderer.Render(new Text("hello"), Theme.Default);

        Assert.Contains("\x1b[2J\x1b[H", terminal.Output);
        Assert.DoesNotContain("\x1b[3J", terminal.Output);
        Assert.DoesNotContain("\x1b[?1049h", terminal.Output);
    }

    [Fact]
    public void Render_WhenVisibleContentChanges_PatchesLineWithoutFullRedraw()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new Text("hello"), Theme.Default);
        terminal.ClearOutput();

        renderer.Render(new Text("hello world"), Theme.Default);

        Assert.DoesNotContain("\x1b[2J\x1b[H", terminal.Output);
        Assert.Contains("\x1b[1;7H", terminal.Output);
        Assert.Contains("world", terminal.Output);
    }

    [Fact]
    public void Render_PreservesStyledTrailingSpaces()
    {
        using var terminal = new TestTerminal(5, 2);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new StyledFillLineComponent(), Theme.Default);

        Assert.Contains("x    ", terminal.Output);
    }

    [Fact]
    public void Render_WhenLineAppends_WritesTailWithoutFullRedraw()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new LinesComponent("one"), Theme.Default);
        terminal.ClearOutput();

        renderer.Render(new LinesComponent("one", "two"), Theme.Default);

        Assert.DoesNotContain("\x1b[2J\x1b[H", terminal.Output);
        Assert.Contains("\x1b[2;1H", terminal.Output);
        Assert.Contains("two", terminal.Output);
    }

    [Fact]
    public void Render_FirstFrameWithLongContent_ClipsToPhysicalScreen()
    {
        using var terminal = new TestTerminal(40, 3);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new LinesComponent("one", "two", "three", "four", "five"), Theme.Default);

        Assert.Contains("one", terminal.Output);
        Assert.Contains("two", terminal.Output);
        Assert.Contains("three", terminal.Output);
        Assert.DoesNotContain("four", terminal.Output);
        Assert.DoesNotContain("five", terminal.Output);
    }

    [Fact]
    public void Render_ConstrainsLayoutToPhysicalViewportHeight()
    {
        using var terminal = new TestTerminal(40, 7);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);
        var component = new ContextHeightComponent();

        renderer.Render(component, Theme.Default);

        Assert.Equal(7, component.ObservedHeight);
    }

    [Fact]
    public void Render_WhenContentShrinks_ErasesOnlyStaleRuns()
    {
        using var terminal = new TestTerminal(40, 5);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new LinesComponent("zero", "one", "two", "three", "four", "five"), Theme.Default);
        terminal.ClearOutput();

        renderer.Render(new LinesComponent("zero", "one"), Theme.Default);

        Assert.DoesNotContain("\x1b[2J\x1b[H", terminal.Output);
        Assert.DoesNotContain("\x1b[3J", terminal.Output);
        Assert.DoesNotContain("zero", terminal.Output);
        Assert.DoesNotContain("one", terminal.Output);
        Assert.Contains("\x1b[3;1H", terminal.Output);
    }

    [Fact]
    public void Render_AfterShrink_AppendsWithoutAnotherFullRedraw()
    {
        using var terminal = new TestTerminal(40, 5);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new LinesComponent("zero", "one", "two", "three", "four", "five"), Theme.Default);
        renderer.Render(new LinesComponent("zero", "one"), Theme.Default);
        terminal.ClearOutput();

        renderer.Render(new LinesComponent("zero", "one", "two"), Theme.Default);

        Assert.DoesNotContain("\x1b[2J\x1b[H", terminal.Output);
        Assert.Contains("two", terminal.Output);
    }

    [Fact]
    public void Render_WhenOnlyClippedContentChanges_EmitsNoFrame()
    {
        using var terminal = new TestTerminal(40, 10);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new LinesComponent(
            "Chat 0", "Chat 1", "Chat 2", "Chat 3", "Chat 4",
            "Chat 5", "Chat 6", "Chat 7", "Chat 8", "Chat 9",
            "Chat 10", "Chat 11", "Chat 12", "Selector 0", "Selector 1"), Theme.Default);
        terminal.ClearOutput();

        renderer.Render(new LinesComponent(
            "Chat 0", "Chat 1", "Chat 2", "Chat 3", "Chat 4",
            "Chat 5", "Chat 6", "Chat 7", "Chat 8", "Chat 9",
            "Chat 10", "Chat 11"), Theme.Default);

        Assert.Empty(terminal.Output);
    }

    [Fact]
    public void Render_WhenHardwareCursorTrackingIsDisabled_DoesNotMoveTerminalCursor()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new CursorComponent(), Theme.Default);

        Assert.DoesNotContain("\x1b[4G", terminal.Output);
        Assert.Equal(0, terminal.ShowCursorCount);
        Assert.Equal(0, terminal.HideCursorCount);
        Assert.Contains("\x1b[?25l", terminal.Output);
    }

    [Fact]
    public void Render_WhenHardwareCursorTrackingIsEnabled_MovesTerminalCursor()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new ManagedTerminalTuiRenderer(terminal)
        {
            TrackHardwareCursor = true
        };

        renderer.Render(new CursorComponent(), Theme.Default);

        Assert.Contains("\x1b[1;4H", terminal.Output);
        Assert.Contains("\x1b[?25h", terminal.Output);
        Assert.Equal(0, terminal.ShowCursorCount);
    }

    [Fact]
    public void Render_WhenPerformanceSinkIsConfigured_PublishesFrameEvent()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);
        var sink = new RecordingSink();
        renderer.PerformanceSink = sink;

        renderer.Render(new Text("hello"), Theme.Default);

        var evt = Assert.IsType<TuiFrameDiagnostics>(Assert.Single(sink.Events));
        Assert.Equal(EventKind.Diagnostic, evt.Kind);
        Assert.Equal(EventChannel.Streaming, evt.Channel);
        Assert.True(evt.RowsDamaged > 0);
        Assert.True(evt.DisplayCommandsBuilt > 0);
        Assert.True(evt.OutputCharacters > 0);
        Assert.True(evt.FullRepaint);
        Assert.True(evt.EncodeDuration >= TimeSpan.Zero);
        Assert.True(evt.OutputDuration >= TimeSpan.Zero);
    }

    [Fact]
    public void Application_PerformanceSink_ForwardsToRenderer()
    {
        using var terminal = new TestTerminal(40, 8);
        using var app = new ManagedTerminalTuiApplication(terminal);
        var sink = new RecordingSink();
        app.PerformanceSink = sink;
        app.SetRoot(new Text("hello"));

        app.Render();

        Assert.IsType<TuiFrameDiagnostics>(Assert.Single(sink.Events));
    }

    [Fact]
    public void Render_BackpressurePublishesMeasuredDeferredFrame()
    {
        using var terminal = new TestTerminal(40, 8);
        var transport = new BackpressureOnceTransport();
        using var renderer = new ManagedTerminalTuiRenderer(terminal, transport);
        var sink = new RecordingSink();
        renderer.PerformanceSink = sink;

        Assert.Throws<TerminalBackpressureException>(() => renderer.Render(new Text("hello")));

        var diagnostics = Assert.IsType<TuiFrameDiagnostics>(Assert.Single(sink.Events));
        Assert.True(diagnostics.Backpressured);
        Assert.True(diagnostics.FullRepaint);
        Assert.True(diagnostics.OutputCharacters > 0);
        Assert.True(diagnostics.DisplayCommandsBuilt > 0);
    }

    [Fact]
    public async Task Application_InputEvent_RendersOnceAfterHandledInput()
    {
        using var terminal = new TestTerminal(40, 8);
        using var app = new ManagedTerminalTuiApplication(terminal);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        var sink = new RecordingSink();
        var component = new InputCountingComponent();
        app.PerformanceSink = sink;
        app.SetRoot(component);
        terminal.EnqueueKey(new KeyEvent(KeyCode.Character, new Rune('x')));

        await app.RunAsync(cancellationToken: cancellation.Token);

        Assert.Equal(1, component.InputCount);
        Assert.Equal(2, sink.Events.OfType<TuiFrameDiagnostics>().Count());
    }

    [Fact]
    public async Task Application_ResizeEvent_RendersWithoutDispatchingToComponent()
    {
        using var terminal = new TestTerminal(40, 8);
        using var app = new ManagedTerminalTuiApplication(terminal);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        var sink = new RecordingSink();
        var component = new InputCountingComponent();
        app.PerformanceSink = sink;
        app.SetRoot(component);
        terminal.Enqueue(TerminalInputEvent.FromResize(new TerminalSize(60, 12)));

        await app.RunAsync(cancellationToken: cancellation.Token);

        Assert.Equal(0, component.InputCount);
        Assert.Equal(2, sink.Events.OfType<TuiFrameDiagnostics>().Count());
    }

    [Fact]
    public async Task Application_Backpressure_RetriesLatestFrameAndCommitsScrollbackOnce()
    {
        using var terminal = new TestTerminal(40, 8);
        var transport = new BackpressureOnceTransport();
        using var app = new ManagedTerminalTuiApplication(terminal, transport);
        var source = new RecordingScrollbackSource();
        app.ScrollbackSource = source;
        app.SetRoot(new Text("live"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));

        await app.RunAsync(cancellationToken: cancellation.Token);

        Assert.Equal(3, transport.Attempts);
        Assert.Equal(1, source.CommitCount);
        Assert.Equal(1, source.RollbackCount);
        Assert.Contains("history", transport.AcceptedPayload);
        Assert.Contains("live", transport.AcceptedPayload);
    }

    [Fact]
    public void TextWriterSink_FormatsFrameSummary()
    {
        var writer = new StringWriter();
        var sink = new TextWriterTuiPerformanceEventSink(writer);

        sink.Publish(new TuiFrameDiagnostics(
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(4.25), TimeSpan.Zero,
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 1, 1, 2, 0, 3, 0, 3, 2, 4,
            20, FullRepaint: false, Backpressured: false));

        Assert.Contains("display=4.25ms", writer.ToString());
    }

    private sealed class TestTerminal : ITerminal, ITerminalInput, IManagedTerminalCapabilitySource
    {
        private readonly StringBuilder _output = new();
        private readonly Queue<TerminalInputEvent> _events = new();
        private TerminalSize _size;

        public TestTerminal(int width, int height)
        {
            _size = new TerminalSize(width, height);
        }

        public string Output => _output.ToString();

        public int HideCursorCount { get; private set; }

        public int ShowCursorCount { get; private set; }

        public ITerminalInput Input => this;

        public ManagedTerminalCapabilityProfile ManagedTerminalCapabilities => ManagedTerminalCapabilityProfile.Verified;

        public TerminalSize GetSize() => _size;

        public void SetSize(int width, int height)
        {
            _size = new TerminalSize(width, height);
        }

        public void ClearOutput() => _output.Clear();

        public void EnqueueKey(KeyEvent key)
        {
            Enqueue(TerminalInputEvent.FromKey(key));
        }

        public void Enqueue(TerminalInputEvent input) => _events.Enqueue(input);

        public void Write(ReadOnlySpan<char> text)
        {
            _output.Append(text);
        }

        public void Flush()
        {
        }

        public ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (_events.TryDequeue(out var input))
            {
                return ValueTask.FromResult(input);
            }

            return WaitAsync(cancellationToken);
        }

        public void HideCursor()
        {
            HideCursorCount++;
        }

        public void ShowCursor()
        {
            ShowCursorCount++;
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static async ValueTask<TerminalInputEvent> WaitAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return TerminalInputEvent.Stop;
        }
    }

    private sealed class RecordingSink : IHpdTuiPerformanceEventSink
    {
        public List<Event> Events { get; } = [];

        public void Publish(Event evt)
        {
            Events.Add(evt);
        }
    }

    private sealed class BackpressureOnceTransport : ITerminalOutputTransport
    {
        public int Attempts { get; private set; }
        public string AcceptedPayload { get; private set; } = string.Empty;

        public ValueTask<TerminalWriteResult> TryWriteFrameAsync(
            TerminalFrameLease frame,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts == 1)
                return ValueTask.FromResult(TerminalWriteResult.Backpressured);
            AcceptedPayload += frame.Payload.ToString();
            return ValueTask.FromResult(TerminalWriteResult.Written);
        }

        public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class RecordingScrollbackSource : IScrollbackSource
    {
        private readonly ScrollbackBatch _batch = new(
            0,
            0,
            [new ScrollbackRow("row:0", [new ScrollbackCell("history", Style.Default, default, 7)])]);
        private bool _committed;
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }

        public ScrollbackBatch? PrepareScrollback(in RenderContext context, int maxRows)
            => _committed ? null : _batch;

        public void CommitScrollback(ScrollbackBatch batch)
        {
            Assert.Same(_batch, batch);
            _committed = true;
            CommitCount++;
        }

        public void RollbackScrollback(ScrollbackBatch batch)
        {
            Assert.Same(_batch, batch);
            RollbackCount++;
        }
    }

    private sealed class FailOnceTransport : ITerminalOutputTransport
    {
        public int Attempts { get; private set; }
        public string AcceptedPayload { get; private set; } = string.Empty;

        public ValueTask<TerminalWriteResult> TryWriteFrameAsync(
            TerminalFrameLease frame,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts == 1)
                return ValueTask.FromResult(new TerminalWriteResult(
                    TerminalWriteStatus.Failed,
                    new IOException("partial write")));
            AcceptedPayload = frame.Payload.ToString();
            return ValueTask.FromResult(TerminalWriteResult.Written);
        }

        public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class LinesComponent : Component
    {
        private readonly string[] _lines;

        public LinesComponent(params string[] lines)
        {
            _lines = lines;
        }

        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints) => new(constraints.MaxWidth, _lines.Length);

        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            var maxWidth = output.MaxWidth;
            for (var i = 0; i < _lines.Length; i++)
            {
                if (i > 0)
                {
                    output.WriteLineBreak();
                }

                output.Write(_lines[i], context.Theme.Text);
            }
        }

        public override bool HandleInput(in TuiInputEvent key)
        {
            return false;
        }
    }

    private sealed class CursorComponent : Component
    {
        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints) => new(4, 4);

        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            var maxWidth = output.MaxWidth;
            output.Write("text", context.Theme.Text);
            output.SetTerminalCursor(3, 0);
        }

        public override bool HandleInput(in TuiInputEvent key)
        {
            return false;
        }
    }

    private sealed class ContextHeightComponent : Component
    {
        public int ObservedHeight { get; private set; }

        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints) => new(1, 1);

        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            var maxWidth = output.MaxWidth;
            ObservedHeight = context.Height;
            output.Write("x", context.Theme.Text);
        }

        public override bool HandleInput(in TuiInputEvent key) => false;
    }

    private sealed class StyledFillLineComponent : Component
    {
        private static readonly Style Fill = new(Color.White, new Color(10, 20, 30));

        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints) => new(constraints.MaxWidth, constraints.MaxWidth);

        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            var maxWidth = output.MaxWidth;
            output.Write("x", Fill);
            output.Write(new string(' ', Math.Max(0, maxWidth - 1)), Fill);
        }

        public override bool HandleInput(in TuiInputEvent key)
        {
            return false;
        }
    }

    private sealed class InputCountingComponent : Component
    {
        public int InputCount { get; private set; }

        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints) => new(1, 1);

        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            var maxWidth = output.MaxWidth;
            output.Write(InputCount.ToString(), context.Theme.Text);
        }

        public override bool HandleInput(in TuiInputEvent key)
        {
            InputCount++;
            return true;
        }
    }
}
