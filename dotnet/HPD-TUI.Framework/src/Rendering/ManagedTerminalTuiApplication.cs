using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Observability;
using HPD.TUI.Terminal;
using HPD.Events.Signals;

namespace HPD.TUI.Rendering;

public sealed class ManagedTerminalTuiApplication : IDisposable, ITuiDispatcher
{
    private readonly AsyncLocal<int> _dispatcherDepth = new();
    public static readonly TimeSpan DefaultFrameInterval = TimeSpan.FromMilliseconds(16);

    private readonly ITerminal _terminal;
    private readonly ManagedTerminalTuiRenderer _renderer;
    private IComponent? _root;
    private Theme _theme = Theme.Default;
    private EventLoopMailbox<TuiLoopEvent>? _mailbox;
    private ComponentSurface? _surface;
    private bool _stopRequested;
    private bool _disposed;

    public ManagedTerminalTuiApplication(ITerminal terminal)
        : this(terminal, new SynchronousTerminalOutputTransport(terminal))
    {
    }

    /// <summary>Creates a managed-terminal application with an explicit output transport.</summary>
    /// <param name="terminal">The terminal used for input, sizing, and cursor visibility.</param>
    /// <param name="transport">The backpressure-aware single-writer output transport.</param>
    public ManagedTerminalTuiApplication(ITerminal terminal, ITerminalOutputTransport transport)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _renderer = new ManagedTerminalTuiRenderer(terminal, transport)
        {
            TrackHardwareCursor = true
        };
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

    /// <summary>Gets the current physical terminal size.</summary>
    public TerminalSize Size => _terminal.GetSize();

    /// <summary>Gets or sets dispatcher-owned preparation performed before each dirty frame.</summary>
    public Action<TerminalSize, Theme>? FramePreparing { get; set; }

    /// <summary>Gets or sets dispatcher-owned cleanup invoked before the mailbox is detached.</summary>
    public Action? Stopping { get; set; }

    /// <summary>Gets or sets the source of immutable rows published into terminal-owned scrollback.</summary>
    public IScrollbackSource? ScrollbackSource { get; set; }

    /// <summary>Gets whether the application mailbox is accepting dispatcher work.</summary>
    public bool IsRunning => _mailbox is not null;

    public IHpdTuiPerformanceEventSink? PerformanceSink
    {
        get => _renderer.PerformanceSink;
        set => _renderer.PerformanceSink = value;
    }

