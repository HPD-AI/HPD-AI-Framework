namespace Helium.Hardware;

/// <summary>
/// Stack-only computation view over contiguous hardware memory.
/// </summary>
public ref struct PackedSpan<T> where T : unmanaged
{
    private Span<T> _data;

    internal PackedSpan(T[] data) => _data = data;

    internal PackedSpan(Span<T> data) => _data = data;

    public int Length => _data.Length;

    public ref T this[int index] => ref _data[index];

    public Span<T> Raw => _data;

    public ReadOnlySpan<T> ReadOnly => _data;

    public PackedSpan<T> Slice(int start, int length) => new(_data.Slice(start, length));
}
