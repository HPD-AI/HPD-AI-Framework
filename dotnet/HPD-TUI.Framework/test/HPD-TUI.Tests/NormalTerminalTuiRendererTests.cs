using System.Text;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Observability;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.Events;

namespace HPD.TUI.Tests;

public sealed class NormalTerminalTuiRendererTests
{
    [Fact]
    public async Task Application_UsesNormalScreen()
    {
        using var terminal = new TestTerminal(40, 8);
        using var app = new NormalTerminalTuiApplication(terminal);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        app.SetRoot(new Text("hello"));

        await app.RunAsync(size => size.Height, TimeSpan.FromMilliseconds(1), cancellation.Token);

        Assert.DoesNotContain("\x1b[?1049h", terminal.Output);
        Assert.DoesNotContain("\x1b[?1049l", terminal.Output);
    }

    [Fact]
    public async Task Application_DoesNotEnterAlternateScreenWhenUserExits()
    {
        using var terminal = new TestTerminal(40, 8);
        using var app = new NormalTerminalTuiApplication(terminal);

        terminal.EnqueueKey(new KeyEvent(KeyCode.Escape, Modifiers: KeyModifiers.Ctrl));
        app.SetRoot(new Text("hello"));

        await app.RunAsync(size => size.Height, TimeSpan.FromMilliseconds(1));

        Assert.DoesNotContain("\x1b[?1049h", terminal.Output);
        Assert.DoesNotContain("\x1b[?1049l", terminal.Output);
    }

    [Fact]
    public void Render_FirstFrame_DoesNotClearTerminalScrollback()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new NormalTerminalTuiRenderer(terminal);

        renderer.Render(new Text("hello"), Theme.Default, virtualHeight: 8);

        Assert.Contains("\x1b[2J\x1b[H", terminal.Output);
        Assert.DoesNotContain("\x1b[3J", terminal.Output);
        Assert.DoesNotContain("\x1b[?1049h", terminal.Output);
    }

    [Fact]
    public void Render_AfterResize_ClearsTerminalScrollbackBeforeRedraw()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new NormalTerminalTuiRenderer(terminal);

        renderer.Render(new Text("hello"), Theme.Default, virtualHeight: 8);
        terminal.ClearOutput();
        terminal.SetSize(24, 8);

        renderer.Render(new Text("hello"), Theme.Default, virtualHeight: 8);

        Assert.Contains("\x1b[3J\x1b[2J\x1b[H", terminal.Output);
        Assert.DoesNotContain("\x1b[?1049h", terminal.Output);
    }

    [Fact]
    public void Render_WhenVisibleContentChanges_PatchesLineWithoutFullRedraw()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new NormalTerminalTuiRenderer(terminal);

        renderer.Render(new Text("hello"), Theme.Default, virtualHeight: 8);
        terminal.ClearOutput();

        renderer.Render(new Text("hello world"), Theme.Default, virtualHeight: 8);

        Assert.DoesNotContain("\x1b[2J\x1b[H", terminal.Output);
        Assert.Contains("\x1b[2K", terminal.Output);
        Assert.Contains("hello world", terminal.Output);
    }

    [Fact]
    public void Render_WhenLineAppends_WritesTailWithoutFullRedraw()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new NormalTerminalTuiRenderer(terminal);

        renderer.Render(new LinesComponent("one"), Theme.Default, virtualHeight: 8);
        terminal.ClearOutput();

        renderer.Render(new LinesComponent("one", "two"), Theme.Default, virtualHeight: 8);

        Assert.DoesNotContain("\x1b[2J\x1b[H", terminal.Output);
        Assert.Contains("\r\n", terminal.Output);
        Assert.Contains("two", terminal.Output);
    }

    [Fact]
    public void Render_WhenHardwareCursorTrackingIsDisabled_DoesNotMoveTerminalCursor()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new NormalTerminalTuiRenderer(terminal);

        renderer.Render(new CursorComponent(), Theme.Default, virtualHeight: 8);

        Assert.DoesNotContain("\x1b[4G", terminal.Output);
        Assert.Equal(0, terminal.ShowCursorCount);
        Assert.Equal(1, terminal.HideCursorCount);
    }

    [Fact]
    public void Render_WhenHardwareCursorTrackingIsEnabled_MovesTerminalCursor()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new NormalTerminalTuiRenderer(terminal)
        {
            TrackHardwareCursor = true
        };

        renderer.Render(new CursorComponent(), Theme.Default, virtualHeight: 8);

        Assert.Contains("\x1b[4G", terminal.Output);
        Assert.Equal(1, terminal.ShowCursorCount);
    }

    [Fact]
    public void Render_WhenPerformanceSinkIsConfigured_PublishesFrameEvent()
    {
        using var terminal = new TestTerminal(40, 8);
        using var renderer = new NormalTerminalTuiRenderer(terminal);
        var sink = new RecordingSink();
        renderer.PerformanceSink = sink;

        renderer.Render(new Text("hello"), Theme.Default, virtualHeight: 8);

        var evt = Assert.IsType<TuiRenderCompleted>(Assert.Single(sink.Events));
        Assert.Equal(EventKind.Diagnostic, evt.Kind);
        Assert.Equal(EventChannel.Streaming, evt.Channel);
        Assert.Equal("normal-terminal", evt.Surface);
        Assert.True(evt.RowsRendered > 0);
        Assert.True(evt.SegmentsWritten > 0);
    }

    [Fact]
    public void Application_PerformanceSink_ForwardsToRenderer()
    {
        using var terminal = new TestTerminal(40, 8);
        using var app = new NormalTerminalTuiApplication(terminal);
        var sink = new RecordingSink();
        app.PerformanceSink = sink;
        app.SetRoot(new Text("hello"));

        app.Render(size => size.Height);

        Assert.IsType<TuiRenderCompleted>(Assert.Single(sink.Events));
    }

    [Fact]
    public void TextWriterSink_FormatsFrameSummary()
    {
        var writer = new StringWriter();
        var sink = new TextWriterTuiPerformanceEventSink(writer);

        sink.Publish(new TuiRenderCompleted(
            Surface: "normal-terminal",
            Duration: TimeSpan.FromMilliseconds(4.25),
            RowsRendered: 3,
            SegmentsWritten: 2,
            CacheHits: 1,
            CacheMisses: 0));

        Assert.Contains("tui frame 4.25ms surface=normal-terminal rows=3 segments=2 cache=1/0", writer.ToString());
    }

    private sealed class TestTerminal : ITerminal
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

        public bool TryReadKey(out KeyEvent key)
        {
            if (_keys.TryDequeue(out key))
            {
                return true;
            }

            key = default;
            return false;
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

        public void HandleInput(in KeyEvent key)
        {
        }

        public void Invalidate()
        {
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

        public void HandleInput(in KeyEvent key)
        {
        }

        public void Invalidate()
        {
        }
    }
}
