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
