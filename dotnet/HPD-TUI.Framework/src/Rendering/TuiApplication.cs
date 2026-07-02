using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Terminal;
using HPD.Events.Signals;

namespace HPD.TUI.Rendering;

public sealed class TuiApplication : IDisposable
{
    private static readonly char[] EnterAlternateScreen = ['\x1b', '[', '?', '1', '0', '4', '9', 'h', '\x1b', '[', '2', 'J', '\x1b', '[', 'H'];
    private static readonly char[] LeaveAlternateScreen = ['\x1b', '[', '?', '1', '0', '4', '9', 'l'];
    public static readonly TimeSpan DefaultFrameInterval = TimeSpan.FromMilliseconds(16);
    private readonly ITerminal _terminal;
    private readonly TuiRenderer _renderer;
    private readonly FocusManager _focus = new();
    private IComponent? _root;
    private Theme _theme = Theme.Default;
    private EventLoopMailbox<TuiLoopEvent>? _mailbox;
    private bool _stopRequested;
    private bool _disposed;

    public TuiApplication(ITerminal terminal)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _renderer = new TuiRenderer(terminal);
    }

    public Theme Theme
    {
        get => _theme;
        set
        {
            _theme = value ?? throw new ArgumentNullException(nameof(value));
            RequestRender();
        }
    }

    public IComponent? Root => _root;

    public FocusManager Focus => _focus;

    public IComponent? Focused => _focus.Focused;

    public void SetRoot(IComponent root)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _root = root ?? throw new ArgumentNullException(nameof(root));
        RequestRender();
    }

    public void ClearRoot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _root = null;
        _focus.Clear();
        RequestRender();
    }

    public void SetFocus(IComponent? component)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _focus.SetFocus(component);
        RequestRender();
    }

    public void Render()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_root is null)
        {
            return;
        }

        _renderer.Render(_root, _theme);
    }

    public async Task RunAsync(TuiRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        options ??= new TuiRunOptions();

        _terminal.Write(EnterAlternateScreen);
        _terminal.HideCursor();

        using var mailbox = CreateMailbox(options);
        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _mailbox = mailbox;
        _stopRequested = false;
        var inputPump = PumpInputAsync(mailbox, loopCts.Token);

        try
        {
            var dirty = options.RenderOnStart;
            while (!loopCts.IsCancellationRequested && !_stopRequested)
            {
                if (dirty)
                {
                    Render();
                    dirty = false;
                }

                dirty |= await WaitForEventOrFrameAsync(
                        mailbox,
                        options.AnimationTickInterval ?? options.MaxFrameInterval,
                        loopCts.Token)
                    .ConfigureAwait(false);
                dirty |= DrainEvents(mailbox);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _mailbox = null;
            await loopCts.CancelAsync().ConfigureAwait(false);
            await inputPump.ConfigureAwait(false);
            _terminal.ShowCursor();
            _terminal.Write(LeaveAlternateScreen);
        }
    }

    public bool HandleInput(in TuiInputEvent key)
        => HandleInput(in key, requestRender: true);

    private bool HandleInput(in TuiInputEvent key, bool requestRender)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_focus.HandleInput(in key))
        {
            if (requestRender)
            {
                RequestRender();
            }

            return true;
        }

        var handled = _root?.HandleInput(in key) == true;
        if (handled && requestRender)
        {
            RequestRender();
        }

        return handled;
    }

    public void RequestRender()
    {
        _mailbox?.TryWrite(new TuiLoopEvent(TuiLoopEventKind.RenderRequested));
    }

    private static async ValueTask<bool> WaitForEventOrFrameAsync(
        EventLoopMailbox<TuiLoopEvent> mailbox,
        TimeSpan? frameInterval,
        CancellationToken cancellationToken)
    {
        if (frameInterval is null)
        {
            await mailbox.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var waitTask = mailbox.WaitToReadAsync(waitCts.Token).AsTask();
        var delayTask = Task.Delay(frameInterval.Value, cancellationToken);
        var completed = await Task.WhenAny(waitTask, delayTask).ConfigureAwait(false);
        if (completed == waitTask)
        {
            await waitTask.ConfigureAwait(false);
            return false;
        }

        await waitCts.CancelAsync().ConfigureAwait(false);
        try
        {
            await waitTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return true;
    }

    private static EventLoopMailbox<TuiLoopEvent> CreateMailbox(TuiRunOptions options) =>
        new(new EventLoopMailboxOptions
        {
            Capacity = options.InputMailboxCapacity,
            OverflowMode = options.InputOverflowMode
        });

    private async Task PumpInputAsync(
        EventLoopMailbox<TuiLoopEvent> mailbox,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var input = await _terminal.Input.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (!mailbox.TryWrite(new TuiLoopEvent(TuiLoopEventKind.Input, input)))
                {
                    await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private bool DrainEvents(EventLoopMailbox<TuiLoopEvent> mailbox)
    {
        var dirty = false;
        var events = new TuiLoopEvent[64];
        int count;
        do
        {
            count = mailbox.Drain(events);
            for (var i = 0; i < count; i++)
            {
                var evt = events[i];
                if (evt.Kind == TuiLoopEventKind.RenderRequested)
                {
                    dirty = true;
                }
                else if (evt.Kind == TuiLoopEventKind.Input)
                {
                    var input = new TuiInputEvent(evt.Input);
                    if (input.Key == KeyCode.Escape && input.Modifiers == KeyModifiers.Ctrl)
                    {
                        _stopRequested = true;
                        return false;
                    }

                    dirty |= HandleInput(in input, requestRender: false);
                }
            }
        }
        while (count == events.Length);

        return dirty;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _renderer.Dispose();
    }
}
