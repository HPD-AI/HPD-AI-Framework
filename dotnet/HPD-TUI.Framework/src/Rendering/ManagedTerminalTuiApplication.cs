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
    private bool _urgentRender;
    private bool _dropIntermediateVisualStates;
    private int _owedVisualStates;
    private long _renderRequestsReceived;
    private long _renderRequestsCoalesced;
    private long _framesAdmitted;
    private long _framesDeferredByPacing;
    private long _framesDeferredByBackpressure;
    private TuiPerformanceCounters? _performanceCounters;
    private long _oldestVisualRequestTimestamp;
    private IScrollbackSource? _presentedSource;
    private long _presentedHistoryRevision;

    /// <summary>Gets whether the production terminal supports native history publication.</summary>
    public bool SupportsManagedScrollback => _renderer.SupportsManagedScrollback;

    /// <summary>Gets or sets the duration after which enabled diagnostics report incomplete dispatcher work.</summary>
    /// <remarks>The watchdog is dormant when <see cref="PerformanceSink"/> is <see langword="null"/>.</remarks>
    public TimeSpan EventLoopStallThreshold { get; set; } = TimeSpan.FromSeconds(2);

    public ManagedTerminalTuiApplication(ITerminal terminal)
        : this(terminal, new SynchronousTerminalOutputTransport(terminal))
    {
    }

    /// <summary>Creates a managed-terminal application with an explicit output transport.</summary>
    /// <param name="terminal">The terminal used for input, sizing, and cursor visibility.</param>
    /// <param name="transport">The backpressure-aware single-writer output transport.</param>
    public ManagedTerminalTuiApplication(ITerminal terminal, ITerminalOutputTransport transport)
        : this(terminal, transport, ManagedTerminalCapabilityProfile.Detect(terminal))
    {
    }

    /// <summary>Creates a managed-terminal application with explicit capabilities and failure policy.</summary>
    /// <param name="terminal">The terminal used for input and sizing.</param>
    /// <param name="transport">The only transport allowed to publish terminal bytes.</param>
    /// <param name="capabilities">Capabilities detected or configured for this session.</param>
    /// <param name="fallbackPolicy">Behavior when split-footer requirements are unavailable.</param>
    /// <param name="recoveryPolicy">Behavior after uncertain output or committed-history mutation.</param>
    public ManagedTerminalTuiApplication(
        ITerminal terminal,
        ITerminalOutputTransport transport,
        ManagedTerminalCapabilityProfile capabilities,
        ManagedTerminalFallbackPolicy fallbackPolicy = ManagedTerminalFallbackPolicy.BoundedScreen,
        ManagedTerminalRecoveryPolicy recoveryPolicy = ManagedTerminalRecoveryPolicy.VisibleEpochBoundary)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _renderer = new ManagedTerminalTuiRenderer(terminal, transport, capabilities, fallbackPolicy, recoveryPolicy)
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

    /// <summary>Gets or sets the shared cumulative performance-counter recorder.</summary>
    /// <remarks>Leave this property <see langword="null"/> to disable counter work on the hot path.</remarks>
    public TuiPerformanceCounters? PerformanceCounters
    {
        get => _performanceCounters;
        set
        {
            _performanceCounters = value;
            _renderer.PerformanceCounters = value;
        }
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
        _dropIntermediateVisualStates = options.FramePolicy.DropIntermediateVisualStates;
        _owedVisualStates = options.RenderOnStart ? 1 : 0;
        _oldestVisualRequestTimestamp = options.RenderOnStart ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        using var mailbox = CreateMailbox(options);
        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _mailbox = mailbox;
        _surface = new ComponentSurface(RequestRender, () => CheckAccess());
        _dispatcherDepth.Value++;
        try { _surface.ReplaceRoot(_root); }
        finally { _dispatcherDepth.Value--; }
        _stopRequested = false;
        var inputPump = PumpInputAsync(mailbox, loopCts.Token);

        try
        {
            var dirty = _owedVisualStates > 0;
            var nextFrame = DateTimeOffset.MinValue;
            while (!loopCts.IsCancellationRequested && !_stopRequested)
            {
                if (dirty)
                {
                    if (!(options.FramePolicy.RenderImmediatelyOnInput && _urgentRender) && DateTimeOffset.UtcNow < nextFrame)
                    {
                        _framesDeferredByPacing++;
                        _performanceCounters?.RecordPacingDeferral();
                        await Task.Delay(nextFrame - DateTimeOffset.UtcNow, loopCts.Token).ConfigureAwait(false);
                    }
                    _dispatcherDepth.Value++;
                    try
                    {
                        try
                        {
                            _renderer.SchedulingDelay = _oldestVisualRequestTimestamp == 0
                                ? TimeSpan.Zero
                                : System.Diagnostics.Stopwatch.GetElapsedTime(_oldestVisualRequestTimestamp);
                            using (StartOperationWatchdog("frame-render/publication"))
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
                        AnimationParticipants.ResolveInterval(_root, options.AnimationTickInterval),
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
            _dispatcherDepth.Value++;
            try { _surface.Detach(); }
            finally { _dispatcherDepth.Value--; }
            _surface = null;
            _mailbox = null;
            var shutdown = _renderer.Shutdown();
            while (shutdown.Status == TerminalWriteStatus.Backpressured)
            {
                await _renderer.WaitUntilWritableAsync(CancellationToken.None).ConfigureAwait(false);
                shutdown = _renderer.Shutdown();
            }
            await loopCts.CancelAsync().ConfigureAwait(false);
            await inputPump.ConfigureAwait(false);
            if (shutdown.Status == TerminalWriteStatus.Failed)
                throw new InvalidOperationException("Managed terminal shutdown failed; terminal state is uncertain.", shutdown.Error);
        }
    }

    /// <summary>Renders one presentation frame, keeping its prepared batch attached to its original source.</summary>
    /// <remarks>A replacement source is presented in a subsequent frame. Transport-accepted batches are
    /// never rolled back in response to a later semantic commit or scheduling failure.</remarks>
    public void Render()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var frameRoot = _root;
        var frameSource = ScrollbackSource;
        if (frameRoot is null)
        {
            return;
        }

        var size = _terminal.GetSize();
        var context = new RenderContext(size.Width, size.Height, _theme);
        if (frameSource is { } source &&
            (!ReferenceEquals(source, _presentedSource) || source.HistoryRevision != _presentedHistoryRevision || _renderer.RequiresRecovery))
        {
            if (!_renderer.SupportsManagedScrollback)
                throw new NotSupportedException("Native chat requires a supported terminal; select a supported terminal before starting chat.");
            long epoch;
            if (_presentedSource is null && !_renderer.RequiresRecovery) epoch = _renderer.StartPresentationEpoch();
            else
            {
                var policy = _renderer.RequiresRecovery ? _renderer.RecoveryPolicy : source.HistoryResetPolicy;
                if (!_renderer.RequiresRecovery) _renderer.SetFullScreen(false);
                var transition = _renderer.RebaseCommittedHistory(policy);
                if (transition.Status == ManagedHistoryRebaseStatus.Backpressured) throw new TerminalBackpressureException();
                if (transition.Status != ManagedHistoryRebaseStatus.Written)
                    throw new InvalidOperationException("The conversation presentation transition failed.", transition.Error);
                epoch = transition.PresentationEpoch;
            }
            source.ResetPresentation(epoch, in context);
            _presentedSource = source;
            _presentedHistoryRevision = source.HistoryRevision;
        }
        using (StartOperationWatchdog("frame-preparation"))
            FramePreparing?.Invoke(size, _theme);
        // A preparation callback may select another shell. Start that presentation on
        // the next frame instead of mixing its root with this source's generation.
        if (!ReferenceEquals(frameRoot, _root) || !ReferenceEquals(frameSource, ScrollbackSource))
        {
            OweVisualState(coalesce: false);
            return;
        }
        ScrollbackBatch? batch = null;
        var outputAccepted = false;
        try
        {
            _renderer.SetFullScreen(frameSource?.IsFullScreen == true);
            batch = frameSource?.PrepareScrollback(in context, Math.Max(size.Height * 4, 64));
            _renderer.Render(frameRoot, _theme, batch);
            outputAccepted = true;
            if (batch is not null)
            {
                frameSource!.CommitScrollback(batch);
                batch = null;
                // Drain a bounded batch per admitted frame, including historical replay with no new input.
                OweVisualState(coalesce: false);
            }
        }
        catch (TerminalBackpressureException)
        {
            if (!outputAccepted && batch is not null) frameSource!.RollbackScrollback(batch);
            throw;
        }
        catch when (_renderer.RequiresRecovery && ScrollbackSource is not null)
        {
            if (!outputAccepted && batch is not null) frameSource!.RollbackScrollback(batch);
            var recovery = RebaseCommittedHistory(_renderer.RecoveryPolicy);
            if (recovery.Status == ManagedHistoryRebaseStatus.Backpressured) throw new TerminalBackpressureException();
            if (recovery.Status != ManagedHistoryRebaseStatus.Written)
                throw new InvalidOperationException("Terminal history recovery failed.", recovery.Error);
            OweVisualState(coalesce: false);
        }
        catch
        {
            if (!outputAccepted && batch is not null) frameSource!.RollbackScrollback(batch);
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

    private void OweVisualState(bool coalesce = true)
    {
        if (_owedVisualStates == 0)
            _oldestVisualRequestTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        _renderRequestsReceived++;
        var coalesced = coalesce && _dropIntermediateVisualStates && _owedVisualStates > 0;
        _performanceCounters?.RecordRenderRequest(coalesced);
        if (coalesced) _renderRequestsCoalesced++;
        else _owedVisualStates++;
    }

    /// <summary>Gets an immutable snapshot of mailbox frame-admission counters.</summary>
    public TuiSchedulingDiagnostics GetSchedulingDiagnostics() => new(
        _renderRequestsReceived, _renderRequestsCoalesced, _framesAdmitted,
        _framesDeferredByPacing, _framesDeferredByBackpressure);

    /// <summary>Applies the selected terminal policy after a model mutation reports committed-history impact.</summary>
    /// <param name="policy">The explicit recovery policy selected for terminal-visible history.</param>
    /// <returns>The structured publication outcome.</returns>
    public ManagedHistoryRebaseResult RebaseCommittedHistory(ManagedTerminalRecoveryPolicy policy)
    {
        var result = _renderer.RebaseCommittedHistory(policy);
        if (result.Status == ManagedHistoryRebaseStatus.Written && ScrollbackSource is { } source)
        {
            var size = _terminal.GetSize();
            var context = new RenderContext(size.Width, size.Height, _theme);
            source.ResetPresentation(result.PresentationEpoch, in context);
            _presentedSource = source;
            _presentedHistoryRevision = source.HistoryRevision;
            RequestRender();
        }
        return result;
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
        => InvokeAsync("callback", callback, cancellationToken);

    /// <summary>Runs named asynchronous work on the dispatcher for responsiveness diagnostics.</summary>
    /// <param name="operationName">Stable diagnostic name for the work.</param>
    /// <param name="callback">Work to run on the dispatcher.</param>
    /// <param name="cancellationToken">
    /// Cancels the callback before it begins and releases a caller waiting for dispatcher admission.
    /// </param>
    public ValueTask InvokeAsync(
        string operationName,
        Func<ValueTask> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
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
        if (!mailbox.TryWrite(new TuiLoopEvent(
                TuiLoopEventKind.Callback,
                Callback: invocation,
                OperationName: operationName)))
            throw new InvalidOperationException("The TUI event-loop mailbox rejected the callback.");
        return new ValueTask(cancellationToken.CanBeCanceled
            ? completion.Task.WaitAsync(cancellationToken)
            : completion.Task);
    }

    /// <summary>Queues external-process output behind all previously submitted frames and control traffic.</summary>
    /// <param name="output">The immutable output to publish before managed rendering resumes.</param>
    /// <param name="cancellationToken">Cancels the mailbox callback before publication begins.</param>
    public ValueTask PublishExternalOutputAsync(string output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        return InvokeAsync(() => _renderer.PublishExternalOutput(output), cancellationToken);
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

                    _dispatcherDepth.Value++;
                    try
                    {
                        var handled = HandleInput(in input, requestRender: false);
                        dirty |= handled;
                        if (handled) OweVisualState();
                        _urgentRender |= handled;
                    }
                    finally { _dispatcherDepth.Value--; }
                }
                else if (evt.Kind == TuiLoopEventKind.Callback)
                {
                    _dispatcherDepth.Value++;
                    try
                    {
                        using var watchdog = StartOperationWatchdog(evt.OperationName ?? "callback");
                        await evt.Callback!().ConfigureAwait(false);
                    }
                    finally { _dispatcherDepth.Value--; }
                    dirty = true;
                    OweVisualState();
                }
            }
        }
        while (count == events.Length);

        return dirty;
    }

    private IDisposable? StartOperationWatchdog(string operation)
    {
        var sink = PerformanceSink;
        if (sink is null || EventLoopStallThreshold <= TimeSpan.Zero)
            return null;

        return OperationWatchdog.Start(sink, operation, EventLoopStallThreshold);
    }

    private sealed class OperationWatchdog
    {
        private int _completed;

        private OperationWatchdog() { }

        internal static IDisposable Start(
            IHpdTuiPerformanceEventSink sink,
            string operation,
            TimeSpan threshold)
        {
            var watchdog = new OperationWatchdog();
            _ = watchdog.ObserveAsync(sink, operation, threshold);
            return new Completion(watchdog);
        }

        private async Task ObserveAsync(
            IHpdTuiPerformanceEventSink sink,
            string operation,
            TimeSpan threshold)
        {
            await Task.Delay(threshold).ConfigureAwait(false);
            if (Volatile.Read(ref _completed) == 0)
                sink.Publish(new TuiEventLoopOperationStalled(operation, threshold));
        }

        private sealed class Completion(OperationWatchdog owner) : IDisposable
        {
            public void Dispose() => Interlocked.Exchange(ref owner._completed, 1);
        }
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
