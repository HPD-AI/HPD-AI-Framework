using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Flows;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class PromptFlowTests
{
    [Fact]
    public void TextFlow_ComponentSubmitsValidatedValue()
    {
        var flow = PromptFlow.Text("Name").Validate(value =>
            value.Length >= 2 ? PromptValidationResult.Valid : PromptValidationResult.Invalid("Too short."));
        PromptResult<string>? result = null;
        var component = flow.CreateComponentForTesting(r => result = r);

        component.HandleInput(new KeyEvent(KeyCode.Character, new Rune('a')));
        component.HandleInput(new KeyEvent(KeyCode.Enter));

        Assert.Null(result);
        Assert.Contains("Too short", TuiCapture.RenderToString(component, 20, 2));

        component.HandleInput(new KeyEvent(KeyCode.Character, new Rune('b')));
        component.HandleInput(new KeyEvent(KeyCode.Enter));

        Assert.True(result?.IsSubmitted);
        Assert.Equal("ab", result?.Value);
    }

    [Fact]
    public async Task TextFlow_RunAsyncCompletesFromTerminalInput()
    {
        using var terminal = new TestTerminal();
        using var app = new TuiApplication(terminal);

        terminal.Enqueue(new KeyEvent(KeyCode.Character, new Rune('o')));
        terminal.Enqueue(new KeyEvent(KeyCode.Character, new Rune('k')));
        terminal.Enqueue(new KeyEvent(KeyCode.Enter));

        var result = await PromptFlow.Text("Name").RunAsync(app, TimeSpan.FromMilliseconds(1));

        Assert.True(result.IsSubmitted);
        Assert.Equal("ok", result.Value);
        Assert.True(terminal.CursorShown);
    }

    [Fact]
    public async Task TextFlow_RunAsyncRestoresPreviousRootAndFocus()
    {
        using var terminal = new TestTerminal();
        using var app = new TuiApplication(terminal);
        var root = new Text("root");
        var focused = new TestFocusable();

        app.SetRoot(root);
        app.SetFocus(focused);
        terminal.Enqueue(new KeyEvent(KeyCode.Enter));

        var result = await PromptFlow.Text("Name").AllowEmpty().RunAsync(app, TimeSpan.FromMilliseconds(1));

        Assert.True(result.IsSubmitted);
        Assert.Same(root, app.Root);
        Assert.Same(focused, app.Focused);
        Assert.True(focused.IsFocused);
    }

    [Fact]
    public async Task ModalSession_RunAsyncRestoresPreviousRootAndFocus()
    {
        using var terminal = new TestTerminal();
        using var app = new TuiApplication(terminal);
        var root = new Text("root");
        var focused = new TestFocusable();
        var session = new ModalSession<string>();
        var component = new SubmitOnEnterComponent(() => session.Submit("done"));

        app.SetRoot(root);
        app.SetFocus(focused);
        terminal.Enqueue(new KeyEvent(KeyCode.Enter));

        var result = await session.RunAsync(app, component, component, TimeSpan.FromMilliseconds(1));

        Assert.True(result.IsSubmitted);
        Assert.Equal("done", result.Value);
        Assert.Same(root, app.Root);
        Assert.Same(focused, app.Focused);
    }

    private sealed class SubmitOnEnterComponent : IFocusable
    {
        private readonly Action _submit;

        public SubmitOnEnterComponent(Action submit)
        {
            _submit = submit;
        }

        public bool IsFocused { get; set; }

        public Measurement Measure(in RenderContext context, int maxWidth) => new(0, 0);

        public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
        {
        }

        public void HandleInput(in KeyEvent key)
        {
            if (key.Key == KeyCode.Enter)
            {
                _submit();
            }
        }

        public void Invalidate()
        {
        }
    }

    private sealed class TestFocusable : IFocusable
    {
        public bool IsFocused { get; set; }

        public Measurement Measure(in RenderContext context, int maxWidth) => new(0, 0);

        public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
        {
        }

        public void HandleInput(in KeyEvent key)
        {
        }

        public void Invalidate()
        {
        }
    }

    private sealed class TestTerminal : ITerminal
    {
        private readonly Queue<KeyEvent> _keys = new();

        public bool CursorShown { get; private set; }

        public void Enqueue(KeyEvent key) => _keys.Enqueue(key);

        public TerminalSize GetSize() => new(40, 6);

        public void Write(ReadOnlySpan<char> text)
        {
        }

        public void Flush()
        {
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
