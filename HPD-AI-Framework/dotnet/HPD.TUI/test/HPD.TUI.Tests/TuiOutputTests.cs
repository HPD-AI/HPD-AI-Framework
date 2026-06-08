using HPD.TUI.Components;
using HPD.TUI.Content;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class TuiOutputTests
{
    [Fact]
    public void Render_ReturnsPlainTextByDefault()
    {
        var output = TuiOutput.Render(new Text("hello"), new TuiOutputOptions { Width = 8, Height = 2 });

        Assert.Equal("hello   ", output);
    }

    [Fact]
    public void Render_CanReturnAnsi()
    {
        var output = TuiOutput.Render(new Text("x"), new TuiOutputOptions { Width = 2, Height = 1, UseAnsi = true });

        Assert.Contains("\x1b[0m", output);
        Assert.Contains("x", output);
    }

    [Fact]
    public void Render_AcceptsContentBlocks()
    {
        var output = TuiOutput.Render(TextBlock.Create("block"), new TuiOutputOptions { Width = 6, Height = 1 });

        Assert.Equal("block ", output);
    }

    [Fact]
    public void Write_WritesToTextWriter()
    {
        using var writer = new StringWriter();

        TuiOutput.Write(writer, new Text("ok"), new TuiOutputOptions { Width = 4, Height = 1 });

        Assert.Equal("ok  ", writer.ToString());
    }

    [Fact]
    public void Write_WritesToTerminal()
    {
        using var terminal = new TestTerminal();

        TuiOutput.Write(terminal, new Text("ok"), new TuiOutputOptions { Width = 4, Height = 1 });

        Assert.Equal("ok  ", terminal.Output);
    }

    private sealed class TestTerminal : ITerminal
    {
        private readonly StringBuilder _output = new();

        public string Output => _output.ToString();

        public TerminalSize GetSize() => new(10, 3);

        public void Write(ReadOnlySpan<char> text)
        {
            _output.Append(text);
        }

        public void Flush()
        {
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
