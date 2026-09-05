using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Terminal;
using HPD.Events.Signals;

namespace HPD.TUI.Rendering;

public sealed class TuiApplication : IDisposable, ITuiDispatcher
{
    private readonly AsyncLocal<int> _dispatcherDepth = new();
    private static readonly char[] EnterAlternateScreen = ['\x1b', '[', '?', '1', '0', '4', '9', 'h', '\x1b', '[', '2', 'J', '\x1b', '[', 'H'];
    private static readonly char[] LeaveAlternateScreen = ['\x1b', '[', '?', '1', '0', '4', '9', 'l'];
    public static readonly TimeSpan DefaultFrameInterval = TimeSpan.FromMilliseconds(16);
    private readonly ITerminal _terminal;
    private readonly TuiRenderer _renderer;
    private readonly FocusManager _focus = new();
    private IComponent? _root;
    private Theme _theme = Theme.Default;
    private EventLoopMailbox<TuiLoopEvent>? _mailbox;
    private ComponentSurface? _surface;
    private bool _stopRequested;
    private bool _disposed;
    private int _eventLoopThreadId;
    private bool _urgentRender;
    private bool _dropIntermediateVisualStates;
    private int _owedVisualStates;
    private long _renderRequestsReceived;
    private long _renderRequestsCoalesced;
    private long _framesAdmitted;
    private long _framesDeferredByPacing;
    private long _framesDeferredByBackpressure;
    private HPD.TUI.Observability.TuiPerformanceCounters? _performanceCounters;
    private long _oldestVisualRequestTimestamp;

    public TuiApplication(ITerminal terminal)
        : this(terminal, new SynchronousTerminalOutputTransport(terminal))
    {
    }

