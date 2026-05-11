using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.TUI.Views;

namespace HPD.TUI.Tests;

public sealed class TuiApplicationTests
{
    [Fact]
    public void SetFocus_UpdatesFocusableState()
    {
        using var terminal = new TestTerminal();
        using var app = new TuiApplication(terminal);
        var prompt = PromptView.Create();

        app.SetFocus(prompt);

        Assert.True(prompt.IsFocused);
        Assert.Same(prompt, app.Focused);
    }

    [Fact]
    public void HandleInput_ForwardsToFocusedComponent()
    {
        using var terminal = new TestTerminal();
        using var app = new TuiApplication(terminal);
        var prompt = PromptView.Create();

        app.SetFocus(prompt);
        app.HandleInput(new KeyEvent(KeyCode.Character, new Rune('x')));

        Assert.Equal("x", prompt.Model.Value);
    }

    [Fact]
    public async Task RunAsync_PollsTerminalInputAndRenders()
    {
        using var terminal = new TestTerminal();
        using var app = new TuiApplication(terminal);
        var prompt = PromptView.Create();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));

        terminal.Enqueue(new KeyEvent(KeyCode.Character, new Rune('z')));
        app.SetRoot(prompt);
        app.SetFocus(prompt);
        await app.RunAsync(TimeSpan.FromMilliseconds(1), cts.Token);

        Assert.Equal("z", prompt.Model.Value);
        Assert.True(terminal.WriteCount > 0);
        Assert.True(terminal.CursorHidden);
        Assert.True(terminal.CursorShown);
    }

    [Fact]
    public async Task RunAsync_CtrlEscapeStopsLoop()
    {
        using var terminal = new TestTerminal();
        using var app = new TuiApplication(terminal);

        terminal.Enqueue(new KeyEvent(KeyCode.Escape, default, KeyModifiers.Ctrl));
        app.SetRoot(new Text("hello"));
        await app.RunAsync(TimeSpan.FromMilliseconds(1));

        Assert.True(terminal.CursorShown);
    }

    private sealed class TestTerminal : ITerminal
    {
        private readonly Queue<KeyEvent> _keys = new();

        public int WriteCount { get; private set; }

        public bool CursorHidden { get; private set; }

        public bool CursorShown { get; private set; }

        public void Enqueue(KeyEvent key) => _keys.Enqueue(key);

        public TerminalSize GetSize() => new(10, 2);

        public void Write(ReadOnlySpan<char> text)
        {
            WriteCount++;
        }

        public bool TryReadKey(out KeyEvent key)
        {
            if (_keys.Count == 0)
            {
                key = default;
                return false;
            }

            key = _keys.Dequeue();
            return true;
        }

        public void HideCursor()
        {
            CursorHidden = true;
        }

        public void ShowCursor()
        {
            CursorShown = true;
        }

        public void Dispose()
        {
        }
    }
}
