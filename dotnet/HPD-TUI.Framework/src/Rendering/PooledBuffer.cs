using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;

namespace HPD.TUI.Rendering;

/// <summary>Reusable generation storage whose backing array is returned to the shared pool.</summary>
internal sealed class PooledBuffer<T> : IEnumerable<T>, IDisposable
{
    private T[] _items = ArrayPool<T>.Shared.Rent(64);
    public int Count { get; private set; }
    public T this[int index] => (uint)index < (uint)Count ? _items[index] : throw new ArgumentOutOfRangeException(nameof(index));
    public void Add(T item) { if (Count == _items.Length) Grow(); _items[Count++] = item; }
    public void Clear() { if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) Array.Clear(_items, 0, Count); Count = 0; }
    public Enumerator GetEnumerator() => new(_items, Count);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void Dispose()
    {
        var items = _items; _items = [];
        if (items.Length != 0) ArrayPool<T>.Shared.Return(items, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        Count = 0;
    }
    private void Grow()
    {
        var replacement = ArrayPool<T>.Shared.Rent(_items.Length * 2);
        _items.AsSpan(0, Count).CopyTo(replacement);
        ArrayPool<T>.Shared.Return(_items, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        _items = replacement;
    }
    internal struct Enumerator : IEnumerator<T>
    {
        private readonly T[] _items; private readonly int _count; private int _index;
        public Enumerator(T[] items, int count) { _items = items; _count = count; _index = -1; }
        public T Current => _items[_index]; object IEnumerator.Current => Current!;
        public bool MoveNext() => ++_index < _count; public void Reset() => _index = -1; public void Dispose() { }
    }
}
