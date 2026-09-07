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

    [Fact]
    public async Task Dispatcher_SerializesAsyncAndNestedCallbacksOnOneLogicalLoop()
    {
        using var terminal = new TestTerminal();
        using var app = new TuiApplication(terminal);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var sequence = new List<int>();
        app.SetRoot(new Text("hello"));
        var run = app.RunAsync(cancellationToken: cts.Token);

        await app.InvokeAsync(async () =>
        {
            sequence.Add(1);
            await Task.Yield();
            await app.InvokeAsync(() => sequence.Add(2));
            sequence.Add(3);
        });
        await cts.CancelAsync();
        await run;

        Assert.Equal([1, 2, 3], sequence);
    }

    [Fact]
    public async Task Dispatcher_CancellationReleasesInvocationWaitingBehindBlockedCallback()
    {
        using var terminal = new TestTerminal();
        using var app = new TuiApplication(terminal);
        using var runCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var invocationCancellation = new CancellationTokenSource();
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.SetRoot(new Text("hello"));
        var run = app.RunAsync(cancellationToken: runCancellation.Token);

        async ValueTask BlockEventLoopAsync()
        {
            callbackStarted.TrySetResult();
            await releaseCallback.Task.ConfigureAwait(false);
        }

        var blockingInvocation = app.InvokeAsync(BlockEventLoopAsync).AsTask();
        await callbackStarted.Task;
        var waitingInvocationSource = new TaskCompletionSource<Task>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var invokingThread = new Thread(() => waitingInvocationSource.TrySetResult(
            app.InvokeAsync(() => ValueTask.CompletedTask, invocationCancellation.Token).AsTask()));
        invokingThread.Start();
        invokingThread.Join();
        var waitingInvocation = await waitingInvocationSource.Task;

        await invocationCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitingInvocation);

        releaseCallback.TrySetResult();
        await blockingInvocation;
        await runCancellation.CancelAsync();
        await run;
    }

    [Fact]
    public async Task RunAsync_RoutesControlsAndFramesThroughOneBackpressureAwareTransport()
    {
        using var terminal = new TestTerminal();
        var transport = new FrameBackpressureTransport();
        using var app = new TuiApplication(terminal, transport);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        app.SetRoot(new Text("latest"));

        await app.RunAsync(cancellationToken: cts.Token);

        Assert.True(transport.WaitCount >= 1);
        Assert.Equal(4, transport.Attempts.Count);
        Assert.Contains("\x1b[?1049h", transport.Attempts[0]);
        Assert.Contains("latest", transport.Attempts[2]);
        Assert.Contains("\x1b[?1049l", transport.Attempts[3]);
        Assert.Equal(string.Empty, terminal.Output);
    }

    private sealed class FrameBackpressureTransport : ITerminalOutputTransport
    {
        public List<string> Attempts { get; } = [];
        public int WaitCount { get; private set; }
        public ValueTask<TerminalWriteResult> TryWriteFrameAsync(TerminalFrameLease frame, CancellationToken cancellationToken = default)
        {
            Attempts.Add(frame.Payload.ToString());
            return ValueTask.FromResult(Attempts.Count == 2
                ? TerminalWriteResult.Backpressured
                : TerminalWriteResult.Written);
        }
        public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default)
        {
            WaitCount++;
            return ValueTask.CompletedTask;
        }
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
