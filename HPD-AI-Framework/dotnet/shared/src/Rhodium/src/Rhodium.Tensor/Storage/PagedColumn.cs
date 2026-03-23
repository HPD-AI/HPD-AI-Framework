namespace Rhodium.Tensor.Storage;

/// <summary>
/// Paged column storage for a single tensor field.
/// Manages a list of aligned pages.
/// </summary>
/// <typeparam name="T">The unmanaged element type.</typeparam>
internal sealed class PagedColumn<T> : IDisposable where T : unmanaged
{
    private readonly List<AlignedPage<T>> _pages = new(64);

    /// <summary>
    /// Get a reference to the value at a specific virtual index.
    /// </summary>
    public ref T ValueAt(int virtualIndex)
    {
        int pageIdx = virtualIndex / AlignedPage<T>.Capacity;
        int offset = virtualIndex % AlignedPage<T>.Capacity;
        return ref _pages[pageIdx].AsSpan()[offset];
    }

    /// <summary>
    /// Get a span over an entire page.
    /// </summary>
    public Span<T> GetPage(int pageIndex) => _pages[pageIndex].AsSpan();

    /// <summary>
    /// Add a new page to the column.
    /// </summary>
    public void Grow() => _pages.Add(new AlignedPage<T>());

    public void Dispose()
    {
        foreach (var p in _pages)
            p.Dispose();
        _pages.Clear();
    }
}
