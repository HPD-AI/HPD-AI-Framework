using System.Runtime.CompilerServices;
using HPD.Agent.TUI.Models;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.TUI.Observability;

namespace HPD.Agent.TUI.Views;

/// <summary>Resolves bounded retained transcript layouts for immutable entries.</summary>
public interface ITranscriptLayoutCache : IDisposable
{
    /// <summary>Gets or prepares the layout for an immutable entry and structural layout key.</summary>
    PreparedTranscriptEntry Resolve(TranscriptEntry entry, TranscriptLayoutKey key);
}

/// <summary>Identifies every structural input to a prepared transcript layout.</summary>
/// <param name="Width">Available terminal columns.</param>
/// <param name="Theme">Structural theme identity.</param>
/// <param name="ColorSystem">Negotiated terminal color system.</param>
/// <param name="RendererRevision">Application renderer revision.</param>
public readonly record struct TranscriptLayoutKey(
    int Width,
    ThemeKey Theme,
    ColorSystem ColorSystem,
    long RendererRevision);

/// <summary>Owns the exact bounded raster and stable row metadata for one transcript entry.</summary>
public sealed class PreparedTranscriptEntry : IDisposable
{
    private readonly TerminalGrid _grid;
    private bool _disposed;

    internal PreparedTranscriptEntry(TranscriptEntry source, TranscriptLayoutKey key, TerminalGrid grid, int lineCount)
    {
        Source = source;
        Key = key;
        _grid = grid;
        LineCount = lineCount;
    }

    /// <summary>Gets the immutable source entry by reference identity.</summary>
    public TranscriptEntry Source { get; }
    /// <summary>Gets the structural key used to prepare this entry.</summary>
    public TranscriptLayoutKey Key { get; }
    /// <summary>Gets the count of stable raster rows.</summary>
    public int LineCount { get; }
    /// <summary>Gets exact owned pooled-grid storage in bytes.</summary>
    public long ByteSize => _grid.EstimatedByteSize;
    internal bool IsDisposed => _disposed;

    internal void WriteLine(int line, ref DisplayListBuilder output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        if (line >= LineCount) throw new ArgumentOutOfRangeException(nameof(line));
        TuiCapture.WriteLineTo(_grid, line, ref output);
    }

    internal ScrollbackRow CreateScrollbackRow(string id, int line)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        if (line >= LineCount) throw new ArgumentOutOfRangeException(nameof(line));
        var cells = new List<ScrollbackCell>();
        for (var column = 0; column < _grid.Width; column++)
        {
            var cell = _grid.GetCell(column, line);
            if (cell.IsContinuation) continue;
            cells.Add(new ScrollbackCell(_grid.GetGrapheme(cell).ToString(), cell.Style,
                new TerminalRunMetadata(_grid.GetHyperlink(cell)), cell.DisplayWidth));
        }
        // Styled spaces can carry backgrounds, underlines, or other visible decoration.
        // Only undecorated default cells are safe to omit from native scrollback.
        while (cells.Count > 0 && cells[^1].Grapheme == " " && cells[^1].Style == Style.Default &&
            cells[^1].Metadata.Hyperlink is null)
            cells.RemoveAt(cells.Count - 1);
        return new ScrollbackRow(id, cells.ToArray());
    }

    /// <summary>Returns the retained grid to its pool.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _grid.Dispose();
    }
}

internal sealed class TranscriptLayoutCache : ITranscriptLayoutCache
{
    private readonly long _byteBudget;
    private readonly Func<TranscriptEntry, TranscriptLayoutKey, PreparedTranscriptEntry> _prepare;
    private readonly Dictionary<CacheIdentity, LinkedListNode<CacheItem>> _items = new(CacheIdentityComparer.Instance);
    private readonly LinkedList<CacheItem> _lru = [];
    private readonly HashSet<LinkedListNode<CacheItem>> _pinned = [];
    private readonly TuiPerformanceCounters? _performanceCounters;
    private long _bytes;

    internal TranscriptLayoutCache(long byteBudget,
        Func<TranscriptEntry, TranscriptLayoutKey, PreparedTranscriptEntry> prepare,
        TuiPerformanceCounters? performanceCounters = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteBudget);
        _byteBudget = byteBudget;
        _prepare = prepare;
        _performanceCounters = performanceCounters;
    }

    internal long ByteSize => _bytes;
    internal long Evictions { get; private set; }
    internal bool LastResolveWasHit { get; private set; }
    internal void BeginProjection() => _pinned.Clear();
    internal void EndProjection()
    {
        _pinned.Clear();
        TrimToBudget();
    }

    public PreparedTranscriptEntry Resolve(TranscriptEntry entry, TranscriptLayoutKey key)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var identity = new CacheIdentity(entry, key);
        if (_items.TryGetValue(identity, out var node))
        {
            LastResolveWasHit = true;
            _lru.Remove(node);
            _lru.AddLast(node);
            _pinned.Add(node);
            return node.Value.Entry;
        }
        LastResolveWasHit = false;
        var prepared = _prepare(entry, key);
        node = _lru.AddLast(new CacheItem(identity, prepared));
        _items.Add(identity, node);
        _bytes = checked(_bytes + prepared.ByteSize);
        _performanceCounters?.RecordSurfaceAllocation(prepared.ByteSize);
        _pinned.Add(node);
        TrimToBudget();
        return prepared;
    }

    internal void Remove(TranscriptEntry entry)
    {
        foreach (var node in _items.Where(pair => ReferenceEquals(pair.Key.Entry, entry)).Select(static pair => pair.Value).ToArray())
            Evict(node);
    }

    private void Evict(LinkedListNode<CacheItem> node)
    {
        _lru.Remove(node);
        _items.Remove(node.Value.Identity);
        var bytes = node.Value.Entry.ByteSize;
        _bytes -= bytes;
        node.Value.Entry.Dispose();
        Evictions++;
        _performanceCounters?.RecordSurfaceEviction(bytes);
    }

    private void TrimToBudget()
    {
        while (_bytes > _byteBudget)
        {
            var victim = _lru.First;
            while (victim is not null && _pinned.Contains(victim)) victim = victim.Next;
            if (victim is null) return;
            Evict(victim);
        }
    }

    public void Dispose()
    {
        while (_lru.First is { } node) Evict(node);
    }

    private readonly record struct CacheIdentity(TranscriptEntry Entry, TranscriptLayoutKey Key);
    private sealed record CacheItem(CacheIdentity Identity, PreparedTranscriptEntry Entry);
    private sealed class CacheIdentityComparer : IEqualityComparer<CacheIdentity>
    {
        internal static CacheIdentityComparer Instance { get; } = new();
        public bool Equals(CacheIdentity x, CacheIdentity y) => ReferenceEquals(x.Entry, y.Entry) && x.Key == y.Key;
        public int GetHashCode(CacheIdentity value) => HashCode.Combine(RuntimeHelpers.GetHashCode(value.Entry), value.Key);
    }
}
