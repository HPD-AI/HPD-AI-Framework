using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class TuiRendererTests
{
    [Fact]
    public void Render_WritesRootComponentToTerminal()
    {
        using var terminal = new TestTerminal(20, 4);
        using var renderer = new TuiRenderer(terminal);

        renderer.Render(new Text("Hello"));

        Assert.Contains("\x1b[2J\x1b[H", terminal.Output);
        Assert.Contains("Hello", terminal.Output);
    }

    [Fact]
    public void Render_SecondFrameWritesDifferentialOutput()
    {
        using var terminal = new TestTerminal(20, 4);
        using var renderer = new TuiRenderer(terminal);
        var text = new Text("Hello");

        renderer.Render(text);
        terminal.ClearOutput();
        text.SetText("Hxllo");
        renderer.Render(text);

        Assert.DoesNotContain("\x1b[H", terminal.Output);
        Assert.Contains("\x1b[1;2H", terminal.Output);
        Assert.Contains("x", terminal.Output);
        Assert.DoesNotContain("Hello", terminal.Output);
    }

    [Fact]
    public void Render_WhenTerminalSizeChanges_ClearsBeforeFullRedraw()
    {
        using var terminal = new TestTerminal(20, 4);
        using var renderer = new TuiRenderer(terminal);

        renderer.Render(new Text("Hello"));
        terminal.ClearOutput();
        terminal.SetSize(12, 3);
        renderer.Render(new Text("Hi"));

        Assert.Contains("\x1b[2J\x1b[H", terminal.Output);
        Assert.Contains("Hi", terminal.Output);
    }

    [Fact]
    public void Render_UnchangedSecondFrameEmitsNoCellContent()
    {
        using var terminal = new TestTerminal(20, 4);
        using var renderer = new TuiRenderer(terminal);
        var text = new Text("Hello");

        renderer.Render(text);
        terminal.ClearOutput();
        renderer.Render(text);

        Assert.DoesNotContain("Hello", terminal.Output);
        Assert.DoesNotContain("\x1b[1;1H", terminal.Output);
    }

    [Fact]
    public void Render_StableComponentReplaysRetainedDisplayList()
    {
        using var terminal = new TestTerminal(20, 4);
        using var renderer = new TuiRenderer(terminal);
        var component = new CountingComponent();

        renderer.Render(component);
        renderer.Render(component);

        Assert.Equal(1, component.RenderCount);
    }

    [Fact]
    public void Render_OneChangedSibling_ReusesUnchangedSiblingCommandSlice()
    {
        using var terminal = new TestTerminal(20, 4);
        using var renderer = new TuiRenderer(terminal);
        var changed = new CountingComponent();
        var stable = new CountingComponent();
        var root = new Container();
        root.Add(changed);
        root.Add(stable);

        renderer.Render(root);
        changed.ChangePaint();
        renderer.Render(root);

        Assert.Equal(2, changed.RenderCount);
        Assert.Equal(1, stable.RenderCount);
    }

    [Fact]
    public void Render_BackpressuredFrameDoesNotAdvanceCommittedScreen()
    {
        using var terminal = new TestTerminal(20, 4);
        var transport = new BackpressureOnceTransport();
        using var renderer = new TuiRenderer(terminal, transport);
        var text = new Text("old");

        Assert.Throws<TerminalBackpressureException>(() => renderer.Render(text));
        text.SetText("newest");
        renderer.Render(text);

        Assert.Equal(2, transport.Attempts);
        Assert.Contains("newest", transport.AcceptedPayload);
        Assert.DoesNotContain("old", transport.AcceptedPayload);
        Assert.Contains("\x1b[2J\x1b[H", transport.AcceptedPayload);
    }

    [Fact]
    public void Render_WarmedOneCellUpdate_DoesNotAllocateForSynchronousPublication()
    {
        using var terminal = new TestTerminal(20, 4);
        using var renderer = new TuiRenderer(terminal);
        var component = new MutableCharacterComponent();
        renderer.Render(component);
        component.ChangeTo('y');
        renderer.Render(component);
        component.ChangeTo('z');

        var before = GC.GetAllocatedBytesForCurrentThread();
        renderer.Render(component);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    private sealed class CountingComponent : Component
    {
        public int RenderCount { get; private set; }
        public override ComponentDependencies Dependencies => new(RenderContextFields.None, RenderContextFields.None);
        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints) => new(1, 1, 1);
        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            var maxWidth = output.MaxWidth;
            RenderCount++;
            output.Write('x', context.Theme.Text);
        }

        public void ChangePaint() => InvalidatePaint();
    }

    private sealed class MutableCharacterComponent : Component
    {
        private char _value = 'x';
        public override ComponentDependencies Dependencies => new(RenderContextFields.None, RenderContextFields.None);
        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints) => new(1, 1, 1);
        public override void Render(in RenderContext context, ref DisplayListBuilder output) => output.Write(_value, Style.Default);
        public void ChangeTo(char value) { _value = value; InvalidatePaint(); }
    }

    private sealed class BackpressureOnceTransport : ITerminalOutputTransport
    {
        public int Attempts { get; private set; }
        public string AcceptedPayload { get; private set; } = string.Empty;
        public ValueTask<TerminalWriteResult> TryWriteFrameAsync(TerminalFrameLease frame, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts == 1) return ValueTask.FromResult(TerminalWriteResult.Backpressured);
            AcceptedPayload = frame.Payload.ToString();
            return ValueTask.FromResult(TerminalWriteResult.Written);
        }
        public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class TestTerminal : ITerminal, ITerminalInput
    {
        private readonly StringBuilder _output = new();
        private TerminalSize _size;

        public TestTerminal(int width, int height)
        {
            _size = new TerminalSize(width, height);
        }

        public string Output => _output.ToString();

        public ITerminalInput Input => this;

        public void ClearOutput() => _output.Clear();

        public void SetSize(int width, int height)
        {
            _size = new TerminalSize(width, height);
        }

        public TerminalSize GetSize() => _size;

        public void Write(ReadOnlySpan<char> text)
        {
            _output.Append(text);
        }

        public void Flush()
        {
        }

        public ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(TerminalInputEvent.Stop);

        public void HideCursor()
        {
        }

        public void ShowCursor()
        {
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
