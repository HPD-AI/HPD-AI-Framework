using System.Diagnostics;
using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

public sealed class TuiRenderer : IDisposable
{
    private static readonly char[] ClearScreenAndCursorHome = ['\x1b', '[', '2', 'J', '\x1b', '[', 'H'];
    private static readonly char[] CursorHide = ['\x1b', '[', '?', '2', '5', 'l'];
    private static readonly char[] CursorShow = ['\x1b', '[', '?', '2', '5', 'h'];
    private readonly ITerminal _terminal;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly AnsiFrameWriter _output = new();
    private TerminalGrid? _currentGrid;
    private TerminalGrid? _previousGrid;
    private bool _hasPreviousFrame;
    private bool _disposed;

    public TuiRenderer(ITerminal terminal)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
    }

    public void Render(IComponent root, Theme? theme = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);

        var size = _terminal.GetSize();
        EnsureGrid(size);

        var context = new RenderContext(size.Width, size.Height, theme ?? Theme.Default, elapsed: _clock.Elapsed);
        _currentGrid!.Clear();

        var writer = new SegmentWriter(_currentGrid);
        root.Render(in context, size.Width, ref writer);

        _output.Clear();
        if (!_hasPreviousFrame)
        {
            _output.Write(ClearScreenAndCursorHome);
            AnsiGridRenderer.WriteFull(_currentGrid, _output);
        }
        else
        {
            AnsiGridRenderer.WriteDifferential(_previousGrid!, _currentGrid, _output);
        }

        AppendCursorState(_currentGrid, _output);
        _output.FlushTo(_terminal);
        (_currentGrid, _previousGrid) = (_previousGrid, _currentGrid);
        _hasPreviousFrame = true;
    }

    private static void AppendCursorState(TerminalGrid grid, AnsiFrameWriter output)
    {
        output.Write(grid.HasTerminalCursor ? CursorShow : CursorHide);
        if (grid.HasTerminalCursor)
        {
            AnsiGridRenderer.WriteCursorMove(grid.TerminalCursorX, grid.TerminalCursorY, output);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _output.Dispose();
        _currentGrid?.Dispose();
        _previousGrid?.Dispose();
        _terminal.Dispose();
    }

    private void EnsureGrid(TerminalSize size)
    {
        if (_currentGrid is not null &&
            _previousGrid is not null &&
            _currentGrid.Width == size.Width &&
            _currentGrid.Height == size.Height)
        {
            return;
        }

        _currentGrid?.Dispose();
        _previousGrid?.Dispose();
        _currentGrid = new TerminalGrid(size.Width, size.Height);
        _previousGrid = new TerminalGrid(size.Width, size.Height);
        _hasPreviousFrame = false;
    }
}
