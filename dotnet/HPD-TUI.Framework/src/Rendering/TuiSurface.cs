using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

/// <summary>Owns a bounded retained raster surface for explicitly buffered component output.</summary>
public sealed class TuiSurface : IDisposable
{
    private readonly TerminalGrid _grid;
    private ulong _revision = 1;
    private ulong _revisionEpoch = 1;

    /// <summary>Creates an empty retained surface.</summary>
    /// <param name="width">Surface width in terminal columns.</param>
    /// <param name="height">Surface height in terminal rows.</param>
    public TuiSurface(int width, int height) => _grid = new TerminalGrid(width, height);

    /// <summary>Gets the width in terminal columns.</summary>
    public int Width => _grid.Width;

    /// <summary>Gets the height in terminal rows.</summary>
    public int Height => _grid.Height;

    /// <summary>Gets the revision of the currently captured pixels.</summary>
    public TuiRevision Revision => new(_revision);

    internal SurfaceRevisionIdentity CacheRevision => new(_revisionEpoch, _revision);

    internal TerminalGrid Grid => _grid;
    internal long EstimatedByteSize => _grid.EstimatedByteSize;

    internal void ReplayTo(ISegmentSink destination, int originX, int originY, HPD.TUI.Layout.LayoutRect? clip = null)
    {
        for (var row = 0; row < Height; row++)
        for (var column = 0; column < Width; column++)
        {
            var cell = _grid.GetCell(column, row);
            if (cell.IsContinuation) continue;
            var x = originX + column;
            var y = originY + row;
            if (clip is { } region && (x < region.X || y < region.Y || x + cell.DisplayWidth > region.Right || y >= region.Bottom))
                continue;
            destination.MoveTo(x, y);
            destination.Write(_grid.GetGrapheme(cell), cell.Style,
                new TerminalRunMetadata(_grid.GetHyperlink(cell)));
        }
    }

    /// <summary>Replaces the surface contents with a component capture.</summary>
    /// <param name="component">Component whose output is captured.</param>
    /// <param name="context">Render context for the capture.</param>
    public void Capture(IComponent component, in RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(component);
        _grid.Clear();
        var displayList = new RetainedDisplayList();
        displayList.Prepare(component, in context, Width);
        displayList.Replay(_grid);
        if (_revision == ulong.MaxValue)
        {
            _revisionEpoch = _revisionEpoch == ulong.MaxValue ? 1 : _revisionEpoch + 1;
            _revision = 1;
        }
        else
        {
            _revision++;
        }
    }

    /// <summary>Returns the retained buffers to their pools.</summary>
    public void Dispose() => _grid.Dispose();
}

internal readonly record struct SurfaceRevisionIdentity(ulong Epoch, ulong Revision);