    /// <summary>Creates an alternate-screen application with an explicit backpressure-aware transport.</summary>
    /// <param name="terminal">The terminal used for input, sizing, and cursor visibility.</param>
    /// <param name="transport">The transport shared by frame and control-sequence publication.</param>
    public TuiApplication(ITerminal terminal, ITerminalOutputTransport transport)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _renderer = new TuiRenderer(terminal, transport);
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
        _surface?.ReplaceRoot(_root);
        RequestRender();
    }

    public void ClearRoot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _root = null;
        _surface?.ReplaceRoot(null);
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
        _dropIntermediateVisualStates = options.FramePolicy.DropIntermediateVisualStates;
        _owedVisualStates = options.RenderOnStart ? 1 : 0;
        _oldestVisualRequestTimestamp = options.RenderOnStart ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        await PublishControlWithBackpressureAsync(EnterAlternateScreen, cancellationToken).ConfigureAwait(false);
        _terminal.HideCursor();

        using var mailbox = CreateMailbox(options);
        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _mailbox = mailbox;
        _surface = new ComponentSurface(RequestRender, () => CheckAccess());
        _eventLoopThreadId = Environment.CurrentManagedThreadId;
        _surface.ReplaceRoot(_root);
        _stopRequested = false;
        var inputPump = PumpInputAsync(mailbox, loopCts.Token);

        try
        {
            var dirty = _owedVisualStates > 0;
            var nextFrame = DateTimeOffset.MinValue;
            while (!loopCts.IsCancellationRequested && !_stopRequested)
            {
                _eventLoopThreadId = Environment.CurrentManagedThreadId;
                if (dirty)
                {
                    if (!(options.FramePolicy.RenderImmediatelyOnInput && _urgentRender) && DateTimeOffset.UtcNow < nextFrame)
                    {
                        _framesDeferredByPacing++;
                        _performanceCounters?.RecordPacingDeferral();
                        await Task.Delay(nextFrame - DateTimeOffset.UtcNow, loopCts.Token).ConfigureAwait(false);
                    }
                    try
                    {
                        _renderer.SchedulingDelay = _oldestVisualRequestTimestamp == 0
                            ? TimeSpan.Zero
                            : System.Diagnostics.Stopwatch.GetElapsedTime(_oldestVisualRequestTimestamp);
                        Render();
                        _framesAdmitted++;
                        _performanceCounters?.RecordFrameAdmitted();
                        if (_owedVisualStates > 0) _owedVisualStates--;
                        _oldestVisualRequestTimestamp = _owedVisualStates > 0
                            ? System.Diagnostics.Stopwatch.GetTimestamp()
                            : 0;
                        dirty = _owedVisualStates > 0;
                        _urgentRender = false;
                        nextFrame = DateTimeOffset.UtcNow + options.FramePolicy.MinimumFrameInterval;
                    }
                    catch (TerminalBackpressureException)
                    {
                        _framesDeferredByBackpressure++;
                        _performanceCounters?.RecordBackpressureDeferral();
                        dirty = await WaitForWritableWhileDrainingAsync(mailbox, loopCts.Token).ConfigureAwait(false);
                    }
                    if (dirty)
                        continue;
                }

                dirty |= await WaitForEventOrFrameAsync(
                        mailbox,
                        AnimationParticipants.ResolveInterval(_root, options.AnimationTickInterval),
                        loopCts.Token)
                    .ConfigureAwait(false);
                _eventLoopThreadId = Environment.CurrentManagedThreadId;
                dirty |= await DrainEventsAsync(mailbox).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _dispatcherDepth.Value++;
            try { _surface.Detach(); }
            finally { _dispatcherDepth.Value--; }
            _surface = null;
            _mailbox = null;
            _eventLoopThreadId = 0;
            await loopCts.CancelAsync().ConfigureAwait(false);
            await inputPump.ConfigureAwait(false);
            _terminal.ShowCursor();
            await PublishControlWithBackpressureAsync(LeaveAlternateScreen, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async ValueTask PublishControlWithBackpressureAsync(ReadOnlyMemory<char> payload, CancellationToken cancellationToken)
    {
        while (true)
        {
            try { _renderer.PublishControl(payload.Span); return; }
            catch (TerminalBackpressureException)
            {
                await _renderer.WaitUntilWritableAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

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

    private void OweVisualState()
    {
        if (_owedVisualStates == 0)
            _oldestVisualRequestTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        _renderRequestsReceived++;
        var coalesced = _dropIntermediateVisualStates && _owedVisualStates > 0;
        _performanceCounters?.RecordRenderRequest(coalesced);
        if (coalesced) _renderRequestsCoalesced++;
        else _owedVisualStates++;
    }

    /// <summary>Gets or sets the shared cumulative performance-counter recorder.</summary>
    /// <remarks>Leave this property <see langword="null"/> to disable counter work on the hot path.</remarks>
    public HPD.TUI.Observability.TuiPerformanceCounters? PerformanceCounters
    {
        get => _performanceCounters;
        set
        {
            _performanceCounters = value;
            _renderer.PerformanceCounters = value;
        }
    }

    /// <summary>Gets an immutable snapshot of mailbox frame-admission counters.</summary>
    public HPD.TUI.Observability.TuiSchedulingDiagnostics GetSchedulingDiagnostics() => new(
        _renderRequestsReceived, _renderRequestsCoalesced, _framesAdmitted,
        _framesDeferredByPacing, _framesDeferredByBackpressure);

    /// <inheritdoc />
    public bool CheckAccess() => _dispatcherDepth.Value > 0 ||
        (_eventLoopThreadId != 0 && _eventLoopThreadId == Environment.CurrentManagedThreadId);

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
    /// <remarks>
    /// Cancellation releases the caller even when the invocation is still queued behind other
    /// dispatcher work. A queued invocation observes the same token before executing.
    /// </remarks>
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
        return new ValueTask(cancellationToken.CanBeCanceled
            ? completion.Task.WaitAsync(cancellationToken)
            : completion.Task);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Cancellation releases the caller even when the invocation is still queued behind other
    /// dispatcher work. A queued invocation observes the same token before executing.
    /// </remarks>
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
        return new ValueTask(cancellationToken.CanBeCanceled
            ? completion.Task.WaitAsync(cancellationToken)
            : completion.Task);
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
                    OweVisualState();
                    dirty = true;
                }
                else if (evt.Kind == TuiLoopEventKind.Input)
                {
                    if (evt.Input.Kind == TerminalInputEventKind.Resize)
                    {
                        dirty = true;
                        OweVisualState();
                        continue;
                    }

                    var input = new TuiInputEvent(evt.Input);
                    if (input.Key == KeyCode.Escape && input.Modifiers == KeyModifiers.Ctrl)
                    {
                        _stopRequested = true;
                        return false;
                    }

                    var handled = HandleInput(in input, requestRender: false);
                    dirty |= handled;
                    if (handled) OweVisualState();
                    _urgentRender |= handled;
                }
                else if (evt.Kind == TuiLoopEventKind.Callback)
                {
                    _dispatcherDepth.Value++;
                    try { await evt.Callback!().ConfigureAwait(false); }
                    finally { _dispatcherDepth.Value--; }
                    dirty = true;
                    OweVisualState();
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
