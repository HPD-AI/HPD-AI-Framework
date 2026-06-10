using System.Runtime.InteropServices;
using Rhodium.Unsafe;

namespace Rhodium.Unsafe.Storage;

[HighPerformanceKernel(Reviewer = "system", LastAuditDate = "2026-05-22")]
public sealed unsafe class UnmanagedPagedStore<T> : IDisposable where T : unmanaged
{
    private readonly List<IntPtr> _pages = [];
    private readonly GlobalMemoryTracker? _tracker;
    private readonly int _pageSize;
    private readonly int _pageBytes;

    public UnmanagedPagedStore(int pageSize, GlobalMemoryTracker? tracker = null)
    {
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be positive.");

        _pageSize = pageSize;
        _pageBytes = pageSize * sizeof(T);
        _tracker = tracker;
    }

    public int PageSize => _pageSize;
    public int PageCount => _pages.Count;

    public void EnsurePage(int pageIndex)
    {
        if (pageIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        while (_pages.Count <= pageIndex)
        {
            var ptr = (T*)NativeMemory.AlignedAlloc((nuint)_pageBytes, 64);
            NativeMemory.Clear(ptr, (nuint)_pageBytes);
            _tracker?.TrackAllocation(_pageBytes);
            _pages.Add((IntPtr)ptr);
        }
    }

    public ref T ValueAt(int virtualIndex)
    {
        if (virtualIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(virtualIndex));

        var pageIndex = virtualIndex / _pageSize;
        EnsurePage(pageIndex);
        var ptr = (T*)_pages[pageIndex].ToPointer();
        return ref ptr[virtualIndex % _pageSize];
    }

    public Span<T> PageSpan(int pageIndex)
    {
        EnsurePage(pageIndex);
        var ptr = (T*)_pages[pageIndex].ToPointer();
        return new Span<T>(ptr, _pageSize);
    }

    public void Dispose()
    {
        foreach (var page in _pages)
        {
            NativeMemory.AlignedFree(page.ToPointer());
            _tracker?.TrackDeallocation(_pageBytes);
        }

        _pages.Clear();
    }
}
