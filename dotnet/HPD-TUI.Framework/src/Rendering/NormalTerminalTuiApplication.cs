using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Observability;
using HPD.TUI.Terminal;
using HPD.Events.Signals;

namespace HPD.TUI.Rendering;

public sealed class NormalTerminalTuiApplication : IDisposable
{
    public static readonly TimeSpan DefaultFrameInterval = TimeSpan.FromMilliseconds(16);

    private readonly ITerminal _terminal;
    private readonly NormalTerminalTuiRenderer _renderer;
    private IComponent? _root;
    private Theme _theme = Theme.Default;
    private EventLoopMailbox<TuiLoopEvent>? _mailbox;
    private bool _stopRequested;
    private bool _disposed;

    public NormalTerminalTuiApplication(ITerminal terminal)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _renderer = new NormalTerminalTuiRenderer(terminal);
        _renderer.PerformanceSink = TuiPerformanceDiagnostics.CreateTextWriterSinkFromEnvironment(Console.Error);
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

    public FocusManager Focus { get; } = new();

    public IComponent? Focused => Focus.Focused;

    public Func<KeyEvent, bool>? ShortcutHandler { get; set; }

    public IHpdTuiPerformanceEventSink? PerformanceSink
    {
        get => _renderer.PerformanceSink;
        set => _renderer.PerformanceSink = value;
    }

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
        Focus.Clear();
        RequestRender();
    }

    public void SetFocus(IComponent? component)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Focus.SetFocus(component);
        RequestRender();
    }

    public async Task RunAsync(
        NormalTerminalRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        options ??= new NormalTerminalRunOptions();

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
                    Render(options);
                    dirty = false;
                }

                await mailbox.WaitToReadAsync(loopCts.Token).ConfigureAwait(false);
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
        }
    }

    public void Render(NormalTerminalRunOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);

        if (_root is null)
        {
            return;
        }

        var size = _terminal.GetSize();
        _renderer.Render(_root, _theme, options.Bounds);
    }

    public bool HandleInput(in TuiInputEvent key)
        => HandleInput(in key, requestRender: true);

    private bool HandleInput(in TuiInputEvent key, bool requestRender)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ShortcutHandler?.Invoke(key.KeyEvent) == true)
        {
            if (requestRender)
            {
                RequestRender();
            }

            return true;
        }

        if (Focus.HandleInput(in key))
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
        _terminal.Dispose();
    }
}
