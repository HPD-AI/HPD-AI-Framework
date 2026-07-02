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
    public void Render_FirstFrame_DoesNotClearTerminalScrollback()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new Text("hello"), Theme.Default, ManagedTerminalRenderBounds.ViewportAnchored());

        Assert.Contains("\x1b[2J\x1b[H", terminal.Output);
        Assert.DoesNotContain("\x1b[3J", terminal.Output);
        Assert.DoesNotContain("\x1b[?1049h", terminal.Output);
    }

    [Fact]
    public void Render_AfterResize_RedrawsWithoutClearingTerminalScrollback()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new Text("hello"), Theme.Default, ManagedTerminalRenderBounds.ViewportAnchored());
        terminal.ClearOutput();
        terminal.SetSize(24, 8);

        renderer.Render(new Text("hello"), Theme.Default, ManagedTerminalRenderBounds.ViewportAnchored());

        Assert.Contains("\x1b[2J\x1b[H", terminal.Output);
        Assert.DoesNotContain("\x1b[3J", terminal.Output);
        Assert.DoesNotContain("\x1b[?1049h", terminal.Output);
    }

    [Fact]
    public void Render_WhenVisibleContentChanges_PatchesLineWithoutFullRedraw()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new Text("hello"), Theme.Default, ManagedTerminalRenderBounds.ViewportAnchored());
        terminal.ClearOutput();

        renderer.Render(new Text("hello world"), Theme.Default, ManagedTerminalRenderBounds.ViewportAnchored());

        Assert.DoesNotContain("\x1b[2J\x1b[H", terminal.Output);
        Assert.Contains("\x1b[2K", terminal.Output);
        Assert.Contains("hello world", terminal.Output);
    }

    [Fact]
    public void Render_WhenLineAppends_WritesTailWithoutFullRedraw()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new LinesComponent("one"), Theme.Default, ManagedTerminalRenderBounds.ViewportAnchored());
        terminal.ClearOutput();

        renderer.Render(new LinesComponent("one", "two"), Theme.Default, ManagedTerminalRenderBounds.ViewportAnchored());

        Assert.DoesNotContain("\x1b[2J\x1b[H", terminal.Output);
        Assert.Contains("\r\n", terminal.Output);
        Assert.Contains("two", terminal.Output);
    }

    [Fact]
    public void Render_FullRenderWithVirtualHeight_WritesOnlyVisibleViewport()
    {
        using var terminal = new TestTerminal(40, 3);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(
            new LinesComponent("one", "two", "three", "four", "five"),
            Theme.Default,
            ManagedTerminalRenderBounds.ViewportAnchored(maxRows: 16));

        Assert.DoesNotContain("one", terminal.Output);
        Assert.DoesNotContain("two", terminal.Output);
        Assert.Contains("three", terminal.Output);
        Assert.Contains("four", terminal.Output);
        Assert.Contains("five", terminal.Output);
    }

    [Fact]
    public void Render_WhenHardwareCursorTrackingIsDisabled_DoesNotMoveTerminalCursor()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new CursorComponent(), Theme.Default, ManagedTerminalRenderBounds.ViewportAnchored());

        Assert.DoesNotContain("\x1b[4G", terminal.Output);
        Assert.Equal(0, terminal.ShowCursorCount);
        Assert.Equal(1, terminal.HideCursorCount);
    }

    [Fact]
    public void Render_WhenHardwareCursorTrackingIsEnabled_MovesTerminalCursor()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new ManagedTerminalTuiRenderer(terminal)
        {
            TrackHardwareCursor = true
        };

        renderer.Render(new CursorComponent(), Theme.Default, ManagedTerminalRenderBounds.ViewportAnchored());

        Assert.Contains("\x1b[4G", terminal.Output);
        Assert.Equal(1, terminal.ShowCursorCount);
    }

    [Fact]
    public void Render_WhenPerformanceSinkIsConfigured_PublishesFrameEvent()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new ManagedTerminalTuiRenderer(terminal);
        var sink = new RecordingSink();
        renderer.PerformanceSink = sink;

        renderer.Render(new Text("hello"), Theme.Default, ManagedTerminalRenderBounds.ViewportAnchored());

        var evt = Assert.IsType<TuiRenderCompleted>(Assert.Single(sink.Events));
        Assert.Equal(EventKind.Diagnostic, evt.Kind);
        Assert.Equal(EventChannel.Streaming, evt.Channel);
        Assert.Equal("managed-terminal", evt.Surface);
        Assert.True(evt.RowsRendered > 0);
        Assert.True(evt.SegmentsWritten > 0);
    }

    [Fact]
    public void Application_PerformanceSink_ForwardsToRenderer()
    {
        using var terminal = new TestTerminal(40, 8);
        using var app = new ManagedTerminalTuiApplication(terminal);
        var sink = new RecordingSink();
        app.PerformanceSink = sink;
        app.SetRoot(new Text("hello"));

        app.Render(new ManagedTerminalRunOptions());

        Assert.IsType<TuiRenderCompleted>(Assert.Single(sink.Events));
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
        Assert.Equal(2, sink.Events.OfType<TuiRenderCompleted>().Count());
    }

    [Fact]
    public void TextWriterSink_FormatsFrameSummary()
    {
        var writer = new StringWriter();
        var sink = new TextWriterTuiPerformanceEventSink(writer);

        sink.Publish(new TuiRenderCompleted(
            Surface: "managed-terminal",
            Duration: TimeSpan.FromMilliseconds(4.25),
            RowsRendered: 3,
            SegmentsWritten: 2,
            CacheHits: 1,
            CacheMisses: 0));

        Assert.Contains("tui frame 4.25ms surface=managed-terminal rows=3 segments=2 cache=1/0", writer.ToString());
    }

    private sealed class TestTerminal : ITerminal, ITerminalInput
    {
        private readonly StringBuilder _output = new();
        private readonly Queue<KeyEvent> _keys = new();
        private TerminalSize _size;

        public TestTerminal(int width, int height)
        {
            _size = new TerminalSize(width, height);
        }

        public string Output => _output.ToString();

        public int HideCursorCount { get; private set; }

        public int ShowCursorCount { get; private set; }

        public ITerminalInput Input => this;

        public TerminalSize GetSize() => _size;

        public void SetSize(int width, int height)
        {
            _size = new TerminalSize(width, height);
        }

        public void ClearOutput() => _output.Clear();

        public void EnqueueKey(KeyEvent key)
        {
            _keys.Enqueue(key);
        }

        public void Write(ReadOnlySpan<char> text)
        {
            _output.Append(text);
        }

        public void Flush()
        {
        }

        public ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (_keys.TryDequeue(out var key))
            {
                return ValueTask.FromResult(TerminalInputEvent.FromKey(key));
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

    private sealed class LinesComponent : IComponent
    {
        private readonly string[] _lines;

        public LinesComponent(params string[] lines)
        {
            _lines = lines;
        }

        public Measurement Measure(in RenderContext context, int maxWidth) => new(maxWidth, _lines.Length);

        public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
        {
            for (var i = 0; i < _lines.Length; i++)
            {
                if (i > 0)
                {
                    output.WriteLineBreak();
                }

                output.Write(_lines[i], context.Theme.Text);
            }
        }

        public bool HandleInput(in TuiInputEvent key)
        {
            return false;
        }
    }

    private sealed class CursorComponent : IComponent
    {
        public Measurement Measure(in RenderContext context, int maxWidth) => new(4, 4);

        public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
        {
            output.Write("text", context.Theme.Text);
            output.SetTerminalCursor(3, 0);
        }

        public bool HandleInput(in TuiInputEvent key)
        {
            return false;
        }
    }

    private sealed class InputCountingComponent : IComponent
    {
        public int InputCount { get; private set; }

        public Measurement Measure(in RenderContext context, int maxWidth) => new(1, 1);

        public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
        {
            output.Write(InputCount.ToString(), context.Theme.Text);
        }

        public bool HandleInput(in TuiInputEvent key)
        {
            InputCount++;
            return true;
        }
    }
}