    public void SetRoot(IComponent root)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _surface?.ReplaceRoot(_root);
        RequestRender();
    }

    public void ClearRoot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _root = null;
        _surface?.ReplaceRoot(null);
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
        TuiRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        options ??= new TuiRunOptions();

        _terminal.HideCursor();

        using var mailbox = CreateMailbox(options);
        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _mailbox = mailbox;
        _surface = new ComponentSurface(RequestRender);
        _surface.ReplaceRoot(_root);
        _stopRequested = false;
        var inputPump = PumpInputAsync(mailbox, loopCts.Token);

        try
        {
            var dirty = options.RenderOnStart;
            while (!loopCts.IsCancellationRequested && !_stopRequested)
            {
                if (dirty)
                {
                    _dispatcherDepth.Value++;
                    try
                    {
                        FramePreparing?.Invoke(_terminal.GetSize(), _theme);
                        try
                        {
                            Render();
                            dirty = false;
                        }
                        catch (TerminalBackpressureException)
                        {
                            dirty = await WaitForWritableWhileDrainingAsync(mailbox, loopCts.Token)
                                .ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        _dispatcherDepth.Value--;
                    }
                    if (dirty)
                        continue;
                }

                dirty |= await WaitForEventOrFrameAsync(
                        mailbox,
                        options.AnimationTickInterval ?? options.MaxFrameInterval,
                        loopCts.Token)
                    .ConfigureAwait(false);
                dirty |= await DrainEventsAsync(mailbox).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _dispatcherDepth.Value++;
            try { Stopping?.Invoke(); }
            finally { _dispatcherDepth.Value--; }
            _surface.Detach();
            _surface = null;
            _mailbox = null;
            await loopCts.CancelAsync().ConfigureAwait(false);
            await inputPump.ConfigureAwait(false);
            _terminal.ShowCursor();
        }
    }

    public void Render()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_root is null)
        {
            return;
        }

        var size = _terminal.GetSize();
        var context = new RenderContext(size.Width, size.Height, _theme);
        var batch = ScrollbackSource?.PrepareScrollback(in context, Math.Max(size.Height * 4, 64));
        try
        {
            _renderer.Render(_root, _theme, batch);
            if (batch is not null)
                ScrollbackSource!.CommitScrollback(batch);
        }
        catch
        {
            if (batch is not null)
                ScrollbackSource!.RollbackScrollback(batch);
            throw;
        }
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

    /// <inheritdoc />
    public bool CheckAccess() => _dispatcherDepth.Value > 0;

    /// <inheritdoc />
    public void Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var mailbox = _mailbox ?? throw new InvalidOperationException("The TUI event loop is not running.");
        if (!mailbox.TryWrite(new TuiLoopEvent(TuiLoopEventKind.Callback, Callback: () => { callback(); return ValueTask.CompletedTask; })))
            throw new InvalidOperationException("The TUI event-loop mailbox rejected the callback.");
    }

    /// <inheritdoc />
    public ValueTask InvokeAsync(Action callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (CheckAccess()) { callback(); return ValueTask.CompletedTask; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            if (cancellationToken.IsCancellationRequested) { completion.TrySetCanceled(cancellationToken); return; }
            try { callback(); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return new ValueTask(completion.Task);
    }

    /// <inheritdoc />
    public ValueTask InvokeAsync(Func<ValueTask> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (CheckAccess()) return callback();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mailbox = _mailbox ?? throw new InvalidOperationException("The TUI event loop is not running.");
        Func<ValueTask> invocation = async () =>
        {
            if (cancellationToken.IsCancellationRequested) { completion.TrySetCanceled(cancellationToken); return; }
            try { await callback().ConfigureAwait(false); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
        };
        if (!mailbox.TryWrite(new TuiLoopEvent(TuiLoopEventKind.Callback, Callback: invocation)))
            throw new InvalidOperationException("The TUI event-loop mailbox rejected the callback.");
        return new ValueTask(completion.Task);
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

    private async ValueTask<bool> WaitForWritableWhileDrainingAsync(
        EventLoopMailbox<TuiLoopEvent> mailbox,
        CancellationToken cancellationToken)
    {
        var readiness = _renderer.WaitUntilWritableAsync(cancellationToken).AsTask();
        while (!readiness.IsCompleted)
        {
            var input = mailbox.WaitToReadAsync(cancellationToken).AsTask();
            if (await Task.WhenAny(readiness, input).ConfigureAwait(false) == readiness)
                break;
            await input.ConfigureAwait(false);
            _ = await DrainEventsAsync(mailbox).ConfigureAwait(false);
        }
        await readiness.ConfigureAwait(false);
        return true;
    }

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

    private async ValueTask<bool> DrainEventsAsync(EventLoopMailbox<TuiLoopEvent> mailbox)
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
                    if (evt.Input.Kind == TerminalInputEventKind.Resize)
                    {
                        dirty = true;
                        continue;
                    }

                    var input = new TuiInputEvent(evt.Input);
                    if (input.Key == KeyCode.Escape && input.Modifiers == KeyModifiers.Ctrl)
                    {
                        _stopRequested = true;
                        return false;
                    }

                    _dispatcherDepth.Value++;
                    try { dirty |= HandleInput(in input, requestRender: false); }
                    finally { _dispatcherDepth.Value--; }
                }
                else if (evt.Kind == TuiLoopEventKind.Callback)
                {
                    _dispatcherDepth.Value++;
                    try { await evt.Callback!().ConfigureAwait(false); }
                    finally { _dispatcherDepth.Value--; }
                    dirty = true;
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
