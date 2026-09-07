using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

/// <summary>Owns a bounded retained raster surface for explicitly buffered component output.</summary>
public sealed class TuiSurface : IDisposable
{
    private SurfaceGeneration _generation;
    private bool _disposed;
    internal event Action? RevisionChanged;
    private ulong _revision = 1;
    private ulong _revisionEpoch = 1;

    /// <summary>Creates an empty retained surface.</summary>
    /// <param name="width">Surface width in terminal columns.</param>
    /// <param name="height">Surface height in terminal rows.</param>
    public TuiSurface(int width, int height) => _generation = new SurfaceGeneration(new TerminalGrid(width, height));

    /// <summary>Gets the width in terminal columns.</summary>
    public int Width => _generation.Grid.Width;

    /// <summary>Gets the height in terminal rows.</summary>
    public int Height => _generation.Grid.Height;

    /// <summary>Gets the revision of the currently captured pixels.</summary>
    public TuiRevision Revision => new(_revision);

    internal SurfaceRevisionIdentity CacheRevision => new(_revisionEpoch, _revision);

    internal TerminalGrid Grid => _generation.Grid;
    internal long EstimatedByteSize => _generation.Grid.EstimatedByteSize;

    internal void ReplayTo(ISegmentSink destination, int originX, int originY, HPD.TUI.Layout.LayoutRect? clip = null)
    {
        for (var row = 0; row < Height; row++)
        for (var column = 0; column < Width; column++)
        {
            var cell = _generation.Grid.GetCell(column, row);
            if (cell.IsContinuation) continue;
            var x = originX + column;
            var y = originY + row;
            if (clip is { } region && (x < region.X || y < region.Y || x + cell.DisplayWidth > region.Right || y >= region.Bottom))
                continue;
            destination.MoveTo(x, y);
            destination.Write(_generation.Grid.GetGrapheme(cell), cell.Style,
                new TerminalRunMetadata(_generation.Grid.GetHyperlink(cell)));
        }
    }

    /// <summary>Replaces the surface contents with a component capture.</summary>
    /// <param name="component">Component whose output is captured.</param>
    /// <param name="context">Render context for the capture.</param>
    public void Capture(IComponent component, in RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(component);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var next = new SurfaceGeneration(new TerminalGrid(Width, Height));
        using var displayList = new RetainedDisplayList();
        displayList.Prepare(component, in context, Width);
        displayList.Replay(next.Grid);
        var previous = _generation;
        _generation = next;
        previous.Release();
        if (_revision == ulong.MaxValue)
        {
            _revisionEpoch = _revisionEpoch == ulong.MaxValue ? 1 : _revisionEpoch + 1;
            _revision = 1;
        }
        else
        {
            _revision++;
        }
        RevisionChanged?.Invoke();
    }

    /// <summary>Returns the retained buffers to their pools.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _generation.Release();
    }

    internal SurfaceLease AcquireLease()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _generation.AddReference();
        return new SurfaceLease(_generation);
    }

    internal sealed class SurfaceLease : IDisposable
    {
        private SurfaceGeneration? _generation;
        public SurfaceLease(SurfaceGeneration generation) => _generation = generation;
        public SurfaceLease Clone()
        {
            var generation = _generation ?? throw new ObjectDisposedException(nameof(SurfaceLease));
            generation.AddReference();
            return new SurfaceLease(generation);
        }
        public void ReplayTo(ISegmentSink destination, int x, int y, HPD.TUI.Layout.LayoutRect? clip) =>
            (_generation ?? throw new ObjectDisposedException(nameof(SurfaceLease))).ReplayTo(destination, x, y, clip);
        public void Dispose() => Interlocked.Exchange(ref _generation, null)?.Release();
    }

    internal sealed class SurfaceGeneration
    {
        private int _references = 1;
        public SurfaceGeneration(TerminalGrid grid) => Grid = grid;
        public TerminalGrid Grid { get; }
        public void AddReference() => Interlocked.Increment(ref _references);
        public void Release() { if (Interlocked.Decrement(ref _references) == 0) Grid.Dispose(); }
        public void ReplayTo(ISegmentSink destination, int originX, int originY, HPD.TUI.Layout.LayoutRect? clip)
        {
            for (var row = 0; row < Grid.Height; row++)
            for (var column = 0; column < Grid.Width; column++)
            {
                var cell = Grid.GetCell(column, row);
                if (cell.IsContinuation) continue;
                var x = originX + column;
                var y = originY + row;
                if (clip is { } region && (x < region.X || y < region.Y || x + cell.DisplayWidth > region.Right || y >= region.Bottom)) continue;
                destination.MoveTo(x, y);
                destination.Write(Grid.GetGrapheme(cell), cell.Style, new TerminalRunMetadata(Grid.GetHyperlink(cell)));
            }
        }
    }
}

internal readonly record struct SurfaceRevisionIdentity(ulong Epoch, ulong Revision);
