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
        await app.RunAsync(cancellationToken: cts.Token);

        Assert.Equal("z", prompt.Model.Value);
        Assert.True(terminal.WriteCount > 0);
        Assert.True(terminal.CursorHidden);
        Assert.True(terminal.CursorShown);
        Assert.Contains("\x1b[?1049h", terminal.Output);
        Assert.Contains("\x1b[?1049l", terminal.Output);
    }

    [Fact]
    public async Task RunAsync_CtrlEscapeStopsLoop()
    {
        using var terminal = new TestTerminal();
        using var app = new TuiApplication(terminal);

        terminal.Enqueue(new KeyEvent(KeyCode.Escape, default, KeyModifiers.Ctrl));
        app.SetRoot(new Text("hello"));
        await app.RunAsync();

        Assert.True(terminal.CursorShown);
        Assert.Contains("\x1b[?1049l", terminal.Output);
    }

    private sealed class TestTerminal : ITerminal, ITerminalInput
    {
        private readonly Queue<KeyEvent> _keys = new();
        private readonly StringBuilder _output = new();

        public int WriteCount { get; private set; }

        public string Output => _output.ToString();

        public bool CursorHidden { get; private set; }

        public bool CursorShown { get; private set; }

        public ITerminalInput Input => this;

        public void Enqueue(KeyEvent key) => _keys.Enqueue(key);

        public TerminalSize GetSize() => new(10, 2);

        public void Write(ReadOnlySpan<char> text)
        {
            WriteCount++;
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
            CursorHidden = true;
        }

        public void ShowCursor()
        {
            CursorShown = true;
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
}
