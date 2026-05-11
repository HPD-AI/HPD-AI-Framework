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

        Assert.Contains("\x1b[H", terminal.Output);
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

    private sealed class TestTerminal : ITerminal
    {
        private readonly TerminalSize _size;
        private readonly StringBuilder _output = new();

        public TestTerminal(int width, int height)
        {
            _size = new TerminalSize(width, height);
        }

        public string Output => _output.ToString();

        public void ClearOutput() => _output.Clear();

        public TerminalSize GetSize() => _size;

        public void Write(ReadOnlySpan<char> text)
        {
            _output.Append(text);
        }

        public bool TryReadKey(out KeyEvent key)
        {
            key = default;
            return false;
        }

        public void HideCursor()
        {
        }

        public void ShowCursor()
        {
        }

        public void Dispose()
        {
        }
    }
}
