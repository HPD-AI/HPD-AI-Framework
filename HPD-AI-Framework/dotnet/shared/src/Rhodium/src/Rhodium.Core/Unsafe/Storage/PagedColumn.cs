namespace Rhodium.Unsafe.Storage;

public sealed class PagedColumn<T> : IDisposable where T : unmanaged
{
    private readonly List<AlignedPage<T>> _pages = new(64);
    private readonly AlignedPagePool<T>? _pool;

    public PagedColumn(AlignedPagePool<T>? pool = null)
    {
        _pool = pool;
    }

    public ref T ValueAt(int virtualIndex)
    {
        var pageIdx = virtualIndex / AlignedPage<T>.Capacity;
        var offset = virtualIndex % AlignedPage<T>.Capacity;
        return ref _pages[pageIdx].AsSpan()[offset];
    }

    public Span<T> GetPage(int pageIndex) => _pages[pageIndex].AsSpan();

    public void Grow() => _pages.Add(_pool?.Rent() ?? new AlignedPage<T>());

    public void Dispose()
    {
        foreach (var page in _pages)
        {
            if (_pool is null)
                page.Dispose();
            else
                _pool.Return(page);
        }

        _pages.Clear();
    }
}
