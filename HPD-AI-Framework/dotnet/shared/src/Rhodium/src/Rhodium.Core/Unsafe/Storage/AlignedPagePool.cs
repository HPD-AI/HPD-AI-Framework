using System.Collections.Concurrent;

namespace Rhodium.Unsafe.Storage;

public sealed class AlignedPagePool<T> : IDisposable where T : unmanaged
{
    private readonly ConcurrentBag<AlignedPage<T>> _pool = new();
    private readonly HashSet<AlignedPage<T>> _returnedPages = [];
    private readonly object _gate = new();
    private readonly GlobalMemoryTracker? _tracker;
    private int _totalRented;

    public AlignedPagePool(GlobalMemoryTracker? tracker = null)
    {
        _tracker = tracker;
    }

    public AlignedPage<T> Rent()
    {
        Interlocked.Increment(ref _totalRented);
        if (_pool.TryTake(out var page))
        {
            lock (_gate)
                _returnedPages.Remove(page);

            page.Clear();
            return page;
        }

        return new AlignedPage<T>(_tracker);
    }

    public void Return(AlignedPage<T> page)
    {
        lock (_gate)
        {
            if (!_returnedPages.Add(page))
                throw new InvalidOperationException("AlignedPagePool double-return detected.");
        }

        _pool.Add(page);
    }

    public void Dispose()
    {
        while (_pool.TryTake(out var page))
            page.Dispose();
    }
}
