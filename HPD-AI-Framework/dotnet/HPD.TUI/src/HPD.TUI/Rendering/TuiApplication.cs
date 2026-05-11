using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

public sealed class TuiApplication : IDisposable
{
    public static readonly TimeSpan DefaultFrameInterval = TimeSpan.FromMilliseconds(16);
    private readonly ITerminal _terminal;
    private readonly TuiRenderer _renderer;
    private readonly FocusManager _focus = new();
    private IComponent? _root;
    private Theme _theme = Theme.Default;
    private bool _disposed;

    public TuiApplication(ITerminal terminal)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _renderer = new TuiRenderer(terminal);
    }

    public Theme Theme
    {
        get => _theme;
        set => _theme = value ?? throw new ArgumentNullException(nameof(value));
    }

    public IComponent? Root => _root;

    public FocusManager Focus => _focus;

    public IComponent? Focused => _focus.Focused;

    public void SetRoot(IComponent root)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public void ClearRoot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _root = null;
        _focus.Clear();
    }

    public void SetFocus(IComponent? component)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _focus.SetFocus(component);
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

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await RunAsync(DefaultFrameInterval, cancellationToken).ConfigureAwait(false);
    }

    public async Task RunAsync(TimeSpan frameInterval, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frameInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(frameInterval), "Frame interval must be positive.");
        }

        _terminal.HideCursor();

        try
        {
            using var timer = new PeriodicTimer(frameInterval);
            Render();

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

                Render();
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
        if (_focus.HandleInput(in key))
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
    }
}
