using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Observability;
using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

public sealed class NormalTerminalTuiApplication : IDisposable
{
    public static readonly TimeSpan DefaultFrameInterval = TimeSpan.FromMilliseconds(16);

    private readonly ITerminal _terminal;
    private readonly NormalTerminalTuiRenderer _renderer;
    private IComponent? _root;
    private Theme _theme = Theme.Default;
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
        set => _theme = value ?? throw new ArgumentNullException(nameof(value));
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
    }

    public void ClearRoot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _root = null;
        Focus.Clear();
    }

    public void SetFocus(IComponent? component)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Focus.SetFocus(component);
    }

    public void Render(Func<TerminalSize, int> getVirtualHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(getVirtualHeight);

        if (_root is null)
        {
            return;
        }

        var size = _terminal.GetSize();
        var virtualHeight = Math.Max(size.Height, getVirtualHeight(size));
        _renderer.Render(_root, _theme, virtualHeight);
    }

    public async Task RunAsync(Func<TerminalSize, int> getVirtualHeight, CancellationToken cancellationToken = default)
    {
        await RunAsync(getVirtualHeight, DefaultFrameInterval, cancellationToken).ConfigureAwait(false);
    }

    public async Task RunAsync(
        Func<TerminalSize, int> getVirtualHeight,
        TimeSpan frameInterval,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(getVirtualHeight);
        if (frameInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(frameInterval), "Frame interval must be positive.");
        }

        _terminal.HideCursor();

        try
        {
            using var timer = new PeriodicTimer(frameInterval);
            Render(getVirtualHeight);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_terminal.TryReadKey(out var key))
                {
                    if (key.Key == KeyCode.Escape && key.Modifiers == KeyModifiers.Ctrl)
                    {
                        return;
                    }

                    HandleInput(in key);
                }

                Render(getVirtualHeight);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _terminal.ShowCursor();
        }
    }

    public void HandleInput(in KeyEvent key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ShortcutHandler?.Invoke(key) == true)
        {
            return;
        }

        if (Focus.HandleInput(in key))
        {
            return;
        }

        _root?.HandleInput(in key);
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
